# 11 — Scripting: DevTools Console タブ + 入力記録リプレイ + 外部デバッガ

## 概要

Scripting のデバッグ層の残り 3 項目。独立性が高いので個別に着手可能 (推奨順):

- **A. Gallery chrome の Console タブ** — REPL を Gallery の下ペインに常設 (P3 の明示的な残タスク)
- **B. 入力記録 → 決定的リプレイ** (Docs のデバッグ層⑤)
- **C. PDB emit + 外部デバッガ (VS/VS Code) アタッチの検証と文書化** (デバッグ層④の残り)

## 背景と現状

- **デバッグ層の全体像** (Docs/Scripting = src/Luxel.Gallery/Stories/Docs/DocsRuntime.cs に記載):
  ① コンパイル診断 + 実行時例外の行マップ — 実装済み
  ② Signal/Widget は DevTools/Props/Knobs に映る — 実装済み
  ③ フレームステップ — 実装済み (SurfaceView.Paused / StepFrame(dt)、Gallery ツールバーの ⏸/⏭)
  ④ 本気のステップ実行 = PDB emit + 外部デバッガ — **方針記載のみ**
  ⑤ 入力記録 → 決定的リプレイ — **方針記載のみ**
- **ScriptSession** ([src/Luxel.Scripting/ScriptHost.cs](../src/Luxel.Scripting/ScriptHost.cs)): `host.OpenSession(globals)` → `Submit(code)` で継続 REPL。失敗してもセッションは壊れず最後に成功した状態から続く。`LastValue` = 直近戻り値。UI スレッド専有。
- **REPL UI の前例**: ScriptingStory.ReplConsole (story Demos/Scripting/Repl) — 行入力 (TextArea) + ▷ + 出力履歴 (› 入力 / 緑=成功・赤=失敗)。
- **Gallery chrome**: 下ペインは Tabs [Log / Knobs / Interactions] (選択は `_bottomTab` フィールドで再構築を跨いで保持)。GalleryApp (src/Luxel.Gallery/) 側。
- **DI**: ScriptHost は GalleryServices に AddSingleton 済み — Console タブから `GalleryServices.Provider` で取れる。

## A. Gallery Console タブ

1. 下ペイン Tabs に 4 枚目「Console」を追加 (`_bottomTab` の保持機構に乗せる)。
2. 中身は ReplConsole の流用 (ScriptingStory から共通化するか、GalleryApp 側に軽量再実装)。セッションは **Gallery 起動中ずっと生かす** (タブ切替・ストーリー切替で失わない — ScriptSession をフィールド保持)。
3. globals に「今選択中のストーリー/ホスト」への口があると便利: `Ctx` (StoryContext 相当)、`Host` (UiHost)、`Log(string)`。選択変更で globals を差し替えるか、遅延解決の Func にするかは実装時に判断 (セッション継続と両立するのは後者)。
4. 検証: chrome は snap 対象外の領域だが、GalleryApp のテストがあれば追従。手動確認 + 可能なら DebugServer 経由の E2E (`{"op":"click",...}` で chrome を叩く前例あり)。

## B. 入力記録 → 決定的リプレイ

- **土台は揃っている**: 入力はすべて UiHost の公開口 (Click/KeyDown/Char/Compose/Commit/Wheel/PointerMove) を通り、EngineDiagnostics が `NoGfx.Input` (現 Luxel.Input イベント名 — 実名はコードで確認) として Emit 済み。PlayDriver (src/Luxel.UI/Play.cs) は「固定 dt で操作列を流す」再生機そのもの。
- 実装:
  1. `InputRecorder` — DiagnosticListener 購読 (または UiHost に直接フック) で「フレーム番号 + 操作」を List に記録。開始/停止 API。
  2. シリアライズ — JSON で十分 (op/座標/キー/文字列)。
  3. `InputReplayer` — 記録を PlayDriver 相当で再生: フレーム n まで Step → 操作を発火 → 続行。**固定 dt 前提なので決定的** (wall-clock を挟まない)。
  4. Gallery 統合: ツールバーに ●録画/■停止/▶再生 (Interactions タブに置くのが自然)。録画結果を play コード (`d.Click(...)` 列) としてクリップボードへ吐けると、**手操作から play を起こす**運用ができて価値が跳ねる。
- テスト: 記録 → 再生で同一 golden/状態ハッシュになることを単体で (UiHost headless + Skia で GPU 不要)。
- 注意: アニメーション/物理が絡むストーリーは「操作のフレーム位置」まで再現して初めて決定的 — フレーム番号基準 (経過秒でなく) で記録する。

## C. 外部デバッガアタッチ

- 方針 (Docs 記載): 内蔵ステップ実行は再発明しない。ScriptHost は既に `WithEmitDebugInformation(true)` + FilePath("script.csx") で emit している — 外部デバッガで止まるための残作業を検証する:
  1. スクリプトを**実ファイルとして保存**した状態でロードする経路 (インメモリ文字列だとデバッガがソースを見つけられない)。FilePath を実パスにする option を ScriptHost に追加。
  2. VS / VS Code (C# 拡張) からのプロセスアタッチ → csx 内ブレークポイントがヒットするか実機検証。
  3. 手順を Docs/Scripting のデバッグ層④に「検証済み手順」として書く (アタッチ対象プロセス、Just My Code 設定など)。
- コード変更は小さく、主に検証と文書化のタスク。hot reload ([01](01-scripting-scriptsystem-hot-reload.md)) 実装後なら「編集 → リロード → 再アタッチ不要で継続」まで確認。

## 罠・注意

- ScriptSession は UI スレッド専有 — Console タブの Submit は Gallery スレッド上で同期実行 (長い実行はフレームを止める。v1 は許容し、注意書きを UI に)。
- Roslyn 初回コンパイル 1〜2 秒 — Console 初回 Submit が引っかかるのは既知 (Lazy ウォームアップも検討)。
- 記録リプレイのイベント名/ペイロードは DiagPayloads.cs (Luxel core) の既存型に合わせる。
