# 01 — カスタム IME 候補ウインドウ (排他モード対応)

## 概要

IME (TSF) の変換候補リストを OS 任せにせず、TSF のフックで OS 描画を抑制して**自前で描画**する。排他フルスクリーン (ゲーム) で日本語入力を成立させるのが目的。決定は **ADR-0008** (`Internals/ADR/0008-Custom-Ime-Candidates`、**Proposed**) が正。**排他モードが必要になった時点で着手**する (それまで通常ウインドウは OS 描画で十分)。候補の描画は ADR-0007 の Popup placement を利用する。

## 背景と現状 (調査結果)

- TSF プラグイン: [src/Platform/Luxel.Platform.Windows/Tsf/](../src/Platform/Luxel.Platform.Windows/Tsf/) — `TsfThread` (ITfThreadMgr/ITfKeystrokeMgr、STA、ref-count)、`TsfManager` (per-window)、`TsfTextStore` (ITextStoreACP + ITfContextOwnerCompositionSink + ITfTextEditSink)
- **候補リストは完全に OS 描画**。`TsfTextStore.GetTextExt` ([TextStore.cs](../src/Platform/Luxel.Platform.Windows/Tsf/TextStore.cs) ~119) が `Host.CaretRect` を論理→物理 px 変換 + `ClientToScreen` して TSF に返す → OS/TIP がその矩形に候補ウインドウを描く。**`ITfUIElementSink`/`ITfUIElement`/`ITfCandidateListUIElement`/`BeginUIElement` は未実装 (grep 0 件)**
- 我々が制御できるもの: caret 矩形 (候補位置)、preedit テキスト (実文書へ挿入)、preedit 装飾 (`ITextInput.SetCompositionHighlight` — 下線/対象節、`ReadTargetSegment` 経由)
- `ITextInput.CaretRect` (canvas 座標) は既に「候補ウインドウ配置用」として存在 — Popup のアンカーに使える

## 設計

### プラットフォーム (Luxel.Platform/Tsf)

- `ITfThreadMgr` から `ITfUIElementMgr` (QueryInterface) を取得し **`ITfUIElementSink` を advise**
- `BeginUIElement(pElement, out pbShow, out dwUIElementId)`: 要素が候補リスト (`ITfCandidateListUIElement` を QI 可能) かつ**自前描画が有効なとき** `pbShow = false` を返して OS 描画を抑制。それ以外は `pbShow = true` (OS 描画継続)
- `UpdateUIElement(dwUIElementId)`: `ITfCandidateListUIElement` から候補を読み UI へ通知
  - `GetCount` / `GetString(i)` / `GetSelection` (現在選択) / `GetCurrentPage` / `GetPageIndex` (ページ境界)
- `EndUIElement(dwUIElementId)`: 候補を閉じる通知
- リーディング/ツールチップ等の他 UI 要素は抑制しない (候補リストのみ)

### モデル + ブリッジ

```csharp
public readonly record struct ImeCandidates(
    IReadOnlyList<string> Items, int Selected, int PageStart, int PageSize);

// ITextInput へ任意メソッド追加 (既定 no-op — OS 描画のまま)、または UiHost に候補ホストを持たせる
void SetCandidates(ImeCandidates? candidates);   // null = 閉じる
```

`TsfTextStore`/`TsfManager` → `UiHost.OnImeCandidates(...)` → フォーカス中の `ITextInput.SetCandidates`。

### UI (Luxel.Controls or app)

- 候補リストを **anchored Popup** (ADR-0007、Side=Below、アンカー = `ITextInput.CaretRect`) で描画: 候補行 + 選択強調 + ページ位置 (例 `3/9`)。テーマ統一
- 抑制の切替: **排他フルスクリーン時 (またはオプトイン API) のみ自前描画**。通常ウインドウは OS 描画を既定

### フォールバック

- 非 TSF (IMM フォールバック経路) では OS 描画のまま
- `ITfUIElementMgr` 取得失敗・advise 失敗時は OS 描画のまま (抑制しない)

## ステージ

1. **P1**: `ImeCandidates` モデル + `ITextInput.SetCandidates` (既定 no-op) + `UiHost.OnImeCandidates` 配線
2. **P2**: TSF `ITfUIElementSink` の advise + `BeginUIElement`/`UpdateUIElement`/`EndUIElement` 実装、`ITfCandidateListUIElement` 読み取り。抑制フラグ (排他/オプトイン) でゲート
3. **P3**: 候補 Popup 描画 (ADR-0007 の Popup、CaretRect アンカー、選択 + ページ)。TextEditorView/新スタックに接続
4. **P4**: 実機検証 — 通常ウインドウ (OS 描画のまま) + 排他モード (自前描画) を MS-IME / Google 日本語入力で手動確認

## 罠・注意

- **決定的テスト不可** — 実 IME 依存。golden に乗らない。単体テストは P1 のモデル/配線まで。P2〜P4 は実機手動検証 (`RealWindowOnly`)
- STA スレッド規律 (TsfThread と同じスレッドで COM を触る)
- 抑制しすぎると OS の候補由来の便利機能 (絵文字/顔文字候補等) も消える → 既定は通常=OS 描画
- TIP 差 (MS-IME / Google / ATOK) で候補 UI 要素の出方が違う — 候補リスト以外は抑制しないこと
- 排他フルスクリーンの検証環境が要る (この環境では難しい → 実窓スモーク扱い)

## スコープ外

- 候補以外の IME UI (リーディングウインドウ) の自前化
- 予測変換/クラウド候補など TIP 固有機能の再現
- Windows 以外の IME (対象は Windows/TSF)
