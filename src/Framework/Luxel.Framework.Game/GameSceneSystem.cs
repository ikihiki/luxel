using Luxel.Graphics.RenderSystem;

namespace Luxel.Framework.Game;

public sealed record GameSceneContext(IServiceProvider Services);

public interface IGameScene
{
    ValueTask LoadAsync(GameSceneContext context, CancellationToken token);
    void ConfigureRendering(
        RenderFeatureSetCatalog featureSets,
        RenderFeatureAssignmentBuilder assignments);
    void FixedUpdate(in FixedUpdateContext context);
    void Update(in UpdateContext context);
    ValueTask UnloadAsync(GameSceneContext context, CancellationToken token);
}

public enum GameSceneState
{
    Running,
    Paused,
    Sleeping,
}

public readonly record struct GameSceneSlotPolicy(bool ReceivesInput, bool HasFocus)
{
    public static GameSceneSlotPolicy Default => new(true, true);
}

public readonly record struct GameSceneId(Guid Value)
{
    public static GameSceneId New() => new(Guid.NewGuid());
}

public abstract record GameSceneCommand
{
    private GameSceneCommand() { }
    public sealed record Push(GameSceneId Id, IGameScene Scene) : GameSceneCommand;
    public sealed record Replace(
        GameSceneId ExistingId,
        GameSceneId ReplacementId,
        IGameScene Scene) : GameSceneCommand;
    public sealed record Remove(GameSceneId Id) : GameSceneCommand;
    public sealed record SetState(GameSceneId Id, GameSceneState State) : GameSceneCommand;
    public sealed record SetPolicy(GameSceneId Id, GameSceneSlotPolicy Policy) : GameSceneCommand;
    public sealed record RebuildAssignments : GameSceneCommand;
}

public sealed class GameSceneCommandHandle
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal GameSceneCommandHandle(long commandId) => CommandId = commandId;
    public long CommandId { get; }
    public Task Completion => _completion.Task;
    internal void Complete() => _completion.TrySetResult();
    internal void Fail(Exception exception) => _completion.TrySetException(exception);
}

public interface IGameSceneSystem
{
    GameSceneCommandHandle Enqueue(GameSceneCommand command);
    ValueTask CommitCommandsAsync(CancellationToken token);
    void FixedUpdate(in FixedUpdateContext context);
    void Update(in UpdateContext context);
    RenderSystemFrameSnapshot CreateRenderSnapshot(FrameTime time);
    ValueTask ShutdownAsync(CancellationToken token);
}

public sealed class GameSceneSystem : IGameSceneSystem
{
    private readonly GameSceneContext _context;
    private readonly RenderSystemConfiguration _rendering;

    public GameSceneSystem(IServiceProvider services)
        : this(services, new RenderSystemConfiguration(
            new RenderFeatureSetCatalog(),
            new RenderCadenceCatalog(),
            new Dictionary<RenderFeatureSetId, CompiledRenderFeatureSet>()))
    {
    }

    public GameSceneSystem(IServiceProvider services, RenderSystemConfiguration rendering)
    {
        _context = new GameSceneContext(services ?? throw new ArgumentNullException(nameof(services)));
        _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        RebuildAssignments();
    }
    private readonly object _gate = new();
    private readonly Queue<PendingCommand> _commands = new();
    private readonly List<SceneSlot> _slots = [];
    private CompiledRenderFeatureSetRegistry _featureSets = new(
        new Dictionary<RenderFeatureSetId, CompiledRenderFeatureSet>());
    private long _nextCommandId;
    private ulong _assignmentGeneration;
    private bool _assignmentChanged;

    public GameSceneCommandHandle Enqueue(GameSceneCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var handle = new GameSceneCommandHandle(Interlocked.Increment(ref _nextCommandId));
        lock (_gate) _commands.Enqueue(new PendingCommand(command, handle));
        return handle;
    }

    public async ValueTask CommitCommandsAsync(CancellationToken token)
    {
        List<PendingCommand> pending = [];
        lock (_gate)
        {
            while (_commands.TryDequeue(out PendingCommand? command)) pending.Add(command);
        }

        foreach (PendingCommand item in pending)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await ApplyAsync(item.Command, token);
                item.Handle.Complete();
            }
            catch (Exception exception)
            {
                item.Handle.Fail(exception);
            }
        }
    }

    public void FixedUpdate(in FixedUpdateContext context)
    {
        foreach (SceneSlot slot in _slots)
            if (slot.State == GameSceneState.Running) slot.Scene.FixedUpdate(context);
    }

    public void Update(in UpdateContext context)
    {
        foreach (SceneSlot slot in _slots)
            if (slot.State == GameSceneState.Running) slot.Scene.Update(context);
    }

    public RenderSystemFrameSnapshot CreateRenderSnapshot(FrameTime time)
    {
        RenderSystemChangeFlags changes = _assignmentChanged
            ? RenderSystemChangeFlags.Assignment
            : RenderSystemChangeFlags.None;
        _assignmentChanged = false;
        return new RenderSystemFrameSnapshot(
            new RenderSystemFrameContext(
                TimeSpan.FromSeconds(time.TotalSeconds),
                TimeSpan.FromSeconds(time.DeltaSeconds),
                changes,
                _assignmentGeneration),
            _featureSets,
            new RenderFrameResourceRegistry());
    }

    public async ValueTask ShutdownAsync(CancellationToken token)
    {
        for (int index = _slots.Count - 1; index >= 0; index--)
            await _slots[index].Scene.UnloadAsync(_context, token);
        _slots.Clear();
        RebuildAssignments();
    }

    private async ValueTask ApplyAsync(GameSceneCommand command, CancellationToken token)
    {
        switch (command)
        {
            case GameSceneCommand.Push push:
                await PushAsync(push, token);
                break;
            case GameSceneCommand.Replace replace:
                await ReplaceAsync(replace, token);
                break;
            case GameSceneCommand.Remove remove:
                await RemoveAsync(remove.Id, token);
                break;
            case GameSceneCommand.SetState setState:
                SetState(setState.Id, setState.State);
                break;
            case GameSceneCommand.SetPolicy setPolicy:
                SetPolicy(setPolicy.Id, setPolicy.Policy);
                break;
            case GameSceneCommand.RebuildAssignments:
                RebuildAssignments();
                break;
        }
    }

    private async ValueTask PushAsync(GameSceneCommand.Push push, CancellationToken token)
    {
        try
        {
            await push.Scene.LoadAsync(_context, token);
            var assignments = new RenderFeatureAssignmentBuilder();
            push.Scene.ConfigureRendering(_rendering.FeatureSets, assignments);
            if (_slots.Any(slot => slot.Id == push.Id))
                throw new InvalidOperationException($"Scene '{push.Id.Value}' is already active.");
            _slots.Add(new SceneSlot(
                push.Id,
                push.Scene,
                GameSceneState.Running,
                GameSceneSlotPolicy.Default,
                assignments.Build()));
            RebuildAssignments();
        }
        catch
        {
            await push.Scene.UnloadAsync(_context, CancellationToken.None);
            throw;
        }
    }

    private async ValueTask ReplaceAsync(GameSceneCommand.Replace replace, CancellationToken token)
    {
        int index = _slots.FindIndex(slot => slot.Id == replace.ExistingId);
        if (index < 0)
            throw new InvalidOperationException($"Scene '{replace.ExistingId.Value}' is not active.");
        if (replace.ReplacementId != replace.ExistingId &&
            _slots.Any(slot => slot.Id == replace.ReplacementId))
            throw new InvalidOperationException($"Scene '{replace.ReplacementId.Value}' is already active.");

        SceneSlot previous = _slots[index];
        try
        {
            await replace.Scene.LoadAsync(_context, token);
            var assignments = new RenderFeatureAssignmentBuilder();
            replace.Scene.ConfigureRendering(_rendering.FeatureSets, assignments);
            _slots[index] = new SceneSlot(
                replace.ReplacementId,
                replace.Scene,
                GameSceneState.Running,
                previous.Policy,
                assignments.Build());
            RebuildAssignments();
        }
        catch
        {
            await replace.Scene.UnloadAsync(_context, CancellationToken.None);
            throw;
        }

        await previous.Scene.UnloadAsync(_context, token);
    }

    private async ValueTask RemoveAsync(GameSceneId id, CancellationToken token)
    {
        int index = _slots.FindIndex(slot => slot.Id == id);
        if (index < 0) return;
        SceneSlot slot = _slots[index];
        _slots.RemoveAt(index);
        RebuildAssignments();
        await slot.Scene.UnloadAsync(_context, token);
    }

    private void SetState(GameSceneId id, GameSceneState state)
    {
        int index = _slots.FindIndex(slot => slot.Id == id);
        if (index < 0) return;
        SceneSlot slot = _slots[index];
        _slots[index] = slot with { State = state };
        RebuildAssignments();
    }

    private void SetPolicy(GameSceneId id, GameSceneSlotPolicy policy)
    {
        int index = _slots.FindIndex(slot => slot.Id == id);
        if (index < 0) return;
        _slots[index] = _slots[index] with { Policy = policy };
    }

    private void RebuildAssignments()
    {
        var combined = new Dictionary<RenderFeatureSetId, HashSet<IRenderFeature>>();
        foreach ((RenderFeatureSetId id, CompiledRenderFeatureSet set) in _rendering.GlobalAssignments)
            combined[id] = new HashSet<IRenderFeature>(set.Features, ReferenceEqualityComparer.Instance);

        foreach (SceneSlot slot in _slots)
        {
            if (slot.State == GameSceneState.Sleeping) continue;
            foreach ((RenderFeatureSetId id, CompiledRenderFeatureSet set) in slot.Assignments)
            {
                if (!combined.TryGetValue(id, out HashSet<IRenderFeature>? features))
                {
                    features = new HashSet<IRenderFeature>(ReferenceEqualityComparer.Instance);
                    combined.Add(id, features);
                }
                features.UnionWith(set.Features);
            }
        }

        var materialized = new Dictionary<RenderFeatureSetId, CompiledRenderFeatureSet>();
        foreach ((RenderFeatureSetId id, HashSet<IRenderFeature> features) in combined)
        {
            var builder = new RenderFeatureAssignmentBuilder();
            builder.Register(id, features.ToArray());
            materialized.Add(id, builder.Build()[id]);
        }

        _featureSets = new CompiledRenderFeatureSetRegistry(materialized);
        _assignmentGeneration = checked(_assignmentGeneration + 1);
        _assignmentChanged = true;
    }

    private sealed record PendingCommand(GameSceneCommand Command, GameSceneCommandHandle Handle);
    private sealed record SceneSlot(
        GameSceneId Id,
        IGameScene Scene,
        GameSceneState State,
        GameSceneSlotPolicy Policy,
        IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> Assignments);
}
