# 07 — CodeEditor: マルチカーソル + 矩形選択 (E3.5)

## 概要

VS Code 風のマルチカーソル (Ctrl+D で次の同一語を追加選択、Alt+Click でカーソル追加) と矩形選択を CodeEditor に実装する。**意図的に延期された高リスクタスク** — 共有編集エンジン DocumentEditor が単一 Caret/Anchor モデルであり、複数選択対応は undo/IME に波及し、同じエンジンを使う TextArea/RichTextEditor も揺らす。着手時は「慎重な専用変更」として扱うこと。

## 背景と現状 (延期の経緯)

- E3 計画時に「マルチカーソル + 矩形選択」が入っていたが、**E3.5 として明示的に延期**された。理由: 共有 DocumentEditor は単一 Caret/Anchor モデルで、複数選択対応は undo ジャーナル・IME 合成に波及する。value/risk 比から行操作/検索置換 (E3a/E3b) を先行させた。
- **DocumentEditor**: Luxel.Document (src/Luxel.Document/) の編集エンジン。DocPos = (Line, Offset) のフラット行座標。単一の Caret/Anchor、undo ジャーナル、IME (SetComposition/CommitComposition/Select/Replace)。**TextArea / RichTextEditor / CodeEditor の 3 者が共有** — ここを触ると全員に波及する。
- **CodeEditor**: [src/Luxel.Controls/CodeEditor.cs](../src/Luxel.Controls/CodeEditor.cs)。選択矩形は行ごとに描画、キャレットは 1 本前提の描画・点滅。
- テスト資産: DocumentEditorTests / CodeEditorTests / RichTextEditor 系 / TextArea 系 (tests/Luxel.Tests/) — 波及検知の安全網はある (合計 585+ 本)。

## 実装方針 (推奨: DocumentEditor を単一カーソルのまま保つ)

DocumentEditor に複数カーソルを入れる案と、CodeEditor 側でマルチカーソルを「上位レイヤ」として管理する案がある。**推奨は後者**:

### CodeEditor 上位レイヤ方式

- CodeEditor が `List<(DocPos caret, DocPos anchor)>` のセカンダリ選択群を持ち、DocumentEditor 本体のカーソルは「プライマリ」1 本のまま。
- 編集操作 (文字挿入/Backspace/Delete/貼り付け) は、**オフセット降順に各カーソル位置へ順に適用** (後ろから適用すれば前方のオフセットがずれない)。適用は DocumentEditor の既存 API (Select + Insert/Replace) を呼び回す。
- **undo 単位**: N カーソルへの 1 打鍵 = 1 undo にしたい。DocumentEditor のジャーナルにトランザクション (BeginBatch/EndBatch) が無ければ足す — これは**単一カーソルモデルを変えない追加**なので波及が小さい。行移動 (Alt+↑↓) が「全文スワップ + SetText = 1 undo 単位」で逃げた前例があるが、マルチカーソル打鍵で毎回 SetText は重い/選択が飛ぶので、バッチ API を足す方が筋。
- **IME**: 合成はプライマリカーソルのみ (VS Code も実質同様)。合成開始でセカンダリを確定破棄するのが最も安全。
- **描画**: キャレット描画をループ化 (点滅は同期)、選択矩形も全カーソル分。セカンダリキャレットは色/透明度を変える。

### 操作系

- `Ctrl+D`: 現在選択 (無ければキャレット下の語) と同一のテキストの次の出現を検索し、セカンダリ選択を追加。全件済みならラップ。
- `Alt+Click`: クリック位置にカーソル追加 (既存カーソルと同一位置なら除去)。HitDoc は実装済み。
- `Escape`: セカンダリ全解除 → プライマリのみ (補完ポップアップの Escape との優先順位: ポップアップが開いていればそちらが先)。
- 矩形選択 (`Alt+Shift+ドラッグ` or `Ctrl+Alt+↑↓`): 「各行 1 カーソルの縦列」としてマルチカーソルに正規化する実装が簡単で、VS Code の挙動とも一致。等幅前提なので列 → オフセットは単純。
- Key enum に不足があれば追加 (E3 の前例: D/F/G/H/R/Slash + Win32 KeyMap 0x44 等)。Alt 修飾の扱いを KeyEvent が持っているか要確認。

## 作業ステップ

1. 調査: DocumentEditor のジャーナル構造を読み、バッチ undo の追加コストを見積もる。KeyEvent の修飾キー情報を確認。
2. DocumentEditor にバッチ undo (Begin/End) — 既存 3 コントロールのテスト全緑を確認。
3. CodeEditor にセカンダリ選択群 + 描画。
4. 編集適用ループ (降順適用) + Ctrl+D + Alt+Click + Escape。
5. 矩形選択 (マルチカーソル正規化)。
6. テスト: CodeEditorTests に — Ctrl+D×2 → 打鍵で 3 箇所置換が 1 undo / Alt+Click 追加・除去 / Escape 解除 / IME 開始でセカンダリ破棄 / 矩形選択 → 縦列編集。
7. story Controls/CodeEditor/MultiCursor + play + golden。Docs/Editor 更新 (v2 リストから昇格)。

## 罠・注意

- **TextArea / RichTextEditor を壊さない**こと。DocumentEditor への変更はバッチ undo の追加に留め、マルチカーソル状態は CodeEditor に閉じる。作業後は全テスト (585+ 本) と e2e 全 play を回す。
- 降順適用でも「複数カーソルが同一行で選択が重なる」ケースは事前にマージ (重なった選択は 1 つに統合 — VS Code と同じ)。
- ホイール/スクロール追従 (EnsureCaretVisible) はプライマリ基準のまま。
- 検索置換 (SetSearch/ReplaceCurrent) との相互作用: マルチカーソル中の検索はセカンダリ解除で良い。

## スコープ外

- RichTextEditor / TextArea へのマルチカーソル展開 (CodeEditor で実証してから)。
- 「全件選択 (Ctrl+Shift+L)」等の派生コマンドは Ctrl+D 実装後に自然に足せるが v1 必須ではない。
