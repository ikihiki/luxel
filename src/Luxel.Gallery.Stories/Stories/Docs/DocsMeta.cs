using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — メタ章 (Gallery 自体の使い方・docs の書き方・貢献者向け)。
/// ページは $$""" (hole = 波かっこ 2 連)。Authoring だけは hole 記法 (2 連) を文章として
/// 見せるため $$$ (hole = 3 連)。本文に """ を含むページは引用符 4 連。</summary>
public static class DocsMeta
{
    private const string SampleImage = "src/Luxel.Gallery/assets/sample-sparkline.png";

    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("Internals/Authoring", Order = 91)]
    public static Widget Authoring(StoryContext ctx)
    {
        // snap (静定 1 フレーム) の決定性のため画像を同期 preload — 実アプリでは不要
        if (ctx.ResourcesOrNull is { } resources)
        {
            _imagePreload ??= resources.Load<Luxel.Resources.CpuImage>(SampleImage);
            try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダのまま */ }
        }

        Signal<int> count = ctx.Signal("count", 0, "カウンタの現在値 (± ボタンと連動)");
        Widget counter = HStack(8)[
            Button(_ => { count.Value--; ctx.Log("counter: -1"); }, "-"),
            Text($" {count} ", 20, vAlign: Align.Center),
            Button(_ => { count.Value++; ctx.Log("counter: +1"); }, "+")];

        Widget doc = DocNew(ctx, $$$""""
            # docs ページの書き方

            docs ページは **補完文字列 + markdown** で書きます。リテラル部分は markdown として整形され、hole に `Widget` を置くとその場に**ライブ UI** が埋め込まれます。カラー絵文字 :smile: :rocket: :+1: と "smart quotes" -- SmartyPants も効きます。リンクも張れます: [Reference/Guides/Button を開く](story:Reference/Guides/Button) / [書けるもの へ](#書けるもの) / [No Graphics API (外部)](https://www.sebastianaaltonen.com/blog/no-graphics-api)

            ## ページの骨格

            `[Story("Reference/Guides/...")]` + `Kit.Docs` + `WithDocFonts` (日本語/絵文字フォールバック + シンタックスハイライト + mermaid/math widget の配線) が定型です。文字列は `$$"""` (hole = 波かっこ 2 連) にすると、C# コード例の波かっこ 1 連がそのままリテラルになります:

            ```csharp
            [Story("Reference/Guides/MyPage", Order = 50)]
            public static Widget MyPage(StoryContext ctx) => DocNew(ctx, $$"""
                # 見出し

                本文。hole にはライブ UI が置けます: {{Button(_ => ctx.Log("hi"), "押す")}}

                コード例の波かっこはリテラルです: new Args { Count = n }

                {{StoryRef(ctx, "Controls/Button/Variants", knobs: true)}}
                """, toc: true);
            ```

            `Order` がサイドバーの並び、`toc: true` で H2/H3 の目次が H1 直後に入ります。ページの H2/H3 はサイドバーのツリーにも出るので、節の粒度 = ナビゲーションの粒度です。

            ## ライブ UI

            下のカウンタは本物です — クリックすると Log パネルに出て、値も動きます:

            {{{counter}}}

            文中への差し込みもできます: 状態 {{{Badge("Ready", Intent.Success):inline}}} やボタン {{{Button(_ => ctx.Log("inline click"), "押す", fontSize: 12f):inline}}} が行内に混ざります。

            ## 埋め込み + Knobs

            `StoryRef(ctx, path, knobs: true)` でストーリーの下に **Knobs テーブル** (autodoc の Controls 相当) が付きます。操作列を編集すると上の描画が変わります:

            {{{StoryRef(ctx, "Examples/2D/Orbit", knobs: true)}}}

            `StorySource(path)` はジェネレーターが公開する **完全な `[Story]` method宣言** (属性・signature・本体) をコードフェンスとして差し込みます。同じコードは通常storyでも下部の **Source** タブから確認でき、静的Galleryでは折りたたみSourceとして表示されます。private helper、別file、shaderまでは含まれません。実行可能sampleを教材の正にする場合は、build時に埋め込んだ実file/regionを表示する`SampleSource(path, region)`を使います。コントロール個別ページでは `DocsApi.ControlApiReference("Button")` で API リファレンス表が出ます (実例は [Controls/Button/Overview](story:Controls/Button/Overview))。

            ## 書けるもの

            - 見出し / **強調** / *斜体* / `インラインコード`
            - リスト・引用・コードブロック・テーブル (markdown のまま)
            - `> [!TIP]` / `> [!WARNING]` などのコールアウト
            - hole によるライブ UI と他ストーリーの埋め込み (`StoryRef`、`knobs: true` で操作テーブル)
            - `story:` リンクでストーリー遷移 (起動時に**デッドリンク検証**が走る)、`#見出し` でページ内スクロール、http(s) は既定ブラウザ
            - 画像 (Resource システム経由、URI キャッシュ + RefCount):

            ![サンプル画像 (Sparkline golden)]({{{SampleImage}}})

            数式はインライン $E = mc^2$ / $\pi r^2$ (Unicode 正規化) と、$$ ブロック (自前組版):

            $$
            M = \begin{bmatrix} m_{00} & m_{01} \\ m_{10} & m_{11} \end{bmatrix} ,
            w = \frac{\alpha + \beta}{\sqrt{x^2 + y^2}}
            $$

            ダイアグラムは ```mermaid フェンス (flowchart サブセット) — エンジン自身の Scene2D で描画されます:

            ```mermaid
            flowchart LR
            app[GalleryApp] --> host[UiHost]
            host --> canvas[RetainedCanvas]
            host -->|Load| res(Resources)
            canvas -->|dispatch| gpu(GPU)
            ```

            ## 制約と落とし穴

            > [!TIP]
            > `$` の数が hole の波かっこの数を決めます。docs ページは `$$"""` で書けば hole は波かっこ 2 連、コード例や TeX の波かっこ 1 連はリテラルです。hole の記法 (2 連) 自体を文章に見せたいページは `$` を 3 つに増やして hole を 3 連にします — このページがそうです。本文に `"""` を含むときは引用符も 4 連に増やします。

            - hole は**ブロックレベル** (行内は `:inline` 書式指定のみ)。空行も含め、書いた改行がそのまま表示されます — **段落は折り返さず 1 行で書き**、画面幅への折り返しはレイアウトに任せます
            - テキスト hole (Signal や値) は**構築時の値が焼き込まれ**ます (非リアクティブ)
            - `DocMarkdown` の hole は生 markdown の差し込み — `StorySource` のように **実行時に組み立てた markdown** を入れる用途に使います
            - 埋め込みストーリーの knob 名がページ側と衝突したら後勝ち
            - StoryRef は 1 ページ 1〜3 個まで (実体化 + snap のコストがかかる)
            - snap (オフスクリーン回帰) は日本語フォールバックフォントがなく豆腐になりますが、決定的なので回帰検出には有効です
            """", toc: true);
        // golden ①先頭、②「ライブ UI」見出しへスクロールして**行内 widget** (Badge/Button の :inline hole) を映す
        ctx.Play(async d =>
        {
            await d.Snap();
            if (doc is TextEditorView tev)
                foreach (MarkdownHeading h in MarkdownDecorations.Headings(tev.DocSource!))
                    if (h.Text == "ライブ UI") { tev.ScrollToSource(h.Offset); break; }
            await d.Step(2);
            await d.Snap("inline");
        });
        return doc;
    }

    [Story("Internals/Gallery", Order = 90)]
    public static Widget Gallery(StoryContext ctx) => ctx.Snap(DocNew(ctx, $$"""
        # Gallery — ストーリーの書き方

        この Gallery は Storybook 相当のカタログ + ドキュメント + 回帰基盤です。「ストーリー」= Widget を返す static メソッド 1 つで、実窓カタログ・snap 回帰・ docs への埋め込みのすべてに同じ実装が使われます。

        ## [Story] 属性とレジストリ

        ```csharp
        public static class ButtonStories
        {
            [Story("Controls/Button/Primary", Height = 160)]
            public static Widget Primary() => Frame(Button(_ => { }, "OK"));

            // signal が要るときは StoryContext から — ctx.Signal(...) は自動で knob になる
            [Story("Controls/CheckBox/Basic", Height = 160)]
            public static Widget Check(StoryContext ctx)
                => Frame(Check(ctx.Signal("checked", false), "Subscribe"));
        }
        ```

        - パスは**スラッシュ区切りの階層** (本家 Storybook の title 相当、深さ任意) — `"章/コンポーネント/ストーリー名"`。サイドバーはこれをそのまま木にします (章: Docs / Reference / Controls / Demos / Apps / RealWindow)。`Width`/`Height` (既定 480×320 — **両方省略するとプレビュー領域いっぱい (fill)**。docs ページはこれで、全画面モードではメイン全面に、snap では 800×480 固定で描かれます)、`Theme` ("light"/"dark")、`Order` (並び順 — Docs 0〜 / デモ 100〜 / コントロール既定 1000 / 機能デモ 2000〜)
        - 署名は `static Widget M()` か `static Widget M(StoryContext ctx)`
        - 収集は**ソースジェネレーター** (reflection なし) — `[Story]` を走査して module initializer で `StoryRegistry.Register` を焼き込み、**完全なmethod宣言の C# ソース**も `StoryInfo.Source` に保存します

        ## Sourceビュー

        通常のstoryは下部Dockの **Source** タブで、属性・signature・本体を含む `[Story]` method宣言を読み取り専用・行番号・C#ハイライト付きで確認できます。静的Galleryでは各story末尾の折りたたみ **Story source** に同じ内容が入ります。private helper、別file、shaderは含まれないため、完全な実file/regionを教材にするときは `SampleSource(path, region)` を使います。Reference/Overviewなどproviderが実行時登録するstoryには対応するmethodがなく、Source unavailableになる場合があります。

        ## StoryContext — ホスト設備の窓口

        - `ctx.Signal(name, initial, description)` — **knob** (右パネルで編集可)。bool / int / float / string / 色 / enum / Length に対応
        - `ctx.Log(message)` — Log パネルへ (イベントの実演に)
        - `ctx.Resources` — ホスト所有の ResourceSystem (キャッシュはストーリー横断共有)
        - `ctx.ScopedResources` — story instance 所有のResourceScope。story関数内でCPU/GPU resourceをロード・作成し、GpuView callbackへcaptureして渡す。story破棄時にleaseを一括解放
        - `ctx.Navigate(path)` — ストーリー遷移 (docs の story: リンクの実体)

        ## 2D / 3D デモのストーリー化

        描画結果を widget にする受け皿が 2 つあります:

        - **Canvas2D(w, h, draw: / animate:(s, t))** — Scene2D を直接描く (UI と同じ保持型キャンバスの 1 ノード)。`t` は累積秒
        - **GpuView(w, h, render, animated:, dispose:)** — story関数が`ctx.ScopedResources`で用意したresourceをcaptureし、deviceとGpuView所有surfaceを受け取るcallbackでoffscreen描画。callbackは`Ready`/`Loading`/`Failed`を返し、未準備・失敗時は状態アイコンへフォールバック。bindless buffer経由でゼロコピー合成

        > [!WARNING]
        > GpuView callback の規約: GPU resourceはstory関数内で`ctx.ScopedResources`から取得し、callbackへcaptureする。個別handleを`dispose`へ渡す必要はなく、story scopeが一括解放する。**時間はcallback引数の累積秒のみ** (wall-clock禁止 — snapの決定性)。描画先と256B整列済みframebufferは`GpuViewSurface`が所有する。knobを絵に反映する場合は`animated: true`にする。RenderGraphは1フレーム使い切り — animated rendererではcallback内で毎回作る。

        ## 実窓専用ストーリー

        音声再生や実デバイス入力のように offscreen の決定的描画にならない機能は `[Story("RealWindow/Audio/Tone", RealWindowOnly = true)]` にします — snap 回帰は SKIP され (golden を作らない)、Gallery アプリでは通常どおり表示されます。実例は [RealWindow/Audio/Tone](story:RealWindow/Audio/Tone)。

        ## パスと章

        ストーリーパスは **ID** (golden ファイル名・`story:` リンク・E2E 参照) であると同時に、本家 Storybook の title と同じく**サイドバーの階層そのもの**です — 表示用の別マップはありません。章はパスの先頭セグメント (Docs / Reference / Controls / Demos / Apps / RealWindow)。整理・改名はパスを変えて golden を `git mv` で追従させます (ピクセルはパス非依存 — 再撮影が要るのは、パス文字列を **リンク/StoryRef の見出しとして描画している他ページ** だけ)。

        ## 実行モード

        ```powershell
        dotnet run --project src/Luxel.Gallery.Host -- vk [port] [seconds]           # 実窓 (既定 5180)
        dotnet run --project src/Luxel.Gallery.Host -- vk e2e [--update] [フィルタ]   # E2E 回帰 (play + golden)
        dotnet run --project src/Luxel.Gallery.Host -- vk bench <story> [frames] [--type|--wheel d]
        ```

        実窓は `Ctrl+D` でテーマ切替、ツールバーの「全画面」でプレビューをメイン全面に。サイドバーの検索欄は docs 本文の全文検索です。docs ページの書き方は [Internals/Authoring](story:Internals/Authoring) へ。
        """, toc: true));

    [Story("Internals/Contributing", Order = 92)]
    public static Widget Contributing(StoryContext ctx) => DocNew(ctx, $$"""
        # 貢献者向け — ビルド・テスト・回帰ゲート

        ## ビルドとツール (tools/)

        通常ビルドは Git 管理された `shaders/compiled/` の SPIR-V / DXIL を検証して使うため、Slang/DXC の手動導入は不要です。shader変更時だけ `dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache` を実行すると、固定版ツールを取得・SHA-256検証してcacheを更新します。

        ## テストと E2E 回帰 (play + golden)

        ```powershell
        dotnet test                                                # 純ロジックのユニットテスト
        dotnet run --project src/Luxel.Gallery.Host -- vk e2e           # play を実行し golden と比較
        dotnet run --project src/Luxel.Gallery.Host -- vk e2e "Button"  # 部分一致フィルタ ("パス#play名")
        ```

        テストは**ストーリーに同居する play** (本家 Storybook の play 関数相当) です。**golden は play 内の `d.Snap()` だけが生みます** — 初期絵の回帰だけ欲しければ `ctx.Snap(...)` で包む (または `ctx.Play(d => d.Snap())`) の 1 行、対話テストは `ctx.Play(async d => { await d.Click(btn); await d.Expect(...); await d.Snap("clicked"); })` の形で、クロージャからストーリー自身の signal/widget を直接掴めます。名前付きで複数登録でき、**play ごとにストーリーは作り直されます** (独立実行)。play を持たないストーリーは golden を作りません。

        golden は `src/Luxel.Gallery/goldens/{Story}[.{Play}][.{Snap名}].{vk,dx}.png` — **バックエンド別**に持ちます (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)。比較はピクセル単位で、スクロールバー端などの極小 AA 差だけ許容します (最大 32px / 最大 5 階調)。それを超える差分は `.actual.png` に書き出されます。

        > [!IMPORTANT]
        > **verify-before-update** が運用規約です: まず `e2e` で既存 play が不変なことを確認してから、新規/意図的変更分だけ `--update` する。全 golden を無差別に `--update` して差分を握りつぶさないこと。どの play も生成しない golden は STALE として列挙されます — 消し忘れの検出用。

        決定性の仕組み: 固定 dt (1/60s × 8 ステップ) で warmup → シンタックスハイライトの静定待ち → dt=0 で 2 ステップ (アニメ時間を進めずドレイン)。play の操作 (Click/Type/Drag/Key) も固定 dt で刻まれます — **play 内で Task.Delay や wall-clock を使わないこと**。トランジションを挟む操作は `await d.Step(n)` で静定させてから Snap します。オフスクリーンは日本語フォールバックがなく豆腐になりますが、決定的なので回帰検出には有効です。

        ## bench — canvas 更新コストの回帰ゲート

        ```powershell
        dotnet run --project src/Luxel.Gallery.Host -- vk bench "Examples/2D/Orbit" 300
        dotnet run --project src/Luxel.Gallery.Host -- vk bench "Controls/TextEditorView/Basic" 300 --type
        dotnet run --project src/Luxel.Gallery.Host -- vk bench "Controls/ListView/Huge" 300 --wheel 1
        ```

        フル再構築回数 / 再構築 CPU / アップロードバイト / マネージド確保を区間計測します。期待値 (増分更新の回帰ゲート): **ライブ波形の再生 = フル再構築 0**、エディタのタイプ連打 = 再構築 ~3% (ブロック増減時のみ)、仮想化リストのスクロール = 再構築 0。

        ## その他の検証

        - **vk / dx ピクセル一致** — 新しい描画機能は両バックエンドで検証してから完了
        - **デッドリンク検証** — 実窓起動時に docs 全ページの `story:` / `#アンカー` を検査し、切れたリンクを stderr に警告します (`[gallery] dead link in ...`)
        - **実窓 E2E** — DebugServer の `/winframe` + `/cmd` で操作と描画を確認 ([Reference/Guides/DevTools](story:Reference/Guides/DevTools))
        """, toc: true);
}
