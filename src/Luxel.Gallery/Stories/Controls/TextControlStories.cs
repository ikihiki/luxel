using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>テキスト表示/編集系コントロールのストーリー。</summary>
public static class TextControlStories
{
    [Story("TextField/Basic", Height = 160)]
    public static Widget TextFieldBasic(StoryContext ctx)
        => Frame(TextField(ctx.Signal("text", "Hello"), placeholder: "Type here..."));

    [Story("RichTextEditor/Basic", Height = 460)]
    public static Widget RichTextEditorBasic(StoryContext ctx)
    {
        Signal<string> md = ctx.Signal("markdown",
            "# 見出し 1\ntext with **bold**, *italic*, `code` and [link](https://example.com)\n" +
            "## 見出し 2\n> 引用ブロックはミュート色 + 左バー\n- リスト項目\n- 項目 **強調** 入り\n" +
            "1. 番号付き\n2. 自動採番\n---\n```cs\nvar x = 1;\nvar y = x + 1;\n```\n日本語の段落も編集できる。");
        RichTextEditor ed = RichTextEditor(md, editorHeight: 330);
        ed.Fonts = JpFallback.Value;
        ed.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        (ed.BoldFont, ed.ItalicFont, ed.BoldItalicFont, ed.MonoFont) = EditorFaces.Value;

        Widget Tool(string label, Action<Luxel.Document.DocumentEditor> act)
            => Button(_ => ed.Apply(act), label, variant: Variant.Ghost);
        return Frame(VStack(6)[
            HStack(2)[
                Tool("B", e => e.ToggleBold()),
                Tool("I", e => e.ToggleItalic()),
                Tool("</>", e => e.ToggleCode()),
                Tool("H1", e => e.SetBlockKind(Luxel.Document.BlockKind.Heading, 1)),
                Tool("H2", e => e.SetBlockKind(Luxel.Document.BlockKind.Heading, 2)),
                Tool("•", e => e.SetBlockKind(Luxel.Document.BlockKind.ListItem)),
                Tool("1.", e => e.SetBlockKind(Luxel.Document.BlockKind.ListItem, ordered: true)),
                Tool(">", e => e.SetBlockKind(Luxel.Document.BlockKind.Quote)),
                Tool("{}", e => e.SetBlockKind(Luxel.Document.BlockKind.CodeBlock)),
                Tool("--", e => e.InsertDivider()),
                Tool("Undo", e => e.Undo()),
                Tool("Redo", e => e.Redo())],
            ed]);
    }

    [Story("MarkdownEditor/Hybrid", Height = 420)]
    public static Widget MarkdownEditorHybrid(StoryContext ctx)
    {
        Signal<string> md = ctx.Signal("markdown",
            "# Hybrid 編集\nキャレットのあるブロックだけ **ソース** を表示して編集し、離れると整形に戻る。\n" +
            "- 行頭で `- ` や `# ` を打つと型が確定する (離脱時の再パース)\n> 引用も同様\n```cs\nvar code = true;\n```");
        RichTextEditor ed = RichTextEditor(md, editorHeight: 330);
        ed.HybridSource = true;
        ed.Fonts = JpFallback.Value;
        (ed.BoldFont, ed.ItalicFont, ed.BoldItalicFont, ed.MonoFont) = EditorFaces.Value;
        return Frame(ed);
    }

    [Story("MarkdownEditor/VisualSource", Height = 560)]
    public static Widget MarkdownEditorVisualSource(StoryContext ctx)
    {
        // 同一 signal を Visual (hybrid) と Source (TextArea) が共有 — 双方向バインドの実証。
        // どちらで編集してももう片方へ即時反映される (切替でなく並置デモ)。
        Signal<string> md = ctx.Signal("markdown", "# タイトル\ntext **bold** and *italic*\n- item 1\n- item 2");
        RichTextEditor visual = RichTextEditor(md, editorHeight: 230);
        visual.HybridSource = true;
        visual.Fonts = JpFallback.Value;
        (visual.BoldFont, visual.ItalicFont, visual.BoldItalicFont, visual.MonoFont) = EditorFaces.Value;
        TextArea source = TextArea(md, height: 180);
        source.Fonts = JpFallback.Value;
        return Frame(VStack(8)[
            Text("Visual (hybrid)", 12, color: Bind.From(() => UiTheme.T.TextMuted)),
            visual,
            Text("Source", 12, color: Bind.From(() => UiTheme.T.TextMuted)),
            source]);
    }

    /// <summary>```chart フェンス → FencePayload("chart") へ昇格するリゾルバ (パーサーが判断する実例)。</summary>
    private sealed class ChartResolver : Luxel.Document.IFenceResolver
    {
        public Luxel.Document.IBlockPayload? Resolve(string info, string body)
            => info == "chart" ? new Luxel.Document.FencePayload(info, body) : null;
    }

    /// <summary>「フォーマット + widget 解釈」を対で構成する例 — 専用フォーマット (Strudel 等) は
    /// この形のファクトリを配布して解釈を固定する。markdown ではアプリが自由に構成できる。</summary>
    private static (Luxel.Document.MarkdownFormat Format, BlockWidgetRegistry Widgets) CreateChartMarkdown(Luxel.Resources.ResourceSystem resources)
    {
        var fmt = new Luxel.Document.MarkdownFormat();
        fmt.FenceResolvers.Add(new ChartResolver());
        var widgets = new BlockWidgetRegistry()
            .Register("chart", bc =>
            {
                // payload の Body (CSV) を Sparkline として実体化
                Sparkline s = Sparkline(bc.MaxWidth - 8, 64);
                float[] vals = ((Luxel.Document.FencePayload)bc.Payload).Body
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => float.TryParse(x, out float f) ? f : 0f).ToArray();
                s.SetValues(vals);
                return s;
            })
            .Register("image", ImageBlocks.Factory(resources))
            .Register("table", TableBlocks.Factory());
        return (fmt, widgets);
    }

    private const string SampleImage = "src/Luxel.Gallery/goldens/Sparkline_Basic.vk.png";
    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("MarkdownEditor/Embeds", Height = 460)]
    public static Widget MarkdownEditorEmbeds(StoryContext ctx)
    {
        // snap (1 フレーム描画) の決定性のため画像を同期 preload — 実アプリでは不要
        // (ImageBlock はロード完了をポーリングし Invalidate で実寸に切り替わる)
        _imagePreload ??= ctx.Resources.Load<Luxel.Resources.CpuImage>(SampleImage);
        try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダ表示のまま */ }

        (Luxel.Document.MarkdownFormat fmt, BlockWidgetRegistry widgets) = CreateChartMarkdown(ctx.Resources);

        Signal<string> md = ctx.Signal("markdown",
            "# 埋め込みブロック\n下の ```chart フェンスはリゾルバが embed 化し **widget として実体化**される:\n" +
            "```chart\n3,1,4,1,5,9,2,6,5,3,5,8,9,7,9\n```\n" +
            "テーブルはセルをクリックして直接編集できる (Tab/Enter で移動、最下段 Enter で行追加):\n\n" +
            "| name | value |\n| --- | ---: |\n| alpha | 1 |\n| beta | 2 |\n\n" +
            "画像は Resource システム経由でロードされる (URI キャッシュ + RefCount):\n" +
            $"![サンプル画像]({SampleImage})\n通常の段落は普通に編集できる。");
        RichTextEditor ed = RichTextEditor(md, editorHeight: 360, format: fmt, widgets: widgets);
        ed.HybridSource = true;   // 打った記法 (![alt](src) 等) が離脱で確定し embed 化される
        ed.Fonts = JpFallback.Value;
        ed.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        (ed.BoldFont, ed.ItalicFont, ed.BoldItalicFont, ed.MonoFont) = EditorFaces.Value;
        return Frame(ed);
    }

    [Story("SearchField/Basic", Height = 320)]
    public static Widget SearchFieldBasic(StoryContext ctx)
    {
        // CompositeControl の見本: タイプで候補が絞り込まれ (構造状態 → Rebuild)、行クリックで確定
        string[] langs = ["C#", "C++", "Rust", "Go", "TypeScript", "Python", "Ruby", "Swift", "Kotlin", "Zig"];
        return Frame(SearchField(ctx.Signal("query", ""), langs));
    }

    [Story("TextArea/Basic", Height = 280)]
    public static Widget TextAreaBasic(StoryContext ctx)
    {
        TextArea ta = TextArea(ctx.Signal("text",
            "複数行のプレーンテキスト編集。\nEnter でブロック分割、行頭 Backspace で結合。\n" +
            "折返しの長い行はこのように wrap され、↑↓ は goal-x を保存して表示行単位で動く。\n日本語も IME で入力できる。"),
            height: 180);
        ta.Fonts = JpFallback.Value;
        return Frame(ta);
    }

    [Story("TextArea/Scroll", Height = 280)]
    public static Widget TextAreaScroll(StoryContext ctx)
    {
        TextArea ta = TextArea(ctx.Signal("text",
            string.Join('\n', Enumerable.Range(1, 24).Select(i => $"line {i:00} — キャレット追従スクロールの確認"))),
            height: 180);
        ta.Fonts = JpFallback.Value;
        return Frame(ta);
    }

    [Story("Text/EllipsisVAlign", Height = 360)]
    public static Widget TextEllipsisVAlign()
    {
        const string lng = "The quick brown fox jumps over the lazy dog near the quiet river bank in autumn evenings.";
        Widget Case(string title, Widget t) => VStack(2)[
            Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6, padding: new Thickness(8), width: 300)[t]];
        return Frame(VStack(8)[
            Case("maxLines: 1 + …", Text(lng, 13, wrap: TextWrap.Word, maxLines: 1)),
            Case("maxLines: 2 + …", Text(lng + " " + lng, 13, wrap: TextWrap.Word, maxLines: 2)),
            Case("VAlign Center (height 70)", Text("centered", 13, wrap: TextWrap.Word, height: 70,
                verticalAlign: TextVAlign.Center, textAlign: Luxel.Typography.TextAlign.Center)),
            Case("VAlign Bottom (height 70)", Text("bottom right", 13, wrap: TextWrap.Word, height: 70,
                verticalAlign: TextVAlign.Bottom, textAlign: Luxel.Typography.TextAlign.Right))]);
    }

    [Story("RichText/Basic", Height = 280)]
    public static Widget RichTextBasic()
    {
        var spans = new[]
        {
            new Luxel.Typography.TextSpan("Rich ", new Luxel.Typography.SpanStyle { Size = 24, Color = Tw.Blue500 }),
            new Luxel.Typography.TextSpan("text ", new Luxel.Typography.SpanStyle { Size = 24, Color = Tw.Red500 }),
            new Luxel.Typography.TextSpan("mixes sizes, "),
            new Luxel.Typography.TextSpan("colors ", new Luxel.Typography.SpanStyle { Color = Tw.Green600 }),
            new Luxel.Typography.TextSpan("and 日本語フォールバック。", new Luxel.Typography.SpanStyle { Size = 14 }),
            new Luxel.Typography.TextSpan("\nWrapping works across spans too — the quick brown fox jumps over the lazy dog."),
        };
        RichTextView rtv = RichTextView(spans, wrap: Luxel.Typography.TextWrap.Word);
        rtv.Fonts = JpFallback.Value;
        return Frame(Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6,
                            padding: new Thickness(10), width: 380)[rtv]);
    }

    [Story("Text/Multiline", Height = 480)]
    public static Widget TextMultiline()
    {
        const string en = "The quick brown fox jumps over the lazy dog near the quiet river bank in autumn.";
        const string jp = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。「にゃー」と鳴いた。";
        Widget Case(string title, Widget t) => VStack(2)[
            Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6, padding: new Thickness(8), width: 360)[t]];
        return Frame(VStack(8)[
            Case("Word wrap + \\n + 禁則", Text(en + "\n" + jp, 13, wrap: Luxel.Typography.TextWrap.Word)),
            Case("Center", Text(en, 13, wrap: Luxel.Typography.TextWrap.Word, textAlign: Luxel.Typography.TextAlign.Center)),
            Case("Justify (末行は左)", Text(en + " " + en, 13, wrap: Luxel.Typography.TextWrap.Word, textAlign: Luxel.Typography.TextAlign.Justify))]);
    }

    [Story("Text/Styles", Height = 220)]
    public static Widget TextStyles() => Frame(VStack(6)[
        Text("Large 28px", 28, color: Bind.From(() => UiTheme.T.Text)),
        Text("Body 16px", 16, color: Bind.From(() => UiTheme.T.Text)),
        Text("Muted 13px", 13, color: Bind.From(() => UiTheme.T.TextMuted)),
        Text("Tailwind colored", 16, color: Tw.Blue500),
        Text("Half opacity", 16, color: Bind.From(() => UiTheme.T.Text), opacity: 0.5f)]);
}
