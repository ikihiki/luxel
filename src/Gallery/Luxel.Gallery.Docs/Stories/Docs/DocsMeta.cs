using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Story;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — メタ章 (Gallery 自体の使い方・docs の書き方・貢献者向け)。
/// ページは $$""" (hole = 波かっこ 2 連)。Authoring だけは hole 記法 (2 連) を文章として
/// 見せるため $$$ (hole = 3 連)。本文に """ を含むページは引用符 4 連。</summary>
[StoryMeta("Internals")]
public static class DocsMeta
{
    private const string SampleImage = "src/Gallery/Luxel.Gallery/assets/sample-sparkline.png";

    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story]
    public static StoryResult Authoring(StoryContext ctx)
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

        StoryResult doc = $$$""""
            # docs ページの書き方

            docs ページは **補完文字列 + markdown** で書きます。リテラル部分は markdown として整形され、hole に `Widget` を置くとその場に**ライブ UI** が埋め込まれます。カラー絵文字 :smile: :rocket: :+1: と "smart quotes" -- SmartyPants も効きます。リンクも張れます: [Button overview を開く](story:Controls/Button/Overview) / [書けるもの へ](#書けるもの) / [No Graphics API (外部)](https://www.sebastianaaltonen.com/blog/no-graphics-api)

            ## ページの骨格

            クラスの `[StoryMeta("Internals")]` + メソッドの `[Story]` が定型です。文字列は `$$"""` (hole = 波かっこ 2 連) にすると、C# コード例の波かっこ 1 連がそのままリテラルになります:

            ```csharp
            using static Luxel.Gallery.Story;

            [StoryMeta("Internals")]
            public static class MyStories
            {
                [Story]
                public static StoryResult MyPage(StoryContext ctx) => $$"""
                    # 見出し

                    {{Toc()}}

                    本文。hole にはライブ UI が置けます: {{Button(_ => ctx.Log("hi"), "押す")}}

                    コード例の波かっこはリテラルです: new Args { Count = n }

                    {{StoryRef("Controls/ButtonVariants", knobs: true)}}
                    """;
            }
            ```

            パスは `StoryMeta.title + "/" + 関数名` です。`Toc()` を書いた位置に H2/H3 の目次が入ります。

            ## ライブ UI

            下のカウンタは本物です — クリックすると Log パネルに出て、値も動きます:

            {{{counter}}}

            文中への差し込みもできます: 状態 {{{Badge("Ready", Intent.Success):inline}}} やボタン {{{Button(_ => ctx.Log("inline click"), "押す", fontSize: 12f):inline}}} が行内に混ざります。

            ## 埋め込み + Knobs

            `StoryRef(path, knobs: true)` でストーリーの下に **Knobs テーブル** (autodoc の Controls 相当) が付きます。`using static Luxel.Gallery.Story;` で導入し、Blazor とネイティブで同じ参照形式を使います:

            {{{StoryRef("Examples/Orbit", knobs: true)}}}

            `StorySource(path)` はジェネレーターが公開する **完全な `[Story]` method宣言** (属性・signature・本体) をコードフェンスとして差し込みます。同じコードは通常storyでも下部の **Source** タブから確認でき、静的Galleryでは折りたたみSourceとして表示されます。private helperは含めず、取得したmethod宣言をそのまま表示します。コントロール個別ページでは `DocsApi.ControlApiReference("Button")` で API リファレンス表が出ます (実例は [Controls/Button/Overview](story:Controls/Button/Overview))。

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
            """";
        return doc;
    }

    [Story]
    public static StoryResult Gallery(StoryContext ctx) => $$"""
        # Gallery — ストーリーの書き方

        この Gallery は Storybook 相当のカタログ + ドキュメント + 回帰基盤です。「ストーリー」= Widget を返す static メソッド 1 つで、実窓カタログ・snap 回帰・ docs への埋め込みのすべてに同じ実装が使われます。

        ## [Story] 属性とレジストリ

        ```csharp
        [StoryMeta("Controls")]
        public static class ButtonStories
        {
            [Story]
            public static Widget ButtonPrimary() => Frame(Button(_ => { }, "OK"));

            // signal が要るときは StoryContext から — ctx.Signal(...) は自動で knob になる
            [Story]
            public static Widget CheckBasic(StoryContext ctx)
                => Frame(Check(ctx.Signal("checked", false), "Subscribe"));
        }
        ```

        - `[StoryMeta("章/コンポーネント")]` が本家 Storybook の `title`、関数名がストーリー名です。サイドバーは `title + "/" + 関数名` をそのまま木にし、自然順で並べます。プレビューは常に利用可能領域いっぱいです
        - 署名は `static Widget M()` か `static Widget M(StoryContext ctx)`
        - 収集は**ソースジェネレーター** (reflection なし) — `[Story]` を走査して module initializer で `StoryRegistry.Register` を焼き込み、**完全なmethod宣言の C# ソース**も `StoryInfo.Source` に保存します

        ## Sourceビュー

        通常のstoryは下部Dockの **Source** タブで、属性・signature・本体を含む `[Story]` method宣言を読み取り専用・行番号・C#ハイライト付きで確認できます。静的Galleryでは各story末尾の折りたたみ **Story source** に同じ内容が入ります。Sourceは取得したmethod宣言を加工せずに表示します。Reference/Overviewなどproviderが実行時登録するstoryには対応するmethodがなく、Source unavailableになる場合があります。

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

        音声再生や実デバイス入力のように offscreen の決定的描画にならない機能は、`[StoryMeta("RealWindow/Audio")]` のクラスで `[Story(RealWindowOnly = true)]` を関数に付けます — snap 回帰は SKIP され (golden を作らない)、Gallery アプリでは通常どおり表示されます。

        ## パスと章

        ストーリーパスは **ID** (golden ファイル名・`story:` リンク・E2E 参照) であると同時に、`StoryMeta.title + "/" + 関数名` です。整理・改名では title または関数名を変更し、参照を追従させます。

        ## 実行モード

        ```powershell
        dotnet run --project gallery/GalleryNative -- vk [port] [seconds]           # 実窓 (既定 5180)
        dotnet run --project gallery/GalleryE2E.Native -- vk [--update] [フィルタ]   # E2E 回帰 (play + golden)
        dotnet run --project gallery/GalleryNative -- vk bench <story> [frames] [--type|--wheel d]
        ```

        実窓は `Ctrl+D` でテーマ切替、ツールバーの「全画面」でプレビューをメイン全面に。サイドバーの検索欄は docs 本文の全文検索です。docs ページの書き方は [Internals/Authoring](story:Internals/Authoring) へ。
        """;

    [Story]
    public static StoryResult Contributing(StoryContext ctx) => $$"""
        # 貢献者向け — ビルド・テスト・回帰ゲート

        {{Toc()}}

        ## ビルドとツール (tools/)

        通常ビルドは Git 管理された `shaders/compiled/` の SPIR-V / DXIL を検証して使うため、Slang/DXC の手動導入は不要です。shader変更時だけ `dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache` を実行すると、固定版ツールを取得・SHA-256検証してcacheを更新します。

        ## テストと E2E 回帰 (play + golden)

        ```powershell
        dotnet test                                                # 純ロジックのユニットテスト
        dotnet run --project gallery/GalleryE2E.Native -- vk           # play を実行し golden と比較
        dotnet run --project gallery/GalleryE2E.Native -- vk "Button"  # 部分一致フィルタ ("パス#play名")
        ```

        テストは**ストーリーに同居する play** (本家 Storybook の play 関数相当) です。**golden は play 内の `d.Snap()` だけが生みます** — 初期絵の回帰だけ欲しければ `ctx.Snap(...)` で包む (または `ctx.Play(d => d.Snap())`) の 1 行、対話テストは `ctx.Play(async d => { await d.Click(btn); await d.Expect(...); await d.Snap("clicked"); })` の形で、クロージャからストーリー自身の signal/widget を直接掴めます。名前付きで複数登録でき、**play ごとにストーリーは作り直されます** (独立実行)。play を持たないストーリーは golden を作りません。

        golden は `src/Gallery/Luxel.Gallery/goldens/{Story}[.{Play}][.{Snap名}].{vk,dx}.png` — **バックエンド別**に持ちます (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)。比較はピクセル単位で、スクロールバー端などの極小 AA 差だけ許容します (最大 32px / 最大 5 階調)。それを超える差分は `.actual.png` に書き出されます。

        > [!IMPORTANT]
        > **verify-before-update** が運用規約です: まず `e2e` で既存 play が不変なことを確認してから、新規/意図的変更分だけ `--update` する。全 golden を無差別に `--update` して差分を握りつぶさないこと。どの play も生成しない golden は STALE として列挙されます — 消し忘れの検出用。

        決定性の仕組み: 固定 dt (1/60s × 8 ステップ) で warmup → シンタックスハイライトの静定待ち → dt=0 で 2 ステップ (アニメ時間を進めずドレイン)。play の操作 (Click/Type/Drag/Key) も固定 dt で刻まれます — **play 内で Task.Delay や wall-clock を使わないこと**。トランジションを挟む操作は `await d.Step(n)` で静定させてから Snap します。オフスクリーンは日本語フォールバックがなく豆腐になりますが、決定的なので回帰検出には有効です。

        ## bench — canvas 更新コストの回帰ゲート

        ```powershell
        dotnet run --project gallery/GalleryNative -- vk bench "Examples/Orbit" 300
        dotnet run --project gallery/GalleryNative -- vk bench "Controls/TextEditorView/Basic" 300 --type
        dotnet run --project gallery/GalleryNative -- vk bench "Controls/ListViewHuge" 300 --wheel 1
        ```

        フル再構築回数 / 再構築 CPU / アップロードバイト / マネージド確保を区間計測します。期待値 (増分更新の回帰ゲート): **ライブ波形の再生 = フル再構築 0**、エディタのタイプ連打 = 再構築 ~3% (ブロック増減時のみ)、仮想化リストのスクロール = 再構築 0。

        ## その他の検証

        - **vk / dx ピクセル一致** — 新しい描画機能は両バックエンドで検証してから完了
        - **デッドリンク検証** — 実窓起動時に docs 全ページの `story:` / `#アンカー` を検査し、切れたリンクを stderr に警告します (`[gallery] dead link in ...`)
        - **実窓 E2E** — DebugServer の `/winframe` + `/cmd` で操作と描画を確認
        """;
}
