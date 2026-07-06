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

### M1 — 基盤 (golden をきれいにしてから土台)

- [x] **Q01**: [13 日本語フォント同梱](13-e2e-japanese-font.md) — 全 golden 再生成を伴うため最初。完了 = e2e で日本語が出る + golden がフォント同梱由来で安定
- [x] **Q02**: 12 メンテ: Docs stale + dx golden — golden を新フォント基準で整えた直後に片付ける
- [ ] **Q03**: [14 FixedUpdate + 描画補間](14-framework-fixedupdate.md) — ゲーム/物理の時間基盤。既存デモ (Knockdown 等) の移行まで
- [ ] **Q04**: [19 capstone ①](19-standalone-game-shipping.md) **ステージ A: 骨組み + publish 早回し** — タイトル画面だけの LuxelCavern を作り publish チェックリスト 1〜4, 7, 8 を 1 周 (直すのは Luxel 本体側)。**MD はまだ削除しない**
- [ ] **Q05**: [21 DevTools ゲーム規模対応](21-devtools-game-scale.md) **ステージ ①: A (ECS スケール) + C (DevStats) + D (FixedUpdate/timescale) + E (WithDevTools) + F (fps 化) + DebugDraw コア** — B の機能別 gizmo (物理/カメラ/タイル) は対象機能の実装後 (Q12/Q14) に回す。**MD はまだ削除しない**

### M2 — 2D ゲーム機能 (capstone ① の部品)

- [ ] **Q06**: [15 セーブ/ロード + 設定](15-save-load-settings.md)
- [ ] **Q07**: [17 カメラコントローラ](17-camera-controller.md) — CameraRig2D + OrbitCamera 抽出 (3D 分もここで済ませる)
- [ ] **Q08**: [18 スプライトアトラス + タイルマップ](18-sprite-atlas-tilemap.md) — 大きい。MD の作業ステップ単位で複数セッションに分けて良い (分けた場合は着手中メモに進捗を書く)
- [ ] **Q09**: [16 パーティクル](16-particle-system.md) — コア + .TwoD + .ThreeD (ビルボード) まで全部
- [ ] **Q10**: [10 Audio ストリーミング](10-audio-streaming.md)
- [ ] **Q11**: [01 ScriptSystem + csx hot reload](01-scripting-scriptsystem-hot-reload.md) — capstone ① の敵 AI を csx で書くための土台
- [ ] **Q12**: [21 DevTools](21-devtools-game-scale.md) **ステージ ②: B の 2D gizmo (タイル衝突 / CameraRig デッドゾーン・境界 / エミッタ)** — 17/18/16 が揃ったので実装可。物理 gizmo は Q14 で。**MD はまだ削除しない**

### M3 — capstone ① 完成

- [ ] **Q13**: [19 capstone ①](19-standalone-game-shipping.md) **ステージ B: ゲーム組み上げ → e2e → publish 本番 → Docs「配布」節** — 完了したら **19 の MD を削除**

### M4 — 3D 物理・アニメーション (capstone ② の部品)

- [ ] **Q14**: [03 CCD](03-physics-ccd.md) — 小。続けて [21](21-devtools-game-scale.md) **ステージ ③: 物理 gizmo (コライダーワイヤ/接触点/トリガー/CCD 色分け)** の骨格をここで作ると 04/05 のデバッグが楽 (接触点表示は Q15 後に完成)。21 の全ステージが済んだら **21 の MD を削除**
- [ ] **Q15**: [04 接触イベント + トリガー](04-physics-contact-events.md)
- [ ] **Q16**: [05 メッシュ/凸包コライダー](05-physics-mesh-colliders.md)
- [ ] **Q17**: [09 glTF skin/morph](09-gltf-skin-morph.md) — 大。作業ステップ単位で分割可 (skin → morph)

### M5 — capstone ② 完成

- [ ] **Q18**: [20 capstone ②: 3D 射的](20-game2-3d-shooting-range.md) — 完了したら **20 の MD を削除**

### M6 — エディタ/ツール系 (ゲーム完成後。順不同で良い)

- [ ] **Q19**: [11 デバッグツール (Console タブ / 入力リプレイ / 外部デバッガ)](11-scripting-debug-tools.md) — A/B/C 独立、分割可
- [ ] **Q20**: [02 Strudel REPL の CodeEditor 化](02-strudel-codeeditor.md)
- [ ] **Q21**: [06 CodeEditor 補完磨き込み](06-codeeditor-completion-polish.md)
- [ ] **Q22**: [08 Strudel 音楽機能拡張](08-strudel-music-features.md) — サブタスク分割可
- [ ] **Q23**: [07 CodeEditor マルチカーソル](07-codeeditor-multicursor.md) — リスク高のため最後

## 順序の根拠 (要約)

- **13 が最初**: 全 golden 再生成タスク — golden が増える前ほど差分が小さい。
- **19-A (publish 早回し) を機能実装より前に**: アセットパス/shaders/フォント同梱の穴は早く見つけるほど各機能タスクが正しい前提で書ける。
- **21-① を M2 の前に**: ECS スケール対応・DevStats・fps 化は、ゲーム機能を作っている最中のデバッグ効率に直接効く。gizmo だけは対象機能の後 (②③)。
- **M2 内の順**: 15/17 は独立で軽い → 18 (最大・16 のテクスチャパーティクルの前提でもある) → 16 → 10 → 01。
- **M4 は 03→04→05→09**: 各 MD の推奨順そのまま (小さく積み上げ)。
- **M6 は最後**: ゲーム完成 (このリポジトリの当面のゴール) に寄与しないため。ユーザーの指示があれば前倒しして良い。

## 運用メモ

- キューの分割ステージ (Q04/Q05/Q12〜14 の 19・21) は **MD を消すタイミングに注意** — 全ステージ完了まで残す。
- git worktree で作業する場合は tools/ junction を忘れない (README/メモリ参照)。
- 検証 GPU が無い環境では e2e は Skip される — その場合は「単体テスト + ビルド」までで完了とし、キューに `(e2e 未実施)` を残して次のセッションで実機確認する。
