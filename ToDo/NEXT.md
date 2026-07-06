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
- [x] **Q03**: 14 FixedUpdate + 描画補間 — ゲーム/物理の時間基盤。既存デモ (Knockdown) を移行済み。完了 = FixedUpdate フェーズ/蓄積器/Alpha + InterpolatedTransform + 単体テスト + Demos/Framework/DrawInterpolation + Docs/Framework 追記
- [x] **Q04**: [19 capstone ①](19-standalone-game-shipping.md) **ステージ A: 骨組み + publish 早回し** — タイトル画面だけの LuxelCavern を作り publish チェックリスト 1〜4, 7, 8 を 1 周 (直すのは Luxel 本体側)。**MD はまだ削除しない** (2026-07-06 完了: samples/LuxelCavern 骨組み + shaders publish 修正。詳細は 19 MD 進捗節)
- [ ] **Q05**: [21 DevTools ゲーム規模対応](21-devtools-game-scale.md) **ステージ ①: A (ECS スケール) + C (DevStats) + D (FixedUpdate/timescale) + E (WithDevTools) + F (fps 化) + DebugDraw コア** — B の機能別 gizmo (物理/カメラ/タイル) は対象機能の実装後 (Q12/Q14) に回す。**MD はまだ削除しない** (**A/C/D + DebugDraw コア 完了** (両 UI + 単体テスト 611 passed + Gizmos golden、e2e 54/54 diff 0)。**残り E/F/Docs は後段へ据え置き** (ユーザー判断 2026-07-06): E は Q13 と同時、F は実ゲーム稼働後 (60fps 測定に実ゲーム要)、Docs は E/F 完了後。詳細は 21 MD 進捗節。**MD はまだ削除しない**)

### M2 — 2D ゲーム機能 (capstone ① の部品)

- [x] **Q06**: 15 セーブ/ロード + 設定 (2026-07-06 完了) — A: `WorldSave.Serialize/Deserialize` (Friflo EntitySerializer + version ラッパ + `[ComponentKey(null)]` 除外)。B: `SettingsStore.Get<T>()`→`Signal<T>` + `IFileStore` (インメモリ/物理) + 破損 .bak 退避。新プロジェクト `Luxel.Settings`。デモ Demos/Framework/SaveLoad・Settings (golden 各 1)、Docs/Runtime 「永続化」節。単体テスト 14。ゲーム側配線 (保存先 %APPDATA%/ゲーム名、セーブ/設定 UI、InputBindings 永続化) は **Q13 (ゲーム組み上げ)** で実施。
- [x] **Q07**: 17 カメラコントローラ (2026-07-06 完了) — `CameraRig2D` (Luxel.TwoD): 追従 (デッドゾーン + フレームレート非依存の指数平滑) / ワールド境界クランプ / 画面シェイク (固定シード xorshift) / ズーム平滑。`OrbitCamera` (Luxel core): yaw/pitch/distance → viewProj + Orbit/Dolly。`RectF` 追加。単体テスト 11、デモ Demos/TwoD/CameraRig (golden)、Docs/TwoD・ThreeD にカメラ節。3D の follow/shake は MD 通りスコープ外、シェイクは平行移動のみ (回転は任意)。
- [x] **Q08**: 18 スプライトアトラス + タイルマップ (2026-07-06 完了) — A: プリミティブに `srcX/srcY` 追加 (`GpuPath` 64→72B + raster2d fine/bounds シェーダ) でアトラス任意サブ矩形サンプリング (clamp で隣接に滲まない、既存 golden 全無変更を実証)。B: `SpriteAtlas`/`SpriteRect`/`SpriteAnimation`/`Scene2D.DrawSprite`/`ImageSubRect` + `SpriteAtlasStep`。C: `TileSet`/`TileMap` (CSV + Tiled `.tmj` import) + チャンク描画 `AppendChunk` + 可視チャンク `VisibleChunks` + 保持型 `TileMapLayer` (可視チャンクのみ UiNode 実体化・dirty 再構築) + 衝突 `QueryAabb`/`Sweep` (物理非依存 AABB グリッド、軸分離)。単体テスト 32、デモ Demos/TwoD/Sprites・Tilemap (vk golden 各1)、Docs/Gpu にスプライトアトラス/タイルマップ節。**MD 削除済み**。
- [x] **Q09**: 16 パーティクル (2026-07-06 完了) — 新プロジェクト 4 本 (Luxel.Particles コア + .TwoD/.ThreeD/.UI)。コア: ParticleSystem/Buffer/CpuSimulator/Value(Const/Range/Curve)/Color/Config + Xorshift64 + per-particle tint + Forces フック。.TwoD: ParticleNode (RetainedCanvas ContentColors)。.ThreeD: ParticleBillboards + billboard.slang (カメラ向き instance quad) + Spherical 放出。.UI: ParticleView [UiComponent]。JSON: ParticleConfigJson 往復 + ParticleConfigStep。BreakoutStory を新 ParticleSystem に dogfood 置換。単体テスト 24、デモ Demos/TwoD/Particles・ParticleView・Demos/3D/Particles (vk golden 各1) + Docs/Gpu パーティクル節。**MD 削除済み**。
- [x] **Q10**: 10 Audio ストリーミング (2026-07-06 完了) — `IAudioStream`(int Read(Span<float>)) + `WavStream`(依存なし RIFF パーサ、16bit/float32、逐次) + `LoopingStream`(継ぎ目なしループ) + `StreamingVoice`(StreamMixerSink 踏襲の毎 Tick Pump + キュー深さ<3 補充、float→16bit 量子化、リングバッファ)。単体テスト 10 (WAV デコード/チャンク境界/終端/float32/ループ/ポンプのキュー維持・終端停止/headless 例外なし)。Docs/Runtime にストリーミング節。ogg (NVorbis) は次段・mp3 はスコープ外。**実機スモークストーリーは未追加** (RealWindowOnly = golden 非対象、実窓+実デバイス必須でこの環境で検証不可。backend の submit/BuffersQueued 経路は StreamMixerSink と共通で実証済み)。**MD 削除済み**。
- [x] **Q11**: 01 ScriptSystem + csx hot reload (2026-07-06 完了) — 新プロジェクト `Luxel.Scripting.Framework`: `ScriptSystem` (安定ラッパ Attach + Reload、コンパイル失敗/実行時例外で旧維持+診断公開) + `ScriptSystems`/`ScriptGameGlobals` + `IScriptSource`(Memory/File)。デモ `Demos/Scripting/HotReload` (CodeEditor で .csx 編集 → Apply で箱の動きが変わる、構文エラーで旧ロジック継続 + 診断、Canvas2D で決定的、golden 3枚)。ゲームスクリプト用 ScriptHost はストーリーローカル Lazy (typeof(BoxGlobals))。単体テスト 6、e2e 63/63。Docs/Scripting 面③を実装済みに。**MD 削除済み**。
- [x] **Q12**: 21 DevTools ステージ②: B の 2D gizmo (2026-07-06 完了) — DebugDraw コアの上に `Gizmos2D` (TileCollision=衝突タイルワイヤ / Sweep / CameraRig=デッドゾーン+境界、Luxel.TwoD) と `ParticleGizmos.Emitter` (エミッタ十字+alive 数、Luxel.Particles.TwoD) を追加。OFF 時ゼロ割り当て。`ParticleSystem.EmitPosition` 公開。単体テスト 4 (DebugDrawTests に追記=静的状態直列化)、デモ Demos/TwoD/Gizmos2D (Canvas2D=Skia可・決定的、vk golden)。物理 gizmo はステージ③ (Q14)。**MD はまだ削除しない**

### M3 — capstone ① 完成

- [ ] **Q13**: [19 capstone ①](19-standalone-game-shipping.md) **ステージ B: ゲーム組み上げ → e2e → publish 本番 → Docs「配布」節** — 完了したら **19 の MD を削除**。**ここで合流**: Q05-E (WithDevTools 統合、ゲーム構造が固まってから) + Q06 の保存/設定ゲーム配線 (保存先パス・セーブ/設定 UI・InputBindings 永続化)。 (着手中: 2026-07-06 — **大タスク・複数セッション**。**S1 済**: CavernSim (プレイヤー物理) + CavernLevel + Game/Cavern golden。**S2 済**: 収集/扉/トゲ+HP/巡回敵/無敵+ノックバック+シェイク。**S3 済**: 飛行敵 + 演出イベント + 松明炎/コイン/撃破パーティクル (tint)。**S4 済**: チェックポイント + CavernSave (進捗 JSON 往復)。**S5 済**: GameFlow 状態機械 (Title/Playing/Paused/GameOver/Clear) + CavernHud (HP/コイン/鍵を world-anchored でスクリーン空間へ、ポーズオーバーレイ)。golden に HUD。単体テスト計 27 (CavernSim 21 + GameFlow 6)。**残**: .csx 敵 AI/SettingsStore音量+設定UI(Q06 B)/Audio/**実時間 exe プレイアブル化(GameLoop+補間+入力+実ファイル書込)**/Tiled/WithDevTools+gizmo(Q05-E/Q12)/publish本番/Docs。詳細は 19 MD 進捗節)

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
