using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>GpuView が所有する offscreen 描画先。callback は破棄せず、realize 中だけ使用する。</summary>
public sealed class GpuViewSurface : IDisposable
{
    internal GpuViewSurface(GpuDevice device, uint width, uint height)
    {
        Width = width;
        Height = height;
        StridePixels = (width + 63) / 64 * 64;
        ColorTarget = device.CreateRenderTarget(width, height, GpuFormat.Rgba8Unorm);
        Framebuffer = device.Malloc(checked((ulong)StridePixels * height * 4), GpuMemoryKind.DeviceLocal);
    }

    public uint Width { get; }
    public uint Height { get; }
    public uint StridePixels { get; }
    public GpuTexture ColorTarget { get; }
    public GpuBuffer Framebuffer { get; }

    /// <summary>カラー出力を合成用 framebuffer へ遷移・コピーする。</summary>
    public void CopyColorToFramebuffer(GpuCommandBuffer command)
        => command.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(ColorTarget, Framebuffer, StridePixels);

    public void Dispose()
    {
        Framebuffer.Dispose();
        ColorTarget.Dispose();
    }
}

/// <summary>GpuView callbackの表示結果。Ready以外ではframebufferの代わりに状態アイコンを表示する。</summary>
public enum GpuViewRenderResult
{
    Ready = 0,
    Loading,
    Failed,
}

/// <summary>GpuView の1フレームを記録・submitし、表示可能状態を返す callback。</summary>
public delegate GpuViewRenderResult GpuViewRender(GpuDevice device, GpuViewSurface surface, float time);

/// <summary>
/// GPU callback の描画結果を表示する widget。GpuView が RGBA8 color target と最終 framebuffer を用意し、
/// callback へ device / surface / Tick 由来の累積秒を渡す。callback は必要な command を submitして返す。
/// </summary>
[UiComponent]
public sealed partial class GpuView : Widget
{
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    [UiParam] private readonly Bindable<float> _viewHeight = new();
    [UiParam] private readonly Bindable<GpuViewRender> _render = new();
    [UiParam] private readonly Bindable<bool> _animated = true;
    [UiParam] private readonly Bindable<Action?> _dispose = new((Action?)null);

    private float _t;
    private UiNode? _node;
    private GpuViewRenderResult _renderResult = GpuViewRenderResult.Loading;

    private float W1 => MathF.Max(1, ViewWidth.Get());
    private float H1 => MathF.Max(1, ViewHeight.Get());

    public override string? DebugDetail => $"{(int)W1}x{(int)H1}{(Animated.Get() ? " animated" : "")} {_renderResult}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(W1, H1));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W1;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        float w = W1, h = H1;
        _node = CreateRoot(ctx, parent, worldOrigin);
        GpuDevice device = ctx.RequireGpuRasterizer().Device;
        var surface = new GpuViewSurface(device, (uint)w, (uint)h);
        GpuViewRender render = Render.Get();
        Action? dispose = Dispose.Get();
        var lease = new RenderLease(surface, dispose);
        ctx.Own(lease);

        UiNode image = ctx.Canvas.AddChild(_node);
        image.Content = new Scene2D().ImageRect(
            surface.Framebuffer.BindlessIndex, surface.StridePixels, surface.Width, surface.Height,
            0, 0, Size.Width, Size.Height);
        UiNode fallback = ctx.Canvas.AddChild(_node);
        var fallbackScene = new Scene2D();
        fallbackScene.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, 6);
        fallback.Content = fallbackScene;
        UiNode icon = ctx.Canvas.AddChild(fallback);
        const float iconSize = 28;
        icon.Transform = Affine2D.Translate((Size.Width - iconSize) / 2, (Size.Height - iconSize) / 2);
        var status = new Signal<GpuViewRenderResult>(GpuViewRenderResult.Loading);
        ctx.Effect(() =>
        {
            GpuViewRenderResult result = status.Value;
            bool ready = result == GpuViewRenderResult.Ready;
            image.Visible = ready;
            fallback.Visible = !ready;
            fallback.Color = ctx.Theme.Value.SurfaceAlt;
            icon.Color = result == GpuViewRenderResult.Failed
                ? ctx.Theme.Value.Danger
                : ctx.Theme.Value.TextMuted;
            var scene = new Scene2D();
            Icon.Draw(scene,
                result == GpuViewRenderResult.Failed ? IconKind.Failed : IconKind.Loading,
                iconSize, 2);
            icon.Content = scene;
        });

        if (Animated.Get())
        {
            Draw();
            ctx.AddAnimation(dt =>
            {
                _t += dt;
                Draw();
                return false;
            });
        }
        else
        {
            // Render callbacks may read resource-backed signals. The realization scope owns this
            // effect, so a ready/reload notification redraws exactly this surface and is detached
            // when the widget is re-realized or removed.
            ctx.Effect(Draw);
        }

        void Draw()
        {
            GpuViewRenderResult result;
            try { result = render(device, surface, _t); }
            catch (Exception error)
            {
                UiError.Report(error, "GpuView.Render");
                result = GpuViewRenderResult.Failed;
            }
            _renderResult = result;
            status.Value = result;
            if (result == GpuViewRenderResult.Ready) image.Touch();
        }
    }

    private sealed class RenderLease(GpuViewSurface surface, Action? dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            dispose?.Invoke();
            surface.Dispose();
        }
    }
}
