using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — メタ章 (Gallery 自体の使い方・docs の書き方・貢献者向け)。</summary>
public static class DocsMeta
{
    private const string SampleImage = "src/Luxel.Gallery/goldens/Sparkline_Basic.vk.png";

    // TeX の { } は $""" の補間と衝突するため、$$ ブロックのデモは生 markdown hole で差し込む
    private static readonly DocMarkdown MathDemo = new("""
        $$
        M = \begin{bmatrix} m_{00} & m_{01} \\ m_{10} & m_{11} \end{bmatrix} ,
        w = \frac{\alpha + \beta}{\sqrt{x^2 + y^2}}
        $$
        """);

    private static readonly DocMarkdown AuthoringExample = new(""""
        ```csharp
        [Story("Docs/MyPage", Width = 800, Height = 480, Order = 50)]
        public static Widget MyPage(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
            # 見出し

            markdown の本文。hole にはライブ UI が置けます: {Button(_ => ctx.Log("hi"), "押す")}

            {StoryRef(ctx, "Button/Variants", knobs: true)}
            """, toc: true, fences: DocsFences));
        ```
        """");

    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("Docs/Authoring", Width = 800, Height = 480, Order = 91)]
    public static Widget Authoring(StoryContext ctx)
    {
        // snap (静定 1 フレーム) の決定性のため画像を同期 preload — 実アプリでは不要
        _imagePreload ??= ctx.Resources.Load<Luxel.Resources.CpuImage>(SampleImage);
        try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダのまま */ }

        Signal<int> count = ctx.Signal("count", 0, "カウンタの現在値 (± ボタンと連動)");
        Widget counter = HStack(8)[
            Button(_ => { count.Value--; ctx.Log("counter: -1"); }, "-"),
            Text($" {count} ", 20, vAlign: Align.Center),
            Button(_ => { count.Value++; ctx.Log("counter: +1"); }, "+")];

        RichTextEditor doc = Docs(ctx, $""""
            # docs ページの書き方

            docs ページは **補完文字列 + markdown** で書きます。リテラル部分は markdown として
            整形され、hole に `Widget` を置くとその場に**ライブ UI** が埋め込まれます。
            カラー絵文字 :smile: :rocket: :+1: と "smart quotes" -- SmartyPants も効きます。
            リンクも張れます: [Docs/Button を開く](story:Docs/Button) / [書けるもの へ](#書けるもの) /
            [No Graphics API (外部)](https://www.sebastianaaltonen.com/blog/no-graphics-api)

            ## ページの骨格

            `[Story("Docs/...")]` + `Kit.Docs` + `WithDocFonts` (日本語/絵文字フォールバック +
            シンタックスハイライト + mermaid/math widget の配線) が定型です:

            {AuthoringExample}

            `Order` がサイドバーの並び、`toc: true` で H2/H3 の目次が H1 直後に入ります。
            ページの H2/H3 はサイドバーのツリーにも出るので、節の粒度 = ナビゲーションの粒度です。

            ## ライブ UI

            下のカウンタは本物です — クリックすると Log パネルに出て、値も動きます:

            {counter}

            文中への差し込みもできます: 状態 {Badge("Ready", Intent.Success):inline} や
            ボタン {Button(_ => ctx.Log("inline click"), "押す", fontSize: 12f):inline} が行内に混ざります。

            ## 埋め込み + Knobs

            `StoryRef(ctx, path, knobs: true)` でストーリーの下に **Knobs テーブル**
            (autodoc の Controls 相当) が付きます。操作列を編集すると上の描画が変わります:

            {StoryRef(ctx, "2D/Orbit", knobs: true)}

            `StorySource(path)` はストーリーの **C# ソース** (ジェネレーターが焼き込み) を
            コードフェンスとして差し込みます — 手書きコピーの乖離が起きません。
            コントロール個別ページでは `ApiTable("Button")` で API リファレンス表が出ます
            (実例は [Docs/Button](story:Docs/Button))。

            ## 書けるもの

            - 見出し / **強調** / *斜体* / `インラインコード`
            - リスト・引用・コードブロック・テーブル (markdown のまま)
            - `> [!TIP]` / `> [!WARNING]` などのコールアウト
            - hole によるライブ UI と他ストーリーの埋め込み (`StoryRef`、`knobs: true` で操作テーブル)
            - `story:` リンクでストーリー遷移 (起動時に**デッドリンク検証**が走る)、
              `#見出し` でページ内スクロール、http(s) は既定ブラウザ
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

            ## 制約と落とし穴

            > [!WARNING]
            > `$"""` の中に C# コード例や TeX を書くと、波かっこが hole と衝突します。
            > 長いコード例は **生 markdown の `DocMarkdown` hole** に逃がすか、ドルを 2 つ
            > 重ねた `$$"""` 補間 (波かっこ 2 連が hole、1 連はリテラル) を使ってください。

            - hole は**ブロックレベル** (行内は `:inline` 書式指定のみ)。空行も含め、書いた改行が
              そのまま表示されます
            - テキスト hole (Signal や値) は**構築時の値が焼き込まれ**ます (非リアクティブ)
            - 埋め込みストーリーの knob 名がページ側と衝突したら後勝ち
            - StoryRef は 1 ページ 1〜3 個まで (実体化 + snap のコストがかかる)
            - snap (オフスクリーン回帰) は日本語フォールバックフォントがなく豆腐になりますが、
              決定的なので回帰検出には有効です
            """", toc: true, fences: DocsFences);
        return WithDocFonts(doc);
    }
}
