using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// MDX 風 docs ページ (Storybook Docs 相当) — 入門章。<see cref="Kit.Docs"/> +
/// 補完文字列で markdown の文章にライブ UI / 他ストーリーの埋め込みを混ぜる。
/// サイズは領域いっぱい (プレビュー全面 = 800×480)。
/// </summary>
public static class DocsHome
{
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
}
