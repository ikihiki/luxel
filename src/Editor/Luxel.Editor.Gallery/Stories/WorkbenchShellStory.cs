using System.Numerics;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Controls.EditorKit;

namespace Luxel.Gallery.Stories;

/// <summary>Workbench シェル (ToDo 26 WS-D の D1) — 既存 4 view (Code / Markdown / Strudel /
/// NodeGraph) を IEditorDocument でラップし、MenuBar + Toolbar + DockHost + StatusBar の
/// 1 シェルに束ねる最小構成。レイアウトの真実 = DockTree、コマンドの真実 = CommandRegistry、
/// ドキュメントの真実 = 各アダプタ (エディタ＝構成、内部モデルは統一しない)。</summary>
[StoryMeta("Examples/Workbench")]
public static class WorkbenchShellStory
{
    private static TextEditorView CodeView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static TextEditorView StrudelView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static TextEditorView MarkdownView(Signal<string> text)
    {
        (VectorFont? bold, _, _, VectorFont? mono) = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value;
        return MarkdownDoc.Create(text, () => UiTheme.T, 400, 240,
            bold: bold, mono: mono, fill: true, editable: true);
    }

    private static NodeGraphDoc SampleGraph() => NodeGraphDoc.Of(
        [new GraphNode(1, "source", "Input", new Vector2(30, 40), [new NodePort(0, PortDir.Out, "v", "value")]),
         new GraphNode(2, "op", "Scale", new Vector2(230, 90), [new NodePort(0, PortDir.In, "v", "in"), new NodePort(1, PortDir.Out, "v", "out")]),
         new GraphNode(3, "sink", "Output", new Vector2(430, 60), [new NodePort(0, PortDir.In, "v", "in")])],
        [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0)),
         new GraphEdge(11, new PortId(2, 1), new PortId(3, 0))]);

    [Story]
    public static StoryResult Shell(StoryContext ctx)
    {
        // ---- ドキュメント 4 種 (エディタ＝構成: 同じ TextDocument でも viewFactory が違うだけ) ----
        var code = new TextDocument("code", "Main.cs", CodeView,
            "using Luxel;\n\nvar app = App.Create();\napp.Run();\n");
        var readme = new TextDocument("markdown", "readme.md", MarkdownView,
            "# Workbench\n\n**4 種**のエディタを 1 シェルに束ねる。\n\n- Code\n- Markdown\n- Strudel\n- NodeGraph\n");
        var beat = new TextDocument("strudel", "beat.strudel", StrudelView,
            "s(\"bd sd bd sd\")\n  .fast(2)\n");
        var graph = new NodeGraphDocument("flow.graph", SampleGraph());

        var docs = new Dictionary<string, IEditorDocument>
        { ["code"] = code, ["readme"] = readme, ["beat"] = beat, ["graph"] = graph };
        var session = new EditorSession(
            docs, DockTree.Single("code", "readme", "beat", "graph"));
        Workspace ws = session.Workspace;
        Signal<DockTree> tree = session.Layout;

        // ---- コマンド (真実 = EditorSession.Commands) ----
        CommandRegistry reg = session.Commands;
        reg.Register(EditorCommandIds.Save, "保存", () =>
        {
            if (ws.Active.Peek() is not { } d) return;
            d.Serialize();                       // D1: 保存点の更新のみ (実ファイルは D2 の IDocumentStore)
            d.Dirty.Value = false;
            ctx.Log($"save: {d.Title}");
        }, enabled: () => ws.Active.Peek()?.Dirty.Peek() == true, key: "Ctrl+S", menuPath: "File/保存", toolbar: true);
        reg.Register(EditorCommandIds.Undo, "元に戻す", () => ws.Undo(), enabled: () => ws.CanUndo, menuPath: "Edit/元に戻す");
        reg.Register(EditorCommandIds.Redo, "やり直す", () => ws.Redo(), enabled: () => ws.CanRedo, menuPath: "Edit/やり直す");

        // ---- production の portable shell を Gallery fixture からそのまま起動 ----
        var fixture = new EditorTestFixture { Session = session, ProductName = "Workbench D1" };

        ctx.Play(async d =>
        {
            await d.Snap();                                     // code タブ + シンタックス色 + chrome 一式
            // markdown タブへ → live preview の整形が見える
            DockHost host = fixture.Shell!.DocumentsHost!;
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

        return fixture;
    }
}
