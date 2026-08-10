using System.Numerics;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>新ドメインを「構成だけ」で追加 (ToDo 26 WS-D の D3) — **マテリアルグラフ**
/// (NodeGraphDocument + INodeCatalog) と **Slang シェーダ** (TextDocument + hlsl 文法) が、
/// 新しいエディタ実装ゼロで Workbench に載る。エディタ＝構成の実証。</summary>
public static class WorkbenchMaterialStory
{
    // ---- マテリアルグラフのドメイン定義 (カタログ = ノード種別の宣言だけ) ----

    private static GraphNode Mat(int id, string kind, string title, Vector2 pos, bool input, bool output)
    {
        var ports = new List<NodePort>();
        if (kind == "multiply")
            ports.AddRange([new NodePort(0, PortDir.In, "color", "a"), new NodePort(1, PortDir.In, "color", "b"),
                            new NodePort(2, PortDir.Out, "color", "out")]);
        else
        {
            if (input) ports.Add(new NodePort(0, PortDir.In, "color", "in"));
            if (output) ports.Add(new NodePort(input ? 1 : 0, PortDir.Out, "color", "out"));
        }
        return new GraphNode(id, kind, title, pos, ports);
    }

    private static readonly INodeCatalog MaterialCatalog = new NodeCatalog(
        new NodeCatalogEntry("texture", "Texture", (id, pos) => Mat(id, "texture", "Texture", pos, false, true)),
        new NodeCatalogEntry("color", "Color", (id, pos) => Mat(id, "color", "Color", pos, false, true)),
        new NodeCatalogEntry("multiply", "Multiply", (id, pos) => Mat(id, "multiply", "Multiply", pos, true, true)),
        new NodeCatalogEntry("output", "Output", (id, pos) => Mat(id, "output", "Output", pos, true, false)));

    private static NodeGraphDoc SampleMaterial() => NodeGraphDoc.Of(
        [Mat(1, "texture", "Texture", new Vector2(30, 40), false, true),
         Mat(2, "multiply", "Multiply", new Vector2(220, 80), true, true),
         Mat(3, "output", "Output", new Vector2(420, 110), true, false)],
        [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0)),
         new GraphEdge(11, new PortId(2, 2), new PortId(3, 0))]);

    private sealed class MaterialProvider : IDocumentProvider
    {
        public string Kind => "material";
        public string DisplayName => "マテリアルグラフ";
        public IEditorDocument CreateNew()
            => new NodeGraphDocument("無題", NodeGraphDoc.Of([], []),
                configure: v => v.NodeCatalog = MaterialCatalog, kind: "material");
    }

    // ---- Slang シェーダ (TextDocument + hlsl 文法 = 構成差だけ) ----

    private static TextEditorView SlangView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "hlsl", () => UiTheme.T));
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private sealed class SlangProvider : IDocumentProvider
    {
        public string Kind => "slang";
        public string DisplayName => "Slang シェーダ";
        public IEditorDocument CreateNew() => new TextDocument("slang", "無題", SlangView);
    }

    private static string KindOf(string path) => System.IO.Path.GetExtension(path) switch
    {
        ".graph" => "material",
        ".slang" => "slang",
        _ => "slang",
    };

    [Story("Examples/Workbench/Material", Height = 470)]
    public static Widget Material(StoryContext ctx)
    {
        var fs = new MemoryFileStorage();
        fs.Write("mat/wood.graph", NodeGraphJson.Serialize(SampleMaterial()));
        fs.Write("shaders/surface.slang", "float4 main(float2 uv : TEXCOORD) : SV_Target\n{\n    float3 albedo = tex.Sample(s, uv).rgb;\n    return float4(albedo, 1.0);\n}\n");

        var ws = new Workspace();
        ws.RegisterProvider(new MaterialProvider());
        ws.RegisterProvider(new SlangProvider());
        var store = new DocumentStore(ws, fs);

        var docs = new Dictionary<string, IEditorDocument>();
        var tree = new Signal<DockTree>(DockTree.Single());
        void OpenPath(string path)
        {
            if (docs.ContainsKey(path)) { tree.Value = tree.Value.ActivateTab(path); return; }
            IEditorDocument doc = store.Open(KindOf(path), path);
            switch (doc)
            {
                case TextDocument t: t.Title = System.IO.Path.GetFileName(path); break;
                case NodeGraphDocument n: n.Title = System.IO.Path.GetFileName(path); break;
            }
            docs[path] = doc;
            tree.Value = tree.Value.AddTab(tree.Value.Groups.First().Id, path);
        }
        OpenPath("mat/wood.graph");

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

        DockItem Resolve(string id) => new(docs[id].Title, () => docs[id].CreateView(), docs[id].Dirty);
        DockHost host = DockHost(tree, Resolve,
            onCloseTab: (_, id) => { if (docs.Remove(id, out IEditorDocument? d)) ws.Close(d); });
        AssetBrowser browser = AssetBrowser(fs, expanded: new HashSet<string> { "mat", "shaders" },
            onOpen: (_, path) => OpenPath(path));
        Toolbar toolbar = Toolbar(reg);
        Reactive.Effect(() => { _ = ws.AnyDirty.Value; _ = ws.Active.Value; toolbar.Refresh(); });
        StatusBar status = StatusBar(right: [Badge("エディタ＝構成", Intent.Success)]);
        status.Left.SetBase(new Bindable<Widget[]>(() => ws.Active.Value is { } d && store.BindingOf(d) is { } b
            ? [Muted(b.Path), Muted($"kind: {d.Kind}")] : [Muted("(未選択)")]));

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
        Grid root = Grid(columns: [GridLength.Px(170), GridLength.Star()])[side, main];
        root.HAlign.SetBase(Align.Stretch);
        root.VAlign.SetBase(Align.Stretch);

        ctx.Play(async d =>
        {
            reg.BindShortcuts(d.Host);
            await d.Snap();                                        // マテリアルグラフ (Texture→Multiply→Output)
            // 右クリックパレット → Color ノードを追加 (INodeCatalog = ドメインの宣言だけ)
            var g = (NodeGraphDocument)docs["mat/wood.graph"];
            var view = (NodeGraphView)host.ViewOf("mat/wood.graph")!;
            Vector2 empty = view.ClientOf(new Vector2(120, 260));
            d.Host.ContextClick(empty.X, empty.Y);
            await d.Step(2);
            await d.Snap("palette");                               // Texture / Color / Multiply / Output
            await d.Click(empty.X + 40, empty.Y + 44);             // 2 行目 "Color"
            await d.Expect(() => view.NodeCount == 4, "パレットからノード追加");
            await d.Expect(() => g.Dirty.Value, "グラフ編集でダーティ");
            // Ctrl+S → JSON がストレージへ (NodeGraphJson 往復)
            await d.Key(Key.S, ctrl: true);
            await d.Expect(() => fs.Read("mat/wood.graph")!.Contains("\"color\"") && !g.Dirty.Value,
                           "保存で JSON がストレージへ");
            await d.Snap("saved");
            // Slang シェーダ (hlsl 文法) も構成だけで開ける
            Widget leaf = FindLink(browser, "surface.slang")!;
            await d.Click(leaf);
            await d.Expect(() => store.DocAt("shaders/surface.slang") is not null, "slang を開く");
            await d.Snap("slang");
        });

        return root;
    }

    private static Widget? FindLink(Widget root, string label)
    {
        if (root is LinkText lt && lt.Text.Or("") == label) return root;
        foreach (Widget c in root.DebugChildren())
            if (FindLink(c, label) is { } hit) return hit;
        return null;
    }
}
