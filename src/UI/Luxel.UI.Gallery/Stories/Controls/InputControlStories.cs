using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

[ComponentStory(typeof(Luxel.Controls.Button), "Controls/Input/Button/Examples/Interactive", Factory = typeof(Kit),
    Template = nameof(Template))]
[ComponentArg(nameof(Luxel.Controls.Button.Text), "クリック", Description = "ボタンのラベル。", Order = 10)]
[ComponentArg(nameof(Luxel.Controls.Button.Variant), Variant.Filled, Description = "表示バリエーション。", Order = 20)]
[ComponentArg("Disabled", false, Apply = nameof(ApplyDisabled), Description = "操作を無効にします。", Order = 30)]
internal static class ButtonPlaygroundStory
{
    internal static void ApplyDisabled(Button button, bool disabled) => button.Enabled = !disabled;

    internal static Widget Template(Button button) => button;
}

/// <summary>入力/選択系コントロールのストーリー。ctx.Signal(...) は自動で knob になる。</summary>
[StoryMeta("Controls")]
public static class InputControlStories
{
    // ---- Button ----

    [Story(Path = "Controls/Input/Button/Basic", ArgsEnabled = false)]
    public static StoryResult ButtonPrimary() => Button(_ => { }, "クリック");

    // ---- ColorPicker ----

    [Story(Path = "Controls/Input/ColorPicker/Basic")]
    public static StoryResult ColorPickerBasic()
        => ColorPicker(new Signal<uint>(Tw.Blue500));

    public static IReadOnlyList<StoryArgDefinition> ColorPickerPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("color", "color", Tw.Blue500, "選択中の色。"),
    ];

    [Story(Path = "Controls/Input/ColorPicker/Examples/Interactive", Args = nameof(ColorPickerPlaygroundArgs))]
    public static StoryResult ColorPickerPlayground(StoryContext ctx)
        => ColorPicker(ctx.Arg("color", Tw.Blue500));

    [Story(Path = "Controls/Input/Button/Examples/Variants")]
    public static StoryResult ButtonVariants() => Frame(HStack(8)[
        Button(_ => { }, "Filled"),
        Button(_ => { }, "Tonal", variant: Variant.Tonal),
        Button(_ => { }, "Outline", variant: Variant.Outline),
        Button(_ => { }, "Ghost", variant: Variant.Ghost)]);

    [Story(Path = "Controls/Input/Button/Examples/Intents")]
    public static StoryResult ButtonIntents(StoryContext ctx) => ctx.Snap(Frame(HStack(8)[
        Button(_ => { }, "Primary"),
        Button(_ => { }, "Success", intent: Intent.Success),
        Button(_ => { }, "Danger", intent: Intent.Danger),
        Button(_ => { }, "Neutral", intent: Intent.Neutral)]));

    [Story(Path = "Controls/Input/Button/Examples/Utilities")]
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

    [Story(Path = "Controls/Input/Button/States/Interaction")]
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
        StoryArgDefinition.Create("count", "int", 0, "現在のカウンター値。", min: -999, max: 999, step: 1),
    ];

    public static StoryResult ButtonCounter(StoryContext ctx)
    {
        CanonicalCounterRecipe.Result recipe = CanonicalCounterRecipe.Build(ctx.Arg("count", 0,
            new StoryArgOptions<int> { Description = "現在のカウンター値。", Min = -999, Max = 999, Step = 1 }));
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

    [Story(Path = "Controls/Input/CheckBox/Basic")]
    public static StoryResult CheckBasic(StoryContext ctx)
        => Check(new Signal<bool>(false), "Subscribe to newsletter");

    [Story(Path = "Controls/Input/CheckBox/Examples/Styled")]
    public static StoryResult CheckStyled(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", true), "Custom checked color")
            .When(WidgetState.Checked, background: Tw.Green500));

    [Story(Path = "Controls/Input/Switch/Basic")]
    public static StoryResult SwitchBasic(StoryContext ctx)
        => Switch(new Signal<bool>(true));

    [Story(Path = "Controls/Input/Slider/Basic")]
    public static StoryResult SliderBasic(StoryContext ctx)
        => ctx.Snap(Slider(new Signal<float>(0.35f)));

    public static IReadOnlyList<StoryArgDefinition> SliderPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("value", "float", 35f, "現在値。", min: 0, max: 100, step: 1),
        StoryArgDefinition.Create("min", "float", 0f, "範囲の最小値。", min: -100, max: 99, step: 1),
        StoryArgDefinition.Create("max", "float", 100f, "範囲の最大値。", min: 1, max: 200, step: 1),
        StoryArgDefinition.Create("width", "float", 320f, "スライダーの幅。", min: 120, max: 640, step: 10),
        StoryArgDefinition.Create("trackColor", "color", Tw.Slate300, "トラック色。"),
        StoryArgDefinition.Create("fillColor", "color", Tw.Blue500, "塗りつぶし色。"),
        StoryArgDefinition.Create("knobColor", "color", Tw.Blue500, "ノブ色。"),
    ];

    [Story(Path = "Controls/Input/Slider/Examples/Interactive", Args = nameof(SliderPlaygroundArgs))]
    public static StoryResult SliderPlayground(StoryContext ctx)
    {
        Signal<float> value = ctx.Arg("value", 35f);
        Signal<float> min = ctx.Arg("min", 0f);
        Signal<float> max = ctx.Arg("max", 100f);
        Signal<float> width = ctx.Arg("width", 320f);
        Signal<uint> trackColor = ctx.Arg("trackColor", Tw.Slate300);
        Signal<uint> fillColor = ctx.Arg("fillColor", Tw.Blue500);
        Signal<uint> knobColor = ctx.Arg("knobColor", Tw.Blue500);
        if (max.Value <= min.Value) max.Value = min.Value + 1f;
        value.Value = Math.Clamp(value.Value, min.Value, max.Value);
        Slider slider = Slider(value, min: min, max: max, trackColor: trackColor,
            fillColor: fillColor, knobColor: knobColor, width: width.Value);
        ctx.Play(static d => d.Snap());
        return slider;
    }

    [Story(Path = "Controls/Input/Slider/Examples/Slots")]
    public static StoryResult SliderSlots(StoryContext ctx) => Frame(
        Slider(ctx.Signal("value", 0.65f))
        [
            SliderSlot.Track(() => Box(background: Tw.Slate300, rounded: 3, hAlign: Align.Stretch, vAlign: Align.Stretch)),
            SliderSlot.Knob(() => Box(background: Tw.Amber500, rounded: 9, width: 18, height: 18))
        ]);

    [Story(Path = "Controls/Input/Slider/States/Focused")]
    public static StoryResult SliderFocused(StoryContext ctx)
    {
        Slider slider = Slider(ctx.Signal("value", 0.4f));
        slider.Focused.Value = true;
        return Frame(slider);
    }

    [Story(Path = "Controls/Input/Slider/States/Disabled")]
    public static StoryResult SliderDisabled(StoryContext ctx)
    {
        Slider slider = Slider(ctx.Signal("value", 0.4f), utilities: [U.Opacity(0.45f)]);
        slider.Enabled = false;
        return Frame(slider);
    }

    [Story(Path = "Controls/Input/Slider/Examples/Colors")]
    public static StoryResult SliderColors(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.6f),
            trackColor: Tw.Slate200, fillColor: Tw.Amber500, knobColor: Tw.Amber500));

    [Story(Path = "Controls/Input/SegmentedControl/Basic")]
    public static StoryResult SegmentedBasic(StoryContext ctx)
        => Segmented(["Day", "Week", "Month"], new Signal<int>(0));

    [Story(Path = "Controls/Input/RadioGroup/Basic")]
    public static StoryResult RadiosBasic(StoryContext ctx)
        => Radios(["Small", "Medium", "Large"], new Signal<int>(1));

    [Story(Path = "Controls/Input/Select/Basic")]
    public static StoryResult SelectBasic(StoryContext ctx)
        => ctx.Snap(Select(["Apple", "Banana", "Cherry"], new Signal<int>(0)));

    [Story(Path = "Controls/Input/LengthField/Basic")]
    public static StoryResult LengthFieldBasic()
        => LengthField(new Signal<Length>((Length)"50%"));
}
