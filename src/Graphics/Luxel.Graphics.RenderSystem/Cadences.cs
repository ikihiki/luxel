namespace Luxel.Graphics.RenderSystem;

public readonly record struct RenderCadenceId(string Value);
public readonly record struct RenderCadenceRunnerId(string Value);
public readonly record struct RenderManualTriggerId(string Value);

public enum FrameIdentityPolicy
{
    RenderOpportunity,
}

public abstract record CadenceSchedule
{
    private CadenceSchedule() { }

    public sealed record EveryOpportunitySchedule : CadenceSchedule;
    public sealed record FixedRateSchedule(double Hz) : CadenceSchedule;
    public sealed record InvalidatedSchedule : CadenceSchedule;
    public sealed record ManualSchedule(RenderManualTriggerId Trigger) : CadenceSchedule;
    public sealed record AfterSuccessSchedule(RenderCadenceId Source) : CadenceSchedule;

    public static CadenceSchedule EveryOpportunity() => new EveryOpportunitySchedule();
    public static CadenceSchedule FixedRate(double hz) => new FixedRateSchedule(hz);
    public static CadenceSchedule Invalidated() => new InvalidatedSchedule();
    public static CadenceSchedule Manual(RenderManualTriggerId trigger) => new ManualSchedule(trigger);
    public static CadenceSchedule AfterSuccess(RenderCadenceId source) => new AfterSuccessSchedule(source);
}

public sealed record RenderCadenceConfiguration(
    RenderCadenceId Id,
    string DisplayName,
    RenderCadenceRunnerId Runner,
    CadenceSchedule Schedule,
    IReadOnlySet<RenderFeatureSetId> FeatureSets,
    FrameIdentityPolicy FrameIdentity);

public readonly record struct RenderFeatureSetGeneration(
    RenderFeatureSetId FeatureSet,
    ulong CurrentGeneration,
    ulong CommittedGeneration)
{
    public bool IsDirty => CurrentGeneration > CommittedGeneration;
}

public sealed class RenderFeatureSetStateRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<RenderFeatureSetId, State> _states = [];

    public RenderFeatureSetGeneration Read(RenderFeatureSetId featureSet)
    {
        lock (_gate)
        {
            State state = GetOrCreate(featureSet);
            return new RenderFeatureSetGeneration(featureSet, state.Current, state.Committed);
        }
    }

    public ulong Invalidate(RenderFeatureSetId featureSet)
    {
        lock (_gate)
        {
            State state = GetOrCreate(featureSet);
            state.Current = checked(state.Current + 1);
            return state.Current;
        }
    }

    public void Commit(RenderFeatureSetId featureSet, ulong observedGeneration)
    {
        lock (_gate)
        {
            State state = GetOrCreate(featureSet);
            if (observedGeneration > state.Committed)
                state.Committed = Math.Min(observedGeneration, state.Current);
        }
    }

    private State GetOrCreate(RenderFeatureSetId id)
    {
        if (!_states.TryGetValue(id, out State? state))
        {
            state = new State();
            _states.Add(id, state);
        }
        return state;
    }

    private sealed class State
    {
        public ulong Current;
        public ulong Committed;
    }
}

public sealed class RenderFeatureSetInvalidationSource(
    RenderFeatureSetId featureSet,
    RenderFeatureSetStateRegistry registry) : IDisposable
{
    private int _disposed;
    public ulong Invalidate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return registry.Invalidate(featureSet);
    }
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
