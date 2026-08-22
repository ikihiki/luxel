using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Kit 複合/表示系コントロールとトランジションのストーリー。</summary>
[StoryMeta("Controls")]
public static class MiscControlStories
{
    [Story(Path = "Controls/Layout/Kit/Examples/Badges",
        ShortDescription = "意味の強さや状態を小さなラベルへ圧縮するときの Badge と Chip の使い分けを比較します。")]
    public static StoryResult Badges() => Frame(HStack(8)[
        Badge("Primary"), Badge("OK", Intent.Success), Badge("Error", Intent.Danger), Chip("Chip")]);

    [Story(Path = "Controls/Layout/Kit/Examples/Alert",
        ShortDescription = "情報と危険の Intent を面で伝え、本文より先に注意度を判断できる構成を示します。")]
    public static StoryResult AlertStory() => Frame(VStack(8)[
        Alert("Information message", Intent.Info),
        Alert("Something went wrong", Intent.Danger)]);

    [Story(Path = "Controls/Layout/Kit/Examples/Typography",
        ShortDescription = "見出し、本文、補助文、区切りを組み合わせて情報階層を作る最小セットを示します。")]
    public static StoryResult Typography() => Frame(VStack(6)[
        Heading("Heading 1"), Heading("Heading 2", 2), Label("Body label"), Muted("Muted caption"),
        Divider(), Skeleton(220, 14)]);

    [Story(Path = "Controls/Rendering/Spinner/Basic",
        ShortDescription = "完了時刻が不明な短い待機中に、処理継続中であることを示します。")]
    public static StoryResult SpinnerBasic() => Spinner(36f);

    [Story(Path = "Controls/Text/LinkText/Basic",
        ShortDescription = "文中の遷移や補助操作をリンクとして示し、クリックを Output へ通知します。")]
    public static StoryResult LinkTextBasic(StoryContext ctx) =>
        LinkText(_ => ctx.Log("link click"), "クリックできるリンク");

    [Story(Path = "Controls/Rendering/Icon/Examples/Kinds",
        ShortDescription = "操作や状態へ割り当てる代表的な図形と、Intent に応じた色付けを一覧で比較します。")]
    public static StoryResult IconKinds() => Frame(HStack(10)[
        Icon(IconKind.Check), Icon(IconKind.Close), Icon(IconKind.ChevronDown), Icon(IconKind.ChevronRight),
        Icon(IconKind.Plus), Icon(IconKind.Minus), Icon(IconKind.Dot), Icon(IconKind.Circle),
        Icon(IconKind.Check, color: Tw.Green500), Icon(IconKind.Close, color: Tw.Red500)]);

    [Story(Path = "Controls/Rendering/Sparkline/Basic",
        ShortDescription = "軸や凡例を省いた小さな折れ線で、値の傾向だけを素早く比較します。")]
    public static StoryResult SparklineBasic()
    {
        float[] vals = Enumerable.Range(0, 40)
            .Select(i => MathF.Sin(i * 0.35f) * 0.6f + 1.2f + i % 7 * 0.05f).ToArray();
        Sparkline line = Sparkline(260, 64);
        line.SetValues(vals);
        return line;
    }
}
