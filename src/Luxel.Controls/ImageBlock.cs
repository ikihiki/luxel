using Luxel.Document;
using Luxel.Resources;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 画像埋め込みブロックの標準 widget。**画像の取得/デコード/キャッシュ/寿命は Resource システム**
/// (<see cref="ResourceSystem.Load{T}(string)"/> — RefCount 管理、同一 URI は共有) に委ね、この widget は
/// ハンドルの表示だけを持つ。ロード完了は毎 Tick のポーリングで検知し (継続はプールスレッドのため)、
/// <see cref="Widget.MarkNeedsRealize"/> で実寸へ再実体化する — サイズが変わるので親 (エディタ) が
/// **同一インスタンスのまま**再ホストし、factory 再生成もハンドル再取得も起きない。
/// 幅 = 使える幅と実寸の小さい方 (等比)。ロード中/失敗は alt テキスト付きプレースホルダ。
/// 登録: <c>widgets.Register("image", ImageBlocks.Factory(resources))</c>。
/// </summary>
[UiComponent]
public sealed partial class ImageBlock : Widget, IDisposable
{
    private readonly ImagePayload _payload;
    private readonly ResourceHandle<CpuImage> _handle;
    private readonly float _maxW;
    private GpuBuffer? _buf;

    [UiCtor]
    internal ImageBlock(ImagePayload payload, ResourceSystem resources, float maxWidth)
    {
        _payload = payload;
        _maxW = MathF.Max(40, maxWidth);
        _handle = resources.Load<CpuImage>(payload.Src);
    }

    public override string? DebugDetail => $"{_payload.Src} ({_handle.Status})";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        if (_handle.IsReady && _handle.Value is CpuImage img && img.Width > 0)
        {
            float w = MathF.Min(_maxW, img.Width);
            Size = c.Constrain(new Size(w, w * img.Height / img.Width));   // 等比
        }
        else
        {
            Size = c.Constrain(new Size(_maxW, 56));   // プレースホルダ帯
        }
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        if (_handle.IsReady && _handle.Value is CpuImage img && img.Width > 0)
        {
            GpuDevice device = ctx.Canvas.Rasterizer.Device;
            _buf?.Dispose();
            _buf = device.Malloc((ulong)(img.Width * img.Height * 4), GpuMemoryKind.HostMapped);
            img.Pixels.AsSpan(0, img.Width * img.Height * 4).CopyTo(_buf.Span<byte>(img.Width * img.Height * 4));
            node.Content = new Scene2D().ImageRect(
                _buf.BindlessIndex, (uint)img.Width, (uint)img.Width, (uint)img.Height, 0, 0, Size.Width, Size.Height);
            return;
        }

        // ロード中/失敗: 枠 + alt (失敗はミュートでなく Danger 寄りにしたいがテーマ節約で Muted)
        var s = new Scene2D();
        s.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, 6);
        node.Content = s;
        ctx.Effect(() => node.Color = ctx.Theme.Value.SurfaceAlt);

        UiNode label = ctx.Canvas.AddChild(node);
        var ls = new Scene2D();
        string text = _handle.Status == ResourceStatus.Failed
            ? $"×  {(_payload.Alt.Length > 0 ? _payload.Alt : _payload.Src)}"
            : $"…  {(_payload.Alt.Length > 0 ? _payload.Alt : _payload.Src)}";
        ctx.Font.AppendText(ls, text, 10, Size.Height / 2 + ctx.Font.Ascent(13) / 2 - 2, 13, Color2D.White);
        label.Content = ls;
        ctx.Effect(() => label.Color = ctx.Theme.Value.TextMuted);

        // ロード完了/失敗の検知 (継続はプールスレッド → UI スレッドの Tick でポーリング)
        if (_handle.Status == ResourceStatus.Loading)
        {
            ctx.AddAnimation(_ =>
            {
                if (_handle.Status == ResourceStatus.Loading) return false;
                MarkNeedsRealize();   // 実寸へ再実体化 (同一インスタンス。アニメーションはスコープごと破棄)
                return true;
            });
        }
    }

    public void Dispose()
    {
        _handle.Dispose();   // RefCount-- (Resource システムがキャッシュ/解放を管理)
        _buf?.Dispose();
        _buf = null;
    }
}

/// <summary>画像 embed の標準ファクトリ。フォーマット構成側が registry へ登録する。</summary>
public static class ImageBlocks
{
    public static BlockWidgetFactory Factory(ResourceSystem resources)
        => bc => Kit.ImageBlock((ImagePayload)bc.Payload, resources, bc.MaxWidth);
}
