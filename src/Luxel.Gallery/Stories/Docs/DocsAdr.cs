using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — ADR 章 (Architecture Decision Records)。「なぜそう決めたか」を 1 決定 1 ページで残す。
/// 既存 docs と同じ仕組み ([Story] + Docs + WithDocFonts) に乗せ、全文検索・story: リンク・回帰基盤を共有する。
/// 章の並びは所属ページの最小 Order で決まるため 70 番台 (Reference 60 と Demos 100 の間)。
/// 新規 ADR は Template を複製し `[Story("ADR/NNNN-...", Order = 71 + 番号)]` で登録する。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsAdr
{
    [Story("ADR/Overview", Order = 70)]
    public static Widget Overview(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # Architecture Decision Records (ADR)

        ADR は「**なぜそう決めたか**」を 1 決定 1 ページで残す軽量な記録です。コードや docs は *現在の姿* を説明しますが、ADR は *その姿を選んだ理由* と *検討したが捨てた選択肢* を時系列で残します。後から「なぜ X なのか」を掘り起こす時間を無くすのが目的です。

        ## 新しい ADR の追加

        1. [ADR/Template](story:ADR/Template) のコードをコピーする
        2. 次の連番で `[Story("ADR/NNNN-短いタイトル", Order = 71 + 番号)]` を作る (例: 2 本目なら `ADR/0002-...`, `Order = 73`)
        3. **Status / Context / Decision / Alternatives / Consequences** の各節を埋める
        4. この索引の「記録一覧」に 1 行追加する
        5. `dotnet build` — ソースジェネレーターが自動でサイドバーへ登録します

        > [!TIP]
        > ADR は**不変の記録**です。誤字修正以外は「上書き」ではなく「新しい ADR で置き換え」。決定を変えるときは古い ADR の Status を `Superseded by ADR-NNNN` にし、新しい ADR を追加します。これで決定の履歴が線形に残ります。

        ## ステータスの意味

        | Status | 意味 |
        | --- | --- |
        | Proposed | 提案中 (議論・レビュー待ち) |
        | Accepted | 採用済み (現行の決定) |
        | Superseded by ADR-NNNN | 後続の ADR に置き換えられた |
        | Deprecated | もう当てはまらないが記録として残す |

        ## 何を ADR にするか

        目安は「**後で理由を聞かれそうな、後戻りしにくい選択**」です。ライブラリ選定・レイヤ境界・全体を貫く規律 (例: vk/dx ピクセル一致、docs の Gallery 一本化)・重い方針転換など。瑣末なリファクタや局所的な実装詳細は ADR にしません。

        ## 記録一覧

        - [ADR-0001 — アーキテクチャ決定を ADR として Gallery に記録する](story:ADR/0001-Record-Architecture-Decisions) — **Accepted** (2026-07-08)
        - [ADR-0002 — 3D グラフィック API は「薄い bindless 抽象」を自作する](story:ADR/0002-Thin-Bindless-Gpu-Abstraction) — **Accepted** (2026-07-08)
        - [ADR-0003 — UI は「宣言的 C# DSL + signals」を自作する](story:ADR/0003-Declarative-Signal-Ui) — **Accepted** (2026-07-08)
        - [ADR-0004 — 2D はコンピュートラスタライザ + 保持型キャンバス](story:ADR/0004-Compute-Rasterizer-Retained-2D) — **Accepted** (2026-07-08)
        - [ADR-0005 — ドキュメントとサンプルは Gallery に一本化する](story:ADR/0005-Docs-In-Gallery) — **Accepted** (2026-07-04)
        - [ADR-0006 — テキストエディタは Transaction ベースの新スタックを新規に作る](story:ADR/0006-Editor-New-Stack) — **Accepted** (2026-07-08)

        ## 参考

        - [Docs/Architecture](story:Docs/Architecture) — 現在のレイヤ構成 (ADR は「なぜその構成か」を補う)
        - [Docs/Contributing](story:Docs/Contributing) — ビルド・テスト・回帰ゲート
        - [Docs/Authoring](story:Docs/Authoring) — docs ページの書き方 (ADR も同じ仕組み)
        - [Documenting Architecture Decisions — Michael Nygard (外部)](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
        """, toc: true, fences: DocsFences));

    // 本文に $$""" のコード例 ("""=引用符 3 連) を含むため引用符 4 連、hole 記法 ({{ }}) も
    // 文章として見せるため $ 3 連 (hole = 波かっこ 3 連) — Docs/Authoring と同じレシピ。
    [Story("ADR/Template", Order = 71)]
    public static Widget Template(StoryContext ctx) => WithDocFonts(Docs(ctx, $$$""""
        # ADR テンプレート

        新しい ADR はこの雛形を複製して作ります。`[Story("ADR/NNNN-...", Order = 71 + 番号)]` で登録し、下の 5 節を埋めてください。`Stories/Docs/DocsAdr.cs` に追記するのが定位置です。

        ```csharp
        [Story("ADR/0002-example-decision", Order = 73)]
        public static Widget Adr0002(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
            # ADR-0002 — 決定の短いタイトル

            - **Status**: Proposed
            - **Date**: 2026-07-08
            - **Deciders**: (関係者)

            ## Context

            どんな力学 (制約・要件・課題) があってこの決定が必要になったか。中立的に、まだ結論は書かない。

            ## Decision

            何を採用するか。能動態で「〜する」と言い切る。

            ## Alternatives

            検討したが採らなかった案と、却下の理由。

            ## Consequences

            この決定で何が良くなり、何を引き受けるか (正負の両方を書く)。
            """, toc: true, fences: DocsFences));
        ```

        ## 各節の書き方

        - **Status / Date / Deciders** — 現在の状態・決定日 (絶対日付)・関与者。決定が置き換わったら Status を `Superseded by ADR-NNNN` に更新する
        - **Context** — 決定に至った *力学*。制約・要件・トレードオフを中立に。ここに結論は書かない
        - **Decision** — 採用する内容を能動態で 1〜数文
        - **Alternatives** — 検討して却下した案と理由 (これが後の再検討を助ける)
        - **Consequences** — 得られるもの (✅) と引き受けるもの (⚠️) の両方

        > [!TIP]
        > 節タイトル (H2) はサイドバーのツリーにも出ます。上の 5 節を固定にしておくと、どの ADR も同じ骨格で読めます。
        """", toc: true, fences: DocsFences));

    [Story("ADR/0001-Record-Architecture-Decisions", Order = 72)]
    public static Widget Adr0001(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0001 — アーキテクチャ決定を ADR として Gallery に記録する

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        Luxel は薄い GPU 抽象の上に多数のサブシステムを積む構成で、随所に「なぜこの設計なのか」という判断があります — 固定レイアウト (8B push 定数 + bindless heap)、vk/dx ピクセル一致という開発規律、docs を Gallery に一本化した方針、Effekseer を統合しない選択などです。

        こうした *理由* は現状、コード・docs・コミットログ・個々人の記憶に散在しています。docs は「現在の姿」を説明しますが、「なぜその姿を選び、何を捨てたか」は残りにくく、後から掘り起こすのに時間がかかります。

        ## Decision

        アーキテクチャ上の重要な決定を **ADR (Architecture Decision Record)** として、Gallery の独立章「**ADR**」に **1 決定 1 ページ**で記録します。既存の docs ページと同じ仕組み (`[Story]` + `Kit.Docs` + `WithDocFonts`) を使い、全文検索・`story:` リンク・デッドリンク検証・テーマ切替をそのまま共有します。

        - 番号は連番 `ADR-NNNN`、パスは `ADR/NNNN-短いタイトル`
        - 節は Status / Context / Decision / Alternatives / Consequences で固定
        - 決定を変えるときは元の ADR を**書き換えず**、新しい ADR を追加して古い方を `Superseded by ADR-NNNN` にする

        ## Alternatives

        - **別リポジトリや `docs/adr/` の素の Markdown** — docs は Gallery に一本化済み ([Docs/Gallery](story:Docs/Gallery)) で、二重管理になりデッドリンク検証も全文検索も効かない → 却下
        - **コミットメッセージ / PR 説明に理由を書く** — 検索性・一覧性が低く、決定単位で辿れない → 却下 (補助としては継続してよい)
        - **記録しない (コード + 記憶に頼る)** — まさに現状の課題そのもの → 却下

        ## Consequences

        - ✅ 決定の理由と却下案が 1 か所に線形に残り、Gallery の検索・ナビゲーション・ダークテーマをそのまま使える
        - ✅ docs と同じ執筆・レビュー・デッドリンク検証フローに乗る (新規 ADR は `[Story]` を足して `dotnet build` するだけ)
        - ⚠️ 決定ごとに 1 ページ書く軽い手間が増える。瑣末な決定まで ADR 化しない線引きが要る ([ADR/Overview](story:ADR/Overview) の「何を ADR にするか」が目安)
        - ⚠️ ADR は不変記録。変更は supersede 運用を守る必要がある
        """, toc: true, fences: DocsFences));

    [Story("ADR/0002-Thin-Bindless-Gpu-Abstraction", Order = 73)]
    public static Widget Adr0002(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0002 — 3D グラフィック API は「薄い bindless 抽象」を自作する

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体はプロジェクト開始時)
        - **Deciders**: ikihiki

        ## Context

        Luxel は 2D ベクター・宣言的 UI・アニメーション・レンダーグラフを積み上げる土台として、C#/.NET から使える 3D グラフィック API を必要としていました。要件と力学は次のとおりです:

        - **マルチバックエンド** — Vulkan と DirectX 12 の両方で動かしたい (Windows 主軸だが GPU API に縛られない)
        - **抽象の薄さ** — Vulkan/D3D12 を素で書くとディスクリプタセット・レイアウトオブジェクト・リソース状態遷移・PSO バリアント管理が肥大化し、抽象の *中* が複雑さの置き場になる
        - **現代 GPU 前提でよい** — 対象は bindless / 64bit ポインタ / dynamic rendering / enhanced barriers を備えた最新世代。旧ハードの互換性は要件にない
        - 参照設計として [Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api) がある — 「最新 GPU の共通機能に絞れば、グラフィック API 抽象はほぼ消せる」という主張の実証も兼ねる

        ## Decision

        既存のグラフィック抽象ライブラリを使わず、*No Graphics API* 設計の**薄い bindless 抽象を C# で自作**します (`Luxel` コア + `Luxel.Vulkan` / `Luxel.D3D12`)。核心は次の 5 点:

        - **固定パイプラインレイアウト** — 全 PSO が「8B push 定数 (ルート引数構造体の GPU アドレス/インデックス) + グローバル bindless heap」の 1 レイアウトを共有。ディスクリプタセットも PSO バリアントも存在しない
        - **bindless 一本** — バッファ/テクスチャは作成時に `BindlessIndex` を持ち、シェーダは `g_buffers[index]` / `g_textures[index]` で参照。「どのリソースを使うか」はルート引数内の index が全て
        - **メモリはただのポインタ** — `device.Malloc(bytes, kind)` で確保し、HostMapped は `Span<T>` で直接読み書き (頂点フォーマット宣言なし、頂点プル)
        - **同期は stage バリアのみ** — `Barrier(srcStage, dstStage)` の 1 種類。リソース個別の状態遷移管理を持たない
        - **Slang 統一シェーダ** — 1 ソースを SPIR-V (Vulkan) と DXIL (D3D12) へ併存コンパイル。シェーダはバックエンド非依存

        現在の姿の詳細は [Docs/GpuDevice](story:Docs/GpuDevice) と [Docs/Architecture](story:Docs/Architecture) へ。

        ## Alternatives

        - **Vulkan (または D3D12) を素で 1 本** — マルチバックエンド要件を満たさない。また素の API は旧世代互換の概念 (ディスクリプタ管理・render pass・細粒度バリア) を強制し、コードベース全体にその複雑さが漏れる → 却下
        - **既存の抽象ライブラリ (Veldrid / bgfx / WebGPU 系)** — いずれも「最大公約数」設計で、旧世代 GPU に合わせたディスクリプタ/バインディングモデルを保持している。bindless・64bit ポインタ・push 定数直渡しを前提にした薄さは得られず、抽象の上にさらに抽象を重ねることになる。C# ネイティブでない (bgfx/WebGPU) 相互運用コストも負う → 却下
        - **OpenGL** — レガシー。bindless 世代の設計検証という目的自体と矛盾 → 却下
        - **ディスクリプタセットを持つ自作抽象 (従来型レイヤ)** — 自作でも従来型のバインディングモデルを写すと、素の API と同じ複雑さを自分で保守することになる。固定レイアウト 1 本に比べ PSO/レイアウトの組み合わせ管理が復活する → 却下

        ## Consequences

        - ✅ アプリコードはバックエンド分岐なしの 1 本 — 確保 (Malloc) → パイプライン (Slang) → 記録 → Submit の 4 手で描画が完結し、ディスクリプタ管理コードが存在しない
        - ✅ PSO 爆発が構造的に起きない (レイアウトが 1 つ、状態は GpuRasterDesc の宣言のみ)
        - ✅ 抽象が薄いので、その上の RenderGraph / TwoD / UI が GPU の実挙動に素直に載る
        - ⚠️ **最新 GPU 専用** — Vulkan 1.3 + bindless 必須。旧ハード・モバイル・macOS (Metal) は対象外。対応するなら別バックエンド追加という大工事になる
        - ⚠️ 抽象を自作した以上、**vk / dx ピクセル一致**を自分で保証し続ける必要がある — snap 回帰をバックエンド別 golden で持ち、新機能は両方で検証してから完了とする開発規律が生まれた
        - ⚠️ バックエンド固有の罠は自分で吸収する (例: D3D12 の CopyTextureToBuffer 行 256B 整列 → RGBA8 はターゲット幅 64 の倍数、DXIL は validator 1.7 指定)
        - ⚠️ stage バリアのみの単純化は「単純な使用パターン」が前提 — 複雑な並行アクセスパターンが将来必要になったら見直し対象
        """, toc: true, fences: DocsFences));

    [Story("ADR/0003-Declarative-Signal-Ui", Order = 74)]
    public static Widget Adr0003(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0003 — UI は「宣言的 C# DSL + signals」を自作する

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体は UI 層の着手時)
        - **Deciders**: ikihiki

        ## Context

        Luxel は自前の GPU 抽象 ([ADR-0002](story:ADR/0002-Thin-Bindless-Gpu-Abstraction)) と保持型 2D 層 (RetainedCanvas) の上に UI 層を必要としていました。要件と力学:

        - **描画先が自前** — 描画は RetainedCanvas の部分更新に落としたい。既存 UI フレームワークは自分のレンダラ (Skia / Direct2D / 合成ツリー) を前提にしており、そのまま載らない
        - **エンジンとの一体性** — docs・Gallery・ゲーム内 UI・ツール (DevTools) まで全部この UI で書く (ドッグフーディング)。エンジンの Signal/アニメーション/リソース系と反応系を共有したい
        - **更新コストの規律** — ライブ波形の再生でフル再構築 0、タイプ連打で再構築 ~3% という bench 回帰ゲートを敷けること。つまり「何が変わったら何が起きるか」がフレームワークの構造から予測可能であること
        - **C# 一枚岩** — マークアップ言語 (XAML) や別言語 (TS/HTML) の層を挟まず、型・リファクタリング・スクリプティング (Roslyn) が UI 構築コードにそのまま効くこと

        ## Decision

        UI フレームワークを自作します (`Luxel.UI` + `Luxel.Controls`)。核心は次の 4 点:

        - **宣言的 C# DSL** — `Button(...)` / `VStack(...)` のベアファクトリ + 子は get-only インデクサ `[...]` + 見た目は省略可能引数 + 追加宣言は fluent 拡張 (`.When` / `.Transition` / `.GridColumn`)。ファクトリ/拡張はソースジェネレーターが `[UiComponent]`/`[UiParam]` から生成する (reflection なし)
        - **signals による細粒度リアクティブ、仮想 DOM なし** — `Signal<T>` / `Computed<T>` / `Effect` が反応系の全部。`Bind.From(() => sig.Value)` で束縛したノードだけが再評価され、色/位置の変化は保持型キャンバスへのスタイル/変換書き込みだけで済む。ツリー全体の再構築→diff は行わない
        - **単一パスレイアウト** — Flutter 風の `Layout(Constraints, parentUsesSize) → Size` 1 回で、同じ呼び出し内に子 Offset を書く (Measure/Arrange の 2 パスなし)
        - **状態 3 層の規約** — 外部 props は `[UiParam] Bindable<T>`、内部の値状態は `Signal<T>` (細粒度反映)、内部の構造状態は plain フィールド + 明示 `Rebuild()`。値の変化と構造の変化を型で区別する

        現在の姿は [Docs/UI](story:Docs/UI) / [Docs/Controls](story:Docs/Controls) / [Docs/Styling](story:Docs/Styling) へ。

        ## Alternatives

        - **既存 .NET UI フレームワーク (WPF / Avalonia / MAUI)** — レンダラ (DirectX 合成 / Skia) が自前で、RetainedCanvas + bindless GPU 経路に載らない。XAML + reflection ベースのバインディングは「何が変わったら何が起きるか」が構造から読めず、bench 回帰ゲートの規律と合わない → 却下
        - **immediate-mode GUI (Dear ImGui 系)** — 毎フレーム全再構築が前提で「フル再構築 0」の規律と正反対。テキスト編集・IME・リッチドキュメントのような保持状態の重いコントロールに不向き → 却下 (ツール用オーバーレイとしても、ドッグフーディング方針から採らない)
        - **WebView (HTML/CSS/JS)** — 別言語・別プロセスの巨大ランタイムを抱え、エンジンの Signal/アニメーションと反応系を共有できない。ゲーム内 UI のフレーム予算にも合わない → 却下
        - **仮想 DOM / diff 方式の自作 (React 風)** — 宣言の書き味は近いが、更新のたびにツリー構築 + diff のアロケーションと CPU を払う。細粒度 signal なら「束縛ノードだけ再評価」で同じ書き味が得られる (SolidJS と同じ判断) → 却下
        - **2 パス Measure/Arrange レイアウト (WPF 風)** — 汎用性は高いが、パスが増えるほどレイアウトコストと複雑さが増す。Flutter が実証した単一パス + Constraints で必要十分 → 却下

        ## Consequences

        - ✅ 値の変化は束縛ノードの再評価だけ — bench の回帰ゲート (波形再生 = フル再構築 0、タイプ連打 = 再構築 ~3%、仮想化リストのスクロール = 再構築 0) が構造的に成立する
        - ✅ UI 構築が C# のみ — 型チェック・リファクタリング・Roslyn スクリプティング・ソース焼き込み (StorySource) がそのまま効く。docs も Gallery も DevTools もこの UI で書けている (ドッグフーディング)
        - ✅ ソースジェネレーター経由なので reflection ゼロ — ファクトリ/DebugProps/knobs が自動生成され、AOT にも素直
        - ⚠️ **コントロールは全部自作** — Button から RichTextEditor・仮想化 ListView・IME 対応テキスト編集まで 40 超を自前で作り、保守する (サードパーティのコントロール生態系はない)
        - ⚠️ 値状態と構造状態の区別は**規約** — 構造が変わるのに `Rebuild()` を忘れる・状態を保つ子をフィールド保持しないと壊れる。CompositeControl の 3 層規約を守る教育コストがある
        - ⚠️ Effect の文脈規律が要る — Effect 内から他 widget の状態 signal を書くと依存追跡と干渉するため、「フラグを立てて Update (effect 外) で書く」パターンを守る必要がある (GalleryApp が実例)
        - ⚠️ 単一パスレイアウトは「親サイズが子に依存し、子が親サイズに依存する」ような循環要求を表現できない — Constraints で表せる範囲に設計を寄せる
        """, toc: true, fences: DocsFences));

    [Story("ADR/0004-Compute-Rasterizer-Retained-2D", Order = 75)]
    public static Widget Adr0004(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0004 — 2D はコンピュートラスタライザ + 保持型キャンバス

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体は 2D 層の着手時)
        - **Deciders**: ikihiki

        ## Context

        UI ([ADR-0003](story:ADR/0003-Declarative-Signal-Ui))・docs・2D ゲームの土台として、GPU 抽象 ([ADR-0002](story:ADR/0002-Thin-Bindless-Gpu-Abstraction)) の上に 2D ベクター描画層が必要でした。要件と力学:

        - **ベクター品質** — テキスト (TTF 輪郭、日本語) と図形をズームしてもエッジが崩れないこと。docs のダイアグラム・数式・エディタまでこの層で描く
        - **部分更新が主戦場** — UI の定常フレームは「動いたものだけ」を書きたい (移動 = 変換だけ、色変更 = スタイルだけ)。bench 回帰ゲート (タイプ連打で再構築 ~3% 等) の土台になる
        - **バックエンド中立** — vk / dx ピクセル一致の規律に乗ること。バックエンド固有の 2D API (Direct2D 等) には依存できない
        - 参照実装として Vello (Rust) が「三角形分割しない compute ラスタライズ」の成立を実証している

        ## Decision

        **GPU コンピュートラスタライザ (Vello 風) + 保持型キャンバス (RetainedCanvas)** を自作します (`Luxel.TwoD`)。核心は次の 4 点:

        - **三角形分割しない** — パスを線分のまま GPU に常駐させ、compute シェーダが画素ごとに巻き数/距離で被覆を計算して塗る (NonZero/EvenOdd、距離ベースの画面一定幅ストローク)。framebuffer は bindless バッファなのでバックエンド変更ゼロ
        - **Encode 1 回、ズームはカメラだけ** — ワールド座標で `Encode` したら `Camera2D` を変えるだけで連続拡縮できる (再エンコードも再分割もない)
        - **保持型ツリー + SoA** — `RetainedCanvas` がフレーム間で保持するノードツリーを提供し、データを Transform / Style / Clip / Order / Segment に分離して持つ。シェーダが per-path 変換を適用するため**移動 = 変換だけ書込、色変更 = スタイルだけ書込** (ジオメトリ不変)
        - **増分更新は「slot 据え置き、レンジは容量付き」** — Content 差し替えは容量内なら in-place、伸びたら末尾追記、空きが閾値を超えたときだけコンパクション。定常フレームのコストは O(変わったノード)

        現在の姿は [Docs/TwoD](story:Docs/TwoD) へ。部分更新量は `LastTransformWrites` 等で観測でき、bench が回帰を監視します。

        ## Alternatives

        - **Skia を製品描画経路に採用** — 実績は最大だが、レンダラが自前 GPU 抽象の外にあり bindless 経路・vk/dx ピクセル一致・部分更新の観測に載らない → 製品経路としては却下。ただし **CPU リファレンスバックエンド (Luxel.TwoD.Skia) として検証用に併用**する
        - **三角形分割 (テッセレーション) 方式 (NanoVG / ImDrawList 系)** — ズームや形状変更のたびに再分割が走り、「Encode 1 回でスムーズズーム」が成立しない。曲線の分割粒度と AA 品質のトレードオフも抱える → 却下
        - **Direct2D / バックエンド固有 2D API** — vk 側に対応物がなく、バックエンド中立の規律と矛盾 → 却下
        - **SDF ベース (テキスト/図形を距離場テクスチャ化)** — 小さい字の品質とアトラス管理のコストが重く、任意パス (穴あき・複数パス合成) への一般化が難しい → 却下
        - **即時モード (毎フレーム頂点生成)** — 部分更新の要件と正反対。定常フレームで O(シーン全体) を払い続ける → 却下

        ## Consequences

        - ✅ ベクターのままズームしてもエッジが崩れず、テキストも同じパイプラインで描ける (日本語含む)
        - ✅ UI の定常フレームが「変わったものだけの書込」に落ち、`LastTransformWrites` 等の観測 + bench で回帰を機械的に検出できる
        - ✅ compute + bindless バッファだけで完結するため、バックエンド追加コストが GPU 抽象側に閉じる
        - ⚠️ **現状はブルートフォース** — 画素×線分 (+ bbox 早期スキップ) で、タイル binning は今後の課題。AA も 4x4 スーパーサンプルで解析的 AA ではない
        - ⚠️ compute ラスタライザは非標準経路 — RenderDoc 等での三角形パイプラインのデバッグ手法がそのまま効かず、問題調査は自前の観測カウンタと golden 比較が頼り
        - ⚠️ 容量付き slot の増分更新は複雑さの置き場 — コンパクション条件やノード増減の増分化 (保留中) など、チューニング項目を自前で抱える
        """, toc: true, fences: DocsFences));

    [Story("ADR/0005-Docs-In-Gallery", Order = 76)]
    public static Widget Adr0005(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0005 — ドキュメントとサンプルは Gallery に一本化する

        - **Status**: Accepted
        - **Date**: 2026-07-04
        - **Deciders**: ikihiki

        ## Context

        当初、ドキュメントはリポジトリの `docs/` (計画 MD 17 本)、サンプルは `src/Luxel.Samples` (CLI サンプル群) に分かれていました。ここに次の力学が働いていました:

        - **乖離** — MD のコード例・API 説明は実装の変化に追従せず、静かに古びる。CLI サンプルも回帰テストに乗っておらず、壊れても気づけない
        - **重複** — 同じ機能の説明が計画 MD・サンプル・Gallery のストーリーに三重化し、どれが正か曖昧になる
        - 一方で Gallery には**ストーリー = 実行可能な実例 + snap/e2e 回帰 + ソース焼き込み (StorySource)** という基盤がすでにあり、docs ページ (markdown + ライブ UI 埋め込み) の仕組みも育っていた

        ## Decision

        `docs/` と `Luxel.Samples` を**完全削除**し、ドキュメント・サンプル・回帰テストのすべてを **Luxel.Gallery に一本化**します:

        - **ドキュメント** = Gallery の Docs 章 (`Stories/Docs/Docs*.cs`) — markdown + hole によるライブ UI 埋め込み。計画 MD の「設計ノート」は現在形の仕様として吸収する
        - **サンプル** = 説明的デモストーリー — `StoryRef` で docs に埋め込み、`StorySource` (ジェネレーターがソースを焼き込む) で手書きコピーの乖離を根絶する
        - **API リファレンスは実行時生成** — `[UiComponent]` 由来のコントロール API と、`GenerateAssemblyApi` が XML doc ごと焼き込む型 API (Reference 章)。手書き一覧は作らない
        - **検証を docs にも敷く** — 起動時のデッドリンク検証 (`story:` / `#アンカー`)、play + golden の E2E 回帰、bench。docs ページも「テストされる成果物」にする
        - 今後の**計画文書はリポジトリに置かない** — `docs/` を復活させない

        ## Alternatives

        - **`docs/` の markdown を維持** — GitHub 上で読める利点はあるが、コード例の乖離・リンク切れ・実行不能という核心の課題が残る → 却下
        - **静的サイトジェネレーター (DocFX / Docusaurus 等)** — 別ツールチェーンの保守が増える上、ライブ UI 埋め込み (本物のカウンタ・knobs・GPU デモ) ができない。エンジンのドキュメントスタック (RichTextEditor / mermaid / 数式) のドッグフーディング機会も失う → 却下
        - **XML doc コメントのみ** — API 表は賄えるが、章立てのガイド・設計解説・動く実例は表現できない → 却下 (XML doc は Reference 章の材料として併用)
        - **Luxel.Samples を回帰テスト化して存続** — テストを敷いてもストーリーとの重複は残る。「実例は全部ストーリー」に寄せる方が一本化される → 却下

        ## Consequences

        - ✅ 正が 1 か所 — 実例は実行可能で golden 回帰に守られ、`StorySource` によりドキュメント上のコードが実装と乖離しない。リンク切れは起動時に検出される
        - ✅ docs 執筆がエンジン自身への品質圧になる — RichTextEditor・mermaid・数式・全文検索は docs の必要から鍛えられた (ドッグフーディング)
        - ✅ この ADR 章もタダ乗りできた ([ADR-0001](story:ADR/0001-Record-Architecture-Decisions)) — 検索・リンク検証・テーマ・回帰が最初から付いてくる
        - ⚠️ **GitHub 上でそのまま読めない** — docs を読むには Gallery を起動する (かソースを読む)。公開ドキュメントが必要になったら書き出し手段の検討が要る
        - ⚠️ 執筆に C# ビルドが要り、raw string の規約 (`$` の数 = hole の波かっこ数、段落は 1 ソース行、本文に引用符 3 連を含むページは区切りを 4 連に) を覚える必要がある
        - ⚠️ 計画文書のリポジトリ内の置き場が無くなる — 意図的な制約 (現在形の仕様として docs に吸収するか、リポジトリ外へ)
        """, toc: true, fences: DocsFences));

    [Story("ADR/0006-Editor-New-Stack", Order = 77)]
    public static Widget Adr0006(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0006 — テキストエディタは Transaction ベースの新スタックを新規に作る

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        テキスト編集系コントロールに、性質の異なる要件が**同一エディタの同一行で同時に**求められるようになりました: 標準的な編集作業、Strudel の行内 UI コントロール配置と「いま鳴らしているシーケンス」を示す文字の囲み、C# 診断の下線・波線、コードエディタの行内色分けです。

        既存のエディタ (TextArea / CodeEditor / RichTextEditor) は共有エンジン DocumentEditor の上に建っており、その中核は**単一 Caret/Anchor** です。当初はこの資産を活かす「三層への段階移行」を検討しましたが、詰めると **単一カーソル制約に由来する 2 つの匂い**が避けられないと分かりました:

        - **選択の分裂** — マルチカーソルを入れると、プライマリ (engine) とセカンダリ (presenter) に選択状態が二分される。片方が単一カーソルモデルの都合でしかない
        - **写像機構の二重化** — 編集時の「engine が内部でカーソルを動かす」経路と「装飾を別途 MapThrough で動かす」経路が別物になり、位置写像が 2 系統になる

        これらは *制約の産物*で、native に複数レンジを持つモデルなら消える構造です。今回の 4 要件は**新しい能力の追加**であり、既存を改変せず新機能として作れます。ならば制約を引きずった移行ではなく、**綺麗なモデルを新スタックとして作る**のが筋だ、という力学です。

        ## Decision

        既存の DocumentEditor / TextArea / CodeEditor / RichTextEditor には**一切手を入れず**、**Transaction ベースの新エディタスタックを新規に追加**します。中核は canvas 非依存の新プロジェクト `Luxel.Editor`、ビューは `Luxel.Controls` の薄い widget。CodeMirror 6 に倣った設計です。

        - **不変の `EditorState`** = テキスト文書 + `Selection` (複数レンジ + main index) + 装飾状態。**マルチカーソルが native** — 単一カーソルはレンジ 1 個の特殊ケースで、プライマリ/セカンダリの区別は存在しない
        - **編集は Transaction** — 変更を `ChangeSet` (retain/insert/delete 列) として運び、`ChangeSet.MapPos` が**唯一の写像機構**として選択・装飾・非同期プロバイダの古い結果を一括写像する。undo は反転 ChangeSet (1 Transaction = 1 undo なので、N カーソルの 1 打鍵が自動で 1 undo)
        - **装飾は第一級の状態** — Mark (前景色/背景/下線/波線/囲み)・Widget (サイズ + 不透明キー)・LinePrefix (行頭の番号/記号)・Block (行グループ背景/縦バー)・Line (行背景)。供給側 (シンタックス/診断/検索/Strudel 再生位置) は DecorationSet を差し替えるだけ。**レイアウトに効く装飾** (色/widget/prefix = 行再レイアウト) と**効かない装飾** (背景/下線/波線/囲み = 矩形再計算のみ) を区別し、後者は行キャッシュに触れず毎フレーム更新できる (再生囲みが 60fps で動く根拠)
        - **canvas 非依存のジオメトリ層** — `TextLayout` を使ってソース↔表示写像 (行頭 prefix・widget ボックス・IME 合成を同一機構に統合)・pos↔座標・各種矩形を計算。**選択状態を持たない純射影**。TextLayout が canvas 非依存なので、この層まで GPU 不要の単体テストで固まる (実 DOM 計測を要する DOM 系エディタに対する Luxel の優位点)
        - **view は canvas がないとできないことだけ** ([UiComponent]、Luxel.Controls) — Transaction のディスパッチ、ジオメトリ出力を RetainedCanvas レイヤへ塗る、hit/focus/scroll/IME(TSF) 配線、インライン widget のホスト (resolver + `OnChildNeedsRealize`)、キャレット点滅。生入力を Transaction に変換する「状態を投影して塗るだけ」の皮
        - **具体エディタは構成**で作る — `CodeEditorView` = view + ガター + syntax/診断/検索プロバイダ + 補完 chrome。Strudel ブロック = view + インライン widget + 再生囲みプロバイダ
        - **トークナイザ/言語契約は新スタック側に持つ** (model 非依存の `SyntaxToken`/`TokenKind` は再利用可)。既存の TextMate/Roslyn 実装は薄いアダプタで橋渡しし、新スタックは旧 Luxel.Document モデルに依存しない

        実装計画は ToDo/22 (段階 S1〜S8)。旧 07 のマルチカーソル + 矩形選択は S7 に畳んだ (native モデルではほぼコマンド追加で済む)。現在の姿は [Docs/Editor](story:Docs/Editor) が正。

        ## Alternatives

        - **既存コントロールの段階移行 (当初案)** — プライマリ/セカンダリ分裂と写像 2 系統を制約として抱え込み、単一カーソル前提の undo ハックも要る。加えて 3 コントロール共有の engine を触るため波及が広い。要件が新機能である以上、綺麗な新スタックの方が良い → 却下
        - **DocumentEditor を native 複数レンジへ改修** — RichTextEditor のブロックモデル・IME/TSF ブリッジ・879 本のテストに波及する大手術 → 却下
        - **CodeEditor を機能ごとに増築し続ける** — 要件の組み合わせのたびに描画パスが増殖し、等幅前提の負債も残る (元の課題そのもの) → 却下
        - **既存エディタコンポーネントの移植 (AvaloniaEdit 等)** — 自前レンダラ前提で RetainedCanvas に載らない ([ADR-0003](story:ADR/0003-Declarative-Signal-Ui) と同じ力学) → 却下

        ## Consequences

        - ✅ モデルが一枚岩 — マルチカーソルとバッチ undo が実質タダで手に入り、4 要件は装飾の直交な組み合わせになる
        - ✅ 中核 (状態/変更/選択/装飾写像/ジオメトリ) が canvas 非依存で厚く単体テストでき、e2e play への依存が減る
        - ✅ 等幅ハードコードが無く、プロポーショナル・日本語・合字・折返しが最初から正しい
        - ✅ `ChangeSet` が選択・装飾・非同期結果の位置写像を単一機構に統一 — 逆順ループの職人芸が要らない
        - ⚠️ **エディタスタックが 2 つ併存する** — 旧 CodeEditor/TextArea は新スタックが実証されるまで残す (削除は将来の別判断)。RichTextEditor は文書/Markdown 役として存続。保守面積が一時的に増える
        - ⚠️ **新規コード量が大きい** — IME/TSF ブリッジ・undo・ChangeSet 代数を新 view 向けに正しく作り直す (DocumentEditor の実装は流用しない)
        - ⚠️ Strudel/デモを新コントロールへ移行する必要があり、MiniNotation にソーススパンの配管を足す (greenfield)
        - ⚠️ 「view を純粋な塗り役に保つ」境界規律を維持する必要がある — ロジックは canvas 非依存の層に置く
        """, toc: true, fences: DocsFences));
}
