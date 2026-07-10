# NEXT — 「次へ」で進む実装キュー

ユーザーが**「次へ」**とだけ言ったら、AI はこのファイルの手順に従って次のタスクを 1 つ進める。

## 「次へ」と言われたときの手順

1. このファイルの**実行キュー**から、未チェック (`[ ]`) の最初のエントリを選ぶ。`(着手中: ...)` メモがあればそれを再開する。
2. エントリが指すタスク MD を**全文読む** (背景・実装方針・罠・検証がそこに揃っている)。[README.md](README.md) の共通規約 (ビルド/テスト/golden 運用/UiComponent 規約/決定性) も従うこと。
3. 着手時: エントリ末尾に `(着手中: YYYY-MM-DD)` を書き足してから作業を始める (中断されても次セッションが再開できる)。
4. 実装 → 下の**完了の定義**を全部満たす → キューのチェックを `[x]` にし、`(着手中)` メモを消す。
5. **タスクの全ステージが終わったら**: タスク MD を削除し、README.md の一覧表から行を削除し、仕様は Gallery の Docs ストーリーへ現在形で書く (既存運用)。
6. 作業中に見つかった穴・新タスクは ToDo/ に新しい MD として追加し、このキューの適切な位置に 1 行足す。
7. 1 回の「次へ」で進めるのは**キュー 1 エントリまで**。早く終わっても次のエントリへ勝手に進まない (ユーザーがまた「次へ」と言う)。

## 完了の定義 (全エントリ共通)

- [ ] `dotnet build` / `dotnet test` が通る (新規ロジックには GPU 不要の単体テスト)
- [ ] e2e: `dotnet run --project src/Luxel.Gallery -- vk e2e` が通る。golden 差分は意図分のみ (`--update` 後に `git diff --name-only -- goldens` で意図外を戻す — README 参照)
- [ ] タスク MD 記載のデモストーリー/Docs 追記を実施 (該当があれば)
- [ ] `dotnet format` 相当のスタイルで綺麗 (リポジトリは dotnet/docs の .editorconfig)
- [ ] コミット済み (conventional commits 風: `feat(particles): ...` 等、日本語本文可)

**ユーザーに聞くのは**: タスク MD に「ユーザーに確認」と明記がある箇所、破壊的な選択、スコープの増減だけ。それ以外は MD の記述を正として自走する。

## 実行キュー (上から順)

> **完了済み (2026-07-10 整理)**: M1〜M7 / M9 / M10 の Q01〜Q30b・Q32〜Q44 は全完了につきキューから削除した。capstone 2 本 (`samples/LuxelCavern`・`samples/LuxelRange`)、テキストエディタ新スタック (`Luxel.Document`、ADR-0006/0007)、ノードエディタ (`Luxel.NodeGraph`、ADR-0009)、Workbench (`Luxel.Workbench`、ADR-0010〜0014) まで達成済み。仕様は Gallery の Docs/ADR ストーリー、経緯は git 履歴 (この整理前の NEXT.md に完了ログあり) を参照。

### M11 — ゲームエディタ「Luxel Studio」(ADR-0015〜0018、[27](27-game-editor.md) の大プログラム。**27 MD は全ワークストリーム完了まで残す**)

> 依存順: GE-0 → GE-1 (S1→S2) → GE-2 → GE-3 → GE-4 → GE-5 → GE-6 → GE-7。詳細・罠・検証・ユーザー確認事項は [27](27-game-editor.md) に集約。着手前に MD の「ユーザーに確認」3 点を確認する。

- [ ] **Q45**: 27 **GE-0** — プロジェクト/シーンモデル (`Luxel.SceneEdit`: GameProject/SceneDoc/IComponentSchema、JSON 決定的往復 + 未知コンポーネント保全)。ADR-0015 起草。純ロジック単体テストのみ・golden 影響なし
- [ ] **Q46**: 27 **GE-1 S1** — シーンエディタ変更モデル + ビュー骨格 (SceneChange/History + `SceneEditorView`: グリッド/pan/zoom/選択/移動/undo)。ADR-0016 起草。story + golden
- [ ] **Q47**: 27 **GE-1 S2** — タイル描き込み (TileSet パレット + ブラシ/矩形/消しゴム、ストローク=1 undo、TileMapLayer 流用)。story + golden
- [ ] **Q48**: 27 **GE-2** — インスペクタ (PropertyGrid×IComponentSchema、編集は Transaction 経由) + AssetBrowser 配線 + SpriteAtlas 定義エディタ。story + golden
- [ ] **Q49**: 27 **GE-3** — `Luxel.Player` データ駆動ランタイム (SceneCompiler + LuxelHostBuilder + csx ビヘイビア = ScriptSystem) + `Luxel.Player.App`。ADR-0018 起草。fixture プロジェクト story + 実窓スモーク。e2e は HeadlessAudio に乗せる
- [ ] **Q50**: 27 **GE-4** — プレイインエディタ (▶/⏸/ステップ/⏹、プレイ world 別インスタンス・停止で破棄、gizmo/DevStats オーバーレイ)。ADR-0017 起草。story play + golden
- [ ] **Q51**: 27 **GE-5** — スクリプト編集統合 (csx DocumentProvider = TextEditorView + ScriptHost 診断、保存→ホットリロード、Problems ペイン)。story + golden
- [ ] **Q52**: 27 **GE-6** — 出荷コマンド (dotnet publish Player + コンテンツコピー → リポジトリ外起動 vk/dx exit 0 の自動検証)。capstone チェックリスト踏襲
- [ ] **Q53**: 27 **GE-7** — dogfood: ミニゲーム 1 本をエディタ操作だけで作って出荷 (通し play + golden) + `Docs/Studio` 執筆 → **27 MD 削除・M11 クローズ**

### M8 — 排他モード IME (必要になったら。ADR-0008 は Proposed)

- [ ] **Q31**: [24 カスタム IME 候補ウインドウ](24-custom-ime-candidates.md) — TSF `ITfUIElementSink` で OS 候補を抑制 + `ITfCandidateListUIElement` 読み取り + Popup 描画 (排他フルスクリーン対応)。**排他モードが必要になった時点で着手**、実機手動検証 (golden 不可)。ADR-0008

## 運用メモ

- 分割ステージを持つタスクの MD は**全ステージ完了まで残す** (消すタイミングに注意)。
- git worktree で作業する場合は tools/ junction を忘れない (README/メモリ参照)。
- 検証 GPU が無い環境では e2e は Skip される — その場合は「単体テスト + ビルド」までで完了とし、キューに `(e2e 未実施)` を残して次のセッションで実機確認する。
