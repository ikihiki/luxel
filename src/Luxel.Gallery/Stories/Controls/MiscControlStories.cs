using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Kit 複合/表示系コントロールとトランジションのストーリー。</summary>
public static class MiscControlStories
{
    [Story("Transitions/States", Height = 200)]
    public static Widget TransitionStates(StoryContext ctx) => Frame(
        // 状態レイヤは生成された When (引数はファクトリと同名 — Stateable のみ)、
        // トランジションは fluent Transition 系で「どのプロパティ群を」独立に宣言する (GN):
        //   Background は 400ms 既定 / hover へは 80ms で入り / 押下・解放 (pressed→hover) は即時。
        //   Scale は無指定 = 瞬時。
        Button(_ => ctx.Log("click"), "Hover / Press",
                background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 200, height: 64)
            // transform 成分 (TF): squash & stretch — X と Y で別カーブ/別 duration
            .When(WidgetState.Hover, background: Tw.Red500, scaleX: 1.12f, scaleY: 0.94f, rotate: 0.03f)
            .When(WidgetState.Pressed, background: Tw.Green500)
            .Transition(0.4f, CubicBezierCurve.EaseInOut, ButtonProps.Background)
            .Transition(0.12f, Transform.ScaleX)
            .Transition(0.30f, CubicBezierCurve.EaseInOut, Transform.ScaleY)
            .TransitionTo(WidgetState.Hover, 0.08f, ButtonProps.Background)
            .TransitionTo(WidgetState.Pressed, 0f)
            .TransitionBetween(WidgetState.Pressed, WidgetState.Hover, 0f));

    [Story("Kit/Badges", Height = 160)]
    public static Widget Badges() => Frame(HStack(8)[
        Badge("Primary"), Badge("OK", Intent.Success), Badge("Error", Intent.Danger), Chip("Chip")]);

    [Story("Kit/Alert", Height = 180)]
    public static Widget AlertStory() => Frame(VStack(8)[
        Alert("Information message", Intent.Info),
        Alert("Something went wrong", Intent.Danger)]);

    [Story("Kit/Typography", Height = 240)]
    public static Widget Typography() => Frame(VStack(6)[
        Heading("Heading 1"), Heading("Heading 2", 2), Label("Body label"), Muted("Muted caption"),
        Divider(), Skeleton(220, 14)]);

    [Story("Spinner/Basic", Height = 160)]
    public static Widget SpinnerBasic() => Frame(Spinner(36f));

    [Story("LinkText/Basic", Height = 180)]
    public static Widget LinkTextBasic(StoryContext ctx) => Frame(VStack(8)[
        LinkText(_ => ctx.Log("link click"), "クリックできるリンク"),
        LinkText(_ => { }, "アクティブ状態 (active: true)", active: true),
        LinkText(_ => { }, "色とホバー色の指定", color: Tw.Red500, hoverColor: Tw.Amber500)]);

    [Story("Icon/Kinds", Height = 160)]
    public static Widget IconKinds() => Frame(HStack(10)[
        Icon(IconKind.Check), Icon(IconKind.Close), Icon(IconKind.ChevronDown), Icon(IconKind.ChevronRight),
        Icon(IconKind.Plus), Icon(IconKind.Minus), Icon(IconKind.Dot), Icon(IconKind.Circle),
        Icon(IconKind.Check, color: Tw.Green500), Icon(IconKind.Close, color: Tw.Red500)]);

    [Story("Sparkline/Basic", Height = 260)]
    public static Widget SparklineBasic()
    {
        float[] vals = Enumerable.Range(0, 40)
            .Select(i => MathF.Sin(i * 0.35f) * 0.6f + 1.2f + i % 7 * 0.05f).ToArray();
        Sparkline line = Sparkline(260, 64);
        line.SetValues(vals);
        Sparkline bars = Sparkline(260, 48, bars: true);
        bars.SetValues(vals, min: 0);
        return Frame(VStack(8)[line, bars]);
    }
}
