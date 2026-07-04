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
    private const string SampleImage = "src/Luxel.Gallery/goldens/Sparkline_Basic.vk.png";

    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("Docs/Authoring", Order = 91)]
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

        RichTextEditor doc = Docs(ctx, $$$""""
            # docs ページの書き方

            docs ページは **補完文字列 + markdown** で書きます。リテラル部分は markdown として
            整形され、hole に `Widget` を置くとその場に**ライブ UI** が埋め込まれます。
            カラー絵文字 :smile: :rocket: :+1: と "smart quotes" -- SmartyPants も効きます。
            リンクも張れます: [Docs/Button を開く](story:Docs/Button) / [書けるもの へ](#書けるもの) /
            [No Graphics API (外部)](https://www.sebastianaaltonen.com/blog/no-graphics-api)

            ## ページの骨格

            `[Story("Docs/...")]` + `Kit.Docs` + `WithDocFonts` (日本語/絵文字フォールバック +
            シンタックスハイライト + mermaid/math widget の配線) が定型です。文字列は `$$"""`
            (hole = 波かっこ 2 連) にすると、C# コード例の波かっこ 1 連がそのままリテラルに
            なります:

            ```csharp
            [Story("Docs/MyPage", Order = 50)]
            public static Widget MyPage(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
                # 見出し

                本文。hole にはライブ UI が置けます: {{Button(_ => ctx.Log("hi"), "押す")}}

                コード例の波かっこはリテラルです: new Args { Count = n }

                {{StoryRef(ctx, "Button/Variants", knobs: true)}}
                """, toc: true, fences: DocsFences));
            ```

            `Order` がサイドバーの並び、`toc: true` で H2/H3 の目次が H1 直後に入ります。
            ページの H2/H3 はサイドバーのツリーにも出るので、節の粒度 = ナビゲーションの粒度です。

            ## ライブ UI

            下のカウンタは本物です — クリックすると Log パネルに出て、値も動きます:

            {{{counter}}}

            文中への差し込みもできます: 状態 {{{Badge("Ready", Intent.Success):inline}}} や
            ボタン {{{Button(_ => ctx.Log("inline click"), "押す", fontSize: 12f):inline}}} が行内に混ざります。

            ## 埋め込み + Knobs

            `StoryRef(ctx, path, knobs: true)` でストーリーの下に **Knobs テーブル**
            (autodoc の Controls 相当) が付きます。操作列を編集すると上の描画が変わります:

            {{{StoryRef(ctx, "2D/Orbit", knobs: true)}}}

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
            > `$` の数が hole の波かっこの数を決めます。docs ページは `$$"""` で書けば
            > hole は波かっこ 2 連、コード例や TeX の波かっこ 1 連はリテラルです。
            > hole の記法 (2 連) 自体を文章に見せたいページは `$` を 3 つに増やして
            > hole を 3 連にします — このページがそうです。本文に `"""` を含むときは
            > 引用符も 4 連に増やします。

            - hole は**ブロックレベル** (行内は `:inline` 書式指定のみ)。空行も含め、
              書いた改行がそのまま表示されます
            - テキスト hole (Signal や値) は**構築時の値が焼き込まれ**ます (非リアクティブ)
            - `DocMarkdown` の hole は生 markdown の差し込み — `StorySource` のように
              **実行時に組み立てた markdown** を入れる用途に使います
            - 埋め込みストーリーの knob 名がページ側と衝突したら後勝ち
            - StoryRef は 1 ページ 1〜3 個まで (実体化 + snap のコストがかかる)
            - snap (オフスクリーン回帰) は日本語フォールバックフォントがなく豆腐になりますが、
              決定的なので回帰検出には有効です
            """", toc: true, fences: DocsFences);
        return WithDocFonts(doc);
    }

    [Story("Docs/Gallery", Order = 90)]
    public static Widget Gallery(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # Gallery — ストーリーの書き方

        この Gallery は Storybook 相当のカタログ + ドキュメント + 回帰基盤です。
        「ストーリー」= Widget を返す static メソッド 1 つで、実窓カタログ・snap 回帰・
        docs への埋め込みのすべてに同じ実装が使われます。

        ## [Story] 属性とレジストリ

        ```csharp
        public static class ButtonStories
        {
            [Story("Button/Primary", Height = 160)]
            public static Widget Primary() => Frame(Button(_ => { }, "OK"));

            // signal が要るときは StoryContext から — ctx.Signal(...) は自動で knob になる
            [Story("CheckBox/Basic", Height = 160)]
            public static Widget Check(StoryContext ctx)
                => Frame(Check(ctx.Signal("checked", false), "Subscribe"));
        }
        ```

        - パスは `"コンポーネント/ストーリー名"` の 2 階層。`Width`/`Height` (既定 480×320 —
          **両方省略するとプレビュー領域いっぱい (fill)**。docs ページはこれで、全画面モードでは
          メイン全面に、snap では 800×480 固定で描かれます)、`Theme` ("light"/"dark")、
          `Order` (サイドバーの章立て — Docs 0〜 / デモ章 100〜 / コントロール既定 1000 /
          機能デモ 2000〜)
        - 署名は `static Widget M()` か `static Widget M(StoryContext ctx)`
        - 収集は**ソースジェネレーター** (reflection なし) — `[Story]` を走査して
          module initializer で `StoryRegistry.Register` を焼き込み、**メソッドの C# ソース**も
          `StoryInfo.Source` に保存します (docs の `StorySource` はこれ)

        ## StoryContext — ホスト設備の窓口

        - `ctx.Signal(name, initial, description)` — **knob** (右パネルで編集可)。
          bool / int / float / string / 色 / enum / Length に対応
        - `ctx.Log(message)` — Log パネルへ (イベントの実演に)
        - `ctx.Resources` — ホスト所有の ResourceSystem (キャッシュはストーリー横断共有)
        - `ctx.Navigate(path)` — ストーリー遷移 (docs の story: リンクの実体)

        ## 2D / 3D デモのストーリー化

        描画結果を widget にする受け皿が 2 つあります:

        - **Canvas2D(w, h, draw: / animate:(s, t))** — Scene2D を直接描く (UI と同じ保持型
          キャンバスの 1 ノード)。`t` は累積秒
        - **GpuView(w, h, IGpuScene, animated:)** — offscreen に自前 GPU レンダ →
          bindless バッファ経由でゼロコピー合成。共通下回りは `GpuSceneBase` (Gallery 内)

        > [!WARNING]
        > IGpuScene の規約: **ctor は引数保持のみ** (確保は全部 Init — 起動時に全ストーリーが
        > Build される + Dispose 後の再 Init に耐える)。**時間は Render 引数の累積秒のみ**
        > (wall-clock 禁止 — snap の決定性)。ターゲット幅は 64 の倍数 (D3D12 の 256B 整列)。
        > knob を絵に反映するシーンは `animated: true` (Render が毎フレーム呼ばれる)。
        > RenderGraph は 1 フレーム使い切り — animated シーンでは Render 内で毎回作る。

        ## 実行モード

        ```powershell
        dotnet run --project src/Luxel.Gallery -- vk [port] [seconds]   # 実窓 (既定 5180)
        dotnet run --project src/Luxel.Gallery -- vk snap [--update]    # スナップショット回帰
        dotnet run --project src/Luxel.Gallery -- vk bench <story> [frames] [--type|--wheel d]
        ```

        実窓は `Ctrl+D` でテーマ切替、ツールバーの「全画面」でプレビューをメイン全面に。
        サイドバーの検索欄は docs 本文の全文検索です。docs ページの書き方は
        [Docs/Authoring](story:Docs/Authoring) へ。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Contributing", Order = 92)]
    public static Widget Contributing(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # 貢献者向け — ビルド・テスト・回帰ゲート

        ## ビルドとツール (tools/)

        シェーダビルドに standalone Slang + DXC が必要です (`slang-llvm.dll` が GitHub の
        100MB 制限を超えるためリポジトリ非含)。`tools/slang/` に Slang リリースを展開し、
        DXIL 出力用に `dxcompiler.dll` / `dxil.dll` を `tools/slang/bin/` へコピーします。
        あとは `dotnet build` — .slang は targets が spv/dxil へ自動コンパイルします。

        ## テストと snap 回帰

        ```powershell
        dotnet test                                              # 純ロジックのユニットテスト
        dotnet run --project src/Luxel.Gallery -- vk snap        # 全ストーリーを golden と比較
        dotnet run --project src/Luxel.Gallery -- dx snap        # DirectX 12 でも
        ```

        golden は `src/Luxel.Gallery/goldens/*.{vk,dx}.png` — **バックエンド別**に持ちます
        (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)。比較は**ピクセル単位**
        (PNG をデコードして RGBA 比較) で、差分は `.actual.png` に書き出されます。

        > [!IMPORTANT]
        > **verify-before-update** が運用規約です: まず `snap` で既存ストーリーが不変なことを
        > 確認してから、新規/意図的変更分だけ `--update` する (vk / dx の両方)。
        > 全 golden を無差別に `--update` して差分を握りつぶさないこと。

        決定性の仕組み: 固定 dt (1/60s × 8 ステップ) で warmup → シンタックスハイライトの
        静定待ち → dt=0 で 2 ステップ (アニメ時間を進めずドレイン)。snap はオフスクリーンで
        日本語フォールバックがなく豆腐になりますが、決定的なので回帰検出には有効です。

        ## bench — canvas 更新コストの回帰ゲート

        ```powershell
        dotnet run --project src/Luxel.Gallery -- vk bench "LiveCode/StrudelLike" 300
        dotnet run --project src/Luxel.Gallery -- vk bench "TextArea/Basic" 300 --type
        dotnet run --project src/Luxel.Gallery -- vk bench "ListView/Huge" 300 --wheel 1
        ```

        フル再構築回数 / 再構築 CPU / アップロードバイト / マネージド確保を区間計測します。
        期待値 (増分更新の回帰ゲート): **ライブ波形の再生 = フル再構築 0**、エディタの
        タイプ連打 = 再構築 ~3% (ブロック増減時のみ)、仮想化リストのスクロール = 再構築 0。

        ## その他の検証

        - **vk / dx ピクセル一致** — 新しい描画機能は両バックエンドで検証してから完了
        - **デッドリンク検証** — 実窓起動時に docs 全ページの `story:` / `#アンカー` を
          検査し、切れたリンクを stderr に警告します (`[gallery] dead link in ...`)
        - **実窓 E2E** — DebugServer の `/winframe` + `/cmd` で操作と描画を確認
          ([Docs/DevTools](story:Docs/DevTools))
        """, toc: true, fences: DocsFences));
}
