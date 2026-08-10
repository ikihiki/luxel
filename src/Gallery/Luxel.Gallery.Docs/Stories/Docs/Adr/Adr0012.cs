using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0012-Rich-Document-Stack", Order = 83, Toc = true)]
    public static StoryResult Adr0012(StoryContext ctx) => $$"""
        # ADR-0012 — Markdown/リッチ文書はテキスト新スタックの構成として実装し、RichTextEditor を置き換える

        - **Status**: Accepted
        - **Date**: 2026-07-09
        - **Deciders**: ikihiki

        ## Context

        > [!NOTE]
        > `ToDo/22`〜`ToDo/27` はADR作成当時の計画番号で、現在のファイル参照ではありません。現行の実装と利用手順は本文からリンクするLearnページと実装を正とします。


        `RichTextEditor` (旧 `DocumentEditor` スタック) は「旧エディタ」ではなく、**現役の Docs 描画エンジン**です。`Kit.Docs()` ファクトリが返す型がこれで、Gallery の全ドキュメント (ADR を含む 11 モジュール) がこれで描かれています。中身はブロックモデル (Block→Line→UiNode) + 埋め込みライブ UI + mermaid (`Luxel.Diagram`) + 数式 (`Luxel.MathText`) + シンタックス fence (`Luxel.Highlight.TextMate`) + 全文検索アンカーで、docs の Gallery 一本化 ([ADR-0005](story:Internals/ADR/0005-Docs-In-Gallery)) の土台そのものです。Workbench 化 ([ADR-0010](story:Internals/ADR/0010-Workbench-Framework)) と旧スタック整理 (`CodeEditor` 削除) に合わせ、この文書役を新スタックの流儀に揃えたい。

        当初は「別の Transaction ベース リッチ文書スタックを新規に作る」案でした。しかしテキスト新スタック ([ADR-0006](story:Internals/ADR/0006-Editor-New-Stack)) は **CodeMirror 6 流**で、CM6 / Obsidian の Live Preview はまさに「下地はテキスト (行)、表示はブロック装飾、編集はブロック単位コマンド」で Markdown を成立させています。核心は — **ブロックはデータモデルではなく、行テキストをパースした射影**である点。ならば別スタック (別のブロックモデル) はモデルの二重化で、テキストスタックの構成として載せるのが筋だ、という力学です。

        ## Decision

        Markdown/リッチ文書を、別プロジェクトを作らず**テキスト新スタック ([ADR-0006](story:Internals/ADR/0006-Editor-New-Stack)) の構成**として実装します。

        - **ブロックはテキストの射影** — 編集はテキストの `ChangeSet` のまま。ブロック意味論は「ブロック単位コマンド」(移動/インデント/見出し化/リスト継続/セクション折畳) で表現する。ブロック = 解決した行レンジで、安定 id も別データ構造も要らない (テキストから毎回パース。`ChangeSet.MapPos` が装飾もブロックも編集を生き延びさせる)
        - **表示は行/ブロック/widget 装飾** — Line 背景 / LinePrefix (リスト記号・引用バー) / Block (行グループ背景・縦バー・Indent) / Mark (インライン装飾) / Widget (表・mermaid・数式・埋め込みライブ UI を block/inline で置換)。全文検索はテキストが下地なので native、TOC は見出し行から導出
        - **read-only モード = Docs レンダラ** (`Kit.Docs()` の差し替え先。ADR を含む全 11 モジュールを移行)。**編集モード = Markdown ハイブリッド/Live Preview エディタ** (同じ機構の編集可版)

        テキストスタックに足す小さな拡張:

        1. **font-variant Mark** (太字/斜体/見出しサイズ) — 現状 Mark は色/背景/下線/囲みのみ。フォント変種は**レイアウトに効く装飾** (前景色と同じ `AffectsLayout`)。ジオメトリは行内 mixed-weight ラン対応を足す
        2. **ブロック widget の Markdown 利用** (表/mermaid/数式ブロック) — 機構は既存 (Strudel で実証)、Markdown 用リゾルバを足す
        3. **Markdown 装飾プロバイダ + widget リゾルバ** — パーサ → Line/Block/Mark/LinePrefix + widget。内容プロセッサ (Diagram/MathText/Highlight.TextMate) を **widget の中身**として再利用 (`Luxel.Document` 依存の共有型はここへ移設/中立化)
        4. **ブロック単位コマンド** (move/indent/heading/list 継続/fold)

        移行後、`RichTextEditor` + `TextArea` + `Luxel.Document` の編集核 (`DocumentEditor`) を削除します。編集モードは read-only レンダラの後段 (Markdown 編集需要が実際に出た時点で厚くする)。

        実装計画は ToDo/26。

        ## Alternatives

        - **別の Transaction ベース リッチ文書スタックを新規に作る** (当初案) — テキストスタックが CM6 流でブロックを射影として扱えるため、別スタックはモデルの二重化になる。ブロックは安定 id を要さずテキストから再パースできる → 却下 (テキストスタックの構成に統合)
        - **`RichTextEditor` をレンダラとして存続** — 新スタック統一の方針に反する → 却下
        - **完全削除して Docs 描画を廃止** — [ADR-0005](story:Internals/ADR/0005-Docs-In-Gallery) (docs の Gallery 一本化) を壊す → 却下

        ## Consequences

        - ✅ 編集スタックが **2 つ (テキスト/ノード) に集約** — Markdown/docs/コード/Strudel が全てテキストスタックの構成になり、保守面積が最小
        - ✅ ブロック編集がテキストの `ChangeSet` で表現され、マルチカーソル/バッチ undo/`MapPos` をそのまま享受
        - ✅ Docs レンダラも同じ機構で描け、全文検索が native。別プロジェクト (`Luxel.RichText.Editor`) が不要になり ADR-0010〜0014 のコード量が減る
        - ⚠️ テキストスタックに font-variant Mark / ブロック widget / Markdown プロバイダを足す (ジオメトリの mixed-weight ラン対応)
        - ⚠️ Docs 11 モジュール移行 + プロセッサの widget 化 + golden 全再生成 (依然として最大ワークストリーム)
        - ⚠️ read-only レンダラの要件 (proportional 折返し + インライン混在 + 表 + 数式 + mermaid + 埋め込み UI) を装飾/widget で満たす設計精度が要る
        """;
}
