# 06 — CodeEditor: 補完ポップアップの磨き込み (P2.5 残)

## 概要

E2 で実装した補完ポップアップの既知の粗を仕上げる。3 点セット:

1. **入力絞り込み** — 現状はポップアップを開いた時点の候補で固定、キャレットが動くと閉じる。タイプ続行で候補をフィルタし続けるように。
2. **クリックでの候補選択** — 現状はキーボード (↑↓/Enter) のみ。
3. **マウスホバーツールチップ** — 現状はキャレット位置のホバーのみ (dwell が wall-clock で snap 非決定になるため見送っていた)。

## 背景と現状

- **実装場所**: [src/Luxel.Controls/CodeEditor.cs](../src/Luxel.Controls/CodeEditor.cs)。
  - Ctrl+Space → `OpenCompletion`: キャレット直下フローティングポップアップ。↑↓/Enter/Escape は `OnKeyIntercept` 相当の内部横取り。Enter は「識別子断片を置換して挿入」。
  - 編集 (Sync) 毎に `RefreshDiagnostics` → 波線。`UpdateHover` はキャレット位置シンボルの型を `HoverText` に。
  - play 検証用の公開面: `CompletionOpen/CompletionCount/DiagnosticCount/HoverText`。
- **言語サービス**: `LanguageService` (ICodeLanguage)。C# 実装は [src/Luxel.Gallery/Stories/CsharpCodeLanguage.cs](../src/Luxel.Gallery/Stories/CsharpCodeLanguage.cs) → ScriptWorkspace.Complete。
- **Escape の配線**: UiHost.KeyDown は「フォーカス中コントロールに先に Escape を渡し、未消費ならオーバーレイ dismiss」に修正済み — ポップアップの Escape はこの経路に依存している。壊さないこと。
- テスト: tests/Luxel.Tests/CodeEditorTests.cs (StubLanguage で配線検証、GPU 不要)。story: Controls/CodeEditor/Completion。

## 実装方針

### 1. 入力絞り込み

- ポップアップ open 中の文字入力: 閉じずに「トリガー位置 (open 時の識別子開始オフセット) からキャレットまでの断片」でフィルタ。
- フィルタは open 時に取得した候補リストに対するローカル絞り込みで十分 (prefix 一致 → 前方一致が無ければ大文字小文字無視の包含、程度。Roslyn の再クエリは不要 — 断片が変わるたび Complete を呼び直すと重い)。
- Backspace で断片が縮んだら再フィルタ、トリガー位置より前に戻ったら閉じる。候補 0 件になったら閉じる (または「候補なし」表示 — 閉じるが simple)。
- 選択中インデックスはフィルタ後リストの先頭にリセット。
- `CompletionCount` はフィルタ後の数を返すようにする (play で検証できる)。

### 2. クリック選択

- 候補行それぞれに AddHit (onClick で該当候補を確定挿入 = 既存 Enter 経路の共通化)。
- ポップアップはフローティング (キャレット直下、Z 前面)。イベントバブリングは深さ優先ヒット (TryPick) 修正済みなので、親エディタの全面ヒットに吸われず子の候補行が勝つはず — CodeEditorTests.ChildButton_WinsOverParentFullAreaDrag が前例。
- ホバー中の候補行の背景ハイライトも付ける (Theme の hover 合成)。

### 3. マウスホバーツールチップ (dwell)

- **決定性の壁**: dwell を wall-clock で計ると snap/play が非決定になる。解法: **フレームカウント dwell** — PointerMove で位置を記録し、`AddAnimation`/Tick 経路で「同一位置に N フレーム (例: 30) 留まったら Hover 表示」。play では `d.Step(30)` で決定的に発火させられる。
- ホバー位置 → DocPos は既存 `HitDoc` (等幅逆算) を流用。`LanguageService.Hover(code, offset)` を呼び、ツールチップ (フローティング、ポインタ近傍) に表示。
- 既存のキャレット位置ホバー (`HoverText`) は残して良い (play 検証面として有用)。

## 作業ステップ

1. 絞り込み: トリガー位置の保持 + フィルタ + テスト (StubLanguage: Type で Count が減る / Backspace で戻る / 範囲外で閉じる)。
2. クリック選択: AddHit + テスト (Click で挿入される)。
3. dwell ホバー: フレームカウント + ツールチップ + play (`d.Click(位置)` → `d.Step(30)` → Snap "hover" + Expect HoverText)。
4. story Controls/CodeEditor/Completion に play 追加、golden 更新 (`-- vk e2e --update "CodeEditor"`)。
5. Docs/Editor (src/Luxel.Gallery/Stories/Docs/DocsText.cs) の CodeEditor 節を現状に合わせて更新。

## 罠・注意

- ポップアップの候補リスト UI がオーバーレイ機構 (RegisterOverlay) か直接ノードかを確認してから触る。オーバーレイの場合「閉じ = transform で画面外退避 + hit 無効化」の定石 (node.Opacity は子に継承されない)。
- UiHost.Click は hit rect 内でないとフォーカスしない — テストのクリック座標は widget 内に。行移動の検証はキーで (クリック y は fs 依存でぶれる)。
- IME 合成中 (`ITextInput` の composition) の絞り込みは対象外にする (composition 確定後の文字だけ数える) — 挙動が複雑化するので v1 は「合成開始で閉じる」で良い。

## スコープ外

- シグネチャヘルプ、スニペット展開、候補の詳細ペイン (ドキュメントコメント表示)。
