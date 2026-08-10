using Luxel.Resources;
using Luxel.Graphics.TwoD;
using Luxel.UI;

using Luxel.Typography.TwoD;
namespace Luxel.Controls;

/// <summary>
/// 画像埋め込みブロックの標準 widget。**画像の取得/デコード/キャッシュ/寿命は Resource システム**
/// (<see cref="ResourceSystem.Load{T}(string)"/> — RefCount 管理、同一 URI は共有) に委ね、この widget は
/// ハンドルの表示だけを持つ。ロード状態は <see cref="ResourceSystem.Pump"/> から UI signal へ反映し、
/// <see cref="Widget.MarkNeedsRealize"/> で実寸へ再実体化する — サイズが変わるので親 (エディタ) が
/// **同一インスタンスのまま**再ホストし、factory 再生成もハンドル再取得も起きない。
/// 幅 = 使える幅と実寸の小さい方 (等比)。ロード中/失敗は alt テキスト付きプレースホルダ。
/// 登録: <c>widgets.Register("image", ImageBlocks.Factory(resources))</c>。
/// </summary>
[UiComponent]
public sealed partial class ImageBlock : Widget, IDisposable
{
    /// <summary>画像 embed の payload (Src/Alt)。</summary>
    [UiParam] private readonly Bindable<ImagePayload> _payload = new();
    /// <summary>画像の取得/デコード/キャッシュを担う Resource システム。</summary>
    [UiParam] private readonly Bindable<ResourceSystem> _resources = new();
    /// <summary>使える最大幅 (最小 40)。実寸との小さい方で等比表示。</summary>
    [UiParam] private readonly Bindable<float> _maxWidth = new();

    private ResourceHandle<CpuImage>? _handle;
    private Signal<ResourceState>? _state;
    private IDisposable? _stateSubscription;
    private GpuBuffer? _buf;

    // ハンドルは初回参照で取得 (パラメータは構築後に確定するため遅延)
    private ResourceHandle<CpuImage> Handle
    {
        get
        {
            if (_handle is not null) return _handle;
            _handle = Resources.Get().Load<CpuImage>(Payload.Get().Src);
            _state = new Signal<ResourceState>(_handle.State);
            _stateSubscription = _handle.SubscribeState(state => _state.Value = state);
            return _handle;
        }
    }
    private ResourceState State { get { _ = Handle; return _state!.Peek(); } }
    private ImagePayload Pl => Payload.Get();
    private float MaxW => MathF.Max(40, MaxWidth.Get());

    public override string? DebugDetail => $"{Pl.Src} ({Handle.Status})";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        if (State.HasValue && Handle.Value is CpuImage img && img.Width > 0)
        {
            float w = MathF.Min(MaxW, img.Width);
            Size = c.Constrain(new Size(w, w * img.Height / img.Width));   // 等比
        }
        else
        {
            Size = c.Constrain(new Size(MaxW, 56));   // プレースホルダ帯
        }
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        _ = Handle;
        bool firstStateRun = true;
        ctx.Effect(() =>
        {
            _ = _state!.Value;
            if (firstStateRun) firstStateRun = false;
            else MarkNeedsRealize();
        });

        if (State.HasValue && Handle.Value is CpuImage img && img.Width > 0)
        {
            GpuDevice device = ctx.RequireGpuRasterizer().Device;
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
        string text = State.Status == ResourceStatus.Failed
            ? $"×  {(Pl.Alt.Length > 0 ? Pl.Alt : Pl.Src)}"
            : $"…  {(Pl.Alt.Length > 0 ? Pl.Alt : Pl.Src)}";
        ctx.Font.AppendText(ls, text, 10, Size.Height / 2 + ctx.Font.Ascent(13) / 2 - 2, 13, Color2D.White);
        label.Content = ls;
        ctx.Effect(() => label.Color = ctx.Theme.Value.TextMuted);

    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _state = null;
        _handle?.Dispose();   // RefCount-- (Resource システムがキャッシュ/解放を管理)
        _handle = null;
        _buf?.Dispose();
        _buf = null;
    }
}
