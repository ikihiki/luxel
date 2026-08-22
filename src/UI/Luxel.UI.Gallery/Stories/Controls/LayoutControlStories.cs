using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>コンテナ/レイアウト系コントロールのストーリー。</summary>
[StoryMeta("Controls")]
public static class LayoutControlStories
{
    [Story(Path = "Controls/Layout/Border/Basic")]
    public static StoryResult BorderCard() =>
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 12, padding: new Thickness(20))
            [Label("Border content")];

    [Story(Path = "Controls/Layout/Grid/Examples/Tracks")]
    public static StoryResult GridColumns() =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))
        [Grid(columns: [1, 2, 1])[
            Box(background: Tw.Blue500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4)).GridColumn(0),
            Box(background: Tw.Amber500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4)).GridColumn(1),
            Box(background: Tw.Green500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch, margin: new Thickness(4)).GridColumn(2)]];

    [Story(Path = "Controls/Layout/Grid/Examples/AttachedUtilities")]
    public static StoryResult GridAttachedUtilities() =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))
        [Grid(columns: [1, 2, 1])[
            Box(background: Tw.Blue500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch,
                utilities: [U.Margin(new Thickness(4)), U.Grid.Column(0)]),
            Box(background: Tw.Amber500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch,
                utilities: [U.Margin(new Thickness(4)), U.Grid.Column(1)]),
            Box(background: Tw.Green500, rounded: 6, hAlign: Align.Stretch, vAlign: Align.Stretch,
                utilities: [U.Margin(new Thickness(4)), U.Grid.Column(2)])]];

    [Story(Path = "Controls/Layout/Splitter/Basic")]
    public static StoryResult SplitterBasic(StoryContext ctx) =>
        // 実アプリではドラッグ量 d でレイアウト変数を更新して chrome を再構築する
        // (GalleryApp のサイドバー/Log/右パネルがこの形)。ここでは delta を Log に流すのみ
        HStack(0)[
            Box(background: Tw.Blue500, rounded: 6, width: 170, height: 160),
            Splitter(vertical: true, onResized: (_, d) => ctx.Log($"drag {d:+0.0;-0.0}px")),
            Box(background: Tw.Amber500, rounded: 6, width: 170, height: 160)];

    [Story(Path = "Controls/Collections/TreeView/Basic")]
    public static StoryResult TreeViewBasic(StoryContext ctx)
    {
        // Key は展開/選択の永続キー (再構築をまたいで一意なパス文字列)。
        // Tag != null の子持ちノードはラベルクリック = 選択 + 展開、開閉はシェブロン
        Signal<string> selected = new("docs/gpu/device");
        var expanded = new HashSet<string> { "docs", "docs/gpu" };
        ctx.Play(static d => d.Snap());
        return TreeView(TreeRoots(), expanded: expanded,
            onSelect: (_, n) => { selected.Value = n.Key; ctx.Log($"select {n.Key}"); },
            selected: selected, width: 280);
    }

    public static IReadOnlyList<StoryArgDefinition> TreeViewPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("selected", "string", "docs/gpu/device", "選択中のノードキー。"),
        StoryArgDefinition.Create("filter", "string", "", "ツリーの絞り込み文字列。"),
    ];

    [Story(Path = "Controls/Collections/TreeView/Examples/Interactive", Args = nameof(TreeViewPlaygroundArgs),
        ShortDescription = "selected と filter を変更し、階層の選択・展開・絞り込みを確認する例です。")]
    public static StoryResult TreeViewPlayground(StoryContext ctx)
    {
        Signal<string> selected = ctx.Arg("selected", "docs/gpu/device",
            new StoryArgOptions<string> { Description = "選択中のノードキー。" });
        Signal<string> filter = ctx.Arg("filter", "",
            new StoryArgOptions<string> { Description = "ツリーの絞り込み文字列。" });
        // The filter field is a structural trigger for TreeView's filtering behavior.
        return VStack(8)[
            TextField(filter, placeholder: "ツリーを絞り込む..."),
            TreeView(TreeRoots(), expanded: new HashSet<string> { "docs", "docs/gpu" },
                selected: selected, filter: filter, onSelect: (_, node) => selected.Value = node.Key, width: 300)];
    }

    [Story(Path = "Controls/Collections/TreeView/States/Selection")]
    public static StoryResult TreeViewSelection() => Frame(
        TreeView(TreeRoots(), expanded: new HashSet<string> { "docs", "docs/gpu" },
            selected: "docs/gpu/device", width: 300));

    [Story(Path = "Controls/Collections/TreeView/States/Expanded")]
    public static StoryResult TreeViewExpanded() => Frame(
        TreeView(TreeRoots(), expanded: new HashSet<string> { "docs", "docs/gpu", "docs/ui", "samples" }, width: 300));

    [Story(Path = "Controls/Collections/TreeView/Examples/Utilities")]
    public static StoryResult TreeViewUtilities() => Frame(
        TreeView(TreeRoots(), expanded: new HashSet<string> { "docs", "docs/gpu" }, width: 320, utilities:
        [
            U.TreeView.RowHeight(30),
            U.TreeView.RowSpacing(3),
            U.TreeView.Indent(24),
            U.TreeView.Radius(8),
            U.TreeView.SelectedBackground(Tw.Blue500),
        ]));

    private static List<TreeNode> TreeRoots() =>
    [
        new("docs", "Docs",
        [
            new("docs/gpu", "GPU",
            [
                new("docs/gpu/device", "GpuDevice", Tag: "page"),
                new("docs/gpu/2d", "TwoD", Tag: "page"),
            ]),
            new("docs/ui", "UI", [new("docs/ui/controls", "Controls", Tag: "page")]),
        ]),
        new("samples", "Samples", [new("samples/gltf", "GltfBox", Tag: "page")]),
    ];

    [Story(Path = "Controls/Collections/ScrollViewer/Basic")]
    public static StoryResult ScrollBasic(StoryContext ctx)
    {
        var rows = Enumerable.Range(1, 20).Select(i => (Widget)Label($"Row {i}")).ToArray();
        ctx.Play(static d => d.Snap());
        return Scroll(160f, width: 240)[VStack(4)[rows]];
    }

    /// <summary>ヒットの transform 追従 + スクロールバードラッグの実証。クリックは Log にも記録。</summary>
    [Story(Path = "Controls/Collections/ScrollViewer/Examples/Clickable")]
    public static StoryResult ScrollClickable(StoryContext ctx)
    {
        Signal<string> last = ctx.Signal("lastClicked", "(none)");
        var rows = Enumerable.Range(1, 20)
            .Select(i => (Widget)Button(_ => { last.Value = $"Row {i}"; ctx.Log($"Row {i} clicked"); }, $"Row {i}", height: 30f))
            .ToArray();
        return Frame(VStack(8)[
            Text($"clicked: {last}", 14, color: Bind.From(() => UiTheme.T.Text)),
            Scroll(160f, width: 240)[VStack(4)[rows]]]);
    }

    [Story(Path = "Controls/Collections/ListView/Basic")]
    public static StoryResult ListViewBasic(StoryContext ctx)
    {
        // EV: コールバックはファクトリの省略可能引数 (第一引数 = 発火元)。items も UI パラメータ
        ListView lv = ListView(180f, 18f, onSelect: (_, i) => ctx.Log($"selected: Item {i + 1}"),
            items: new Signal<IReadOnlyList<string>>(Enumerable.Range(1, 40).Select(i => $"Item {i}").ToArray()), width: 260f);
        return lv;
    }

    [Story(Path = "Controls/Collections/ListView/Examples/Reorder")]
    public static StoryResult ListViewReorder(StoryContext ctx)
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
        ctx.Play(static d => d.Snap());
        return Frame(lv);
    }

    [Story(Path = "Controls/Collections/ListView/Test/Huge")]
    public static StoryResult ListViewHuge(StoryContext ctx)
    {
        // 仮想化ゲート (AP-M3): 10 万行でも実体化は可視行プールのみ、スクロール/選択が破綻しない
        ListView lv = ListView(180f, 18f, onSelect: (_, i) => ctx.Log($"selected: {i}"),
            items: new Signal<IReadOnlyList<string>>(Enumerable.Range(1, 100_000).Select(i => $"Row {i:n0}").ToArray()), width: 260f);
        return Frame(lv);
    }

    [Story(Path = "Controls/Layout/Layout/Examples/Units")]
    public static StoryResult LayoutUnits() => Frame(
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(8), width: 400)[
            VStack(6)[
                Box(background: Tw.Sky500, rounded: 4, width: "100%", height: 18),
                Box(background: Tw.Indigo500, rounded: 4, width: "50%", height: 18),
                Box(background: Tw.Green500, rounded: 4, width: "10em", height: 18),
                Box(background: Tw.Red500, rounded: 4, width: "25vw", height: 18)]]);

    [Story(Path = "Controls/Layout/WrapPanel/Basic")]
    public static StoryResult WrapBasic() =>
        Wrap(8, 8, width: 300f)[
            Enumerable.Range(1, 12).Select(i => (Widget)Box(
                background: (i % 3) switch { 0 => Tw.Sky500, 1 => Tw.Indigo500, _ => Tw.Green500 },
                rounded: 4, width: 40 + i % 4 * 25, height: 26)).ToArray()];

    public static IReadOnlyList<StoryArgDefinition> LayoutBoxPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("background", "color", Tw.Blue500, "背景色。"),
        StoryArgDefinition.Create("rounded", "float", 10f, "角丸半径。", min: 0, max: 48, step: 1),
        StoryArgDefinition.Create("width", "float", 180f, "幅 (px)。", min: 40, max: 480, step: 10),
        StoryArgDefinition.Create("height", "float", 96f, "高さ (px)。", min: 24, max: 280, step: 8),
    ];

    [Story(Path = "Controls/Layout/Box/Examples/Interactive", Args = nameof(LayoutBoxPlaygroundArgs))]
    public static StoryResult BoxPlayground(StoryContext ctx) => Box(
        background: ctx.Arg("background", Tw.Blue500), rounded: ctx.Arg("rounded", 10f),
        width: ctx.Arg("width", 180f).Value, height: ctx.Arg("height", 96f).Value);

    [Story(Path = "Controls/Layout/Border/Examples/Interactive", Args = nameof(LayoutBoxPlaygroundArgs))]
    public static StoryResult BorderPlayground(StoryContext ctx) => Border(
        background: ctx.Arg("background", Tw.Blue500), rounded: ctx.Arg("rounded", 10f),
        padding: new Thickness(20), width: ctx.Arg("width", 180f).Value, height: ctx.Arg("height", 96f).Value)
        [Label("Border child fixture")];

    [Story(Path = "Controls/Layout/Center/Examples/Interactive", Args = nameof(LayoutBoxPlaygroundArgs))]
    public static StoryResult CenterPlayground(StoryContext ctx)
    {
        Signal<uint> background = ctx.Arg("background", Tw.Blue500);
        Signal<float> rounded = ctx.Arg("rounded", 10f);
        Signal<float> width = ctx.Arg("width", 180f);
        Signal<float> height = ctx.Arg("height", 96f);
        return Center(width: width.Value, height: height.Value)
            [Box(background: background, rounded: rounded, width: 72, height: 40)];
    }

    public static IReadOnlyList<StoryArgDefinition> StackPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("vertical", "bool", true, "縦方向に並べる。"),
        StoryArgDefinition.Create("spacing", "float", 10f, "子要素間の間隔。", min: 0, max: 48, step: 1),
    ];

    [Story(Path = "Controls/Layout/Stack/Examples/Interactive", Args = nameof(StackPlaygroundArgs))]
    public static StoryResult StackPlayground(StoryContext ctx) => Stack(
        vertical: ctx.Arg("vertical", true), spacing: ctx.Arg("spacing", 10f))
        [
            Box(background: Tw.Blue500, rounded: 6, width: 120, height: 36),
            Box(background: Tw.Amber500, rounded: 6, width: 160, height: 36),
            Box(background: Tw.Green500, rounded: 6, width: 96, height: 36)
        ];

    public static IReadOnlyList<StoryArgDefinition> SpacerPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("width", "float", 48f, "空ける幅 (px)。", min: 0, max: 240, step: 8),
        StoryArgDefinition.Create("height", "float", 64f, "空ける高さ (px)。", min: 0, max: 160, step: 8),
    ];

    [Story(Path = "Controls/Layout/Spacer/Examples/Interactive", Args = nameof(SpacerPlaygroundArgs))]
    public static StoryResult SpacerPlayground(StoryContext ctx) => HStack(0)
    [
        Box(background: Tw.Blue500, rounded: 6, width: 64, height: 64),
        Spacer(width: ctx.Arg("width", 48f).Value, height: ctx.Arg("height", 64f).Value),
        Box(background: Tw.Amber500, rounded: 6, width: 64, height: 64)
    ];

    public static IReadOnlyList<StoryArgDefinition> ListViewPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("items", "string", "Alpha\nBravo\nCharlie\nDelta\nEcho\nFoxtrot", "改行区切りの項目。"),
        StoryArgDefinition.Create("height", "float", 180f, "表示高。", min: 72, max: 420, step: 12),
        StoryArgDefinition.Create("rowHeight", "float", 24f, "行高。", min: 16, max: 56, step: 2),
        StoryArgDefinition.Create("textColor", "color", Tw.Slate200, "文字色。"),
        StoryArgDefinition.Create("selectedColor", "color", Tw.Blue500, "選択色。"),
    ];

    [Story(Path = "Controls/Collections/ListView/Examples/Interactive", Args = nameof(ListViewPlaygroundArgs))]
    public static StoryResult ListViewPlayground(StoryContext ctx)
    {
        Signal<string> source = ctx.Arg("items", "Alpha\nBravo\nCharlie\nDelta\nEcho\nFoxtrot");
        IReadOnlyList<string> rows = source.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ListView(ctx.Arg("height", 180f), ctx.Arg("rowHeight", 24f),
            onSelect: (_, index) => ctx.Log($"selected: {index}"), items: new Signal<IReadOnlyList<string>>(rows),
            textColor: ctx.Arg("textColor", Tw.Slate200), selectedColor: ctx.Arg("selectedColor", Tw.Blue500), width: 300);
    }

    public static IReadOnlyList<StoryArgDefinition> TabsPlaygroundArgs() =>
    [
        StoryArgDefinition.Create("labels", "string", "概要,設定,アクティビティ", "カンマ区切りのタブラベル。"),
        StoryArgDefinition.Create("selected", "int", 0, "選択中のタブ。", min: 0, max: 2, step: 1),
        StoryArgDefinition.Create("foreground", "color", Tw.Slate200, "タブ文字色。"),
    ];

    [Story(Path = "Controls/Collections/Tabs/Examples/Interactive", Args = nameof(TabsPlaygroundArgs),
        ShortDescription = "labels と selected を変更し、タブ見出しと内容の対応を確認する例です。")]
    public static StoryResult TabsPlayground(StoryContext ctx)
    {
        string[] labels = ctx.Arg("labels", "概要,設定,アクティビティ").Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length == 0) labels = ["タブ"];
        Widget[] contents = labels.Select((label, index) => (Widget)Border(
            background: index % 2 == 0 ? Tw.Slate800 : Tw.Slate700, padding: new Thickness(20),
            width: 360, height: 120)[Label($"{label} の内容")]).ToArray();
        Signal<int> selected = ctx.Arg("selected", 0);
        if (selected.Value >= labels.Length) selected.Value = labels.Length - 1;
        return Tabs(labels, contents, selected, foreground: ctx.Arg("foreground", Tw.Slate200), width: 380);
    }
}
