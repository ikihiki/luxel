using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// MDX 風 docs ページ (Storybook Docs 相当) のストーリー。<see cref="Kit.Docs"/> +
/// 補完文字列で markdown の文章にライブ UI / 他ストーリーの埋め込みを混ぜる。
/// サイズは領域いっぱい (プレビュー全面 = 800×480)。
/// </summary>
public static class DocsStories
{
    /// <summary>他ストーリーの埋め込み (Storybook の <c>&lt;Story of=... /&gt;</c> 相当)。
    /// docs ページの <paramref name="ctx"/> で実体化するので Log/knob は docs ページに合流する。
    /// <paramref name="knobs"/> = true でストーリーの下に Knobs テーブル (autodoc の Controls 相当) を
    /// 出す — **この Build が登録した knob だけ** (登録数の前後差分で切り出す)。
    /// ソース表示は <see cref="StorySource"/> (生 markdown hole) を隣に置く。
    /// パス不明はエラーカード (ページ全体は落とさない)。</summary>
    private static Widget StoryRef(StoryContext ctx, string path, bool knobs = false)
    {
        StoryInfo? s = StoryRegistry.Find(path);
        if (s is null)
            return VStack(6)[
                Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                Alert($"ストーリーが見つかりません: {path}", Intent.Danger)];

        int before = ctx.Knobs.Count;
        Widget body = s.Build(ctx);
        var parts = new List<Widget>
        {
            Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
            body,
        };
        if (knobs)
        {
            StoryKnob[] mine = ctx.Knobs.Skip(before).ToArray();
            parts.Add(Divider());
            parts.Add(KnobsTable(mine, width: 640,
                onEdit: (_, k, v) => ctx.QueueKnobEdit(k, v)));
        }
        return VStack(6)[parts.ToArray()];
    }

    /// <summary>ストーリーの C# ソース (storysource — ジェネレーターが焼き込み) をコードフェンスとして
    /// 差し込む生 markdown hole。ページ本体のシンタックスハイライトがそのまま効く。</summary>
    private static DocMarkdown StorySource(string path)
        => StoryRegistry.Find(path) is { Source.Length: > 0 } s
            ? new DocMarkdown($"```csharp\n{s.Source}\n```")
            : new DocMarkdown($"```\n(ソースなし: {path})\n```");

    private const string SampleImage = "src/Luxel.Gallery/goldens/Sparkline_Basic.vk.png";

    // TeX の { } は $""" の補間と衝突するため、$$ ブロックのデモは生 markdown hole で差し込む
    private static readonly DocMarkdown MathDemo = new("""
        $$
        M = \begin{bmatrix} m_{00} & m_{01} \\ m_{10} & m_{11} \end{bmatrix} ,
        w = \frac{\alpha + \beta}{\sqrt{x^2 + y^2}}
        $$
        """);
    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("Docs/GettingStarted", Width = 800, Height = 480, Order = 0)]   // 章立て: Docs を先頭、入門を最初に
    public static Widget GettingStarted(StoryContext ctx)
    {
        // snap (静定 1 フレーム) の決定性のため画像を同期 preload — 実アプリでは不要
        _imagePreload ??= ctx.Resources.Load<Luxel.Resources.CpuImage>(SampleImage);
        try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダのまま */ }

        Signal<int> count = ctx.Signal("count", 0, "カウンタの現在値 (± ボタンと連動)");
        Widget counter = HStack(8)[
            Button(_ => { count.Value--; ctx.Log("counter: -1"); }, "-"),
            Text($" {count} ", 20, vAlign: Align.Center),
            Button(_ => { count.Value++; ctx.Log("counter: +1"); }, "+")];

        RichTextEditor doc = Docs(ctx, $"""
            # MDX 風 docs ページ

            これは **補完文字列 + markdown** で書くドキュメントです。リテラル部分は markdown として
            整形され、hole に `Widget` を置くとその場に**ライブ UI** が埋め込まれます。
            カラー絵文字 :smile: :rocket: :+1: と "smart quotes" -- SmartyPants も効きます。
            リンクも張れます: [Docs/Button を開く](story:Docs/Button) / [書けるもの へ](#書けるもの) /
            [No Graphics API (外部)](https://www.sebastianaaltonen.com/blog/no-graphics-api)

            ## ライブ UI

            下のカウンタは本物です — クリックすると Log パネルに出て、値も動きます:

            {counter}

            文中への差し込みもできます: 状態 {Badge("Ready", Intent.Success):inline} や
            ボタン {Button(_ => ctx.Log("inline click"), "押す", fontSize: 12f):inline} が行内に混ざります。

            ## 埋め込み + Knobs

            `StoryRef(ctx, path, knobs: true)` でストーリーの下に **Knobs テーブル**
            (autodoc の Controls 相当) が付きます。操作列を編集すると上の描画が変わります:

            {StoryRef(ctx, "2D/Orbit", knobs: true)}

            ## 書けるもの

            - 見出し / **強調** / *斜体* / `インラインコード`
            - リスト・引用・コードブロック・テーブル (markdown のまま)
            - hole によるライブ UI と他ストーリーの埋め込み (`StoryRef`、`knobs: true` で操作テーブル)
            - `story:` リンクでストーリー遷移、`#見出し` でページ内スクロール、TOC は `toc: true`
            - 画像 (Resource システム経由、URI キャッシュ + RefCount):

            ![サンプル画像 (Sparkline golden)]({SampleImage})

            数式はインライン $E = mc^2$ / $\pi r^2$ (Unicode 正規化) と、$$ ブロック (自前組版):

            {MathDemo}

            ダイアグラムは ```mermaid フェンス (flowchart サブセット) — エンジン自身の Scene2D で描画されます:

            ```mermaid
            flowchart LR
            app[GalleryApp] --> host[UiHost]
            host --> canvas[RetainedCanvas]
            host -->|Load| res(Resources)
            canvas -->|dispatch| gpu(GPU)
            ```

            > hole はブロックレベル。空行も含め、書いた改行がそのまま表示されます。
            > テキスト hole (Signal や値) は構築時の値が焼き込まれます。
            """, toc: true, fences: DocsFences);
        return WithDocFonts(doc);   // 日本語/絵文字フォールバック + ハイライト + mermaid widget
    }

    [Story("Docs/Button", Width = 800, Height = 480, Order = 1)]
    public static Widget ButtonDocs(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # Button

        ボタンは **Variant × Intent × 状態** から配色を解決します。未指定のプロパティは
        テーマ値へフォールバックし、hover/press はトランジション (状態機械) で補間されます。

        ## Variant (形)

        > [!TIP]
        > 下の実例のすぐ下に `StorySource` で**実際のストーリーソース**を出しています
        > (ジェネレーターが焼き込むため、手書きコピーの乖離が起きません)。

        {StoryRef(ctx, "Button/Variants")}

        {StorySource("Button/Variants")}

        ## Intent (意味色)

        {StoryRef(ctx, "Button/Intents")}

        ## 使い方

        ```csharp
        Button(_ => ctx.Log("clicked"), "OK", variant: Variant.Tonal, intent: Intent.Success)
        ```

        コールバックの第一引数は**発火元の Button 自身** (sender-first 規約) です。
        入門は [GettingStarted](story:Docs/GettingStarted) へ。

        ## API

        {ApiTable("Button")}
        """, toc: true, fences: DocsFences));

    /// <summary>docs ページ共通のフェンス拡張 (```mermaid → Luxel.Diagram)。</summary>
    internal static readonly Luxel.Document.IFenceResolver[] DocsFences =
        [Luxel.Diagram.MermaidFenceResolver.Instance];

    private static RichTextEditor WithDocFonts(RichTextEditor doc)
    {
        doc.Fonts = ControlStories.JpFallback.Value;
        doc.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        doc.Widgets.Register("mermaid", bc => Luxel.Diagram.Factories.DiagramBlock(
            ((Luxel.Document.FencePayload)bc.Payload).Body, bc.MaxWidth));
        doc.Widgets.Register("math", bc => Luxel.MathText.Factories.MathBlockView(
            ((Luxel.Document.MathPayload)bc.Payload).Source, maxWidth: bc.MaxWidth));
        return doc;
    }
}
