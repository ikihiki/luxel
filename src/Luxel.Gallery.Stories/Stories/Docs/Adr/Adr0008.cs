using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0008-Custom-Ime-Candidates", Order = 79, Toc = true)]
    public static StoryResult Adr0008(StoryContext ctx) => $$"""
        # ADR-0008 — IME 候補ウインドウを自前描画する (排他モード対応)

        - **Status**: Proposed
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        > [!NOTE]
        > `ToDo/22`〜`ToDo/27` はADR作成当時の計画番号で、現在のファイル参照ではありません。現行の実装と利用手順は本文からリンクするLearnページと実装を正とします。


        IME (TSF) の**変換候補リスト**は現状 OS/TIP が描画し、我々は `CaretRect` (`GetTextExt`) で位置を渡すだけです。preedit テキスト・下線・変換対象節の強調は既に自前 (`ITextInput` 経由) ですが、候補リストだけ OS 任せです。

        排他フルスクリーン (ゲーム) では OS の候補ウインドウがスワップチェーン上に出ない/破綻することがあり、エンジン内で候補を描けないと日本語入力が実質使えません。TSF の `ITfUIElementSink` / `ITfCandidateListUIElement` を使ったフックは未実装です。

        ## Decision

        TSF の `ITfUIElementMgr` に **`ITfUIElementSink` を advise** し、候補リスト UI 要素で `BeginUIElement` の `pbShow=false` を返して**OS 描画を抑制**、`ITfCandidateListUIElement` から候補文字列・選択・ページを読み、UI 層へ渡します。UI 層は候補を **[ADR-0007](story:Internals/ADR/0007-Floating-Ui-Placement) の Popup** として `CaretRect` にアンカーして自前描画します。

        - 抑制は**排他モード時 (またはオプトイン) のみ**。通常ウインドウでは OS 描画を既定にする (OS の絵文字候補等の利点を残す)
        - 失敗時・非 TSF (IMM フォールバック) は OS 描画へフォールバック
        - モデル `ImeCandidates { IReadOnlyList<string> Items, int Selected, int PageStart, int PageSize }` を `ITextInput` (または新ホスト) へ通知

        実装計画は ToDo/24。**Proposed** — 排他モードが必要になった時点で着手する。

        ## Alternatives

        - **OS 任せのまま** — 通常ウインドウでは十分だが排他モードで日本語が使えない → 排他対応が要るなら却下 (通常時は既定として残す)
        - **IMM32 へ切替** — レガシーで TSF より候補情報が乏しい・将来性がない → 却下
        - **候補用に別ウインドウ (レイヤードウインドウ) を出す** — 排他フルスクリーンでは前面に出せない/合成外 → 却下

        ## Consequences

        - ✅ 排他フルスクリーンで日本語入力の候補を描け、見た目もテーマに統一できる
        - ⚠️ TSF COM の追加実装 (`ITfUIElementSink` の advise、`ITfCandidateListUIElement` の読み取り、STA スレッド規律) が要る
        - ⚠️ **実 IME 依存で決定的テストが困難** — 実機 + 各 IME (MS-IME/Google 日本語入力等) の手動検証が必須。golden に乗らない
        - ⚠️ 候補以外の UI 要素 (リーディングウインドウ/ツールチップ) もあり、抑制範囲の線引きが要る
        - ⚠️ 抑制すると OS の候補由来の便利機能も消えるため、既定は通常ウインドウ=OS 描画・排他=自前、の切替方針を守る必要がある
        """;
}
