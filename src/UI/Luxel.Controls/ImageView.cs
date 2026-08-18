using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// CPU の RGBA8 画像を表示する (image プリミティブの CPU ソース版)。
/// <see cref="SetPixels"/> でいつでも差し替え可能 — 同サイズなら bindless バッファへの書込 + dirty のみ
/// (構造変更なし)、サイズが変わったときだけバッファ再確保 + Invalidate。
/// DevTools のフレームプレビュー等、毎フレーム更新される CPU 画像向け。表示は widget サイズへ nearest 拡縮。
/// </summary>
[UiComponent]
public sealed partial class ImageView : Widget, IDisposable
{
    /// <summary>表示幅 (px、最小 1)。ソース寸法とは独立 — 表示は nearest 拡縮。</summary>
    [UiParam] private readonly Bindable<float> _width = new();
    /// <summary>表示高 (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _height = new();

    private GpuDevice? _device;
    private readonly RgbaImagePresenter _image = new();
    private byte[]? _pending;   // 実体化前に SetPixels された分
    private int _pendingW, _pendingH;

    private UiBuildContext? _ctx;
    private UiNode? _node;

    // 旧 ctor のクランプは読み出し側で適用
    private float W => MathF.Max(1, Width.Get());
    private float H => MathF.Max(1, Height.Get());

    public override string? DebugDetail => _image.Width > 0 ? $"{_image.Width}x{_image.Height}" : "(no image)";

    /// <summary>画像を差し替える (実体化前後どちらでも可)。ソース寸法は自由 — 表示は widget サイズへ拡縮。</summary>
    public void SetPixels(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4) return;
        if (_ctx is null || _device is null)
        {
            _pending = rgba[..(width * height * 4)].ToArray();
            _pendingW = width; _pendingH = height;
            return;
        }
        bool resized = _image.Update(_device, width, height, rgba);
        if (resized)
        {
            _node!.Content = MakeContent();    // ソース index/寸法が変わった → content 部分更新
        }
        else
        {
            _node!.Touch();                    // 中身だけ変わった → 再合成を促す (style dirty)
        }
    }

    private Scene2D MakeContent() => _image.CreateScene(Size.Width, Size.Height);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(W, H));
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _device = ctx.RequireGpuRasterizer().Device;
        _node = CreateRoot(ctx, parent, worldOrigin);
        _node.Content = MakeContent();

        if (_pending is not null)
        {
            byte[] p = _pending; _pending = null;
            SetPixels(_pendingW, _pendingH, p);
        }
    }

    public void Dispose()
    {
        _image.Dispose();
    }
}
