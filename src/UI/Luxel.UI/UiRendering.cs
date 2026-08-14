using Luxel.Graphics.TwoD;
using Luxel.Graphics.RenderSystem;

namespace Luxel.UI;

/// <summary>UI surface の合成位置。feature 自身は Set/Cadence を知らず、この役割だけで担当 surface を選ぶ。</summary>
public enum UiSurfaceRole
{
    Content,
    World,
    Present,
}

/// <summary>最後に成功した出力と、次の batch が書く出力を分離して保持する。</summary>
public sealed class PersistentUiOutput<T> : IDisposable where T : class
{
    private readonly Action<T>? _dispose;

    public PersistentUiOutput(Action<T>? dispose = null) => _dispose = dispose;

    public T? Current { get; private set; }
    public T? Pending { get; private set; }

    public void SetCurrent(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(Current, value)) return;
        T? previous = Current;
        Current = value;
        if (previous is not null && !ReferenceEquals(previous, Pending)) _dispose?.Invoke(previous);
    }

    public void Stage(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(Pending, value)) return;
        if (Pending is not null && !ReferenceEquals(Pending, Current)) _dispose?.Invoke(Pending);
        Pending = value;
    }

    /// <summary>成功時だけ pending を current として公開する。失敗時は current を維持する。</summary>
    public bool Complete(bool succeeded)
    {
        if (Pending is null) return false;
        T pending = Pending;
        Pending = null;
        if (!succeeded)
        {
            if (!ReferenceEquals(pending, Current)) _dispose?.Invoke(pending);
            return false;
        }

        T? previous = Current;
        Current = pending;
        if (previous is not null && !ReferenceEquals(previous, pending)) _dispose?.Invoke(previous);
        return true;
    }

    public void Dispose()
    {
        T? pending = Pending;
        T? current = Current;
        Pending = null;
        Current = null;
        if (pending is not null && !ReferenceEquals(pending, current)) _dispose?.Invoke(pending);
        if (current is not null) _dispose?.Invoke(current);
    }
}

/// <summary>
/// 1 枚の retained UI surface。logical Tick と GPU publication を分離し、Flush 後も batch 成功までは dirty を commit しない。
/// </summary>
public sealed class UiSurfaceState : IDisposable
{
    private readonly Action<float> _logicalTick;
    private readonly Func<GpuBuffer>? _createPending;
    private readonly Action<global::Luxel.Graphics.RenderGraph.RenderGraph, GpuBuffer> _addRasterPass;
    private readonly RenderFeatureSetInvalidationSource _invalidation;
    private ulong _observedCanvasGeneration;
    private ulong _publishedCanvasGeneration;
    private ulong _pendingCanvasGeneration;
    private bool _forcedDirty = true;
    private bool _batchPending;
    private bool _disposed;

    public UiSurfaceState(
        string key,
        UiSurfaceRole role,
        RetainedCanvas canvas,
        PersistentUiOutput<GpuBuffer> output,
        RenderFeatureSetInvalidationSource invalidation,
        Action<float> logicalTick,
        Action<global::Luxel.Graphics.RenderGraph.RenderGraph, GpuBuffer> addRasterPass,
        Func<GpuBuffer>? createPending = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Surface key cannot be empty.", nameof(key));
        Key = key;
        Role = role;
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
        _logicalTick = logicalTick ?? throw new ArgumentNullException(nameof(logicalTick));
        _addRasterPass = addRasterPass ?? throw new ArgumentNullException(nameof(addRasterPass));
        _createPending = createPending;
        _observedCanvasGeneration = canvas.ChangeGeneration;
        _invalidation.Invalidate();
    }

    public event Action<GpuBuffer>? Published;

    public string Key { get; }
    public UiSurfaceRole Role { get; }
    public RetainedCanvas Canvas { get; }
    public PersistentUiOutput<GpuBuffer> Output { get; }
    public bool IsDirty => _forcedDirty || Output.Current is null || _publishedCanvasGeneration < Canvas.ChangeGeneration;

    /// <summary>Cadence に関係なく毎 game frame 呼ぶ。retained change は Set invalidation へ一度だけ伝播する。</summary>
    public void Tick(float dt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logicalTick(dt);
        ObserveChanges();
    }

    public void ObserveChanges()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong generation = Canvas.ChangeGeneration;
        if (generation == _observedCanvasGeneration) return;
        _observedCanvasGeneration = generation;
        _invalidation.Invalidate();
    }

    public void ForceRedraw()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _forcedDirty = true;
        _invalidation.Invalidate();
    }

    public void StagePending(GpuBuffer output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Output.Stage(output);
        _forcedDirty = true;
        _invalidation.Invalidate();
    }

    internal bool AddPasses(global::Luxel.Graphics.RenderGraph.RenderGraph graph)
    {
        ObserveChanges();
        if (_batchPending || !IsDirty) return false;
        if (Output.Pending is null)
        {
            if (_createPending is null) return false;
            Output.Stage(_createPending());
        }

        _pendingCanvasGeneration = Canvas.ChangeGeneration;
        _addRasterPass(graph, Output.Pending!);
        _batchPending = true;
        return true;
    }

    internal void CompleteBatch(bool succeeded)
    {
        if (!_batchPending) return;
        bool published = Output.Complete(succeeded);
        if (succeeded && published)
        {
            _publishedCanvasGeneration = Math.Max(_publishedCanvasGeneration, _pendingCanvasGeneration);
            _forcedDirty = false;
            Published?.Invoke(Output.Current!);
        }
        _pendingCanvasGeneration = 0;
        _batchPending = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Output.Dispose();
        _invalidation.Dispose();
    }
}

/// <summary>renderer 単位で keyed UI surfaces を所有し、logical frame を一括して進める。</summary>
public sealed class UiRendererState : IDisposable
{
    private readonly Dictionary<string, UiSurfaceState> _surfaces = [];
    private bool _disposed;

    public UiRendererState(RenderFeatureSetStateRegistry? featureSetStates = null)
        => FeatureSetStates = featureSetStates ?? new RenderFeatureSetStateRegistry();

    public RenderFeatureSetStateRegistry FeatureSetStates { get; }
    public IReadOnlyCollection<UiSurfaceState> Surfaces => _surfaces.Values;

    public RenderFeatureSetInvalidationSource CreateInvalidationSource(UiSurfaceRole role)
        => new(role switch
        {
            UiSurfaceRole.Content => RenderFeatureSets.UiContent,
            UiSurfaceRole.World => RenderFeatureSets.WorldUi,
            UiSurfaceRole.Present => RenderFeatureSets.PresentUi,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        }, FeatureSetStates);

    public void Add(UiSurfaceState surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (!_surfaces.TryAdd(surface.Key, surface))
            throw new InvalidOperationException($"UI surface '{surface.Key}' is already registered.");
    }

    public bool Remove(string key, bool dispose = true)
    {
        if (!_surfaces.Remove(key, out UiSurfaceState? surface)) return false;
        if (dispose) surface.Dispose();
        return true;
    }

    public void Tick(float dt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (UiSurfaceState surface in _surfaces.Values.ToArray()) surface.Tick(dt);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (UiSurfaceState surface in _surfaces.Values) surface.Dispose();
        _surfaces.Clear();
    }
}

public abstract class UiRenderFeature(UiRendererState rendererState, UiSurfaceRole role) : IRenderFeature, IRenderFeatureBatchObserver
{
    private readonly List<UiSurfaceState> _batch = [];

    public void AddPasses(RenderFeatureContext context)
    {
        _batch.Clear();
        foreach (UiSurfaceState surface in rendererState.Surfaces)
            if (surface.Role == role && surface.AddPasses(context.Graph))
                _batch.Add(surface);
    }

    public void CompleteBatch(bool succeeded)
    {
        foreach (UiSurfaceState surface in _batch) surface.CompleteBatch(succeeded);
        _batch.Clear();
    }
}

public sealed class UiContentRenderFeature(UiRendererState state) : UiRenderFeature(state, UiSurfaceRole.Content);
public sealed class WorldUiRenderFeature(UiRendererState state) : UiRenderFeature(state, UiSurfaceRole.World);
public sealed class PresentUiRenderFeature(UiRendererState state) : UiRenderFeature(state, UiSurfaceRole.Present);
