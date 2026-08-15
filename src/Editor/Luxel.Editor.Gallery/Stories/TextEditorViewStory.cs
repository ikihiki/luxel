using Luxel.Controls;
using Luxel.Document;
using Luxel.Strudel;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Editor.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>TextEditorView — テキストエディタ新スタック (ADR-0006 / ToDo 22) のビュー。
/// 編集意味論・座標写像・装飾は canvas 非依存の Luxel.Document が持ち、この widget は入力を Transaction にして
/// ジオメトリの矩形を塗るだけ。折返し・プロポーショナル・マルチカーソルはジオメトリ由来で最初から正しい。</summary>
[StoryMeta("Controls/TextEditorView")]
public static class TextEditorViewStory
{
    [Story]
    public static Widget Basic(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text",
            "新スタックのテキストエディタ。\nTransaction ベースで undo が正確、\nマルチカーソルが native。");
        TextEditorView ed = TextEditorView(value, editorHeight: 200f, editorWidth: 560f);

        ctx.Play("edit", async d =>
        {
            await d.Snap();                         // 初期テキスト + キャレット
            await d.Click(ed);                      // フォーカス
            await d.Key(Key.End);
            await d.Type(" 折返しも正しい。");
            await d.Expect(() => ed.Text.Contains("折返し"), "入力が反映される");
            await d.Snap("typed");
            await d.Key(Key.A, ctrl: true);         // 全選択
            await d.Expect(() => ed.HasSelection, "Ctrl+A で全選択");
            await d.Snap("selected");
        });
        ctx.Play("undo", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.End);
            await d.Type("XYZ");
            await d.Expect(() => ed.Text.Contains("XYZ"), "入力される");
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => !ed.Text.Contains("XYZ"), "undo で戻る");
            await d.Snap("undone");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView (新スタック)"),
                Muted("Luxel.Document (Transaction + native 複数レンジ + 装飾) を canvas に載せた薄いビュー。"),
                ed]];
    }

    [Story]
    public static Widget Code(StoryContext ctx, ICodeLanguage lang)
    {
        Signal<string> code = ctx.Signal("code",
            "// 新スタックのコードエディタ (プロバイダで色分け + 波線)\n" +
            "public int Fib(int n)\n" +
            "{\n" +
            "    if (n < 2) return n;\n" +
            "    return Fib(n - 1) + Fib(n - 2);\n" +
            "}\n" +
            "int broken = ;");
        TextEditorView ed = TextEditorView(code, editorHeight: 260f, editorWidth: 560f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;

        Func<Theme> theme = () => UiTheme.T;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", theme));
        var diags = new DiagnosticsProvider(lang, theme);
        ed.Providers.Add(diags);
        ed.Providers.Add(new CurrentLineProvider(theme));

        ctx.Play(async d =>
        {
            await d.Snap();                               // 色分け + 行番号 + 現在行 + "int broken = ;" に波線
            await d.Expect(() => diags.Count > 0, "エラー行に波線が付く");
            await d.Click(ed);
            await d.Key(Key.Down); await d.Key(Key.Down); await d.Key(Key.Down);
            await d.Snap("caret");                        // 現在行ハイライトが移動
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — コード (色分け + 診断波線 + ガター)"),
                Muted("SyntaxHighlightProvider / DiagnosticsProvider / CurrentLineProvider を Providers に足すだけ。"),
                ed]];
    }

    [Story]
    public static Widget Edit(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code", "int foo = 1;\nint bar = foo + foo;\nreturn foo * bar;");
        TextEditorView ed = TextEditorView(code, editorHeight: 190f, editorWidth: 520f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));

        Signal<string> query = ctx.Signal("find", "");
        TextField findBar = TextField(query, placeholder: "find", width: 140);
        Widget bar = HStack(6)[
            findBar,
            Button(_ => ed.SetSearch(query.Value), "検索", fontSize: 12f),
            Button(_ => ed.FindNext(), "›", fontSize: 12f),
            Button(_ => ed.ReplaceAll("qux"), "全置換→qux", fontSize: 12f)];

        ctx.Play("line-ops", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.Down);                          // 2 行目へ
            await d.Key(Key.Down, alt: true);               // 行を下へ移動
            await d.Key(Key.Down, shift: true, alt: true);  // 行を複製
            await d.Key(Key.Slash, ctrl: true);             // コメントトグル
            await d.Expect(() => ed.Text.Contains("//"), "行操作が反映される");
            await d.Snap("line-ops");
        });
        ctx.Play("search", async d =>
        {
            ed.SetSearch("foo");
            await d.Step(1);
            await d.Expect(() => ed.SearchMatchCount == 4, "4 マッチをハイライト");
            await d.Snap("matches");
            ed.ReplaceAll("qux");
            await d.Step(1);
            await d.Expect(() => ed.Text.Contains("qux") && !ed.Text.Contains("foo"), "全置換される");
            await d.Snap("replaced");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 行操作 / 検索置換"),
                Muted("Alt+↑↓ 行移動 / Shift+Alt+↓ 複製 / Ctrl+/ コメント。検索は背景ハイライト + ナビ + 全置換。"),
                bar,
                ed]];
    }

    // Strudel の再生囲み + 行内スライダを駆動する小さなルート (beat 変化でパターンを点クエリ → Mark.Box)
    private sealed class StrudelDemoRoot(TextEditorView ed, Pattern<string> pat, Signal<int> beat, uint boxColor) : CompositeControl
    {
        protected override Widget Build() => ed;

        protected override void OnRealize(UiBuildContext ctx)
        {
            // 行内スライダ (数値 "0.8" を置換 widget に) — realize 後に設定 (EnsureInit が _state を作った後)
            ed.SetDecorations("widget", new DecorationSet([new WidgetDecoration(0, 3, 64f, 18f, "gain")]));
            ctx.Effect(() =>
            {
                int b = beat.Value;                                   // beat 変化で購読・再計算
                var now = new Fraction(2 * b + 1, 8);                 // トークン中央 (4 トークン/サイクル)
                var boxes = pat.ActiveAt(now)
                    .Select(sp => (Decoration)new MarkDecoration(sp.Start, sp.End, Box: new BoxStyle(boxColor)))
                    .ToList();
                ed.SetDecorations("playing", new DecorationSet(boxes));   // レイアウト非依存 = 行キャッシュに触れない
            });
        }
    }

    [Story]
    public static Widget Strudel(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code", "0.8 bd sd hh");     // "0.8"[0,3) bd[4,6) sd[7,9) hh[10,12)
        TextEditorView ed = TextEditorView(code, editorHeight: 90f, editorWidth: 420f);
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;

        // 行内スライダ: 数値 "0.8" を置換 widget に (Strudel の「行内 UI コントロール」要件)
        Signal<float> gain = ctx.Signal("gain", 0.8f);
        Slider slider = Slider(gain, min: 0f, max: 1f);
        ed.WidgetResolver = key => key as string == "gain" ? slider : null;   // 装飾は root の OnRealize で設定

        Pattern<string> pat = MiniNotation.Parse(code.Peek());
        Signal<int> beat = ctx.Signal("beat", 1);
        var root = new StrudelDemoRoot(ed, pat, beat, 0xFF4A90D9);

        ctx.Play(async d =>
        {
            await d.Step(1);
            await d.Snap("beat-bd");        // 再生囲みが "bd" を囲む + 行内スライダ
            beat.Value = 2; await d.Step(1);
            await d.Snap("beat-sd");        // 囲みが "sd" へ移動 (行キャッシュ非再構築)
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — Strudel (再生囲み + 行内スライダ)"),
                Muted("MiniNotation のソーススパンで「いま鳴っているトークン」を Mark.Box で囲む。数値は行内スライダに置換。"),
                root]];
    }

    [Story]
    public static Widget RichText(StoryContext ctx)
    {
        // Markdown/リッチ文書の素地 (WS-A / ADR-0012、S(A1)): font-variant Mark で見出し/太字/小サイズを
        // 同一の行テキストモデルの上に描く — 表示は行に分解、装飾は push 型。
        Signal<string> text = ctx.Signal("text", "Title\nbold here\nsmall");
        TextEditorView ed = TextEditorView(text, editorHeight: 130f, editorWidth: 460f);
        ed.BoldFont = EditorFaces.Value.Bold;

        var set = new DecorationSet(
        [
            new MarkDecoration(0, 5, Variant: FontVariant.Bold, FontScale: 2.0f),          // 見出し (太字 + 2x)
            new MarkDecoration(6, 10, Variant: FontVariant.Bold),                          // "bold" 太字
            new MarkDecoration(16, 21, Foreground: 0xFF888888, FontScale: 0.8f),           // "small" 小さめ・淡色
        ]);

        ctx.Play(async d =>
        {
            ed.SetDecorations("md", set);
            await d.Step(1);
            await d.Snap("variants");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — font-variant (Markdown 素地)"),
                Muted("MarkDecoration の Variant (Bold) + FontScale で見出し・太字・小サイズを同一行モデルに描く。表示は行、装飾は push。"),
                ed]];
    }

    [Story]
    public static Widget Markdown(StoryContext ctx)
    {
        // read-only 文書レンダラの核 (WS-A / ADR-0012): MarkdownProvider が Markdown ソースを
        // font-variant Mark + Block/Line/LinePrefix 装飾に変換 → 同一の行テキストモデルの上に
        // 見出し/太字/斜体/インラインコード + 引用/箇条書き/コードフェンスが乗る。
        Signal<string> md = ctx.Signal("md",
            "# Markdown on the text stack\n" +
            "Rich text via decorations: **bold**, *italic*, `code`.\n" +
            "Links are rendered inline with the text.\n" +
            "> A quoted line renders with a left bar.\n" +
            "- first list item\n" +
            "- second list item\n" +
            "```\n" +
            "let mono = code_block;\n" +
            "```");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 520f, height: 240f,
            bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fonts: JpFallback.Value);
        int clickOff = -1;
        ed.OnClickOffset = o => clickOff = o;   // クリック→ソースオフセット (リンクナビの当たり判定用)

        ctx.Play(async d =>
        {
            await d.Snap("rendered");
            await d.Click(ed);
            await d.Expect(() => clickOff >= 0, "OnClickOffset がクリックで発火する");
            await d.Type("XXX");
            await d.Expect(() => !ed.Text.Contains("XXX"), "ReadOnly は編集を無視する");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — Markdown (プロバイダ)"),
                Muted("MarkdownDoc.Create が記法を隠し、見出し/太字/斜体/コードを read-only 文書としてレンダリングする。"),
                ed]];
    }

    [Story]
    public static Widget LivePreviewStory(StoryContext ctx)
    {
        // Live Preview 編集モード (WS-A / S(A4)): editable:true でキャレット行だけ記法マーカを raw で見せ、
        // 離れた行は整形表示 (Typora 風)。read-only の MarkdownDoc は全行マーカ非表示だった。
        Signal<string> md = ctx.Signal("md",
            "# Live Preview 編集モード\n" +
            "キャレット行だけ **記法** が `raw` で見え、離れると整形されます。\n" +
            "- リスト項目 (行を離れると • になる)\n" +
            "> 引用ブロック");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 520f, height: 220f, bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fonts: JpFallback.Value, editable: true);

        ctx.Play(async d =>
        {
            await d.Snap("h1");        // 初期: キャレットは先頭 = 見出し行の "# " が raw、他行は整形
            await d.Click(ed);          // クリック位置 (中央付近の行) へキャレット → その行が raw に切替わる
            await d.Step(1);
            await d.Snap("moved");      // reveal がキャレット行に追従したのを実証
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — Live Preview (編集モード)"),
                Muted("editable:true。キャレット行のマーカだけ raw、他行は整形 (Typora 風)。read-only は全行非表示。"),
                ed]];
    }

    [Story]
    public static Widget MilkdownStyleEditor(StoryContext ctx)
    {
        Signal<string> md = ctx.Signal("milkdown-md",
            "# Milkdown-style editor\n\n" +
            "Select text and use the toolbar to apply **formatting**.\n\n" +
            "- [ ] Add a task\n" +
            "- Lists, quotes, code, links and tables come from Markdown\n\n" +
            "> The editor owns interaction; Markdown supplies blocks and actions.\n\n" +
            "---\n\n" +
            "```csharp\n" +
            "var answer = 42;\n" +
            "Console.WriteLine(answer);\n" +
            "```\n\n" +
            "| Feature | Status |\n" +
            "| :--- | ---: |\n" +
            "| Table block | Working |\n" +
            "| Inline edit | Ready |\n\n" +
            "![Luxel sample](src/Gallery/Luxel.Gallery/assets/sample-sparkline.png)\n\n" +
            "Use the block menu on the left or right-click to insert another block.");

        var appearance = new TextEditorAppearance(fontSize: 16f, lineHeight: 1.55f, wrapLineHeight: 1.35f)
            .WithBlock(MarkdownBlockKinds.Heading(1), new TextEditorBlockAppearance(
                FontSize: 30f, FontVariant: FontVariant.Bold))
            .WithBlock(MarkdownBlockKinds.Heading(2), new TextEditorBlockAppearance(
                FontSize: 23f, FontVariant: FontVariant.Bold))
            .WithBlock(MarkdownBlockKinds.Quote, new TextEditorBlockAppearance(
                Indent: 14f, BarWidth: 3f))
            .WithBlock(MarkdownBlockKinds.CodeBlock, new TextEditorBlockAppearance(
                FontVariant: FontVariant.Mono));

        (VectorFont? bold, VectorFont? italic, VectorFont? boldItalic, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 800f, height: 600f,
            bold: bold, italic: italic, boldItalic: boldItalic, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance,
            fonts: JpFallback.Value, fill: true, editable: true, appearance: appearance, resources: ctx.Resources);

        ctx.Play("format-selection", async d =>
        {
            int from = ed.Text.IndexOf("Select text", StringComparison.Ordinal);
            ((ITextInput)ed).Select(from, from + "Select text".Length);
            ed.Execute(ed.SelectionActions.Single(x => x.Id == "bold"));
            await d.Step(1);
            await d.Expect(() => ed.Text.Contains("**Select text**", StringComparison.Ordinal),
                "選択ツールバーの操作が Markdown 記法として反映される");
            await d.Snap("formatted");
        });
        ctx.Play("insert-block", async d =>
        {
            ((ITextInput)ed).Select(ed.Text.Length, ed.Text.Length);
            ed.Execute(ed.InsertItems.Single(x => x.Id == "task-list"));
            await d.Step(1);
            await d.Expect(() => ed.Text.EndsWith("- [ ] ", StringComparison.Ordinal),
                "追加メニューの候補が現在位置へ挿入される");
            await d.Snap("inserted");
        });

        return ed;
    }

    [Story]
    public static Widget MarkdownDocStory(StoryContext ctx)
    {
        // 文書レンダラを 1 ファクトリで (WS-A / ADR-0012、Kit.Docs() 差し替えの部品):
        // MarkdownDoc.Create が TextEditorView + MarkdownProvider を read-only + 折返しで束ねる。
        Signal<string> md = ctx.Signal("md",
            "# Document renderer\n" +
            "This is a longer paragraph that wraps across the available width, showing that the read-only Markdown document renderer handles flowing prose — not just short lines.\n" +
            "日本語の段落も混在できます（フォールバックフォント）。\n" +
            "## Features\n" +
            "- **bold**, *italic*, `code`\n" +
            "- inline links\n" +
            "> Blockquotes render with a left bar.\n" +
            "```csharp\n" +
            "var answer = 42;  // syntax colored, selectable\n" +
            "```");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 520f, height: 280f, bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fonts: JpFallback.Value);

        ctx.Play(async d => await d.Snap("doc"));

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("MarkdownDoc — 文書レンダラ (1 ファクトリ)"),
                Muted("MarkdownDoc.Create が TextEditorView + MarkdownProvider を read-only + 折返しで束ねる。折返す段落 + 全 Markdown。"),
                ed]];
    }

    [Story]
    public static Widget MarkdownFillStory(StoryContext ctx)
    {
        // Fill モード (WS-A / ADR-0012、実 Docs ページ移行の前提):
        // fill:true で固定幅でなく「制約サイズいっぱい」に広がる = ペインに合わせて折返す文書ページ相当。
        // ここでは 440×240 の枠に入れて、fallback (width:160/height:120) でなく枠サイズで折返すのを示す。
        Signal<string> md = ctx.Signal("md",
            "# Fill モード\n" +
            "This paragraph wraps at the **container** width, not a fixed 160px fallback — the renderer stretches to fill whatever pane it is placed in, exactly like a real docs page. 段落は枠いっぱいの幅で折返します。\n" +
            "## 用途\n" +
            "- `fill:true` で制約サイズを採用\n" +
            "- resize すれば再折返し (geometry 再構築)");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 160f, height: 120f, bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fonts: JpFallback.Value, fill: true);

        ctx.Play(async d => { await d.Step(2); await d.Snap("fill"); });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("MarkdownDoc — Fill (領域いっぱい)"),
                Muted("fill:true で固定幅でなく制約サイズに広がる = 文書ページ向け。下の枠は 440×240、fallback 160×120 でなく枠幅で折返す。"),
                Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: UiTheme.T.Radius,
                       width: 440f, height: 240f)[ed]]];
    }

    [Story]
    public static Widget DocBridge(StoryContext ctx)
    {
        // 移行の本命 (WS-A / ADR-0012 S(A3)): 既存の Docs($"...{Widget}...") 記法 (DocString) を
        // そのまま MarkdownDoc.FromDoc で新スタック描画。既存 Docs ページを 1 行変更で移行できる。
        Widget live = Button(_ => { }, "a live embedded widget");
        DocString content = $$"""
            # Migrated docs page

            This page is authored with the **DocString** API but renders on the *new stack* via `MarkdownDoc.FromDoc`.

            {{live}}

            A mermaid diagram, reusing Luxel.Diagram:

            ```mermaid
            flowchart LR
            Docs --> FromDoc --> NewStack
            ```
            """;
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.FromDoc(content, () => UiTheme.T, width: 520f, height: 330f, bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance,
            fences: new Dictionary<string, Func<string, Widget>> { ["mermaid"] = b => Luxel.Diagram.Factories.DiagramBlock(b, 460f) });

        ctx.Play(async d => { await d.Step(3); await d.Snap("bridge"); });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — Docs 橋 (DocString → 新スタック)"),
                Muted("既存の Docs($\"...{Widget}...\") 記法を MarkdownDoc.FromDoc で新スタック描画 = 移行の 1 行化。"),
                ed]];
    }

    [Story]
    public static Widget DocEmbeds(StoryContext ctx)
    {
        // mermaid/数式アダプタ (WS-A / ADR-0012): ```embed mermaid|math フェンス本文を、既存の
        // Luxel.Diagram / Luxel.MathText を再利用して図/式 widget に解決 (自動高さで載る)。
        Signal<string> md = ctx.Signal("md",
            "# Diagrams and math\n" +
            "A mermaid diagram, reusing Luxel.Diagram:\n" +
            "```embed mermaid\n" +
            "flowchart LR\n" +
            "A[Editor] --> B[Provider]\n" +
            "B --> C[BlockWidget]\n" +
            "```\n" +
            "A formula via Luxel.MathText:\n" +
            "```embed math\n" +
            "w = \\frac{\\alpha + \\beta}{\\sqrt{x^2 + y^2}}\n" +
            "```\n" +
            "Both are just embed widgets.");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 520f, height: 380f, bold: bold, mono: mono);
        const float cw = 460f;
        ed.WidgetResolver = key => key is not EmbedRef r ? null : r.Key switch
        {
            "mermaid" => Luxel.Diagram.Factories.DiagramBlock(r.Body, cw),
            "math" => Luxel.MathText.Factories.MathBlockView(r.Body, maxWidth: cw),
            _ => null,
        };

        ctx.Play(async d => { await d.Step(3); await d.Snap("docembeds"); });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — mermaid / 数式 埋め込み"),
                Muted("```embed mermaid|math の本文を既存プロセッサ (Diagram/MathText) で解決 = 内容プロセッサ再利用。"),
                ed]];
    }

    [Story]
    public static Widget Embed(StoryContext ctx)
    {
        // 埋め込みライブ UI (WS-A / ADR-0012、Kit.Docs() の目玉を新スタックで): ```embed <key> フェンス →
        // MarkdownProvider が自動高さ block widget を出し、WidgetResolver が key から実 Widget を解決する。
        Signal<string> md = ctx.Signal("md",
            "# Embedded live UI\n" +
            "Docs embed real widgets via an embed fence:\n" +
            "```embed demo\n" +
            "```\n" +
            "Embeds can carry a body (diagram/math source):\n" +
            "```embed note\n" +
            "body passed to the resolver\n" +
            "```\n" +
            "Text continues after.");
        (VectorFont? bold, _, _, VectorFont? mono) = EditorFaces.Value;
        TextEditorView ed = MarkdownDoc.Create(md, () => UiTheme.T, width: 520f, height: 260f, bold: bold, mono: mono);
        ed.WidgetResolver = key => key is not EmbedRef r ? null : r.Key switch
        {
            "demo" => Border(background: Bind.From(() => Styles.WithAlpha(UiTheme.T.Primary, 25)), padding: new Thickness(12))[
                VStack(6)[
                    Text("Embedded live widget", color: Bind.From(() => UiTheme.T.Primary)),
                    Button(_ => { }, "a real button in the document")]],
            "note" => Border(background: Bind.From(() => Styles.WithAlpha(UiTheme.T.TextMuted, 22)), padding: new Thickness(12))[
                Text($"note body → \"{r.Body}\"", color: Bind.From(() => UiTheme.T.Text))],
            _ => null,
        };

        ctx.Play(async d =>
        {
            await d.Step(3);   // 自動高さの収束を待つ
            await d.Snap("embed");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 埋め込みライブ UI"),
                Muted("```embed key フェンス → 自動高さ block widget → WidgetResolver で live Widget。Kit.Docs() の hole 埋め込み相当。"),
                ed]];
    }

    [Story]
    public static Widget BlockWidgetAuto(StoryContext ctx)
    {
        // ブロック widget 自動高さ (WS-A / ADR-0012): Height=0 は「宣言しない」= view が widget の自然高さを
        // 測って geometry に返し、その高さぶんを確保する (埋め込みライブ UI の前提)。範囲 [11,22) = 中間 1 行。
        Signal<string> text = ctx.Signal("text", "Intro line\nPLACEHOLDER\nOutro line");
        TextEditorView ed = TextEditorView(text, editorHeight: 220f, editorWidth: 460f);
        ed.WrapText = true;
        ed.WidgetResolver = key => key as string == "auto"
            ? Border(background: Bind.From(() => Styles.WithAlpha(UiTheme.T.Primary, 30)), padding: new Thickness(12))[
                  VStack(4)[
                      Text("Auto-height block widget", color: Bind.From(() => UiTheme.T.Primary)),
                      Text("declares no height (0) —", color: Bind.From(() => UiTheme.T.Text)),
                      Text("the editor measures its content and reserves it.", color: Bind.From(() => UiTheme.T.Text))]]
            : null;

        ctx.Play(async d =>
        {
            ed.SetDecorations("block", new DecorationSet([new BlockWidgetDecoration(11, 22, "auto", 0f)]));
            await d.Step(3);   // 実測 → 次フレーム採用 の収束を待つ
            await d.Snap("auto");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — ブロック widget (自動高さ)"),
                Muted("Height=0 で widget の自然高さを測って確保。宣言不要 = 埋め込みライブ UI の前提。"),
                ed]];
    }

    [Story]
    public static Widget BlockWidget(StoryContext ctx)
    {
        // 複数ソース行を占有するブロック widget (WS-A S(A2b) / ADR-0012): 表/図/数式/埋め込みの土台。
        // 範囲 [6,31) = 中間 2 ソース行を 80px の全幅パネルに置換 (先頭行=widget、残りは高さ0に畳む)。
        Signal<string> text = ctx.Signal("text", "Above\nblock line 1\nblock line 2\nBelow");
        TextEditorView ed = TextEditorView(text, editorHeight: 220f, editorWidth: 480f);
        ed.WrapText = true;

        ed.WidgetResolver = key => key as string == "panel"
            ? Border(background: Bind.From(() => Styles.WithAlpha(UiTheme.T.Primary, 40)),
                     padding: new Thickness(14))[
                  Text("▦ block widget — 2 ソース行を占有", color: Bind.From(() => UiTheme.T.Primary))]
            : null;

        ctx.Play(async d =>
        {
            ed.SetDecorations("block", new DecorationSet([new BlockWidgetDecoration(6, 31, "panel", 80f)]));
            await d.Step(1);
            await d.Snap("block");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — ブロック widget"),
                Muted("BlockWidgetDecoration が複数ソース行を全幅 widget に置換 (表/図/埋め込みの土台)。行↔ソースの 1:1 は保つ。"),
                ed]];
    }

    [Story]
    public static Widget MultiCursor(StoryContext ctx)
    {
        Signal<string> code = ctx.Signal("code", "foo = 1;\nfoo = 2;\nfoo = 3;");
        TextEditorView ed = TextEditorView(code, editorHeight: 170f, editorWidth: 460f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;

        ctx.Play("ctrl-d", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.Up); await d.Key(Key.Up); await d.Key(Key.Home);   // 1 行目頭へ
            await d.Key(Key.D, ctrl: true);                                    // "foo" を選択
            await d.Key(Key.D, ctrl: true);                                    // 2 つ目
            await d.Key(Key.D, ctrl: true);                                    // 3 つ目
            await d.Expect(() => ed.CursorCount == 3, "Ctrl+D×3 で 3 箇所選択");
            await d.Snap("selected");
            await d.Type("bar");                                              // 3 箇所同時置換
            await d.Expect(() => ed.Text == "bar = 1;\nbar = 2;\nbar = 3;", "1 打鍵で 3 箇所置換");
            await d.Snap("replaced");
        });
        ctx.Play("column", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.Up); await d.Key(Key.Up); await d.Key(Key.Home);   // 1 行目頭へ
            await d.Key(Key.Down, ctrl: true, alt: true);                      // 下の行へカーソル追加
            await d.Key(Key.Down, ctrl: true, alt: true);
            await d.Expect(() => ed.CursorCount == 3, "Ctrl+Alt+↓ で縦列 3 カーソル");
            await d.Snap("column");
            await d.Type("// ");                                              // 縦列に一括挿入
            await d.Expect(() => ed.Text.StartsWith("// foo") && ed.Text.Contains("// foo = 3"), "縦列に一括挿入");
            await d.Snap("column-typed");
        });
        ctx.Play("alt-click", async d =>
        {
            // Alt+Click で追加キャレット (ADR-0011: PointerEvent の修飾キー = ポインタからのマルチカーソル)
            float x = ed.WorldPos.X + 40;
            await d.Click(x, ed.WorldPos.Y + 16);                             // 1 行目にキャレット
            await d.Expect(() => ed.CursorCount == 1, "クリックで単一キャレット");
            await d.Click(x, ed.WorldPos.Y + 44, KeyModifiers.Alt);          // Alt+Click で 2 行目に追加
            await d.Click(x, ed.WorldPos.Y + 72, KeyModifiers.Alt);          // 3 行目にも
            await d.Expect(() => ed.CursorCount == 3, "Alt+Click で 3 キャレット");
            await d.Snap("alt-click");
            await d.Type("X");                                               // 3 箇所同時挿入
            await d.Expect(() => ed.CursorCount == 3, "3 キャレット維持で一括挿入");
            await d.Snap("alt-typed");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — マルチカーソル"),
                Muted("Ctrl+D 次の同一語 / Ctrl+Alt+↑↓ 縦列 / Alt+Click 追加キャレット / Esc 解除。native 複数レンジ = 1 打鍵で全箇所編集・1 undo。"),
                ed]];
    }

    [Story]
    public static Widget Completion(StoryContext ctx, ICodeLanguage lang)
    {
        Signal<string> code = ctx.Signal("code", "var s = \"hi\";\ns.");
        TextEditorView ed = TextEditorView(code, editorHeight: 240f, editorWidth: 560f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont mono) = EditorFaces.Value;
        ed.EditorFont = mono;
        ed.LanguageService = lang;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        ed.Providers.Add(new DiagnosticsProvider(lang, () => UiTheme.T));

        ctx.Play("complete", async d =>
        {
            await d.Snap();
            await d.Click(ed);
            await d.Key(Key.Down); await d.Key(Key.End);      // 2 行目 "s." の末尾へ
            await d.Key(Key.Space, ctrl: true);               // 補完を開く
            await d.Step(2);
            await d.Expect(() => ed.CompletionOpen && ed.CompletionCount > 0, "補完ポップアップが開く");
            await d.Snap("popup");
            int all = ed.CompletionCount;
            await d.Type("Le");                               // タイプで絞り込み (閉じずにフィルタ)
            await d.Step(1);
            await d.Expect(() => ed.CompletionOpen && ed.CompletionCount <= all, "タイプで候補が絞られる");
            await d.Snap("filtered");
            await d.Key(Key.Enter);                           // 確定
            await d.Expect(() => !ed.CompletionOpen && ed.Text.Contains("Le"), "Enter で確定して閉じる");
            await d.Snap("confirmed");
        });
        ctx.Play("hover", async d =>
        {
            await d.Click(ed);
            var wp = ed.WorldPos;
            d.Host.PointerMove(wp.X + 34, wp.Y + 33);         // 2 行目 "s." の識別子 s の上に留まる
            await d.Step(31);                                 // dwell (フレーム基準 = 決定的)
            await d.Expect(() => ed.HoverTipOpen, "同一位置に留まるとホバーツールチップ");
            await d.Snap("hover");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 補完 / ホバー (Popup)"),
                Muted("Ctrl+Space で補完 (CaretRect にアンカー、画面端でフリップ)。dwell でホバー。"),
                ed]];
    }

    // 行頭番号 + 行内 widget (◯ をチェックボックスに置換) + 左縦バーを供給する装飾プロバイダ (デモ用)
    private sealed class ListDecoProvider : IDecorationProvider
    {
        public string Owner => "list-demo";
        public DecorationSet Provide(EditorState s)
        {
            var d = new List<Decoration>();
            TextDoc doc = s.Doc;
            for (int i = 0; i < doc.LineCount; i++)
                d.Add(new LinePrefixDecoration(doc.LineStart(i), $"{i + 1}. ", 0xFF8A8A8A));
            int ci = 0;
            for (int idx = doc.Text.IndexOf('◯'); idx >= 0; idx = doc.Text.IndexOf('◯', idx + 1))
                d.Add(new WidgetDecoration(idx, idx + 1, 44f, 22f, $"chk{ci++}"));
            d.Add(new BlockDecoration(0, doc.Length, BarColor: 0xFF4A90D9, BarWidth: 3f, Indent: 14f));
            return new DecorationSet(d);
        }
    }

    [Story]
    public static Widget Widgets(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text", "牛乳を買う ◯\n卵を買う ◯\nパンを買う ◯");
        TextEditorView ed = TextEditorView(value, editorHeight: 150f, editorWidth: 460f);

        Signal<bool>[] checks = [ctx.Signal("c0", false), ctx.Signal("c1", false), ctx.Signal("c2", true)];
        Switch[] toggles = [Switch(checks[0]), Switch(checks[1]), Switch(checks[2])];
        ed.WidgetResolver = key => key switch
        {
            "chk0" => toggles[0],
            "chk1" => toggles[1],
            "chk2" => toggles[2],
            _ => null,
        };
        ed.Providers.Add(new ListDecoProvider());

        ctx.Play("check", async d =>
        {
            await d.Snap();                                  // 番号 + 行内チェックボックス + 左バー
            await d.Click(toggles[0]);                       // 行内 widget を押す
            await d.Step(1);
            await d.Expect(() => checks[0].Value, "行内チェックボックスが押せて ON になる");
            await d.Snap("checked");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 行内 widget + 行頭装飾"),
                Muted("装飾プロバイダが行頭番号・置換 widget (◯→チェックボックス)・左縦バーを供給。widget は行内でホストされ状態を持つ。"),
                ed]];
    }
}
