namespace Luxel.Graphics.RenderSystem;

public sealed record DueRenderFeatureSet(
    CompiledRenderFeatureSet FeatureSet,
    IReadOnlySet<RenderCadenceId> TriggeredBy,
    ulong ObservedGeneration);

public readonly record struct RenderCadenceExecutionResult(bool Success)
{
    public static RenderCadenceExecutionResult Succeeded => new(true);
    public static RenderCadenceExecutionResult Failed => new(false);
}

public interface IRenderCadenceRunner
{
    ValueTask<RenderCadenceExecutionResult> ExecuteAsync(
        RenderOpportunity opportunity,
        RenderSystemFrameSnapshot frame,
        IReadOnlyList<DueRenderFeatureSet> featureSets,
        CancellationToken token);

    ValueTask DrainAsync(CancellationToken token);
}

public interface ICadenceExecutionCoordinator
{
    ValueTask ExecuteAsync(
        RenderOpportunity opportunity,
        RenderSystemFrameSnapshot frame,
        CancellationToken token);

    ValueTask DrainAsync(CancellationToken token);
}

public sealed class RenderManualTriggerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<RenderManualTriggerId, Generation> _states = [];

    public ulong Request(RenderManualTriggerId id)
    {
        lock (_gate)
        {
            Generation state = GetOrCreate(id);
            return state.Pending = checked(state.Pending + 1);
        }
    }

    internal (ulong Pending, ulong Committed) Read(RenderManualTriggerId id)
    {
        lock (_gate)
        {
            Generation state = GetOrCreate(id);
            return (state.Pending, state.Committed);
        }
    }

    internal void Commit(RenderManualTriggerId id, ulong observed)
    {
        lock (_gate)
        {
            Generation state = GetOrCreate(id);
            state.Committed = Math.Max(state.Committed, Math.Min(observed, state.Pending));
        }
    }

    private Generation GetOrCreate(RenderManualTriggerId id)
    {
        if (!_states.TryGetValue(id, out Generation? state))
        {
            state = new Generation();
            _states.Add(id, state);
        }
        return state;
    }

    private sealed class Generation
    {
        public ulong Pending;
        public ulong Committed;
    }
}

public sealed class RenderManualTriggerSource(
    RenderManualTriggerId id,
    RenderManualTriggerRegistry registry)
{
    public ulong Request() => registry.Request(id);
}

public sealed class CadenceExecutionCoordinator : ICadenceExecutionCoordinator
{
    private readonly IReadOnlyList<RenderCadenceConfiguration> _cadences;
    private readonly IReadOnlyList<RenderFeatureSetId> _setOrder;
    private readonly IReadOnlyDictionary<RenderCadenceRunnerId, IRenderCadenceRunner> _runners;
    private readonly RenderFeatureSetStateRegistry _setStates;
    private readonly RenderManualTriggerRegistry _manualTriggers;
    private readonly Dictionary<RenderCadenceId, TimeSpan> _fixedDeadlines = [];
    private readonly Dictionary<RenderCadenceId, ulong> _afterSuccessPending = [];
    private readonly Dictionary<RenderCadenceId, ulong> _afterSuccessCommitted = [];

    public CadenceExecutionCoordinator(
        IReadOnlyList<RenderCadenceConfiguration> cadences,
        IReadOnlyList<RenderFeatureSetId> setOrder,
        IReadOnlyDictionary<RenderCadenceRunnerId, IRenderCadenceRunner> runners,
        RenderFeatureSetStateRegistry setStates,
        RenderManualTriggerRegistry manualTriggers)
    {
        _cadences = cadences ?? throw new ArgumentNullException(nameof(cadences));
        _setOrder = setOrder ?? throw new ArgumentNullException(nameof(setOrder));
        _runners = runners ?? throw new ArgumentNullException(nameof(runners));
        _setStates = setStates ?? throw new ArgumentNullException(nameof(setStates));
        _manualTriggers = manualTriggers ?? throw new ArgumentNullException(nameof(manualTriggers));
    }

    public async ValueTask ExecuteAsync(
        RenderOpportunity opportunity,
        RenderSystemFrameSnapshot frame,
        CancellationToken token)
    {
        var selections = new Dictionary<RenderCadenceRunnerId, Dictionary<RenderFeatureSetId, Selection>>();
        var manualObservations = new Dictionary<RenderCadenceId, (RenderManualTriggerId Id, ulong Generation)>();

        foreach (RenderCadenceConfiguration cadence in _cadences)
        {
            if (cadence.Schedule is CadenceSchedule.AfterSuccessSchedule) continue;
            IReadOnlySet<RenderFeatureSetId> selected = Evaluate(cadence, opportunity, manualObservations);
            AddSelections(selections, cadence, selected, frame);
        }

        HashSet<RenderCadenceId> succeededCadences = [];
        await ExecuteSelections(selections, opportunity, frame, succeededCadences, token);

        var downstream = new Dictionary<RenderCadenceRunnerId, Dictionary<RenderFeatureSetId, Selection>>();
        foreach (RenderCadenceConfiguration cadence in _cadences)
        {
            if (cadence.Schedule is not CadenceSchedule.AfterSuccessSchedule after) continue;
            if (succeededCadences.Contains(after.Source))
                _afterSuccessPending[cadence.Id] = checked(_afterSuccessPending.GetValueOrDefault(cadence.Id) + 1);
            ulong pending = _afterSuccessPending.GetValueOrDefault(cadence.Id);
            ulong committed = _afterSuccessCommitted.GetValueOrDefault(cadence.Id);
            if (pending > committed) AddSelections(downstream, cadence, cadence.FeatureSets, frame);
        }

        HashSet<RenderCadenceId> downstreamSucceeded = [];
        await ExecuteSelections(downstream, opportunity, frame, downstreamSucceeded, token);
        foreach (RenderCadenceId id in downstreamSucceeded)
            _afterSuccessCommitted[id] = _afterSuccessPending.GetValueOrDefault(id);

        foreach ((RenderCadenceId cadenceId, (RenderManualTriggerId triggerId, ulong generation)) in manualObservations)
            if (succeededCadences.Contains(cadenceId)) _manualTriggers.Commit(triggerId, generation);
    }

    public async ValueTask DrainAsync(CancellationToken token)
    {
        var drained = new HashSet<IRenderCadenceRunner>(ReferenceEqualityComparer.Instance);
        foreach (IRenderCadenceRunner runner in _runners.Values)
            if (drained.Add(runner)) await runner.DrainAsync(token);
    }

    private IReadOnlySet<RenderFeatureSetId> Evaluate(
        RenderCadenceConfiguration cadence,
        RenderOpportunity opportunity,
        Dictionary<RenderCadenceId, (RenderManualTriggerId Id, ulong Generation)> manualObservations)
    {
        switch (cadence.Schedule)
        {
            case CadenceSchedule.EveryOpportunitySchedule:
                return cadence.FeatureSets;
            case CadenceSchedule.InvalidatedSchedule:
                return cadence.FeatureSets.Where(id => _setStates.Read(id).IsDirty).ToHashSet();
            case CadenceSchedule.ManualSchedule manual:
            {
                (ulong pending, ulong committed) = _manualTriggers.Read(manual.Trigger);
                if (pending <= committed) return Empty;
                manualObservations[cadence.Id] = (manual.Trigger, pending);
                return cadence.FeatureSets;
            }
            case CadenceSchedule.FixedRateSchedule fixedRate:
                return IsFixedRateDue(cadence.Id, fixedRate.Hz, opportunity.Timestamp)
                    ? cadence.FeatureSets
                    : Empty;
            default:
                return Empty;
        }
    }

    private bool IsFixedRateDue(RenderCadenceId id, double hz, TimeSpan now)
    {
        if (!(hz > 0) || double.IsInfinity(hz) || double.IsNaN(hz)) return false;
        TimeSpan period = TimeSpan.FromSeconds(1d / hz);
        if (!_fixedDeadlines.TryGetValue(id, out TimeSpan deadline)) deadline = now;
        if (now < deadline) return false;
        do deadline += period; while (deadline <= now);
        _fixedDeadlines[id] = deadline;
        return true;
    }

    private void AddSelections(
        Dictionary<RenderCadenceRunnerId, Dictionary<RenderFeatureSetId, Selection>> all,
        RenderCadenceConfiguration cadence,
        IEnumerable<RenderFeatureSetId> selected,
        RenderSystemFrameSnapshot frame)
    {
        if (!all.TryGetValue(cadence.Runner, out Dictionary<RenderFeatureSetId, Selection>? runnerSets))
        {
            runnerSets = [];
            all.Add(cadence.Runner, runnerSets);
        }

        foreach (RenderFeatureSetId setId in selected)
        {
            if (!frame.FeatureSets.TryGet(setId, out CompiledRenderFeatureSet? set) || set is null) continue;
            if (!runnerSets.TryGetValue(setId, out Selection? selection))
            {
                selection = new Selection(set, _setStates.Read(setId).CurrentGeneration);
                runnerSets.Add(setId, selection);
            }
            selection.TriggeredBy.Add(cadence.Id);
        }
    }

    private async ValueTask ExecuteSelections(
        Dictionary<RenderCadenceRunnerId, Dictionary<RenderFeatureSetId, Selection>> selections,
        RenderOpportunity opportunity,
        RenderSystemFrameSnapshot frame,
        HashSet<RenderCadenceId> succeededCadences,
        CancellationToken token)
    {
        foreach ((RenderCadenceRunnerId runnerId, Dictionary<RenderFeatureSetId, Selection> selected) in selections)
        {
            if (selected.Count == 0 || !_runners.TryGetValue(runnerId, out IRenderCadenceRunner? runner)) continue;
            var due = new List<DueRenderFeatureSet>(selected.Count);
            foreach (RenderFeatureSetId id in _setOrder)
                if (selected.TryGetValue(id, out Selection? selection)) due.Add(selection.ToDue());
            foreach ((RenderFeatureSetId id, Selection selection) in selected)
                if (!_setOrder.Contains(id)) due.Add(selection.ToDue());
            if (due.Count == 0) continue;

            RenderCadenceExecutionResult result = await runner.ExecuteAsync(opportunity, frame, due, token);
            if (!result.Success) continue;
            foreach (DueRenderFeatureSet set in due)
            {
                _setStates.Commit(set.FeatureSet.Id, set.ObservedGeneration);
                succeededCadences.UnionWith(set.TriggeredBy);
            }
        }
    }

    private static readonly IReadOnlySet<RenderFeatureSetId> Empty = new HashSet<RenderFeatureSetId>();

    private sealed class Selection(CompiledRenderFeatureSet set, ulong observed)
    {
        public CompiledRenderFeatureSet Set { get; } = set;
        public ulong Observed { get; } = observed;
        public HashSet<RenderCadenceId> TriggeredBy { get; } = [];
        public DueRenderFeatureSet ToDue() => new(Set, TriggeredBy, Observed);
    }
}
