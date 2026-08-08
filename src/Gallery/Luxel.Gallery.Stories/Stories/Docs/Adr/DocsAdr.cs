using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — ADR 章 (Architecture Decision Records)。「なぜそう決めたか」を 1 決定 1 ページで残す。
/// 既存 docs と同じ仕組み ([Story] + Docs + WithDocFonts) に乗せ、全文検索・story: リンク・回帰基盤を共有する。
/// 章の並びは所属ページの最小 Order で決まるため 70 番台 (Reference 60 と Demos 100 の間)。
/// **1 ファイル 1 ADR** — このフォルダ `Stories/Docs/Adr/` に `AdrNNNN.cs` を 1 本ずつ置く
/// (全ファイルが `partial class DocsAdr`)。索引 (Overview) と Template はこの `DocsAdr.cs` に同居。
/// 新規 ADR は Template を複製し `[Story("Internals/ADR/NNNN-...", Order = 71 + 番号)]` で登録する。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static partial class DocsAdr
{
    [Story("Internals/ADR/Overview", Order = 70, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Architecture Decision Records (ADR)

        ADR は「**なぜそう決めたか**」を 1 決定 1 ページで残す軽量な記録です。コードや docs は *現在の姿* を説明しますが、ADR は *その姿を選んだ理由* と *検討したが捨てた選択肢* を時系列で残します。後から「なぜ X なのか」を掘り起こす時間を無くすのが目的です。

        ## 新しい ADR の追加

        1. [Internals/ADR/Template](story:Internals/ADR/Template) のコードをコピーする
        2. 次の連番で `[Story("Internals/ADR/NNNN-短いタイトル", Order = 71 + 番号)]` を作る (例: 2 本目なら `Internals/ADR/0002-...`, `Order = 73`)
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

        - [ADR-0001 — アーキテクチャ決定を ADR として Gallery に記録する](story:Internals/ADR/0001-Record-Architecture-Decisions) — **Accepted** (2026-07-08)
        - [ADR-0002 — 3D グラフィック API は「薄い bindless 抽象」を自作する](story:Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction) — **Accepted** (2026-07-08)
        - [ADR-0003 — UI は「宣言的 C# DSL + signals」を自作する](story:Internals/ADR/0003-Declarative-Signal-Ui) — **Accepted** (2026-07-08)
        - [ADR-0004 — 2D はコンピュートラスタライザ + 保持型キャンバス](story:Internals/ADR/0004-Compute-Rasterizer-Retained-2D) — **Accepted** (2026-07-08)
        - [ADR-0005 — ドキュメントとサンプルは Gallery に一本化する](story:Internals/ADR/0005-Docs-In-Gallery) — **Accepted** (2026-07-04)
        - [ADR-0006 — テキストエディタは Transaction ベースの新スタックを新規に作る](story:Internals/ADR/0006-Editor-New-Stack) — **Accepted** (2026-07-08)
        - [ADR-0007 — 浮遊 UI は単一の anchored placement エンジンに統一する](story:Internals/ADR/0007-Floating-Ui-Placement) — **Accepted** (2026-07-08)
        - [ADR-0008 — IME 候補ウインドウを自前描画する (排他モード対応)](story:Internals/ADR/0008-Custom-Ime-Candidates) — **Proposed** (2026-07-08)
        - [ADR-0009 — ノードエディタは汎用の Transaction ベース新スタックとして作る](story:Internals/ADR/0009-Node-Editor-Stack) — **Accepted** (2026-07-08)
        - [ADR-0010 — 複数エディタを束ねる Workbench フレームワークを新規に作る](story:Internals/ADR/0010-Workbench-Framework) — **Accepted** (2026-07-09)
        - [ADR-0011 — PointerEvent にボタンと修飾キーを通す](story:Internals/ADR/0011-Pointer-Button-Modifiers) — **Accepted** (2026-07-09)
        - [ADR-0012 — Markdown/リッチ文書はテキスト新スタックの構成として実装する](story:Internals/ADR/0012-Rich-Document-Stack) — **Accepted** (2026-07-09)
        - [ADR-0013 — メニューは CommandRegistry を単一の真実として全サーフェスを生成する](story:Internals/ADR/0013-Menu-Command-System) — **Accepted** (2026-07-09)
        - [ADR-0014 — Workbench 基盤 UI コントロール群を新設する](story:Internals/ADR/0014-Workbench-Ui-Controls) — **Accepted** (2026-07-09)
        - [ADR-0015 — ゲームプロジェクト/シーン形式はエディタ専用モデル + 一方向コンパイルにする](story:Internals/ADR/0015-Game-Project-Scene-Format) — **Accepted** (2026-07-10)
        - [ADR-0016 — シーンエディタは第 3 の Transaction スタック + 空間アダプタで作る](story:Internals/ADR/0016-Scene-Editor-Stack) — **Accepted** (2026-07-11)
        - [ADR-0017 — プレイインエディタは「都度コンパイル + 停止で破棄」の別インスタンスで動かす](story:Internals/ADR/0017-Play-In-Editor) — **Accepted** (2026-07-11)
        - [ADR-0018 — ゲームの挙動は csx ビヘイビア (状態レス Update) で書く](story:Internals/ADR/0018-Csx-Behaviour-Model) — **Accepted** (2026-07-11)
        - [ADR-0019 — portable GPU semantics として WebGPU backend を追加する](story:Internals/ADR/0019-Portable-Gpu-Semantics-WebGPU-Backend) — **Accepted** (2026-07-28)、Amends ADR-0002

        ## 参考

        - [Internals/Architecture](story:Internals/Architecture) — 現在のレイヤ構成 (ADR は「なぜその構成か」を補う)
        - [Internals/Contributing](story:Internals/Contributing) — ビルド・テスト・回帰ゲート
        - [Internals/Authoring](story:Internals/Authoring) — docs ページの書き方 (ADR も同じ仕組み)
        - [Documenting Architecture Decisions — Michael Nygard (外部)](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
        """;

    // 本文に $$""" のコード例 ("""=引用符 3 連) を含むため引用符 4 連、hole 記法 ({{ }}) も
    // 文章として見せるため $ 3 連 (hole = 波かっこ 3 連) — Internals/Authoring と同じレシピ。
    [Story("Internals/ADR/Template", Order = 71)]
    public static StoryResult Template(StoryContext ctx) => $$$""""
        # ADR テンプレート

        新しい ADR はこの雛形を複製して作ります。`[Story("Internals/ADR/NNNN-...", Order = 71 + 番号)]` で登録し、下の 5 節を埋めてください。**1 ファイル 1 ADR** — `Stories/Docs/Adr/AdrNNNN.cs` を新規に作り (`partial class DocsAdr`)、そこに書くのが定位置です。

        ```csharp
        [Story("Internals/ADR/0002-example-decision", Order = 73, Toc = true)]
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
        """";
}
