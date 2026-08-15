using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Workbench × 実ファイル (ToDo 26 WS-D の D2) — AssetBrowser → IDocumentStore.Open →
/// タブで編集 → Ctrl+S で保存 → 外部変更の検知と再読込。ファイル IO は IFileStorage
/// (デモは MemoryFileStorage、実機は PhysicalFileStorage に差し替えるだけ)。</summary>
[StoryMeta("Examples/Workbench")]
public static class WorkbenchFilesStory
{
    private sealed class TextProvider(string kind, Func<Signal<string>, TextEditorView> viewFactory) : IDocumentProvider
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public IEditorDocument CreateNew() => new TextDocument(kind, "無題", viewFactory);
    }

    private static TextEditorView CodeView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static TextEditorView PlainView(Signal<string> text)
    {
        TextEditorView v = TextEditorView(text, editorHeight: 240f, editorWidth: 400f);
        v.Fill = true;
        v.EditorFont = Luxel.Editor.Gallery.StoryKit.EditorFaces.Value.Mono;
        v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
        return v;
    }

    private static string KindOf(string path) => System.IO.Path.GetExtension(path) switch
    {
        ".cs" => "code",
        _ => "text",
    };

    [Story]
    public static Widget Files(StoryContext ctx)
    {
        // ---- ストレージ (デモはメモリ — 決定的。実機は PhysicalFileStorage) ----
        var fs = new MemoryFileStorage();
        fs.Write("src/main.cs", "var x = 1;\nConsole.WriteLine(x);\n");
        fs.Write("src/util.cs", "static int Add(int a, int b) => a + b;\n");
        fs.Write("notes/todo.txt", "- 保存フローを試す\n");

        var ws = new Workspace();
        ws.RegisterProvider(new TextProvider("code", CodeView));
        ws.RegisterProvider(new TextProvider("text", PlainView));
        var store = new DocumentStore(ws, fs);

        // ---- タブ id = path。AssetBrowser → store.Open → DockTree へタブ追加 ----
        var docs = new Dictionary<string, IEditorDocument>();
        var tree = new Signal<DockTree>(DockTree.Single());
        void OpenPath(string path)
        {
            if (docs.ContainsKey(path))
            {
                tree.Value = tree.Value.ActivateTab(path);
                return;
            }
            IEditorDocument doc = store.Open(KindOf(path), path);
            ((TextDocument)doc).Title = System.IO.Path.GetFileName(path);
            docs[path] = doc;
            tree.Value = tree.Value.AddTab(tree.Value.Groups.First().Id, path);
        }
        OpenPath("notes/todo.txt");

        // アクティブタブ → Workspace.Active 同期
        Reactive.Effect(() =>
        {
            DockGroup? g = tree.Value.Groups.FirstOrDefault();
            if (g is { Active: >= 0 } && g.Active < g.Tabs.Count && docs.TryGetValue(g.Tabs[g.Active], out IEditorDocument? d))
                ws.Activate(d);
        });

        // ---- コマンド: 保存 / 再読込 (外部変更時のみ活性) ----
        DocumentBinding? ActiveBinding() => ws.Active.Peek() is { } d ? store.BindingOf(d) : null;
        var reg = new CommandRegistry();
        reg.Register("file.save", "保存", () =>
        {
            if (ws.Active.Peek() is not { } d) return;
            store.Save(d);
            ctx.Log($"save: {store.BindingOf(d)?.Path}");
        }, enabled: () => ws.Active.Peek()?.Dirty.Peek() == true, key: "Ctrl+S", toolbar: true);
        reg.Register("file.reload", "再読込", () =>
        {
            if (ws.Active.Peek() is { } d) store.Reload(d);
        }, enabled: () => ActiveBinding()?.ExternalChange.Peek() == true, key: "Ctrl+R", toolbar: true);

        DockItem Resolve(string id) => new(docs[id].Title, () => docs[id].CreateView(), docs[id].Dirty);
        DockHost host = DockHost(tree, Resolve,
            onCloseTab: (_, id) => { if (docs.Remove(id, out IEditorDocument? d)) ws.Close(d); });
        AssetBrowser browser = AssetBrowser(fs, expanded: new HashSet<string> { "src", "notes" },
            onOpen: (_, path) => OpenPath(path));
        Toolbar toolbar = Toolbar(reg);
        // ダーティ/外部変更/アクティブ切替で enablement を再評価
        Reactive.Effect(() =>
        {
            _ = ws.AnyDirty.Value;
            _ = ws.Active.Value;
            foreach (IEditorDocument d in ws.Documents)
                _ = store.BindingOf(d)?.ExternalChange.Value;
            toolbar.Refresh();
        });
        StatusBar status = StatusBar(right: [Badge("MemoryFileStorage", Intent.Primary)]);
        // 左セグメントはライブ (アクティブ doc の path + 外部変更バッジ) — getter 束縛で Build が追従
        status.Left.SetBase(new Bindable<Widget[]>(() => ws.Active.Value is { } d && store.BindingOf(d) is { } b
            ? [Muted(b.Path), .. b.ExternalChange.Value ? new Widget[] { Badge("外部変更", Intent.Warning) } : []]
            : [Muted("(未選択)")]));

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
            await d.Snap();                                          // ブラウザ + todo.txt タブ
            // ブラウザから main.cs を開く → コードタブが増える
            Widget leaf = FindLink(browser, "main.cs")!;
            await d.Click(leaf);
            await d.Expect(() => store.DocAt("src/main.cs") is not null, "AssetBrowser → store.Open");
            await d.Snap("opened");
            // 編集 → ダーティ → Ctrl+S で実ストレージへ書ける
            Widget view = host.ViewOf("src/main.cs")!;
            await d.Click(view.WorldPos.X + 40, view.WorldPos.Y + 10);
            await d.Type("// saved\n");
            await d.Expect(() => docs["src/main.cs"].Dirty.Value, "編集でダーティ");
            await d.Key(Key.S, ctrl: true);
            await d.Expect(() => fs.Read("src/main.cs")!.Contains("// saved") && !docs["src/main.cs"].Dirty.Value,
                           "Ctrl+S = store.Save → 実ストレージへ");
            await d.Snap("saved");
            // 外部変更 → ステータスにバッジ + 再読込で取り込み
            fs.Write("src/main.cs", "// 外部で書き換え\nvar y = 2;\n");
            await d.Expect(() => store.BindingOf(docs["src/main.cs"])!.ExternalChange.Value, "外部変更を検知");
            await d.Snap("external");
            await d.Key(Key.R, ctrl: true);
            await d.Expect(() => ((TextDocument)docs["src/main.cs"]).Text.Value.Contains("外部で書き換え"),
                           "再読込で取り込み");
            await d.Snap("reloaded");
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
