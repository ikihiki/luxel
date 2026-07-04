using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using S = Luxel.UI.Tailwind.S;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>入力/選択系コントロールのストーリー。ctx.Signal(...) は自動で knob になる。</summary>
public static class InputControlStories
{
    // ---- Button ----

    [Story("Button/Primary", Height = 160)]
    public static Widget ButtonPrimary() => Frame(Button(_ => { }, "Click me"));

    [Story("Button/Variants", Height = 160)]
    public static Widget ButtonVariants() => Frame(HStack(8)[
        Button(_ => { }, "Filled"),
        Button(_ => { }, "Tonal", variant: Variant.Tonal),
        Button(_ => { }, "Outline", variant: Variant.Outline),
        Button(_ => { }, "Ghost", variant: Variant.Ghost)]);

    [Story("Button/Intents", Height = 160)]
    public static Widget ButtonIntents() => Frame(HStack(8)[
        Button(_ => { }, "Primary"),
        Button(_ => { }, "Success", intent: Intent.Success),
        Button(_ => { }, "Danger", intent: Intent.Danger),
        Button(_ => { }, "Neutral", intent: Intent.Neutral)]);

    [Story("Button/Tailwind", Height = 160)]
    public static Widget ButtonTailwind() => Frame(
        Button(_ => { }, "Hover me",
            background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 180, height: 64,
            parts: [S.On(WidgetState.Hover, S.Bg(Tw.Red500), S.Scale(1.08f)),
                    S.On(WidgetState.Pressed, S.Scale(0.94f))]));

    [Story("Button/Counter", Height = 160)]
    public static Widget ButtonCounter(StoryContext ctx)
    {
        Signal<int> count = ctx.Signal("count", 0);
        return Frame(HStack(8)[
            Button(_ => count.Value--, "-"),
            Text($" {count} ", 22, color: Bind.From(() => UiTheme.T.Text), vAlign: Align.Center),
            Button(_ => count.Value++, "+")]);
    }

    // ---- 入力/選択 ----

    [Story("CheckBox/Basic", Height = 160)]
    public static Widget CheckBasic(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", false), "Subscribe to newsletter"));

    [Story("CheckBox/CheckedStyle", Height = 160)]
    public static Widget CheckStyled(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", true), "Custom checked color",
            parts: S.On(WidgetState.Checked, S.Bg(Tw.Green500))));

    [Story("Switch/Basic", Height = 160)]
    public static Widget SwitchBasic(StoryContext ctx)
        => Frame(Switch(ctx.Signal("on", true)));

    [Story("Slider/Basic", Height = 160)]
    public static Widget SliderBasic(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.35f)));

    [Story("Slider/CustomColors", Height = 160)]
    public static Widget SliderColors(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.6f),
            trackColor: Tw.Slate200, fillColor: Tw.Amber500, knobColor: Tw.Amber500));

    [Story("Segmented/Basic", Height = 160)]
    public static Widget SegmentedBasic(StoryContext ctx)
        => Frame(Segmented(["Day", "Week", "Month"], ctx.Signal("selected", 0)));

    [Story("Radios/Basic", Height = 200)]
    public static Widget RadiosBasic(StoryContext ctx)
        => Frame(Radios(["Small", "Medium", "Large"], ctx.Signal("selected", 1)));

    [Story("Select/Basic", Height = 240)]
    public static Widget SelectBasic(StoryContext ctx)
        => Frame(Select(["Apple", "Banana", "Cherry"], ctx.Signal("selected", 0)));

    [Story("LengthField/Basic", Height = 200)]
    public static Widget LengthFieldBasic(StoryContext ctx)
    {
        var len = new Signal<Length>((Length)"50%");
        return Frame(VStack(8)[
            Text($"value: {len}", 13, color: Bind.From(() => UiTheme.T.Text)),
            LengthField(len)]);
    }
}
