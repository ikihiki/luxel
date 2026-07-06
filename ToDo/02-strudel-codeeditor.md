# 02 — Strudel REPL の CodeEditor 化 + Ctrl+Enter 評価 + 診断波線

## 概要

Strudel REPL (Strudel/Repl ストーリー) のエディタを TextArea ベースの独自ライブブロックから CodeEditor に置き換え、Ctrl+Enter での行/ブロック評価、MiniNotation の位置付きエラーを診断波線として表示する。csx プレイグラウンド (E4) で確立したのと同じ編集体験を Strudel にも揃える、低リスク・高効果のタスク。

## 背景と現状

- **Strudel REPL**: [src/Luxel.Gallery/Stories/StrudelStory.cs](../src/Luxel.Gallery/Stories/StrudelStory.cs) — Story "Strudel/Repl"。独自 IDocumentFormat (`--` = コメント、行 Enter → ライブブロック化 = LiveCodeStory と同型)。
  - **スロット所有権の仕組みに注意**: Run = commit でブロックが再構築されるため、payload info に "strudel <slot>" を埋めて新ブロックがスロットを引き継ぎ、旧ブロック Dispose は所有者チェックで無害化している (音が途切れない)。エディタを差し替えてもこの仕組み (または同等の音の連続性) を壊さないこと。
  - XAudio2 は初回 Run で遅延初期化、失敗時 NullAudioBackend (headless E2E 対応)。駆動 = ReplRoot の AddAnimation で毎 Tick `BuffersQueued < 3` までポンプ。
- **Ctrl+Enter が v1 スコープ外だった理由**: 「TextArea に公開 key hook がない」。**このブロッカーは CodeEditor.OnKeyIntercept (`Func<KeyEvent,bool>` — true 返しで消費) により解消済み。**
- **CodeEditor**: [src/Luxel.Controls/CodeEditor.cs](../src/Luxel.Controls/CodeEditor.cs)。ガター/現在行/選択/トークン色/検索置換/行操作/スクロールバー実装済み。`LanguageService` (ICodeLanguage) を挿すと Ctrl+Space 補完 + 診断波線 + ホバーが有効。
- **ICodeLanguage**: [src/Luxel.Controls/ICodeLanguage.cs](../src/Luxel.Controls/ICodeLanguage.cs) — `Complete/Diagnose/Hover`。DTO: `CodeCompletion(Label/InsertText/Kind)` / `CodeDiagnostic(Line/Column/Length/Message/IsError)`。C# 実装は [src/Luxel.Gallery/Stories/CsharpCodeLanguage.cs](../src/Luxel.Gallery/Stories/CsharpCodeLanguage.cs)。
- **MiniNotation**: [src/Luxel.Strudel/MiniNotation.cs](../src/Luxel.Strudel/MiniNotation.cs) — Parse は**位置付き `MiniNotationError`** を投げる/返す。診断波線の材料はここにある。評価は [src/Luxel.Strudel/StrudelEval.cs](../src/Luxel.Strudel/StrudelEval.cs) (チェーン式極小インタプリタ)。
- **E4 の前例** (同型の作業): ScriptingStory.CsxBlock の TextArea → CodeEditor 差し替え。手動補完ボタンを廃し内蔵機能に一本化、`LanguageService = CsharpCodeLanguage(Ws)`、play は Ctrl+Space + `Editor.CompletionOpen` で検証。この commit (e087efd) を手本にする。

## 実装方針

### 1. StrudelCodeLanguage : ICodeLanguage (Gallery/Stories に配置)

- `Diagnose(code)`: 各行を StrudelEval/MiniNotation でパースし、`MiniNotationError` の位置 → `CodeDiagnostic(Line, Column, Length, Message, IsError: true)`。評価まではしない (音を出さない) — パース/構文チェックのみ。
- `Complete(code, pos)`: 最小で良い。StrudelKit の音色名 (bd/sd/hh/oh/cp/rim/lt/ht/sine/tri/saw/square) + チェーンメソッド名 (rev/fast/slow/every/off/jux/…、StrudelEval が受け付ける語彙) を静的リストで返す。文脈判定は「`.` の直後ならメソッド、クォート内なら音色」程度から。
- `Hover`: 任意 (無理せず null でよい)。

### 2. エディタ差し替え + Ctrl+Enter

- StrudelStory のライブブロックの編集面を CodeEditor に。`Highlighter` は当面 TextMate の適当な文法 or 無し (トークン色より診断波線が価値)。
- `OnKeyIntercept`: Ctrl+Enter を横取り → 現在ブロック (または現在行が属するパラグラフ) を評価 = 既存 Run 経路を呼ぶ → true で消費。
- 全文 1 エディタにするか、ブロック単位の複数 CodeEditor にするかは既存構造に合わせて判断。既存の「行 Enter → ライブブロック化」の文書構造を保つなら、各ライブブロックの中身を CodeEditor にするのが最小差分。全文 1 CodeEditor + Ctrl+Enter でカーソル行のスロットを評価、の方が Strudel 本家に近い — どちらでも良いが、**スロット所有権 (音の連続性) と play の決定性を守る方を優先**。

### 3. play + golden + Docs

- play 例: SetText で `d1 $ "bd sd"` 相当 → Ctrl+Enter (d.Key) → Expect (スケジューラのスロットが埋まった/LastRunOk) → 不正記法をセット → Expect (DiagnosticCount > 0) → Snap "diag" (波線の golden)。
- 初期状態は無音 (既存 Repl と同じ、snap 決定的)。
- Docs/Strudel ([src/Luxel.Gallery/Stories/Docs/DocsStrudel.cs](../src/Luxel.Gallery/Stories/Docs/DocsStrudel.cs)) の「v1 スコープ外」から Ctrl+Enter を外し、エディタ統合の節を追記。

## 作業ステップ

1. StrudelCodeLanguage 実装 + 単体テスト (tests/Luxel.Tests に — パースエラー位置が CodeDiagnostic に正しく写るか。GPU 不要)。
2. StrudelStory のエディタ差し替え + Ctrl+Enter。既存 play が通ることを確認してから新 play 追加。
3. golden 更新 (`-- vk e2e --update "Strudel"`) — Strudel/Repl の既存 golden は絵が変わるので意図分として残す。
4. DocsStrudel 更新 (v1 スコープ外リストの修正)。

## 罠・注意

- **音の連続性**: commit → ブロック再構築 → 旧ブロック Dispose の流れでスロット所有権チェックが効いていること。エディタ差し替えで Dispose 経路が変わると再生中の音が切れる。
- headless E2E では NullAudioBackend — play の Expect は音ではなくスケジューラ/フラグの状態で書く。
- Key enum に Ctrl+Enter 判定用の修飾情報があるか確認 (E3 で D/F/G/H/R/Slash を足した前例あり — 不足キーは Key enum + Win32 KeyMap の両方に追加)。
- CodeEditor は等幅・折り返しなし前提。Strudel コードは短いので問題ないはず。

## スコープ外

- 音楽機能の拡張 (scale/chord/filter/…) → [08](08-strudel-music-features.md)。
- Strudel 専用トークンハイライト文法の作り込み (診断が出れば v1 は十分)。
