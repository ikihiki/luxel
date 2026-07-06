using Luxel.Particles.TwoD;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Particles.UI;

/// <summary>
/// <see cref="ParticleSystem"/> を UI ツリー/ストーリーに載せる widget。<see cref="ParticleNode"/> を
/// キャンバスへ 1 個生成し、<c>animated: true</c> なら毎 Tick (<c>AddAnimation</c>) で
/// <see cref="ParticleSystem.Update"/> + Sync する。パーティクルはウィジェットローカル座標で放出する
/// (親変換で配置)。ゲーム (ECS) からは system を直接回す — こちらは UI 埋め込み用。
/// </summary>
[UiComponent(Factory = "Kit")]
public sealed partial class ParticleView : Widget
{
    /// <summary>描画するパーティクルシステム (放出/設定は呼び出し側が行う)。</summary>
    [UiParam] private readonly Bindable<ParticleSystem> _particles = new();
    /// <summary>表示幅 (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    /// <summary>表示高さ (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _viewHeight = new();
    /// <summary>true = 毎 Tick で Update + Sync (アニメ)、false = 初回のみ描く (静的)。</summary>
    [UiParam] private readonly Bindable<bool> _animated = true;
    /// <summary>円形パーティクルの分割数。</summary>
    [UiParam] private readonly Bindable<int> _circleSegments = 12;

    private ParticleNode? _node;

    private float W1 => MathF.Max(1, ViewWidth.Get());
    private float H1 => MathF.Max(1, ViewHeight.Get());

    public override string? DebugDetail => $"{(int)W1}x{(int)H1} alive={Particles.Get()?.Alive ?? 0}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(W1, H1));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W1;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode root = CreateRoot(ctx, parent, worldOrigin);
        ParticleSystem sys = Particles.Get();
        _node = new ParticleNode(ctx.Canvas, root, sys, CircleSegments.Get());
        _node.Sync();
        if (Animated.Get())
            ctx.AddAnimation(dt =>
            {
                sys.Update(dt);
                _node.Sync();
                return false;   // 継続
            });
    }
}
