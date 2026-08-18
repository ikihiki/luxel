using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

[ComponentStory(typeof(Luxel.Controls.Button), "Controls/Button/Playground", Factory = typeof(Kit),
    Template = nameof(Template))]
[ComponentArg(nameof(Luxel.Controls.Button.Text), "Click me", Description = "Button label", Order = 10)]
[ComponentArg(nameof(Luxel.Controls.Button.Variant), Variant.Filled, Description = "Visual variant", Order = 20)]
[ComponentArg("Disabled", false, Apply = nameof(ApplyDisabled), Description = "Disable interaction", Order = 30)]
internal static class ButtonPlaygroundStory
{
    internal static void ApplyDisabled(Button button, bool disabled) => button.Enabled = !disabled;

    internal static Widget Template(Button button) => Frame(button);
}

/// <summary>入力/選択系コントロールのストーリー。ctx.Signal(...) は自動で knob になる。</summary>
[StoryMeta("Controls")]
public static class InputControlStories
{
    // ---- Button ----

    [Story(Path = "Controls/Button/Basic")]
    public static StoryResult ButtonPrimary() => Frame(Button(_ => { }, "Click me"));

    // ---- ColorPicker ----

    [Story(Path = "Controls/ColorPicker/Basic")]
    public static StoryResult ColorPickerBasic(StoryContext ctx)
    {
        Signal<uint> color = new(Tw.Blue500);
        return Frame(VStack(12)[
            ColorPicker(color),
            HStack(10)[
                Box(background: color, rounded: 8, width: 44, height: 44),
                Label("選択色は Signal<uint> に反映される")]]);
    }

    [Story(Path = "Controls/Button/Examples/Variants")]
    public static StoryResult ButtonVariants() => Frame(HStack(8)[
        Button(_ => { }, "Filled"),
        Button(_ => { }, "Tonal", variant: Variant.Tonal),
        Button(_ => { }, "Outline", variant: Variant.Outline),
        Button(_ => { }, "Ghost", variant: Variant.Ghost)]);

    [Story(Path = "Controls/Button/Examples/Intents")]
    public static StoryResult ButtonIntents(StoryContext ctx) => ctx.Snap(Frame(HStack(8)[
        Button(_ => { }, "Primary"),
        Button(_ => { }, "Success", intent: Intent.Success),
        Button(_ => { }, "Danger", intent: Intent.Danger),
        Button(_ => { }, "Neutral", intent: Intent.Neutral)]));

    [Story(Path = "Controls/Button/Examples/Utilities")]
    public static StoryResult ButtonTailwind() => Frame(
        Button(_ => { }, "Hover me", utilities:
        [
            U.Background(Tw.Blue500),
            U.Foreground(Tw.White),
            U.Rounded(10),
            U.Width(180),
            U.Height(64),
            U.Hover(
            [
                U.Background(Tw.Red500),
                U.ScaleX(1.08f),
                U.ScaleY(1.08f),
            ]),
            U.Pressed(
            [
                U.ScaleX(0.94f),
                U.ScaleY(0.94f),
            ]),
        ]));

    [Story(Path = "Controls/Button/States/Interaction")]
    public static StoryResult ButtonInteractionStates()
    {
        Button hovered = Button(_ => { }, "Hovered");
        hovered.Hovered.Value = true;
        Button pressed = Button(_ => { }, "Pressed");
        pressed.Pressed.Value = true;
        Button disabled = Button(_ => { }, "Disabled");
        disabled.Enabled = false;
        return Frame(HStack(8)[Button(_ => { }, "Normal"), hovered, pressed, disabled]);
    }

    public static IReadOnlyList<StoryArgDefinition> CounterArgs() =>
    [
        StoryArgDefinition.Create("count", "int", 0, "Current counter value.", min: -999, max: 999, step: 1),
    ];

    public static StoryResult ButtonCounter(StoryContext ctx)
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

    [Story(Path = "Controls/CheckBox/Basic")]
    public static StoryResult CheckBasic(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", false), "Subscribe to newsletter"));

    [Story(Path = "Controls/CheckBox/Examples/Styled")]
    public static StoryResult CheckStyled(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", true), "Custom checked color")
            .When(WidgetState.Checked, background: Tw.Green500));

    [Story(Path = "Controls/Switch/Basic")]
    public static StoryResult SwitchBasic(StoryContext ctx)
        => Frame(Switch(ctx.Signal("on", true)));

    [Story(Path = "Controls/Slider/Basic")]
    public static StoryResult SliderBasic(StoryContext ctx)
        => ctx.Snap(Frame(Slider(ctx.Signal("value", 0.35f))));

    [Story(Path = "Controls/Slider/Playground")]
    public static StoryResult SliderPlayground(StoryContext ctx)
    {
        Signal<float> value = ctx.Signal("value", 0.5f);
        Slider slider = Slider(value);
        ctx.Play(static d => d.Snap());
        return Frame(VStack(8)[slider, Text($"value: {value}", 13)]);
    }

    [Story(Path = "Controls/Slider/Examples/Slots")]
    public static StoryResult SliderSlots(StoryContext ctx) => Frame(
        Slider(ctx.Signal("value", 0.65f))
        [
            SliderSlot.Track(() => Box(background: Tw.Slate300, rounded: 3, hAlign: Align.Stretch, vAlign: Align.Stretch)),
            SliderSlot.Knob(() => Box(background: Tw.Amber500, rounded: 9, width: 18, height: 18))
        ]);

    [Story(Path = "Controls/Slider/States/Focused")]
    public static StoryResult SliderFocused(StoryContext ctx)
    {
        Slider slider = Slider(ctx.Signal("value", 0.4f));
        slider.Focused.Value = true;
        return Frame(slider);
    }

    [Story(Path = "Controls/Slider/States/Disabled")]
    public static StoryResult SliderDisabled(StoryContext ctx)
    {
        Slider slider = Slider(ctx.Signal("value", 0.4f), utilities: [U.Opacity(0.45f)]);
        slider.Enabled = false;
        return Frame(slider);
    }

    [Story(Path = "Controls/Slider/Examples/Colors")]
    public static StoryResult SliderColors(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.6f),
            trackColor: Tw.Slate200, fillColor: Tw.Amber500, knobColor: Tw.Amber500));

    [Story(Path = "Controls/Segmented/Basic")]
    public static StoryResult SegmentedBasic(StoryContext ctx)
        => Frame(Segmented(["Day", "Week", "Month"], ctx.Signal("selected", 0)));

    [Story(Path = "Controls/Radios/Basic")]
    public static StoryResult RadiosBasic(StoryContext ctx)
        => Frame(Radios(["Small", "Medium", "Large"], ctx.Signal("selected", 1)));

    [Story(Path = "Controls/Select/Basic")]
    public static StoryResult SelectBasic(StoryContext ctx)
        => ctx.Snap(Frame(Select(["Apple", "Banana", "Cherry"], ctx.Signal("selected", 0))));

    [Story(Path = "Controls/LengthField/Basic")]
    public static StoryResult LengthFieldBasic(StoryContext ctx)
    {
        var len = new Signal<Length>((Length)"50%");
        return Frame(VStack(8)[
            Text($"value: {len}", 13, color: Bind.From(() => UiTheme.T.Text)),
            LengthField(len)]);
    }
}
