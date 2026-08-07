using Luxel.UI;
using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>
/// 既存の stateful Gallery renderer を GpuView の lambda callback へ接続する内部 helper。
/// GpuView が color target / framebuffer を所有し、この型は scene 固有 resource だけを追跡する。
/// </summary>
internal abstract class GpuSceneBase : IDisposable
{
    protected GpuDevice Device { get; private set; } = null!;
    protected GpuTexture Target => Surface.ColorTarget;
    protected GpuBuffer OutBuffer => Surface.Framebuffer;
    protected uint W => Surface.Width;
    protected uint H => Surface.Height;
    protected uint StridePixels => Surface.StridePixels;
    protected GpuViewSurface Surface { get; private set; } = null!;

    private readonly List<IDisposable> _resources = [];
    private GpuViewSurface? _generation;
    private bool _rendered;

    internal static Widget View(float width, float height, GpuSceneBase scene, bool animated = true)
        => Luxel.Controls.Kit.GpuView(width, height,
            (device, surface, time) => scene.Render(device, surface, time),
            animated: animated, dispose: scene.Dispose);

    protected T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    internal GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
    {
        if (!ReferenceEquals(_generation, surface))
        {
            DisposeResources();
            Device = device;
            Surface = surface;
            _generation = surface;
            _rendered = false;
            OnInit();
        }
        if (RenderEveryFrame || !_rendered)
        {
            _rendered = true;
            OnRender(time);
        }
        return GpuViewRenderResult.Ready;
    }

    protected abstract void OnInit();
    protected abstract void OnRender(float time);
    protected virtual bool RenderEveryFrame => false;

    public void Dispose()
    {
        DisposeResources();
        _generation = null;
    }

    private void DisposeResources()
    {
        for (int i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
        _resources.Clear();
    }
}
