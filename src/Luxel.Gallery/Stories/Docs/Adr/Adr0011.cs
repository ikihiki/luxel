using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0011-Pointer-Button-Modifiers", Order = 82)]
    public static Widget Adr0011(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0011 — PointerEvent にボタンと修飾キーを通す

        - **Status**: Accepted
        - **Date**: 2026-07-09
        - **Deciders**: ikihiki

        ## Context

        `PointerEvent` (`Luxel.UI`) はローカル/画面座標とドラッグ差分だけを運び、**どのマウスボタンか**・**修飾キー (Ctrl/Shift/Alt/Meta) が押されているか**を持ちません。このため複数の機能が「フレームワークの穴」として延期されています:

        - テキストスタックの **Alt+Click マルチカーソル** ([ADR-0006](story:ADR/0006-Editor-New-Stack) の S7 で延期)
        - ノードスタックの **対話 pan ドラッグ・Ctrl+Click 追加選択** ([ADR-0009](story:ADR/0009-Node-Editor-Stack) で延期、空白ドラッグを範囲選択に取られる)
        - これから作る本格ドッキング ([ADR-0010](story:ADR/0010-Workbench-Framework)) の **タブ D&D 並べ替え・ペインのドラッグ再配置・ドロップゾーン**判定 — 左ボタンドラッグと右クリックメニューの区別が要る

        Workbench 系の土台になるため、ここで穴を塞ぎます。

        ## Decision

        `UiHost` の PointerDown/Move/Up → `PointerEvent` → `HitTarget` の経路を貫いて、**押下ボタン** (`PointerButton` = Left/Right/Middle) と **修飾キー** (`KeyModifiers` = Ctrl/Shift/Alt/Meta のビットフラグ) を運びます。

        - 既存ハンドラは新フィールドを無視すれば従来どおり動く (無回帰が既定)
        - ドラッグ捕獲時のボタンも保持し、`Drag` 中に一貫して読める
        - 修飾キーは**イベント発生時点の状態**をプラットフォーム層 (Win32 メッセージ) で拾って詰める — キーボードのグローバル状態を後から参照しない

        ## Alternatives

        - **各コントロールが生入力を別経路で覗く** — 層を貫かず散らかり、ヒットテスト対象ごとに再実装になる → 却下
        - **修飾キーをキーボードのグローバル状態から読む** — ポインタイベントと非同期で、押下/離しのタイミングがズレる → 却下 (イベントに同梱する)
        - **穴のまま範囲選択 + キーボードで代替を続ける** (現状) — マルチカーソル・pan・ドッキング D&D の操作感が根本的に成立しない → 却下

        ## Consequences

        - ✅ Alt+Click マルチカーソル・Ctrl+Click 追加選択・対話 pan ドラッグ・タブ/ペインの D&D が解禁され、両エディタの「延期」注記が解消する
        - ✅ 本格ドッキング ([ADR-0010](story:ADR/0010-Workbench-Framework) / [ADR-0014](story:ADR/0014-Workbench-Ui-Controls)) の前提が揃う
        - ⚠️ `PointerEvent` の構造とその生成/配布経路 (`UiHost`) を触るため、**全ドラッグ系の無回帰**を golden で確認する必要がある
        - ⚠️ 修飾キー/ボタンの取得はプラットフォーム依存 — Win32 のメッセージから正しく組み立てる (他 OS 対応時は各 Platform で埋める)
        """, toc: true);
}
