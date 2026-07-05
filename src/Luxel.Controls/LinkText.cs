using Luxel.Animation;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// リンク風のクリック可能テキスト (背景・余白なし、左寄せ)。ナビゲーション項目
/// (サイドバー/ツリー/パンくず) 向け — ボタンの中央寄せ+padding が要らない場面で使う。
/// 通常は控えめな色、hover で本文色へフェード、<see cref="Active"/> でアクセント色 (選択中表示)。
/// </summary>
[UiComponent]
public sealed partial class LinkText : Widget
{
    /// <summary>クリック (EV: 第一引数は発火元の LinkText 自身)。</summary>
    [UiEvent] public UiEvent<LinkText> OnClick;

    [UiParam] private readonly BindableString _text = new();
    /// <summary>文字サイズ。未設定 → テーマ FontSm。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();
    /// <summary>選択中/現在地の強調 (色がアクセントになる)。</summary>
    [UiParam] private readonly Bindable<bool> _active = new();
    /// <summary>通常色。未設定 → Active ? Primary : TextMuted。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _color = new();
    /// <summary>hover 色。未設定 → Active ? Primary : Text。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _hoverColor = new();

    public override string? DebugDetail => Text.Get();

    private float Fs(Theme t) => FontSize.Or(t.FontSm);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        (float tw, float th) = ctx.Font.Measure(Text.Get(), Fs(ctx.Theme));
        float w = ResolveW(c, ctx, tw);
        float h = ResolveH(c, ctx, th);
        Size = c.Constrain(new Size(w, h));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => ctx.Font.Measure(Text.Get(), Fs(ctx.Theme)).width;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        Signal<Theme> theme = ctx.Theme;
        float fontSize = Fs(theme.Peek());
        var ts = new Scene2D();
        ctx.Font.AppendText(ts, Text.Get(), 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        node.Content = ts;

        // hover 80ms フェード (AS-M3)。Active は即時 (選択の手応えを遅らせない)
        UiStates hover = ctx.States(new TransitionTable().Default(new TransitionSpec(0.08f)))
            .AddState("normal", ("t", 0f))
            .AddState("hover", ("t", 1f));
        hover.Start(Hovered.Peek() ? "hover" : "normal");
        ctx.Effect(() => hover.Goto(Hovered.Value ? "hover" : "normal"));
        ctx.Effect(() =>
        {
            Theme t = theme.Value;
            bool active = Active.Get();
            uint baseCol = Color.Or(active ? t.Primary : t.TextMuted);
            uint hovCol = HoverColor.Or(active ? t.Primary : t.Text);
            node.Color = new RgbaTween(baseCol, hovCol).Lerp(hover.Float("t"));
        });

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            onClick: () => OnClick.Invoke(this), onHover: h => Hovered.Value = h,
            cursor: CursorKind.Hand);
    }
}
