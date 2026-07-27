using Luxel.Animation;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// オン/オフ トグル。状態=signal、つまみは transform スライド (部分更新)。Space/Enter で切替。
/// <para>スタイルは [UiParam] な <see cref="Bindable{T}"/> フィールド。Tailwind の <c>checked:</c> は
/// <c>On(WidgetState.Checked, Bg(...))</c>。未設定はテーマ (on ? Primary : SurfaceAlt) にフォールバック。</para>
/// </summary>
[UiComponent]
public sealed partial class Switch : Widget
{
    /// <summary>オン/オフ状態の signal (クリック/Space/Enter で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<bool>> _on = new();

    private const float W = 46, H = 26, Pad = 3;

    /// <summary>トラック色。未設定 → on ? Primary : SurfaceAlt。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _background = new();
    /// <summary>つまみ色。未設定 → テーマ OnAccent。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _knobColor = new();
    [UiParam(Stateable = true)] private readonly Bindable<float> _opacity = 1f;

    public override bool IsStateActive(WidgetState state)
        => state == WidgetState.Checked ? On.Get().Value : base.IsStateActive(state);

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(W, H));
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        Signal<bool> on = On.Get();
        FocusRing.Add(ctx, node, -3, -3, W + 6, H + 6, (H + 6) / 2, Focused);

        var track = new Scene2D();
        track.FillRoundedRect(Color2D.White, 0, 0, W, H, H / 2);
        node.Content = track;

        UiNode knob = ctx.Canvas.AddChild(node);
        knob.Z = 1;
        float r = (H - 2 * Pad) / 2;
        var ks = new Scene2D();
        ks.FillCircle(Color2D.White, Pad + r, H / 2, r);
        knob.Content = ks;
        // つまみスライド + トラック色クロスフェード — on/off の状態遷移 (AS-M3: 状態機械へ統一)
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.14f)))
            .AddState("off", ("t", 0f))
            .AddState("on", ("t", 1f));
        st.Start(on.Peek() ? "on" : "off");
        ctx.Effect(() => st.Goto(on.Value ? "on" : "off"));
        ctx.Effect(() => knob.Transform = Affine2D.Translate(st.Float("t") * (W - H), 0));   // スライド=transform

        uint currentBg = Color2D.White, currentKnob = Color2D.White;
        float currentOpacity = 1f;

        void ApplyAll()
        {
            node.Color = StyleApply.MultiplyAlpha(currentBg, currentOpacity);
            knob.Color = StyleApply.MultiplyAlpha(currentKnob, currentOpacity);
        }

        Action<uint> bgSet = WrapSetter<uint>("Background", v => { currentBg = v; ApplyAll(); });
        Action<uint> knSet = WrapSetter<uint>("KnobColor", v => { currentKnob = v; ApplyAll(); });
        Action<float> opSet = WrapSetter<float>("Opacity", v => { currentOpacity = v; ApplyAll(); });

        ctx.Effect(() =>
        {
            // トラック色はスライド量に同期したクロスフェード (明示 Background 指定時は従来どおり)
            bgSet(Background.Or(new RgbaTween(ctx.Theme.Value.SurfaceAlt, ctx.Theme.Value.Primary).Lerp(st.Float("t"))));
            knSet(KnobColor.Or(ctx.Theme.Value.OnAccent));
            opSet(Opacity.Get());
        });

        void Toggle() => on.Value = !on.Value;
        ctx.AddHit(node, new Rect(0, 0, W, H), onClick: Toggle, onHover: h => Hovered.Value = h);
        ctx.AddFocusable(onFocus: f => Focused.Value = f,
            onKey: ev => { if (ev.Key is Key.Space or Key.Enter) { Toggle(); return true; } return false; });
    }
}

/// <summary>フォーカスリング (コントロールより少し大きい角丸の**アウトライン**、opacity を focus signal に束縛)。
/// 塗りつぶしではなくストロークで描く — 保持型ツリーの描画順は「親自身の Content → 子 (Z 順)」なので、
/// 子ノードの塗り角丸 (旧実装, Z=-1) は root 自身に載った背景より必ず後に描かれ、
/// コントロール全面を覆ってしまう。輪郭なら描画順に依存しない。</summary>
internal static class FocusRing
{
    public static void Add(UiBuildContext ctx, UiNode parent, float x, float y, float w, float h, float r, Signal<bool> focused)
    {
        UiNode ring = ctx.Canvas.AddChild(parent);
        ring.Z = -1;   // 輪郭はコントロール矩形の外側なので Z は実質無関係 (慣例で背面)
        var s = new Scene2D();
        s.StrokeRoundedRect(Color2D.White, 2f, x, y, w, h, r);
        ring.Content = s;
        ctx.Effect(() => ring.Color = ctx.Theme.Value.Primary);
        // フェード — focus/blur の状態遷移 (AS-M3)。100ms 以下 = 状態強制の snap warmup 内に静定
        UiStates st = ctx.States(new TransitionTable().Default(new TransitionSpec(0.10f)))
            .AddState("blur", ("o", 0f))
            .AddState("focus", ("o", 0.85f));
        st.Start(focused.Peek() ? "focus" : "blur");
        ctx.Effect(() => st.Goto(focused.Value ? "focus" : "blur"));
        ctx.Effect(() => ring.Opacity = st.Float("o"));
    }
}
