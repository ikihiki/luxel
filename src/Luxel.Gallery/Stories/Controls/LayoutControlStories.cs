using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>コンテナ/レイアウト系コントロールのストーリー。</summary>
public static class LayoutControlStories
{
    [Story("Border/Card", Height = 220)]
    public static Widget BorderCard() => Frame(
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 12, padding: new Thickness(20))
            [VStack(6)[
                Heading("Card title", 2),
                Muted("Supporting description text"),
                Spacer(height: 8f),
                Button(_ => { }, "Action")]]);

    [Story("Grid/Columns", Height = 240)]
    public static Widget GridColumns() =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))
        [Grid(columns: [1, 2, 1])[
            Box(background: Tw.Blue500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4), parts: P.Grid.Column(0)),
            Box(background: Tw.Amber500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4), parts: P.Grid.Column(1)),
            Box(background: Tw.Green500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4), parts: P.Grid.Column(2))]];

    [Story("ScrollViewer/Basic", Height = 240)]
    public static Widget ScrollBasic()
    {
        var rows = Enumerable.Range(1, 20).Select(i => (Widget)Label($"Row {i}")).ToArray();
        return Frame(Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(8), clip: true)
            [Scroll(160f, width: 240)[VStack(4)[rows]]]);
    }

    /// <summary>ヒットの transform 追従 + スクロールバードラッグの実証。クリックは Log にも記録。</summary>
    [Story("ScrollViewer/Clickable", Height = 260)]
    public static Widget ScrollClickable(StoryContext ctx)
    {
        Signal<string> last = ctx.Signal("lastClicked", "(none)");
        var rows = Enumerable.Range(1, 20)
            .Select(i => (Widget)Button(_ => { last.Value = $"Row {i}"; ctx.Log($"Row {i} clicked"); }, $"Row {i}", height: 30f))
            .ToArray();
        return Frame(VStack(8)[
            Text($"clicked: {last}", 14, color: Bind.From(() => UiTheme.T.Text)),
            Scroll(160f, width: 240)[VStack(4)[rows]]]);
    }

    [Story("ListView/Basic", Height = 260)]
    public static Widget ListViewBasic(StoryContext ctx)
    {
        // EV: コールバックはファクトリの省略可能引数 (第一引数 = 発火元)。items も UI パラメータ
        ListView lv = ListView(180f, 18f, onSelect: (_, i) => ctx.Log($"selected: Item {i + 1}"),
            items: Enumerable.Range(1, 40).Select(i => $"Item {i}").ToArray(), width: 260f);
        return Frame(lv);
    }

    [Story("ListView/Reorder", Height = 260)]
    public static Widget ListViewReorder(StoryContext ctx)
    {
        // D&D 並べ替え (QP-M4): 行をドラッグ → 挿入位置インジケータ → ドロップで OnReorder。
        // データは items signal が持ち、並べ替えは signal への入れ直しで反映 (コントロールはデータを所有しない)
        var items = new Signal<IReadOnlyList<string>>(Enumerable.Range(1, 12).Select(i => $"Track {i}").ToArray());
        ListView lv = ListView(180f, 18f,
            onSelect: (_, i) => ctx.Log($"selected: {i}"),
            onReorder: (_, from, to) =>
            {
                var next = new List<string>(items.Value);
                string s = next[from];
                next.RemoveAt(from);
                next.Insert(to > from ? to - 1 : to, s);
                items.Value = next;
                ctx.Log($"reorder: {from} → {to}");
            },
            items: items, width: 260f);
        lv.AllowReorder = true;
        return Frame(lv);
    }

    [Story("ListView/Huge", Height = 260)]
    public static Widget ListViewHuge(StoryContext ctx)
    {
        // 仮想化ゲート (AP-M3): 10 万行でも実体化は可視行プールのみ、スクロール/選択が破綻しない
        ListView lv = ListView(180f, 18f, onSelect: (_, i) => ctx.Log($"selected: {i}"),
            items: Enumerable.Range(1, 100_000).Select(i => $"Row {i:n0}").ToArray(), width: 260f);
        return Frame(lv);
    }

    [Story("Layout/Units", Height = 240)]
    public static Widget LayoutUnits() => Frame(
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(8), width: 400)[
            VStack(6)[
                Box(background: Tw.Sky500, rounded: 4, width: "100%", height: 18),
                Box(background: Tw.Indigo500, rounded: 4, width: "50%", height: 18),
                Box(background: Tw.Green500, rounded: 4, width: "10em", height: 18),
                Box(background: Tw.Red500, rounded: 4, width: "25vw", height: 18)]]);

    [Story("WrapPanel/Basic", Height = 260)]
    public static Widget WrapBasic() => Frame(
        Wrap(8, 8, width: 300f)[
            Enumerable.Range(1, 12).Select(i => (Widget)Box(
                background: (i % 3) switch { 0 => Tw.Sky500, 1 => Tw.Indigo500, _ => Tw.Green500 },
                rounded: 4, width: 40 + i % 4 * 25, height: 26)).ToArray()]);
}
