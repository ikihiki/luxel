using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.Controls;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Tailwind;
using S = Luxel.UI.Tailwind.S;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Gallery.Stories;

/// <summary>基本コントロールのストーリー。ctx.Signal(...) は自動で knob になる。</summary>
public static class ControlStories
{
    private static Border Frame(Widget child) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[child]];

    // ---- Button ----

    [Story("Button/Primary", Height = 160)]
    public static Widget ButtonPrimary() => Frame(Button(_ => { }, "Click me"));

    [Story("Button/Variants", Height = 160)]
    public static Widget ButtonVariants() => Frame(HStack(8)[
        Button(_ => { }, "Filled"),
        Button(_ => { }, "Tonal", variant: Variant.Tonal),
        Button(_ => { }, "Outline", variant: Variant.Outline),
        Button(_ => { }, "Ghost", variant: Variant.Ghost)]);

    [Story("Button/Intents", Height = 160)]
    public static Widget ButtonIntents() => Frame(HStack(8)[
        Button(_ => { }, "Primary"),
        Button(_ => { }, "Success", intent: Intent.Success),
        Button(_ => { }, "Danger", intent: Intent.Danger),
        Button(_ => { }, "Neutral", intent: Intent.Neutral)]);

    [Story("Button/Tailwind", Height = 160)]
    public static Widget ButtonTailwind() => Frame(
        Button(_ => { }, "Hover me",
            background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 180, height: 64,
            parts: [S.On(WidgetState.Hover, S.Bg(Tw.Red500), S.Scale(1.08f)),
                    S.On(WidgetState.Pressed, S.Scale(0.94f))]));

    [Story("Button/Counter", Height = 160)]
    public static Widget ButtonCounter(StoryContext ctx)
    {
        Signal<int> count = ctx.Signal("count", 0);
        return Frame(HStack(8)[
            Button(_ => count.Value--, "-"),
            Text($" {count} ", 22, color: Bind.From(() => UiTheme.T.Text), vAlign: Align.Center),
            Button(_ => count.Value++, "+")]);
    }

    // ---- 入力/選択 ----

    [Story("Transitions/States", Height = 200)]
    public static Widget TransitionStates(StoryContext ctx) => Frame(
        // 状態レイヤは生成された When (引数はファクトリと同名 — Stateable のみ)、
        // トランジションは fluent Transition 系で「どのプロパティ群を」独立に宣言する (GN):
        //   Background は 400ms 既定 / hover へは 80ms で入り / 押下・解放 (pressed→hover) は即時。
        //   Scale は無指定 = 瞬時。
        Button(_ => ctx.Log("click"), "Hover / Press",
                background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 200, height: 64)
            // transform 成分 (TF): squash & stretch — X と Y で別カーブ/別 duration
            .When(WidgetState.Hover, background: Tw.Red500, scaleX: 1.12f, scaleY: 0.94f, rotate: 0.03f)
            .When(WidgetState.Pressed, background: Tw.Green500)
            .Transition(0.4f, CubicBezierCurve.EaseInOut, ButtonProps.Background)
            .Transition(0.12f, Transform.ScaleX)
            .Transition(0.30f, CubicBezierCurve.EaseInOut, Transform.ScaleY)
            .TransitionTo(WidgetState.Hover, 0.08f, ButtonProps.Background)
            .TransitionTo(WidgetState.Pressed, 0f)
            .TransitionBetween(WidgetState.Pressed, WidgetState.Hover, 0f));

    [Story("CheckBox/Basic", Height = 160)]
    public static Widget CheckBasic(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", false), "Subscribe to newsletter"));

    [Story("CheckBox/CheckedStyle", Height = 160)]
    public static Widget CheckStyled(StoryContext ctx)
        => Frame(Check(ctx.Signal("checked", true), "Custom checked color",
            parts: S.On(WidgetState.Checked, S.Bg(Tw.Green500))));

    [Story("Switch/Basic", Height = 160)]
    public static Widget SwitchBasic(StoryContext ctx)
        => Frame(Switch(ctx.Signal("on", true)));

    [Story("Slider/Basic", Height = 160)]
    public static Widget SliderBasic(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.35f)));

    [Story("Slider/CustomColors", Height = 160)]
    public static Widget SliderColors(StoryContext ctx)
        => Frame(Slider(ctx.Signal("value", 0.6f),
            trackColor: Tw.Slate200, fillColor: Tw.Amber500, knobColor: Tw.Amber500));

    [Story("Segmented/Basic", Height = 160)]
    public static Widget SegmentedBasic(StoryContext ctx)
        => Frame(Segmented(["Day", "Week", "Month"], ctx.Signal("selected", 0)));

    [Story("Radios/Basic", Height = 200)]
    public static Widget RadiosBasic(StoryContext ctx)
        => Frame(Radios(["Small", "Medium", "Large"], ctx.Signal("selected", 1)));

    [Story("Tabs/Basic", Height = 260)]
    public static Widget TabsBasic(StoryContext ctx)
        => Frame(Tabs(["One", "Two", "Three"],
            [Label("Content of tab one"), Label("Content of tab two"), Label("Content of tab three")],
            ctx.Signal("selected", 0), width: 380, height: 160));



    [Story("Tabs/Event", Height = 260)]
    public static Widget TabsEvnet(StoryContext ctx)
    => Frame(Tabs(["One", "Two", "Three"],
        [
            Button(_ => ctx.Log("Content of tab one clicked"), "Content of tab one", margin: new Thickness(0,0,0,0)),
            Button(_ => ctx.Log("Content of tab two clicked"), "Content of tab two", margin: new Thickness(10,0,0,0)),
            Button(_ => ctx.Log("Content of tab three clicked"), "Content of tab three", margin: new Thickness(20,0,0,0))
        ],
        ctx.Signal("selected", 0), width: 380, height: 160));



    [Story("TextField/Basic", Height = 160)]
    public static Widget TextFieldBasic(StoryContext ctx)
        => Frame(TextField(ctx.Signal("text", "Hello"), placeholder: "Type here..."));

    /// <summary>リッチエディタ用の書体 (太字/斜体/等幅。無ければ通常フォント代用)。</summary>
    private static readonly Lazy<(VectorFont? Bold, VectorFont? Italic, VectorFont? BoldItalic, VectorFont? Mono)> EditorFaces = new(() =>
    {
        VectorFont? Try(params string[] names)
        {
            try { return VectorFont.LoadSystem(names); } catch { return null; }
        }
        return (Try("segoeuib.ttf", "arialbd.ttf"), Try("segoeuii.ttf", "ariali.ttf"),
                Try("segoeuiz.ttf", "arialbi.ttf"), Try("consola.ttf", "cour.ttf"));
    });

    [Story("RichTextEditor/Basic", Height = 460)]
    public static Widget RichTextEditorBasic(StoryContext ctx)
    {
        Signal<string> md = ctx.Signal("markdown",
            "# 見出し 1\ntext with **bold**, *italic*, `code` and [link](https://example.com)\n" +
            "## 見出し 2\n> 引用ブロックはミュート色 + 左バー\n- リスト項目\n- 項目 **強調** 入り\n" +
            "1. 番号付き\n2. 自動採番\n---\n```cs\nvar x = 1;\nvar y = x + 1;\n```\n日本語の段落も編集できる。");
        RichTextEditor ed = RichTextEditor(md, height: 330);
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
        RichTextEditor ed = RichTextEditor(md, height: 330);
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
        RichTextEditor visual = RichTextEditor(md, height: 230);
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
        RichTextEditor ed = RichTextEditor(md, height: 360, format: fmt, widgets: widgets);
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

    [Story("Select/Basic", Height = 240)]
    public static Widget SelectBasic(StoryContext ctx)
        => Frame(Select(["Apple", "Banana", "Cherry"], ctx.Signal("selected", 0)));

    // ---- コンテナ/レイアウト ----

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

    /// <summary>UI + 日本語 + カラー絵文字 (COLR — 無い環境では省略) のフォールバック連鎖。</summary>
    internal static readonly Lazy<Luxel.Typography.FontCollection> JpFallback = new(() =>
    {
        VectorFont? emoji = null;
        try { emoji = VectorFont.LoadSystem("seguiemj.ttf"); } catch { /* 絵文字フォント無し */ }
        return emoji is null
            ? new Luxel.Typography.FontCollection(VectorFont.LoadSystem(), VectorFont.LoadSystemJapanese())
            : new Luxel.Typography.FontCollection(VectorFont.LoadSystem(), VectorFont.LoadSystemJapanese(), emoji);
    });

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

    [Story("LengthField/Basic", Height = 200)]
    public static Widget LengthFieldBasic(StoryContext ctx)
    {
        var len = new Signal<Length>((Length)"50%");
        return Frame(VStack(8)[
            Text($"value: {len}", 13, color: Bind.From(() => UiTheme.T.Text)),
            LengthField(len)]);
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

    [Story("Sparkline/Basic", Height = 260)]
    public static Widget SparklineBasic()
    {
        float[] vals = Enumerable.Range(0, 40)
            .Select(i => MathF.Sin(i * 0.35f) * 0.6f + 1.2f + i % 7 * 0.05f).ToArray();
        Sparkline line = Sparkline(260, 64);
        line.SetValues(vals);
        Sparkline bars = Sparkline(260, 48, bars: true);
        bars.SetValues(vals, min: 0);
        return Frame(VStack(8)[line, bars]);
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

    // ---- Kit 複合 ----

    [Story("Kit/Badges", Height = 160)]
    public static Widget Badges() => Frame(HStack(8)[
        Badge("Primary"), Badge("OK", Intent.Success), Badge("Error", Intent.Danger), Chip("Chip")]);

    [Story("Kit/Alert", Height = 180)]
    public static Widget AlertStory() => Frame(VStack(8)[
        Alert("Information message", Intent.Info),
        Alert("Something went wrong", Intent.Danger)]);

    [Story("Kit/Typography", Height = 240)]
    public static Widget Typography() => Frame(VStack(6)[
        Heading("Heading 1"), Heading("Heading 2", 2), Label("Body label"), Muted("Muted caption"),
        Divider(), Skeleton(220, 14)]);

    // ---- アニメ/オーバーレイ ----

    [Story("Spinner/Basic", Height = 160)]
    public static Widget SpinnerBasic() => Frame(Spinner(36f));

    [Story("Accordion/Basic", Height = 280)]
    public static Widget AccordionBasic(StoryContext ctx) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Accordion("Details", VStack(4)[Label("Hidden line 1"), Label("Hidden line 2")],
                       ctx.Signal("expanded", true))];

    [Story("Dropdown/Basic", Height = 280)]
    public static Widget DropdownBasic() =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Dropdown("Open menu", [("Alpha", () => { }), ("Beta", () => { }), ("Gamma", () => { })])];

    [Story("Tooltip/Basic", Height = 220)]
    public static Widget TooltipBasic() => Frame(
        Tooltip(Button(_ => { }, "Hover me"), "Helpful hint"));

    // ---- 追加カバレッジ ----

    [Story("Text/Styles", Height = 220)]
    public static Widget TextStyles() => Frame(VStack(6)[
        Text("Large 28px", 28, color: Bind.From(() => UiTheme.T.Text)),
        Text("Body 16px", 16, color: Bind.From(() => UiTheme.T.Text)),
        Text("Muted 13px", 13, color: Bind.From(() => UiTheme.T.TextMuted)),
        Text("Tailwind colored", 16, color: Tw.Blue500),
        Text("Half opacity", 16, color: Bind.From(() => UiTheme.T.Text), opacity: 0.5f)]);

    [Story("Icon/Kinds", Height = 160)]
    public static Widget IconKinds() => Frame(HStack(10)[
        Icon(IconKind.Check), Icon(IconKind.Close), Icon(IconKind.ChevronDown), Icon(IconKind.ChevronRight),
        Icon(IconKind.Plus), Icon(IconKind.Minus), Icon(IconKind.Dot), Icon(IconKind.Circle),
        Icon(IconKind.Check, color: Tw.Green500), Icon(IconKind.Close, color: Tw.Red500)]);

    [Story("MenuRow/Basic", Height = 200)]
    public static Widget MenuRowBasic() => Frame(
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(6))
            [VStack(2)[
                MenuRow("Open...", _ => { }, hAlign: Align.Stretch),
                MenuRow("Save", _ => { }, hAlign: Align.Stretch),
                MenuRow("Exit", _ => { }, hAlign: Align.Stretch)]]);

    [Story("Dialog/Basic", Height = 320)]
    public static Widget DialogBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                Button(_ => open.Value = true, "Open dialog"),
                Dialog(open, Card(VStack(8)[
                    Heading("Dialog title", 2),
                    Muted("Esc か外側クリックで閉じる"),
                    Button(_ => open.Value = false, "Close")]))]];
    }

    [Story("Toast/Basic", Height = 320)]
    public static Widget ToastBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                Button(_ => open.Value = true, "Show toast"),
                Toast(open, Card(Label("Saved successfully")))]];
    }

    [Story("Drawer/Basic", Height = 320)]
    public static Widget DrawerBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                Button(_ => open.Value = true, "Open drawer"),
                Drawer(open, Card(VStack(6)[Heading("Drawer", 2), Label("Right edge panel")]))]];
    }
}
