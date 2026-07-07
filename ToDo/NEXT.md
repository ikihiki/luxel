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
- [x] **Q05**: [21 DevTools ゲーム規模対応](21-devtools-game-scale.md) **ステージ ①: A (ECS スケール) + C (DevStats) + D (FixedUpdate/timescale) + E (WithDevTools) + F (fps 化) + DebugDraw コア** — 完了 (2026-07-07)。A/C/D + DebugDraw コア (2026-07-06) に加え、**E (WithDevTools 集約: 新 `Luxel.Framework.DevTools` + `IFramePublisher`、Cavern を移行) / F1 (FrameChannel リング, 書き手ゼロ割り当て + 二読者 seqlock) / F3 (WebSocket push, latest-wins, 実機 chrome 検証) / F2 (内蔵版プール化) / F4 (MJPEG 不採用) / Docs (Docs/DevTools に「ゲームを観測する」節)** を実装。単体テスト 805 passed、e2e 65/65 diff 0。**MD はまだ削除しない** — B の物理 gizmo (ステージ③ = Q14) 完了まで残す。詳細は 21 MD 進捗節。

### M2 — 2D ゲーム機能 (capstone ① の部品)

- [x] **Q06**: 15 セーブ/ロード + 設定 (2026-07-06 完了) — A: `WorldSave.Serialize/Deserialize` (Friflo EntitySerializer + version ラッパ + `[ComponentKey(null)]` 除外)。B: `SettingsStore.Get<T>()`→`Signal<T>` + `IFileStore` (インメモリ/物理) + 破損 .bak 退避。新プロジェクト `Luxel.Settings`。デモ Demos/Framework/SaveLoad・Settings (golden 各 1)、Docs/Runtime 「永続化」節。単体テスト 14。ゲーム側配線 (保存先 %APPDATA%/ゲーム名、セーブ/設定 UI、InputBindings 永続化) は **Q13 (ゲーム組み上げ)** で実施。
- [x] **Q07**: 17 カメラコントローラ (2026-07-06 完了) — `CameraRig2D` (Luxel.TwoD): 追従 (デッドゾーン + フレームレート非依存の指数平滑) / ワールド境界クランプ / 画面シェイク (固定シード xorshift) / ズーム平滑。`OrbitCamera` (Luxel core): yaw/pitch/distance → viewProj + Orbit/Dolly。`RectF` 追加。単体テスト 11、デモ Demos/TwoD/CameraRig (golden)、Docs/TwoD・ThreeD にカメラ節。3D の follow/shake は MD 通りスコープ外、シェイクは平行移動のみ (回転は任意)。
- [x] **Q08**: 18 スプライトアトラス + タイルマップ (2026-07-06 完了) — A: プリミティブに `srcX/srcY` 追加 (`GpuPath` 64→72B + raster2d fine/bounds シェーダ) でアトラス任意サブ矩形サンプリング (clamp で隣接に滲まない、既存 golden 全無変更を実証)。B: `SpriteAtlas`/`SpriteRect`/`SpriteAnimation`/`Scene2D.DrawSprite`/`ImageSubRect` + `SpriteAtlasStep`。C: `TileSet`/`TileMap` (CSV + Tiled `.tmj` import) + チャンク描画 `AppendChunk` + 可視チャンク `VisibleChunks` + 保持型 `TileMapLayer` (可視チャンクのみ UiNode 実体化・dirty 再構築) + 衝突 `QueryAabb`/`Sweep` (物理非依存 AABB グリッド、軸分離)。単体テスト 32、デモ Demos/TwoD/Sprites・Tilemap (vk golden 各1)、Docs/Gpu にスプライトアトラス/タイルマップ節。**MD 削除済み**。
- [x] **Q09**: 16 パーティクル (2026-07-06 完了) — 新プロジェクト 4 本 (Luxel.Particles コア + .TwoD/.ThreeD/.UI)。コア: ParticleSystem/Buffer/CpuSimulator/Value(Const/Range/Curve)/Color/Config + Xorshift64 + per-particle tint + Forces フック。.TwoD: ParticleNode (RetainedCanvas ContentColors)。.ThreeD: ParticleBillboards + billboard.slang (カメラ向き instance quad) + Spherical 放出。.UI: ParticleView [UiComponent]。JSON: ParticleConfigJson 往復 + ParticleConfigStep。BreakoutStory を新 ParticleSystem に dogfood 置換。単体テスト 24、デモ Demos/TwoD/Particles・ParticleView・Demos/3D/Particles (vk golden 各1) + Docs/Gpu パーティクル節。**MD 削除済み**。
- [x] **Q10**: 10 Audio ストリーミング (2026-07-06 完了) — `IAudioStream`(int Read(Span<float>)) + `WavStream`(依存なし RIFF パーサ、16bit/float32、逐次) + `LoopingStream`(継ぎ目なしループ) + `StreamingVoice`(StreamMixerSink 踏襲の毎 Tick Pump + キュー深さ<3 補充、float→16bit 量子化、リングバッファ)。単体テスト 10 (WAV デコード/チャンク境界/終端/float32/ループ/ポンプのキュー維持・終端停止/headless 例外なし)。Docs/Runtime にストリーミング節。ogg (NVorbis) は次段・mp3 はスコープ外。**実機スモークストーリーは未追加** (RealWindowOnly = golden 非対象、実窓+実デバイス必須でこの環境で検証不可。backend の submit/BuffersQueued 経路は StreamMixerSink と共通で実証済み)。**MD 削除済み**。
- [x] **Q11**: 01 ScriptSystem + csx hot reload (2026-07-06 完了) — 新プロジェクト `Luxel.Scripting.Framework`: `ScriptSystem` (安定ラッパ Attach + Reload、コンパイル失敗/実行時例外で旧維持+診断公開) + `ScriptSystems`/`ScriptGameGlobals` + `IScriptSource`(Memory/File)。デモ `Demos/Scripting/HotReload` (CodeEditor で .csx 編集 → Apply で箱の動きが変わる、構文エラーで旧ロジック継続 + 診断、Canvas2D で決定的、golden 3枚)。ゲームスクリプト用 ScriptHost はストーリーローカル Lazy (typeof(BoxGlobals))。単体テスト 6、e2e 63/63。Docs/Scripting 面③を実装済みに。**MD 削除済み**。
- [x] **Q12**: 21 DevTools ステージ②: B の 2D gizmo (2026-07-06 完了) — DebugDraw コアの上に `Gizmos2D` (TileCollision=衝突タイルワイヤ / Sweep / CameraRig=デッドゾーン+境界、Luxel.TwoD) と `ParticleGizmos.Emitter` (エミッタ十字+alive 数、Luxel.Particles.TwoD) を追加。OFF 時ゼロ割り当て。`ParticleSystem.EmitPosition` 公開。単体テスト 4 (DebugDrawTests に追記=静的状態直列化)、デモ Demos/TwoD/Gizmos2D (Canvas2D=Skia可・決定的、vk golden)。物理 gizmo はステージ③ (Q14)。**MD はまだ削除しない**

### M3 — capstone ① 完成

- [x] **Q13 (完成)**: **capstone ①「Luxel Cavern」** (2026-07-07 完成、S1〜S17)。`samples/LuxelCavern` (Core 純ロジック + 実時間 exe)。合流済: Q05-E/F (DevTools オーバーレイ + fps + DebugServer 配信) / Q06 (セーブ・設定・キーバインドの %APPDATA% 配線)。**19 MD は削除済み** (仕様は `samples/LuxelCavern/README.md` + git 履歴)。(記録: **大タスク・複数セッション**。**S1 済**: CavernSim (プレイヤー物理) + CavernLevel + Game/Cavern golden。**S2 済**: 収集/扉/トゲ+HP/巡回敵/無敵+ノックバック+シェイク。**S3 済**: 飛行敵 + 演出イベント + 松明炎/コイン/撃破パーティクル (tint)。**S4 済**: チェックポイント + CavernSave (進捗 JSON 往復)。**S5 済**: GameFlow 状態機械 + CavernHud (HP/コイン/鍵、world-anchored)。**S6 済**: .csx 敵 AI ドッグフーディング。**S7 済**: 実時間 exe プレイアブル化 (LuxelHostBuilder+GameScene+GameLoop, WindowSystem 提示, KeyboardSource 操作)。**S8 済**: publish 本番 + 配布検証 (self-contained フォルダ 120MB, リポジトリ外 vk/dx exit 0, single-file は保留)。**S9 済**: タイトルフロー + %APPDATA% オートセーブ (checklist 6, `CavernPersistence`)。**S10 済**: **オーディオ (BGM + イベント SE)** — Q10/Luxel.Audio ドッグフード。`CavernSfxDetector` (sim の出来事→SE、純ロジック)、`CavernSfxBank` (CPU 合成・外部アセット不要)、`CavernAudio` (BGM ループ + AudioMixer ワンショット + Master/Music/Sfx バス)、exe は `UseAudio()` で XAudio2。`CavernSim.JumpedThisStep` 追加。テスト `CavernSfxDetectorTests` 9 + `CavernAudioTests` 3、全 775 passed、e2e 65/65 diff 0。**capstone コア (プレイアブル + 配布 + フロー + セーブ + 音) 達成**。**S11 済**: **設定画面 (音量) + SettingsStore 永続化 (Q06 B)** — `CavernSettings` (SettingsStore 上、Master/Music/Sfx を Signal で、AutoSave で %APPDATA% へ)、`CavernAudio.BindSettings`+Tick で AudioBus.Volume に反映、`GameState.Settings` + タイトル「S : せってい」→ ↑↓ 選択・←→ 調整・Esc 戻る。`CavernSettingsTests` 3、全 778 passed、e2e 65/65 diff 0。**S12 済**: **Tiled (.tmj) レベル外部化** — Q07 `FromTiledJson` ドッグフード。`levels/cavern1.tmj` を Core.dll に埋め込み (旧 Build 規則から生成 = bit 同一)、`CavernTiled` がタイル層 (FromTiledJson) + オブジェクト層 (coin/key/door/walker/flyer/checkpoint/torch) を CavernSim へ。`CavernTiledTests` 5、全 783 passed、**e2e 65/65 diff 0**。**S13 済**: **レベル読み込みを ResourceSystem 経由に** (ユーザー指示) — `EmbeddedResourceSource` (`res://` スキーム) + `CavernLevelLoader` (インスタンス・static 無し、`res://levels/cavern1.tmj` を byte[] ノードでキャッシュ)、`GameFlow` がローダを受け取り `CavernLevel` の static CreateSim/Torches/Build を廃止。全 783 passed、e2e 65/65 diff 0。**S14 済**: **ゲーム内 DevTools オーバーレイ (gizmo + DevStats)** (Q05-E/Q12 合流) — `CavernDevOverlay` (F1 トグル、`Gizmos2D`+`ParticleGizmos` を `DebugDraw` に溜め Flush、統計パネル、`DevStats.Set` 配信)。exe に VK_F1 + fps 計測。`CavernDevOverlayTests` 4、全 787 passed、e2e 65/65 diff 0。**S15 済**: **single-file publish 対応** (ユーザー指示) — 本文フォント .ttf を exe Content から Core.dll 埋め込みへ移動、`CavernAssets.LoadBodyFont(ResourceSystem)` が `res://fonts/…` を `EmbeddedResourceSource` 経由でロード。`-p:PublishSingleFile=true` で ~86MB 単一 exe、`C:\` から vk/dx とも exit 0 (旧: フォント未検出で失敗)。`CavernAssetsTests` 2、全 789 passed、e2e 65/65 diff 0。**S16 済**: **キーバインド再割当 UI** (ユーザー指示) — `CavernSettings` に `Signal<KeyCode>` バインド (A/D/Space 既定、AutoSave)、`CavernBindings.Apply/Rebind` (プライマリ + 矢印セカンダリを InputAction へ)、`IKeyCapture` を `KeyboardSource` が実装 (生キー取得)、設定画面 6 行化 (音量 3 + キーバインド 3、Enter で割当・Esc キャンセル)。`CavernBindingsTests` 4、全 793 passed、e2e 65/65 diff 0。**S17 済**: **DebugServer 起動 (ブラウザ DevTools 配信)** (ユーザー指示) — `CavernDevServer` (exe, `--devtools [port]`) が `DevToolsListener`+`DebugServer` を起動、`DevStats` (fps/状態/HP) を `/custom` へ、ゲームフレームバッファを `DiagFrame` として `/frame` へ配信。**Claude in Chrome で実機検証**: DevTools UI 表示・GAME(DEVSTATS) ライブ更新・`/frame?format=png` が実画面 960×540 PNG。全 793 passed、デフォルトスモーク exit 0。**capstone 完成 (コア + checklist + 設定 UI [音量/キーバインド] + DevTools オーバーレイ + DebugServer 配信、全達成)**。**残 (任意・未着手)**: DevTools オーバーレイ/設定画面の golden ストーリー化 (視覚回帰)。)

### M4 — 3D 物理・アニメーション (capstone ② の部品)

- [x] **Q14**: 03 CCD + [21](21-devtools-game-scale.md) **ステージ ③ 物理 gizmo 骨格** (2026-07-07 完了)。**CCD**: `RigidBody.Dynamic(ccd: true)` / `PhysicsWorld.AddDynamic(continuous:, maxSpeculativeMargin:)` → Bepu `ContinuousDetection.Continuous`。テスト = 薄い壁へ 150m/s の球 (margin 0.1 で投機接触を無効化) で「CCD なし=貫通 / あり=手前で停止」を両 assert。Docs/Physics に CCD 節、ロードマップから CCD 削除。**03 MD 削除済み**。**物理 gizmo 骨格**: 新プロジェクト `Luxel.Physics.Gizmos` の `PhysicsGizmos.DrawColliders(world, dyn, static, ccd 色)` = ECS の Collider を OBB ワイヤ (箱=実寸/球・カプセル=外接) で描画・動的/静的/CCD を色分け・OFF 時ゼロ割り当て。デモ `Demos/3D/PhysicsGizmos` (等角投影・authored pose で決定的、golden 1)。単体テスト 3 (CCD 1 + gizmo 2)、全 808 passed、e2e 66/66 diff 0。トリガー/接触の gizmo は Q15 で完成 (→ 21 全ステージ済み、**21 MD 削除済み**)。
- [x] **Q15**: 04 接触イベント + トリガー + [21](21-devtools-game-scale.md) **ステージ ③ 完了** (2026-07-07)。**接触イベント**: `LuxelNarrowPhaseCallbacks` が `ConfigureContactManifold` で実接触 (depth≥0) ペアを共有コレクタ `PhysicsContacts` へ収集 → `PhysicsWorld` が Timestep 前後で BeginStep/前ステップ差分 → `ContactEvents` (raw, collidable) を公開。`PhysicsStepSystem` が handle→Entity 逆引きで `ContactEvent {A,B,Phase}` (Entity ベース) へ変換。**トリガー**: `Trigger` component (形状は同居 Collider、静的) を `AddTrigger` で登録、callbacks がトリガー絡みペアは manifold 無効化 (力なし) しつつ検知。**gizmo (21 ③ 完成)**: `PhysicsGizmos.DrawColliders` に trigger 色追加 + `ContactMarkers` (接触中ペア = `PhysicsStepSystem.CurrentContacts` を opt-in 収集、中心間midpoint に十字インジケータ)。デモ `Demos/3D/PhysicsTrigger` (落下球がゴールゾーン通過→enter/exit カウント + 着地接触マーカ、物理を固定 dt で回して決定的、golden 1)。Docs/Physics に「接触イベント + トリガー」節 (ロードマップから項目 2 削除)、Docs/DevTools gizmo 節に追記。単体テスト +4 (接触 begin/end・トリガー通過で速度不変・gizmo trigger 色・contact 十字)、全 812 passed、e2e 67/67 diff 0。**04 MD + 21 MD 削除済み**。
- [x] **Q16**: 05 メッシュ/凸包コライダー (2026-07-07)。**静的メッシュ**: `PhysicsWorld.AddStaticMesh` (三角形スープ → Bepu `Mesh`、Shapes 経由で Dispose 連動) + ECS `MeshCollider.Static(verts, indices, scale)`。**動的凸包**: `PhysicsWorld.AddDynamicHull` (頂点群 → `ConvexHull`、重心 recenter の center を out で返し pose を補正) + ECS `HullCollider.Dynamic(points, ...)` (attach で center 保持、書き戻しで元原点へ)。contact 逆引き map に mesh/hull も追加。デモ `Demos/3D/PhysicsMesh` (波打つ地形メッシュ + 球 + 四面体凸包が載って静定、等角投影で決定的、golden 1)。Docs/Physics に「メッシュ/凸包コライダー」節 (winding=片面/重心オフセットの罠、凸分解・動的メッシュはスコープ外) + ロードマップから項目 3 削除。単体テスト +3 (静的メッシュに球静定・四面体凸包が床貫通なし・ECS mesh+hull attach/静定)、全 815 passed、e2e 68/68 diff 0。**05 MD 削除済み**。
- [x] **Q17**: 09 glTF skin/morph (2026-07-07 完了、skin + morph の 2 スライス)。**skin** (scaffold 結線): `scene_pbr_skinned.slang` を SceneInstanceData 形式 (world+baseColor) + joint バッファに整備 (matIdx/material 版から差替、glTF 規約で skinned node 変換は無視=instance world 恒等、SkinningSystem の既存 InverseBind×jointWorld を流用)、デモ `Demos/3D/GltfSkinned` (RiggedSimple、GPU 頂点スキニング、曲がる棒)。罠: RenderGraph の Write 無しパスはデッドパスカリング。**morph** (greenfield): loader が `primitive.targets`/`mesh.weights`/`node.weights` をパース、`SceneBuilder` が morph デルタ (位置+法線 24B × target × vertex) を upload + `MorphWeights` component 初期化、`SceneAnimationPlayer` が weights channel を適用 (flat float[] を targetCount で slice)、新シェーダ `scene_pbr_morph.slang` (頂点で `pos/nrm += Σ w·δ`)、デモ `Demos/3D/GltfMorph` (手続き的な隆起 morph、weight 0.85)。単体テスト +7 (SkinningSystemTests 3 + GltfLoader morph parse 1 + MorphTests 3)、全 822 passed、e2e 70/70 diff 0・vk/dx 一致。Docs/Motion 更新。**09 MD 削除済み**。**→ M4 (物理・アニメ Q14〜17) 完了、次は M5 = Q18 (3D capstone)**。

### M5 — capstone ② 完成

- [ ] **Q18**: [20 capstone ②: 3D 射的](20-game2-3d-shooting-range.md) — 完了したら **20 の MD を削除** (着手中: 2026-07-07、大タスク・複数セッション)。**スライス 1 完了**: `samples/LuxelRange/LuxelRange.Core` の `RangeSim` (床 + 薄板ターゲット + CCD 弾 + ContactBegin スコア、残弾制、最初の発射まで物理停止) + `RangeSimTests` 4 + Gallery `Apps/Game/Range` (OrbitCamera 軌道 + クリック発射 + cube_forward、golden vk/dx 一致)。03/04/17 統合済み。**スライス 2 完了**: `RangeTerrain` の起伏メッシュ地形 (05 in-game、同一頂点を物理 AddStaticMesh + scene_pbr_lite 描画に共有 = 絵=当たり) + 外周壁、Gallery で 2 パイプライン描画 (地形 pbr + 的/弾 cube)、winding 検証テスト +1。全 827 passed、e2e 71/71 diff 0・vk/dx 一致。制約: 高速弾の対 Mesh CCD は貫通あり→kill plane で回収。**スライス 3a 完了**: `PhysicsWorld.RemoveBody`/`RemoveStatic` + `RangeSim` の動的 ConvexHull 小物×3 (単位箱ハル=描画一致) + kill plane despawn (KillY 下回りで RemoveBody+DeleteEntity、貫通弾を回収)。テスト +2、全 829 passed。**スライス 3b 完了**: ボーナスゾーン (Trigger + RangeBonusZone マーカ + 黄床マーカ、ContactBegin 小物×ゾーン→+200 一度だけ)、テスト +1、全 830 passed、e2e 71/71 diff 0。知見: 100m/s 弾で動的小物を吹き飛ばすのは対動体 CCD 限界で不確実。**スライス 3c-1 完了**: キネマティック体インフラ (`PhysicsWorld.AddKinematic`/`SetBodyPose`/`KinematicBody` component) + 動く的 Fox (キネマティック箱を X 巡回、命中 +300 + ひるみ 1.5s)。**キネマティックは CCD 弾が確実命中** (動的小物のトンネリング回避)。テスト +2、全 832 passed、e2e 71/71 diff 0。Fox は今は紫箱、skin モデル差し替えは 3c-2。**残**: 3c-2=Fox を skin(09)に差替 / 3d=命中パーティクル(16)+SE / 4=Title/Result+ハイスコア/設定(15)+BGM(10) / 5=exe+publish 3D 検証。詳細は 20 MD 進捗節。

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
