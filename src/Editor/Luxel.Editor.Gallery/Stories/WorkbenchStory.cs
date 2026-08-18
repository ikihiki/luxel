using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Controls.EditorKit;

namespace Luxel.Gallery.Stories;

/// <summary>Workbench 基盤コントロール (ADR-0014 / ToDo 26 S(C3)) — DocumentTabs / DockHost / StatusBar。
/// レイアウトの真実は Luxel.Workbench の DockTree (不変モデル)。DockHost はそれを描き、
/// タブ操作 (クリック/並べ替え/ドラッグ分割) は tree signal を書き換えて自動で組み直す。</summary>
[StoryMeta("Controls")]
public static class WorkbenchStory
{
    // ---- DocumentTabs 単体 (項目の増減と並べ替えはこのデモ側が所有) ----

    private sealed class TabsDemo : CompositeControl
    {
        private readonly Signal<int> _v = new(0);
        public readonly List<DocTab> Items;
        public string ActiveId = "readme";
        public DocumentTabs? Strip;
        public string Log = "";

        public TabsDemo(Signal<bool> dirty) => Items =
            [new DocTab("readme", "readme.md", dirty), new DocTab("main", "Main.cs"), new DocTab("graph", "flow.graph")];

        protected override Widget Build()
        {
            _ = _v.Value;   // 追跡 — 構造変化 (開閉/並べ替え) で自動 Rebuild
            Strip = DocumentTabs(Items.ToArray(), active: ActiveId, width: 430,
                onActivate: (_, id) => { ActiveId = id; _v.Value++; },
                onClose: (_, id) => { Items.RemoveAll(t => t.Id == id); Log = $"close:{id}"; _v.Value++; },
                onDropTab: (_, id, index) =>
                {
                    int old = Items.FindIndex(t => t.Id == id);
                    if (old < 0) return;
                    DocTab tab = Items[old];
                    Items.RemoveAt(old);
                    Items.Insert(Math.Clamp(old < index ? index - 1 : index, 0, Items.Count), tab);
                    ActiveId = id;
                    _v.Value++;
                });
            return Strip;
        }
    }

    [Story(Path = "Controls/Collections/DocumentTabs/Basic")]
    public static StoryResult DocumentTabsBasic(StoryContext ctx)
    {
        var dirty = ctx.Signal("dirty", true);
        var demo = new TabsDemo(dirty);

        ctx.Play(async d =>
        {
            await d.Snap();                                       // readme アクティブ + ダーティ ●
            await d.Click(demo.Strip!.TabCenterOf("main")!.Value.X, demo.Strip.TabCenterOf("main")!.Value.Y);
            await d.Expect(() => demo.ActiveId == "main", "クリックでアクティブ切替");
            await d.Snap("active");
            var close = demo.Strip!.CloseCenterOf("graph")!.Value;
            await d.Click(close.X, close.Y);                      // × で閉じ要求 → デモが除去
            await d.Expect(() => demo.Items.Count == 2 && demo.Log == "close:graph", "× で閉じる");
            var from = demo.Strip!.TabCenterOf("main")!.Value;
            var to = demo.Strip.TabCenterOf("readme")!.Value;
            await d.Drag(from.X, from.Y, to.X - 20, to.Y);        // D&D で先頭へ並べ替え
            await d.Expect(() => demo.Items[0].Id == "main", "D&D 並べ替え");
            await d.Snap("reordered");
        });

        return VStack(10)[
            Heading("DocumentTabs — ダーティ ● / × 閉じ / D&D 並べ替え"),
            Muted("タブをドラッグすると並べ替え。readme.md はダーティ (●)。"),
            demo];
    }

    // ---- DockHost (DockTree を描く) ----

    private static Widget Pane(string label, uint color) =>
        Border(background: color, rounded: 4f, padding: new Thickness(10), hAlign: Align.Stretch, vAlign: Align.Stretch)[
            Muted(label)];

    [Story(Path = "Controls/Editor/DockHost/Basic")]
    public static StoryResult DockHostBasic(StoryContext ctx)
    {
        var dirty = ctx.Signal("dirty", true);
        var tree = new Signal<DockTree>(DockTree.Single("readme", "main", "graph"));
        DockItem Resolve(string id) => id switch
        {
            "readme" => new DockItem("readme.md", () => Pane("readme.md — ドキュメント", 0x22808080), dirty),
            "main" => new DockItem("Main.cs", () => Pane("Main.cs — コード", 0x2260A060)),
            _ => new DockItem("flow.graph", () => Pane("flow.graph — ノード", 0x226080C0)),
        };
        DockHost host = DockHost(tree, Resolve, width: 640, height: 360);

        ctx.Play(async d =>
        {
            await d.Snap();                                        // 1 グループ 3 タブ
            // "main" タブを右端へドラッグ → 右半分に分割 (ドロップゾーン)
            var from = host.TabCenter("main")!.Value;
            await d.Drag(from.X, from.Y, host.WorldPos.X + host.Size.Width - 24, host.WorldPos.Y + 180, moves: 12);
            await d.Expect(() => tree.Value.Root is DockSplit { Horizontal: true }, "右端ドロップで横分割");
            await d.Snap("docked");
            // スプリッタを左へ 120px → 左ペインが縮む
            var s = (DockSplit)tree.Value.Root;
            float thick = Luxel.Controls.Splitter.Thickness;
            float availW = host.Size.Width - thick;
            float sx = host.WorldPos.X + s.Sizes[0] * availW + thick / 2;
            float sy = host.WorldPos.Y + 200;
            await d.Drag(sx, sy, sx - 120, sy);
            await d.Expect(() => ((DockSplit)tree.Value.Root).Sizes[0] < 0.45f, "スプリッタで縮む");
            await d.Snap("resized");
            // "graph" タブを右グループの帯へドラッグ → グループ間移動
            var g = host.TabCenter("graph")!.Value;
            var target = host.TabCenter("main")!.Value;
            await d.Drag(g.X, g.Y, target.X + 40, target.Y, moves: 12);
            await d.Expect(() => tree.Value.GroupOf("graph")!.Id == tree.Value.GroupOf("main")!.Id, "帯へドロップでグループ間移動");
            await d.Snap("moved");
        });

        return host;
    }

    [Story(Path = "Controls/Editor/DockHost/Examples/Floating")]
    public static StoryResult DockHostFloating(StoryContext ctx)
    {
        // "graph" を最初から窓内フロートにしたレイアウト
        var tree = new Signal<DockTree>(
            DockTree.Single("readme", "main", "graph").Float("graph", 300, 120, 260, 190));
        DockItem Resolve(string id) => id switch
        {
            "readme" => new DockItem("readme.md", () => Pane("readme.md — ドキュメント", 0x22808080)),
            "main" => new DockItem("Main.cs", () => Pane("Main.cs — コード", 0x2260A060)),
            _ => new DockItem("flow.graph", () => Pane("flow.graph — ノード", 0x226080C0)),
        };
        DockHost host = DockHost(tree, Resolve, width: 640, height: 360);

        ctx.Play(async d =>
        {
            await d.Snap();                                   // ドックの上にフロートが浮く
            // つかみバーをドラッグ → フロートが動く (背面ドックのヒットに勝つ = ヒットレイヤ)
            DockFloat fl = tree.Value.Floats[0];
            float gx = host.WorldPos.X + fl.X + fl.W / 2 - 20;
            float gy = host.WorldPos.Y + fl.Y + 7;
            await d.Drag(gx, gy, gx - 120, gy - 60);
            await d.Expect(() => tree.Value.Floats[0].X < 300 && tree.Value.Floats[0].Y < 120, "つかみバーで移動");
            await d.Snap("moved");
            // ドックのタブ "main" をフロートの帯へドラッグ → フロートへ移動 (分割しない)
            var from = host.TabCenter("main")!.Value;
            var to = host.TabCenter("graph")!.Value;
            await d.Drag(from.X, from.Y, to.X + 50, to.Y, moves: 12);
            int floatGid = tree.Value.Floats[0].Group.Id;
            await d.Expect(() => tree.Value.GroupOf("main")!.Id == floatGid, "帯ドロップでフロートへ移動");
            await d.Snap("gathered");
            // フロートのタブ "graph" をドック右端へ → フロートから出て横分割
            var g = host.TabCenter("graph")!.Value;
            await d.Drag(g.X, g.Y, host.WorldPos.X + host.Size.Width - 20, host.WorldPos.Y + 200, moves: 12);
            await d.Expect(() => tree.Value.Root is DockSplit { Horizontal: true }
                              && tree.Value.FloatOf(tree.Value.GroupOf("graph")!.Id) is null, "フロートからドックへ分割");
            await d.Snap("redocked");
        });

        return host;
    }

    /// <summary>DebugChildren を辿って指定ラベルの LinkText を探す (play 用)。</summary>
    private static Widget? FindLink(Widget root, string label)
    {
        if (root is LinkText lt && lt.Text.Or("") == label) return root;
        foreach (Widget c in root.DebugChildren())
            if (FindLink(c, label) is { } hit) return hit;
        return null;
    }

    // ---- PropertyGrid (Inspector) ----

    private enum Quality { Low, Medium, High }

    private sealed class ParticleConfig
    {
        public bool Visible { get; set; } = true;
        [PropertyRange(0, 1)] public float Opacity { get; set; } = 0.65f;
        public int Count { get; set; } = 500;
        [PropertyGroup("見た目")] public uint Tint { get; set; } = 0xFF4A90D9;
        [PropertyGroup("見た目")] public Quality Level { get; set; } = Quality.Medium;
        [PropertyGroup("配置")] public System.Numerics.Vector2 Offset { get; set; } = new(16, 24);
        [PropertyGroup("配置")] public string Layer { get; set; } = "front";
    }

    [Story(Path = "Controls/Editor/PropertyGrid/Basic")]
    public static StoryResult PropertyGridBasic(StoryContext ctx)
    {
        var cfg = new ParticleConfig();
        string lastChange = "";
        PropertyGrid grid = PropertyGrid(cfg, width: 330,
            onChanged: (_, name, v) => { lastChange = $"{name}={v}"; ctx.Log($"changed: {name} = {v}"); });

        ctx.Play(async d =>
        {
            await d.Snap();                                    // 型別エディタ + グループ見出し
            await d.Click(grid.EditorOf("Visible")!);          // Check をクリック → 対象へ即書き込み
            await d.Expect(() => !cfg.Visible && lastChange == "Visible=False", "編集が対象へ書き戻る");
            await d.Snap("edited");
        });

        return VStack(10)[
            Heading("PropertyGrid — 型別エディタで対象を編集"),
            Muted("bool=Check / 範囲 float=Slider / uint=ColorPicker / enum=Select / Vector2=軸別 / [PropertyGroup] 見出し"),
            grid];
    }

    // ---- AssetBrowser (IFileStorage × TreeView) ----

    [Story(Path = "Controls/Collections/AssetBrowser/Basic")]
    public static StoryResult AssetBrowserBasic(StoryContext ctx)
    {
        var fs = new MemoryFileStorage();
        fs.Write("readme.md", "# hi");
        fs.Write("src/Main.cs", "//");
        fs.Write("src/App.cs", "//");
        fs.Write("assets/logo.png", "png");
        fs.Write("assets/shaders/tri.slang", "//");
        string opened = "";
        AssetBrowser browser = AssetBrowser(fs, expanded: new HashSet<string> { "src", "assets" },
            onOpen: (_, path) => { opened = path; ctx.Log($"open: {path}"); });

        ctx.Play(async d =>
        {
            await d.Snap();                                    // フォルダ優先 + 展開済みツリー
            Widget leaf = FindLink(browser, "Main.cs")!;
            await d.Click(leaf);
            await d.Expect(() => opened == "src/Main.cs", "ファイルクリックで OnOpen(path)");
            await d.Snap("opened");
        });

        return VStack(10)[
            Heading("AssetBrowser — IFileStorage のファイルツリー"),
            Muted("フォルダ = 開閉見出し、ファイルクリック = OnOpen(path) → シェルが IDocumentStore.Open へ。"),
            browser];
    }

    // ---- StatusBar ----

    [Story(Path = "Controls/Editor/StatusBar/Basic")]
    public static StoryResult StatusBarBasic(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());
        return VStack(10)[
            Heading("StatusBar — 左右セグメント"),
            Muted("左 = ファイル情報、右 = カーソル位置 + 状態。セグメントは任意 widget。"),
            StatusBar(
                left: [Muted("Main.cs"), Muted("UTF-8"), Muted("C#")],
                right: [Muted("行 12, 桁 4"), Badge("Ready", Intent.Success)])];
    }
}
