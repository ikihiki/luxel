using System.Numerics;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Workbench シェル (ToDo 26 WS-D の D1) — 既存 4 view (Code / Markdown / Strudel /
/// NodeGraph) を IEditorDocument でラップし、MenuBar + Toolbar + DockHost + StatusBar の
/// 1 シェルに束ねる最小構成。レイアウトの真実 = DockTree、コマンドの真実 = CommandRegistry、
/// ドキュメントの真実 = 各アダプタ (エディタ＝構成、内部モデルは統一しない)。</summary>
public static class WorkbenchShellStory
{
    private static TextEditorView CodeView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static TextEditorView StrudelView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static TextEditorView MarkdownView(Signal<string> text)
    {
        (VectorFont? bold, _, _, VectorFont? mono) = StoryKit.EditorFaces.Value;
        return MarkdownDoc.Create(text, () => UiTheme.T, 400, 240,
            bold: bold, mono: mono, fill: true, editable: true);
    }

    private static NodeGraphDoc SampleGraph() => NodeGraphDoc.Of(
        [new GraphNode(1, "source", "Input", new Vector2(30, 40), [new NodePort(0, PortDir.Out, "v", "value")]),
         new GraphNode(2, "op", "Scale", new Vector2(230, 90), [new NodePort(0, PortDir.In, "v", "in"), new NodePort(1, PortDir.Out, "v", "out")]),
         new GraphNode(3, "sink", "Output", new Vector2(430, 60), [new NodePort(0, PortDir.In, "v", "in")])],
        [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0)),
         new GraphEdge(11, new PortId(2, 1), new PortId(3, 0))]);

    [Story("Examples/Workbench/Shell", Height = 470)]
    public static Widget Shell(StoryContext ctx)
    {
        // ---- ドキュメント 4 種 (エディタ＝構成: 同じ TextDocument でも viewFactory が違うだけ) ----
        var code = new TextDocument("code", "Main.cs", CodeView,
            "using Luxel;\n\nvar app = App.Create();\napp.Run();\n");
        var readme = new TextDocument("markdown", "readme.md", MarkdownView,
            "# Workbench\n\n**4 種**のエディタを 1 シェルに束ねる。\n\n- Code\n- Markdown\n- Strudel\n- NodeGraph\n");
        var beat = new TextDocument("strudel", "beat.strudel", StrudelView,
            "s(\"bd sd bd sd\")\n  .fast(2)\n");
        var graph = new NodeGraphDocument("flow.graph", SampleGraph());

        var ws = new Workspace();
        var docs = new Dictionary<string, IEditorDocument>
        { ["code"] = code, ["readme"] = readme, ["beat"] = beat, ["graph"] = graph };
        foreach (IEditorDocument d in docs.Values) ws.Open(d);
        ws.Activate(code);

        // ---- レイアウト (真実 = DockTree) ----
        var tree = new Signal<DockTree>(DockTree.Single("code", "readme", "beat", "graph"));
        DockItem Resolve(string id) => new(docs[id].Title, () => docs[id].CreateView(), docs[id].Dirty);
        // アクティブタブ → Workspace.Active の同期 (D1 = 単一グループ想定。複数グループの
        // フォーカス追跡は Gallery 統合で)
        Reactive.Effect(() =>
        {
            DockGroup? g = tree.Value.Groups.FirstOrDefault();
            if (g is { Active: >= 0 } && g.Active < g.Tabs.Count && docs.TryGetValue(g.Tabs[g.Active], out IEditorDocument? d))
                ws.Activate(d);
        });

        // ---- コマンド (真実 = CommandRegistry) ----
        var reg = new CommandRegistry();
        reg.Register("file.save", "保存", () =>
        {
            if (ws.Active.Peek() is not { } d) return;
            d.Serialize();                       // D1: 保存点の更新のみ (実ファイルは D2 の IDocumentStore)
            d.Dirty.Value = false;
            ctx.Log($"save: {d.Title}");
        }, enabled: () => ws.Active.Peek()?.Dirty.Peek() == true, key: "Ctrl+S", menuPath: "File/保存", toolbar: true);
        reg.Register("edit.undo", "元に戻す", () => ws.Undo(), enabled: () => ws.CanUndo, menuPath: "Edit/元に戻す");
        reg.Register("edit.redo", "やり直す", () => ws.Redo(), enabled: () => ws.CanRedo, menuPath: "Edit/やり直す");

        // ---- シェル chrome (すべて registry / tree / workspace のビュー) ----
        MenuBar menuBar = MenuBar(reg, contributions: () => ws.Active.Value?.Contributions ?? []);
        Toolbar toolbar = Toolbar(reg, contributions: () => ws.Active.Value?.Contributions ?? []);
        DockHost host = DockHost(tree, Resolve, closeRemoves: true,
            onCloseTab: (_, id) => { if (docs.TryGetValue(id, out IEditorDocument? d)) ws.Close(d); });
        StatusBar status = StatusBar(
            left: [Muted("Workbench D1"), Muted("4 docs")],
            right: [Badge("Ready", Intent.Success)]);

        // enablement (保存の活性) はダーティ変化で再評価
        Reactive.Effect(() => { _ = ws.AnyDirty.Value; _ = ws.Active.Value; toolbar.Refresh(); });

        menuBar.GridRow(0);
        toolbar.GridRow(1);
        host.GridRow(2);
        status.GridRow(3);
        Grid shell = Grid(rows: [GridLength.Px(Luxel.Controls.MenuBar.BarH), GridLength.Px(34),
                                 GridLength.Star(), GridLength.Px(Luxel.Controls.StatusBar.BarH)])[
            menuBar, toolbar, host, status];
        shell.HAlign.SetBase(Align.Stretch);
        shell.VAlign.SetBase(Align.Stretch);

        ctx.Play(async d =>
        {
            reg.BindShortcuts(d.Host);                          // keymap を UiHost へ常設 (シェル配線)
            await d.Snap();                                     // code タブ + シンタックス色 + chrome 一式
            // markdown タブへ → live preview の整形が見える
            var t = host.TabCenter("readme")!.Value;
            await d.Click(t.X, t.Y);
            await d.Expect(() => ReferenceEquals(ws.Active.Value, readme), "タブ切替が Workspace.Active に同期");
            await d.Snap("readme");
            // エディタに入力 → ダーティ ● が点く
            Widget view = host.ViewOf("readme")!;
            await d.Click(view.WorldPos.X + 60, view.WorldPos.Y + 12);
            await d.Type("追記 ");
            await d.Expect(() => readme.Dirty.Value, "編集でダーティ");
            await d.Snap("dirty");
            // Ctrl+S (registry keymap 経由、エディタは消費しない) → 保存されて ● が消える
            await d.Key(Key.S, ctrl: true);
            await d.Expect(() => !readme.Dirty.Value, "Ctrl+S = file.save (keymap → registry)");
            // node graph タブ → ノード編集が同じシェルに載る
            var g = host.TabCenter("graph")!.Value;
            await d.Click(g.X, g.Y);
            await d.Expect(() =>
            {
                DockGroup gr = tree.Value.Groups.First();
                return gr.Active >= 0 && gr.Tabs[gr.Active] == "graph";
            }, "graph タブへ切替 (tree)");
            await d.Expect(() => ReferenceEquals(ws.Active.Value, graph), "graph タブへ切替 (workspace)");
            await d.Step(2);
            await d.Snap("graph");
        });

        return shell;
    }
}
