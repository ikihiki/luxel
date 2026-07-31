using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

[ComponentStory(typeof(Luxel.Controls.Button), "Controls/Button/Playground", Factory = typeof(Kit),
    Template = nameof(Template), Height = 160)]
[ComponentArg(nameof(Luxel.Controls.Button.Text), "Click me", Description = "Button label", Order = 10)]
[ComponentArg(nameof(Luxel.Controls.Button.Variant), Variant.Filled, Description = "Visual variant", Order = 20)]
[ComponentArg("Disabled", false, Apply = nameof(ApplyDisabled), Description = "Disable interaction", Order = 30)]
internal static class ButtonPlaygroundStory
{
    internal static void ApplyDisabled(Button button, bool disabled) => button.Enabled = !disabled;

    internal static Widget Template(Button button) => Frame(button);
}

/// <summary>入力/選択系コントロールのストーリー。ctx.Signal(...) は自動で knob になる。</summary>
public static class InputControlStories
{
    // ---- Button ----

    [Story("Controls/Button/Primary", Height = 160)]
    public static Widget ButtonPrimary() => Frame(Button(_ => { }, "Click me"));

    // ---- ColorPicker ----

    [Story("Controls/ColorPicker/Basic", Height = 280)]
    public static Widget ColorPickerBasic(StoryContext ctx)
    {
        Signal<uint> color = new(Tw.Blue500);
        return Frame(VStack(12)[
            ColorPicker(color),
            HStack(10)[
                Box(background: color, rounded: 8, width: 44, height: 44),
                Label("選択色は Signal<uint> に反映される")]]);
    }

    [Story("Controls/Button/Variants", Height = 160)]
    public static Widget ButtonVariants() => Frame(HStack(8)[
        Button(_ => { }, "Filled"),
        Button(_ => { }, "Tonal", variant: Variant.Tonal),
        Button(_ => { }, "Outline", variant: Variant.Outline),
        Button(_ => { }, "Ghost", variant: Variant.Ghost)]);

    [Story("Controls/Button/Intents", Height = 160)]
    public static Widget ButtonIntents(StoryContext ctx) => ctx.Snap(Frame(HStack(8)[
        Button(_ => { }, "Primary"),
        Button(_ => { }, "Success", intent: Intent.Success),
        Button(_ => { }, "Danger", intent: Intent.Danger),
        Button(_ => { }, "Neutral", intent: Intent.Neutral)]));

    [Story("Controls/Button/Tailwind", Height = 160)]
    public static Widget ButtonTailwind() => Frame(
        Button(_ => { }, "Hover me",
                background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 180, height: 64)
            .When(WidgetState.Hover, background: Tw.Red500, scale: 1.08f)
            .When(WidgetState.Pressed, scale: 0.94f));

    public static IReadOnlyList<StoryArgDefinition> CounterArgs() =>
    [
        StoryArgDefinition.Create("count", "int", 0, "Current counter value.", min: -999, max: 999, step: 1),
    ];

    [Story("Controls/Button/Counter", Height = 160, RuntimeBundleId = "webgpu-browser-v1", Args = nameof(CounterArgs))]
    public static Widget ButtonCounter(StoryContext ctx)
    {
        CanonicalCounterRecipe.Result recipe = CanonicalCounterRecipe.Build(ctx.Arg("count", 0,
            new StoryArgOptions<int> { Description = "Current counter value.", Min = -999, Max = 999, Step = 1 }));
        // play: クリック → signal 反映 → クリック後の絵 (E2E の対話ショーケース)
        ctx.Play(async d =>
        {
            await d.Snap();
            await d.Click(recipe.Plus);
            await d.Expect(() => recipe.Count.Value == 1, "クリックでカウンタが増える");
            await d.Snap("clicked");
        });
        return recipe.Root;
    }

    // ---- 入力/選択 ----

    [Story("Controls/CheckBox/Basic", Height = 160)]
    public static Widget CheckBasic(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", false), "Subscribe to newsletter"));

    [Story("Controls/CheckBox/CheckedStyle", Height = 160)]
    public static Widget CheckStyled(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", true), "Custom checked color")
            .When(WidgetState.Checked, background: Tw.Green500));

    [Story("Controls/Switch/Basic", Height = 160)]
    public static Widget SwitchBasic(StoryContext ctx)
        => Frame(Switch(ctx.Signal("on", true)));

    [Story("Controls/Slider/Basic", Height = 160)]
    public static Widget SliderBasic(StoryContext ctx)
        => ctx.Snap(Frame(Slider(ctx.Signal("value", 0.35f))));

    [Story("Controls/Slider/CustomColors", Height = 160)]
    public static Widget SliderColors(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.6f),
            trackColor: Tw.Slate200, fillColor: Tw.Amber500, knobColor: Tw.Amber500));

    [Story("Controls/Segmented/Basic", Height = 160)]
    public static Widget SegmentedBasic(StoryContext ctx)
        => Frame(Segmented(["Day", "Week", "Month"], ctx.Signal("selected", 0)));

    [Story("Controls/Radios/Basic", Height = 200)]
    public static Widget RadiosBasic(StoryContext ctx)
        => Frame(Radios(["Small", "Medium", "Large"], ctx.Signal("selected", 1)));

    [Story("Controls/Select/Basic", Height = 240)]
    public static Widget SelectBasic(StoryContext ctx)
        => ctx.Snap(Frame(Select(["Apple", "Banana", "Cherry"], ctx.Signal("selected", 0))));

    [Story("Controls/LengthField/Basic", Height = 200)]
    public static Widget LengthFieldBasic(StoryContext ctx)
    {
        var len = new Signal<Length>((Length)"50%");
        return Frame(VStack(8)[
            Text($"value: {len}", 13, color: Bind.From(() => UiTheme.T.Text)),
            LengthField(len)]);
    }
}
