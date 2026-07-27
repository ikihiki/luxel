using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0007-Floating-Ui-Placement", Order = 78)]
    public static Widget Adr0007(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0007 — 浮遊 UI は単一の anchored placement エンジンに統一する

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        浮遊 UI (ダイアログ・トースト・ドロワー・ドロップダウン・Select・ColorPicker・コンテキストメニュー・補完ポップアップ・ホバーツールチップ・IME 候補) が増え、配置ロジックが散らばっています。

        - オーバーレイ層 (`OverlayEntry` / `UiHost.Place`、Z=1000) に anchor + フリップ/クランプはあるが、**下↔上のフリップと X のクランプだけ**。水平フリップ・shift-to-fit・左右 side 配置・画面より高いときの max-height/スクロールが無い
        - `ContextMenu` はこの層を使わず Z=3000 の一点物 — **端でクランプもフリップもしない** (右端・下端ではみ出す)
        - CodeEditor の補完ポップアップ/ツールチップは**エディタの content 内の素の Scene2D ノード** — エディタのクリップに閉じ込められて画面外へ出られず、端でフリップもしない
        - 画面端での挙動 (方向を変える) を各コントロールが個別に持つ/持たないため一貫しない

        新スタックの補完ポップアップ ([ADR-0006](story:Internals/ADR/0006-Editor-New-Stack) の S6c) を機に、**全ての浮遊 UI が同じ規則で端に反応する**土台を作りたい、という力学です。

        ## Decision

        `Luxel.UI` に**単一の anchored placement エンジン**を置き、浮遊 UI を統一します。

        - **配置指定** `AnchoredPlacement { Side (Below/Above/Right/Left), Align (Start/Center/End), bool Flip, bool Shift, float Gap, Margin, MaxWidth/MaxHeight }`
        - **純粋なソルバ** `Solve(Rect anchor, Size content, Rect viewport) → (Rect rect, PopupSide actualSide, Size constrained)` — ①希望 side に置く ②入らなければ反対 side へ**フリップ** ③交差軸で画面内へ**シフト** ④viewport を超えるなら**サイズをクランプ** (中身はスクロール)。canvas 非依存で単体テスト可能
        - **2 つの配置ファミリ**を明確に分ける: **anchored** (トリガー/キャレットに紐づく — Side/Align/Flip/Shift) と **region** (ダイアログ/ドロワー/トースト — Center/Edge/Corner、既存踏襲)
        - **移行**: ContextMenu・Select/Dropdown/ColorPicker・**CodeEditor 補完ポップアップ + ツールチップ**をこのエンジンへ。ポップアップは Z=1000 のオーバーレイ層へ昇格し、トリガーの WorldPos 矩形または `ITextInput.CaretRect` にアンカーする (エディタのクリップから出て、画面端でフリップする)
        - **IME 候補ウインドウ** ([ADR-0008](story:Internals/ADR/0008-Custom-Ime-Candidates)) も、自前描画する場合はこの Popup として CaretRect にアンカーする — 浮遊 UI の一消費者になる

        実装計画は ToDo/23。現在の姿は [Reference/Guides/UI](story:Reference/Guides/UI) が正。

        ## Alternatives

        - **各コントロールが個別に配置** (現状) — フリップ/クランプを毎回作り直し、ContextMenu と補完は端でクランプすらせずはみ出す → 却下
        - **Floating UI / CSS Anchor Positioning 相当のフルミドルウェア** (flip/shift/size/arrow/autoPlacement…) — 過剰。必要な部分集合 (flip + shift + size) だけ採る → 却下 (部分採用)
        - **ポップアップ毎に手でクランプ** — 重複・未テスト・端対応が漏れる → 却下

        ## Consequences

        - ✅ フリップ/シフト/クランプが 1 か所・単体テスト済みになり、全浮遊 UI が一貫して画面端に反応する
        - ✅ 補完ポップアップがエディタのクリップから解放され、キャレットにアンカーして端でフリップする
        - ✅ ContextMenu が端でのクランプ/フリップを獲得する
        - ⚠️ 移行が複数コントロール + golden に及ぶ (配置が数 px 動きうる — 意図差分として --update)
        - ⚠️ anchored と region の 2 ファミリを区別して保守する必要がある
        """, toc: true);
}
