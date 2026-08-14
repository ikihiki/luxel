using Luxel.Framework.Game;
using Luxel.Graphics.RenderSystem;

namespace Luxel.Gallery.Stories;

/// <summary>Common scene lifecycle and render-feature wiring for embedded gallery applications.</summary>
public abstract class StorySceneBase : IGameScene, IStoryApp
{
    private readonly IRenderFeature _renderFeature;
    private long _version;
    private long _seen;

    protected StorySceneBase(GpuDevice device)
    {
        Device = device;
        _renderFeature = new SceneRenderFeature(this);
    }

    protected GpuDevice Device { get; }
    protected void MarkRendered() => _version++;

    public abstract uint FbIndex { get; }
    public bool FbReady => _version > 0;

    public bool ConsumeRendered()
    {
        if (_seen == _version) return false;
        _seen = _version;
        return true;
    }

    public virtual ValueTask LoadAsync(GameSceneContext context, CancellationToken token)
        => ValueTask.CompletedTask;

    public void ConfigureRendering(
        RenderFeatureSetCatalog featureSets,
        RenderFeatureAssignmentBuilder assignments)
        => assignments.Register(RenderFeatureSets.RenderOutput, _renderFeature);

    public virtual void FixedUpdate(in FixedUpdateContext context) { }
    public abstract void Update(in UpdateContext context);

    public ValueTask UnloadAsync(GameSceneContext context, CancellationToken token)
        => DisposeAsync();

    protected abstract void AddRenderPasses(RenderFeatureContext context);
    protected abstract ValueTask DisposeAsync();

    public abstract void PointerMove(float x, float y);
    public abstract void PointerDown(float x, float y);
    public abstract void PointerUp(float x, float y);
    public abstract void Wheel(float x, float y, float delta);

    private sealed class SceneRenderFeature(StorySceneBase owner) : IRenderFeature
    {
        public void AddPasses(RenderFeatureContext context) => owner.AddRenderPasses(context);
    }
}
