using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Knob の全型 (bool/int/float/string/color/enum/length) を 1 画面で見せるストーリー。
/// 右パネルの Knobs テーブルから編集すると即座に反映される (KT/KT2 の実演 + E2E 用)。</summary>
public static class KnobStories
{
    [Story("Knobs/Kinds", Height = 240, Order = 2000)]
    public static Widget Kinds(StoryContext ctx)
    {
        Signal<bool> visible = ctx.Signal("visible", true, "チップの表示 (false で淡色化)");
        Signal<int> count = ctx.Signal("count", 3, "カウント表示の値");
        Signal<float> opacity = ctx.Signal("opacity", 1f, "チップの不透明度 (0-1)");
        Signal<string> label = ctx.Signal("label", "chip", "チップのラベル");
        Signal<uint> color = ctx.Signal("color", Tw.Blue500, "チップの色");
        Signal<Align> align = ctx.Signal("align", Align.Start, "サマリの水平位置 (enum)");
        Signal<Length> width = ctx.Signal("width", new Length(320, LengthUnit.Px), "サマリ枠の幅 (length)");

        // enum/length/int/string は値をテキストへ焼き直して反映を見せる (getter = リアクティブ)。
        // bool/float/color はチップの見た目 (色/不透明度) に直結
        Func<string> summary = () =>
            $"count={count.Value} align={align.Value} width={width.Value} label=\"{label.Value}\"";
        Func<string> chipLabel = () => visible.Value ? label.Value : $"{label.Value} (hidden)";
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))[
            VStack(12)[
                Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8,
                       padding: new Thickness(10, 6), width: 340)[
                    Text(summary, 12, color: Bind.From(() => UiTheme.T.Text))],
                HStack(8)[
                    Box(background: Bind.From(() => Styles.WithAlpha(color.Value,
                            (byte)(Math.Clamp(visible.Value ? opacity.Value : 0.15f, 0f, 1f) * 255))),
                        rounded: 8, width: 120, height: 40),
                    Text(chipLabel, 13,
                         color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 12, 0, 0))]]];
    }
}
