using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI.Styling;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 角丸背景 + 中央テキスト + クリック。配色は Theme × Variant × Intent × 状態(hover/press/disabled) から
/// 解決し、hover/テーマ変更は recolor のみで部分更新。
/// <para>スタイルはすべて [UiParam] な <see cref="Bindable{T}"/> フィールド。状態別の上書きは
/// 生成された <c>.When(WidgetState.Hover, background: ...)</c> が <see cref="Bindable{T}.SetState"/> で積む。
/// 未設定のプロパティはテーマ解決値 (Variant/Intent) にフォールバックする。</para>
/// </summary>
[UiComponent]
public sealed partial class Button : Widget
{
    /// <summary>クリック (EV: 第一引数は発火元の Button 自身)。</summary>
    [UiEvent] public UiEvent<Button> OnClick;

    [UiParam] public readonly BindableString Text = new();
    /// <summary>背景色。未設定 → テーマ (Variant × Intent × 状態)。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Background = new();
    /// <summary>文字色。未設定 → テーマ。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Foreground = new();
    [UiParam(Stateable = true)] public readonly Bindable<float> Opacity = new();
    [UiParam(Stateable = true)] public readonly Bindable<float> Scale = new();
    /// <summary>角丸。未設定 → テーマ Radius。</summary>
    [UiParam] public readonly Bindable<float> Rounded = new();
    /// <summary>余白。未設定 → テーマ (BtnPadX, BtnPadY)。</summary>
    [UiParam] public readonly Bindable<Thickness> Padding = new();
    /// <summary>文字サイズ。未設定 → テーマ Font。</summary>
    [UiParam] public readonly Bindable<float> FontSize = new();

    private float Fs(Theme t) => FontSize.Or(t.Font);
    private Thickness Pad(Theme t) => Padding.Or(new Thickness(t.BtnPadX, t.BtnPadY));
    [UiParam] public readonly Bindable<Variant> Variant = new();
    [UiParam] public readonly Bindable<Intent> Intent = new();


    internal Button() { }

    public override string? DebugDetail => Text.Get();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float fontSize = Fs(ctx.Theme);
        (float tw, float th) = ctx.Font.Measure(Text.Get(), fontSize);
        Thickness pad = Pad(ctx.Theme);
        float w = ResolveW(c, ctx, tw + pad.Horizontal);
        float h = ResolveH(c, ctx, th + pad.Vertical);
        Size = c.Constrain(new Size(w, h));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => ctx.Font.Measure(Text.Get(), Fs(ctx.Theme)).width + Pad(ctx.Theme).Horizontal;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin, out TransformHandle tf);
        Point world = WorldPos;

        Signal<Theme> theme = ctx.Theme;
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, Rounded.Or(theme.Peek().Radius));
        node.Content = bg;

        UiNode tnode = ctx.Canvas.AddChild(node);
        float fontSize = Fs(theme.Peek());
        (float tw, float th) = ctx.Font.Measure(Text.Get(), fontSize);
        tnode.Transform = Affine2D.Translate((Size.Width - tw) / 2, (Size.Height - th) / 2);
        tnode.Z = 1;
        float ascent = ctx.Font.Ascent(fontSize);
        var ts = new Scene2D();
        ctx.Font.AppendText(ts, Text.Get(), 0, ascent, fontSize, Color2D.White);
        tnode.Content = ts;

        // 配色: 状態変化 (signal) / テーマ変化で recolor を部分更新。setter は WrapSetter 経由 —
        // トランジション配線があれば自動補間される。transform 成分 (TF) は CreateRoot が配線済みで、
        // 一様 Scale はその handle へ合成する。
        uint currentBg = Color2D.White, currentFg = Color2D.White;
        float currentOpacity = 1f;

        void ApplyAll()
        {
            node.Color = StyleApply.MultiplyAlpha(currentBg, currentOpacity);
            tnode.Color = StyleApply.MultiplyAlpha(currentFg, currentOpacity);
        }

        Action<uint> bgSet = WrapSetter<uint>("Background", v => { currentBg = v; ApplyAll(); });
        Action<uint> fgSet = WrapSetter<uint>("Foreground", v => { currentFg = v; ApplyAll(); });
        Action<float> scSet = WrapSetter<float>("Scale", tf.SetExtraScale);
        Action<float> opSet = WrapSetter<float>("Opacity", v => { currentOpacity = v; ApplyAll(); });

        // hover は 80ms フェードの状態遷移 (AS-M3)。押下/無効は即時 (押した手応えは遅らせない)
        UiStates hover = ctx.States(new TransitionTable().Default(new TransitionSpec(0.08f)))
            .AddState("normal", ("t", 0f))
            .AddState("hover", ("t", 1f));
        hover.Start(Hovered.Peek() ? "hover" : "normal");
        ctx.Effect(() => hover.Goto(Hovered.Value && Enabled && !Pressed.Value ? "hover" : "normal"));
        ctx.Effect(() =>
        {
            Theme t = theme.Value;
            ControlState st = !Enabled ? ControlState.Disabled
                            : Pressed.Value ? ControlState.Active : ControlState.Normal;
            VisualStyle vs = Styles.Resolve(t, Variant.Get(), Intent.Get(), st);
            float k = hover.Float("t");
            if (st == ControlState.Normal && k > 0f)
            {
                VisualStyle hv = Styles.Resolve(t, Variant.Get(), Intent.Get(), ControlState.Hover);
                vs = vs with { Bg = new RgbaTween(vs.Bg, hv.Bg).Lerp(k),
                               Fg = new RgbaTween(vs.Fg, hv.Fg).Lerp(k) };
            }
            bgSet(Background.Or(vs.Bg));
            fgSet(Foreground.Or(vs.Fg));
            scSet(Scale.Or(1f));
            opSet(Opacity.Or(1f));
        });

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            onClick: () => { if (Enabled) OnClick.Invoke(this); }, onHover: h => Hovered.Value = h);
    }
}
