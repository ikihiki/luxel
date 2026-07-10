using System.Numerics;
using System.Text.Json;
using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>PropertyGrid を使うエディタ (ToDo 26 WS-D の D4、ADR-0014 の実証) —
/// パーティクル設定 (.json) を <see cref="ObjectDocument{T}"/> + PropertyGrid で Inspector 編集し、
/// 保存/undo が他のエディタと同じ IEditorDocument 契約で回る。</summary>
public static class WorkbenchInspectorStory
{
    public enum Blend { Alpha, Additive, Multiply }

    /// <summary>デモ用パーティクル設定 (PropertyGrid が型から行を作る)。</summary>
    public sealed class SparkConfig
    {
        public bool Visible { get; set; } = true;
        [PropertyRange(0, 2)] public float Rate { get; set; } = 1.25f;
        public int Count { get; set; } = 200;
        [PropertyGroup("見た目")] public uint Tint { get; set; } = 0xFF66AACC;
        [PropertyGroup("見た目")] public Blend Mode { get; set; } = Blend.Additive;
        [PropertyGroup("配置")] public Vector2 Gravity { get; set; } = new(0, -9.8f);
        [PropertyGroup("配置")] public string Layer { get; set; } = "fx";
    }

    private sealed class ConfigProvider : IDocumentProvider
    {
        public string Kind => "config";
        public string DisplayName => "設定 (Inspector)";
        public IEditorDocument CreateNew() => new ObjectDocument<SparkConfig>("config", "無題", new SparkConfig());
    }

    [Story("Demos/Workbench/Inspector", Height = 470)]
    public static Widget Inspector(StoryContext ctx)
    {
        var fs = new MemoryFileStorage();
        fs.Write("fx/spark.json", JsonSerializer.Serialize(new SparkConfig(),
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));

        var ws = new Workspace();
        ws.RegisterProvider(new ConfigProvider());
        var store = new DocumentStore(ws, fs);

        var docs = new Dictionary<string, IEditorDocument>();
        var tree = new Signal<DockTree>(DockTree.Single());
        void OpenPath(string path)
        {
            if (docs.ContainsKey(path)) { tree.Value = tree.Value.ActivateTab(path); return; }
            IEditorDocument doc = store.Open("config", path);
            ((ObjectDocument<SparkConfig>)doc).Title = System.IO.Path.GetFileName(path);
            docs[path] = doc;
            tree.Value = tree.Value.AddTab(tree.Value.Groups.First().Id, path);
        }
        OpenPath("fx/spark.json");

        Reactive.Effect(() =>
        {
            DockGroup? g = tree.Value.Groups.FirstOrDefault();
            if (g is { Active: >= 0 } && g.Active < g.Tabs.Count && docs.TryGetValue(g.Tabs[g.Active], out IEditorDocument? d))
                ws.Activate(d);
        });

        var reg = new CommandRegistry();
        reg.Register("file.save", "保存", () =>
        {
            if (ws.Active.Peek() is { } d) { store.Save(d); ctx.Log($"save: {store.BindingOf(d)?.Path}"); }
        }, enabled: () => ws.Active.Peek()?.Dirty.Peek() == true, key: "Ctrl+S", toolbar: true);
        reg.Register("edit.undo", "元に戻す", () => ws.Undo(), enabled: () => ws.CanUndo, key: "Ctrl+Z", toolbar: true);
        reg.Register("edit.redo", "やり直す", () => ws.Redo(), enabled: () => ws.CanRedo, key: "Ctrl+Y", toolbar: true);

        DockItem Resolve(string id) => new(docs[id].Title, () => docs[id].CreateView(), docs[id].Dirty);
        DockHost host = DockHost(tree, Resolve,
            onCloseTab: (_, id) => { if (docs.Remove(id, out IEditorDocument? d)) ws.Close(d); });
        AssetBrowser browser = AssetBrowser(fs, expanded: new HashSet<string> { "fx" },
            onOpen: (_, path) => OpenPath(path));
        Toolbar toolbar = Toolbar(reg);
        Reactive.Effect(() => { _ = ws.AnyDirty.Value; _ = ws.Active.Value; toolbar.Refresh(); });
        StatusBar status = StatusBar(right: [Badge("ObjectDocument<SparkConfig>", Intent.Primary)]);
        status.Left.SetBase(new Bindable<Widget[]>(() => ws.Active.Value is { } d && store.BindingOf(d) is { } b
            ? [Muted(b.Path)] : [Muted("(未選択)")]));

        Widget side = Border(background: (Func<uint>)(() => UiTheme.T.Surface), padding: new Thickness(8, 6, 4, 0),
                             hAlign: Align.Stretch, vAlign: Align.Stretch)[browser];
        side.GridColumn(0);
        toolbar.GridRow(0);
        host.GridRow(1);
        status.GridRow(2);
        Grid main = Grid(rows: [GridLength.Px(34), GridLength.Star(), GridLength.Px(Luxel.Controls.StatusBar.BarH)])[
            toolbar, host, status];
        main.GridColumn(1);
        main.HAlign.SetBase(Align.Stretch);
        main.VAlign.SetBase(Align.Stretch);
        Grid root = Grid(columns: [GridLength.Px(150), GridLength.Star()])[side, main];
        root.HAlign.SetBase(Align.Stretch);
        root.VAlign.SetBase(Align.Stretch);

        ctx.Play(async d =>
        {
            reg.BindShortcuts(d.Host);
            var doc = (ObjectDocument<SparkConfig>)docs["fx/spark.json"];
            await d.Snap();                                          // Inspector (グループ/Slider/Color/enum/Vector2)
            // Visible を off → ダーティ + undo 活性
            var grid = (PropertyGrid)host.ViewOf("fx/spark.json")!;
            await d.Click(grid.EditorOf("Visible")!);
            await d.Expect(() => !doc.Target.Visible && doc.Dirty.Value, "PropertyGrid 編集が対象へ + ダーティ");
            await d.Snap("edited");
            // Ctrl+S → JSON がストレージへ
            await d.Key(Key.S, ctrl: true);
            await d.Expect(() => fs.Read("fx/spark.json")!.Contains("\"Visible\": false") && !doc.Dirty.Value,
                           "保存で JSON がストレージへ");
            // Ctrl+Z (registry keymap) → プロパティ単位 undo で Visible が戻り、保存内容と差分 = ダーティ再点灯
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => doc.Target.Visible && doc.Dirty.Value, "プロパティ単位 undo");
            await d.Snap("undone");
        });

        return root;
    }
}
