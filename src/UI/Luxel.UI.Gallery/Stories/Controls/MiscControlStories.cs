using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Kit 複合/表示系コントロールとトランジションのストーリー。</summary>
public static class MiscControlStories
{
    [Story("Controls/Kit/Badges", Height = 160)]
    public static Widget Badges() => Frame(HStack(8)[
        Badge("Primary"), Badge("OK", Intent.Success), Badge("Error", Intent.Danger), Chip("Chip")]);

    [Story("Controls/Kit/Alert", Height = 180)]
    public static Widget AlertStory() => Frame(VStack(8)[
        Alert("Information message", Intent.Info),
        Alert("Something went wrong", Intent.Danger)]);

    [Story("Controls/Kit/Typography", Height = 240)]
    public static Widget Typography() => Frame(VStack(6)[
        Heading("Heading 1"), Heading("Heading 2", 2), Label("Body label"), Muted("Muted caption"),
        Divider(), Skeleton(220, 14)]);

    [Story("Controls/Spinner/Basic", Height = 160)]
    public static Widget SpinnerBasic() => Frame(Spinner(36f));

    [Story("Controls/LinkText/Basic", Height = 180)]
    public static Widget LinkTextBasic(StoryContext ctx) => Frame(VStack(8)[
        LinkText(_ => ctx.Log("link click"), "クリックできるリンク"),
        LinkText(_ => { }, "アクティブ状態 (active: true)", active: true),
        LinkText(_ => { }, "色とホバー色の指定", color: Tw.Red500, hoverColor: Tw.Amber500)]);

    [Story("Controls/Icon/Kinds", Height = 160)]
    public static Widget IconKinds() => Frame(HStack(10)[
        Icon(IconKind.Check), Icon(IconKind.Close), Icon(IconKind.ChevronDown), Icon(IconKind.ChevronRight),
        Icon(IconKind.Plus), Icon(IconKind.Minus), Icon(IconKind.Dot), Icon(IconKind.Circle),
        Icon(IconKind.Check, color: Tw.Green500), Icon(IconKind.Close, color: Tw.Red500)]);

    [Story("Controls/Sparkline/Basic", Height = 260)]
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
