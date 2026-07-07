# 20 — capstone ②: 3D 射的アリーナ「Luxel Range (仮称)」

## 概要

3D 物理・アニメーション系タスクの検証場となる 2 本目のゲーム。glTF 製アリーナを軌道カメラで見回し、高速の弾を発射して的を撃ち抜き、残弾内のスコアを競う射的ゲーム。既存 [KnockdownStory](../src/Luxel.Gallery/Stories/KnockdownStory.cs) (クリックで弾発射 + Bepu 物理) の正統進化。

検証対象: [03 CCD](03-physics-ccd.md) / [04 接触イベント](04-physics-contact-events.md) / [05 メッシュコライダー](05-physics-mesh-colliders.md) / [09 glTF skin](09-gltf-skin-morph.md) / [14 FixedUpdate 物理統合](14-framework-fixedupdate.md) / [16 .ThreeD パーティクル](16-particle-system.md) / [17 OrbitCamera](17-camera-controller.md) / [10](10-audio-streaming.md)・[15](15-save-load-settings.md) の再利用 / [19](19-standalone-game-shipping.md) で作った publish 基盤に **glTF アセット + 3D シェーダ (scene_pbr 系)** が乗るかの追加検証。

**[19](19-standalone-game-shipping.md) (capstone ①) の後に着手する** — 出荷基盤 (assetRoot/フォント/SettingsStore/publish targets) にタダ乗りし、本タスクは 3D 固有の検証に集中する。

## ゲーム仕様

### 状態遷移

```
Title ──開始──→ Play (残弾 20 発) ──残弾 0 + 全弾静定──→ Result (スコア + ハイスコア更新) → Title
                 └─ Esc → Pause (音量/リトライ/タイトルへ)
```

時間制ではなく**残弾制** (テンポと決定性テストの書きやすさを優先)。

### プレイ内容

- **アリーナ**: glTF 地形 1 枚 (目安 40×40m、起伏 + 外周壁)。物理は静的 `Mesh` コライダー ([05](05-physics-mesh-colliders.md)) — 弾が起伏で跳ねる。描画メッシュと同じ頂点から三角形抽出 (絵と当たりのズレが即バレる = 生きた検証)。
- **カメラ**: OrbitCamera ([17](17-camera-controller.md) の 3D 側 — Knockdown のドラッグ軌道カメラの部品化)。ドラッグ/右スティックで yaw/pitch、ホイールで distance。照準は画面中央レティクル (2D HUD)。
- **発射**: クリック/RT で球弾 (半径 0.1m、初速 100 m/s、重力あり、**CCD on** ([03](03-physics-ccd.md)))。残弾 20。
- **的 3 種**:
  1. **薄板ターゲット** ×10 (厚さ 0.1m の静的箱、命中 +100)。**CCD なしでは物理的にすり抜けて当たらない配置にする** — 03 のテストシナリオ (薄壁トンネリング) をゲームデザインに昇格。
  2. **動く的** ×2: skin アニメーションのキャラクター (Khronos CC0 の Fox が定番、[09](09-gltf-skin-morph.md)) が巡回パスを移動 (+300)。命中でひるみアニメ → 数秒後に復帰 (morph は無理に入れない — 09 のデモストーリー検証で可)。
  3. **物理小物**: 樽/宝石 (動的 `ConvexHull`、[05](05-physics-mesh-colliders.md))。吹き飛ばしてボーナスゾーンに落とすと +200。
- **判定** ([04](04-physics-contact-events.md)): 命中 = `ContactBegin` 購読 (弾 × 的のペア)。トリガーボリューム = ボーナスゾーン (通過検知) + 場外 kill plane (落ちた弾/小物の despawn — body リーク防止の実益)。
- **エフェクト**: 命中で .ThreeD パーティクルバースト ([16](16-particle-system.md))、スコアポップは 2D HUD 側。
- **Audio**: BGM ストリーミング ([10](10-audio-streaming.md)) + 命中/発射 SE (クリップ)。
- **永続化** ([15](15-save-load-settings.md)): ハイスコア + 音量設定 (capstone ① の SettingsStore を再利用 — 2 ゲーム目でも通じることの確認)。
- **UI は日本語** (同梱フォント)。入力はマウス + キーボード + ゲームパッド。

### アセット

- Khronos glTF サンプル (Fox 等、CC0) + 地形/樽は自作 glTF or CC0。assets/ に配置 + ライセンス表記。golden 用アセットと goldens/ は分離 (既存規約)。

## 実装方針

### 1. 配置とロジック共有 ([19](19-standalone-game-shipping.md) と同型)

```
samples/LuxelRange/
├─ LuxelRange.Core/   # GameScene 派生シーン + システム群 (StoryContext 非依存)
└─ LuxelRange/        # WinExe。結線 + publish の薄い層
```

- Gallery が Core を参照し `Game/Range` ストーリーとしてホスト — play/golden はストーリー側。
- 参照は ① の構成 + Physics/Gltf/AssetRuntime/AssetsGpu/RenderGraph/Particles.ThreeD。
- **DevTools**: ① と同じく `--devtools`。**物理 gizmo ([21](21-devtools-game-scale.md) B — コライダーワイヤ/接触点/トリガー/CCD 色分け) はこのゲームのデバッグでほぼ必須** — 04/05 の実装デバッグでも威力を発揮するので 20 着手前までに用意。スコア/残弾/body 数は DevStats で発信。

### 2. 決定性と e2e (物理ゲームの定石 = KnockdownStory 方式)

- **最初の入力まで物理停止** (初期絵が snap 決定的) + **固定 1/120 蓄積器** — [14](14-framework-fixedupdate.md) の FixedUpdate 機構に載せる (FixedDt=1/120)。動く的の巡回・ひるみタイマーも FixedUpdate 駆動。
- Bepu は単スレッド既定 = 決定的 — **スコアの数値 assert まで書ける**。
- play 例: `Drag(カメラ回転)` → `Click(発射)` → `Step(n)` → `Expect(スコア=100)` → `Snap` / ConvexHull 小物を吹き飛ばして `Expect(ボーナス加算)` / 薄板に CCD off の弾を撃つ比較はデモ側 ([03](03-physics-ccd.md)) に任せる。
- 乱数はゲーム性に使わない (パーティクル演出のみ、固定シード)。

### 3. publish 追加検証

① で通したチェックリストのうち、3D 固有の項目を確認:

1. **3D シェーダ**: scene_pbr 系 + ビルボード (16) の SPIR-V/DXIL が publish 出力の shaders/ に揃うか。
2. **glTF アセット**: .glb/.gltf + テクスチャが assets コピーに乗り、AppContext.BaseDirectory 基準で解決されるか。
3. リポジトリ外起動 (vk/dx) + 起動時間/サイズ記録 — ① の Docs「配布」節に追記。

## 作業ステップ

1. 前提タスクの実装は各 ToDo 側で: [03](03-physics-ccd.md) (小) → [04](04-physics-contact-events.md) → [05](05-physics-mesh-colliders.md) → [09](09-gltf-skin-morph.md) (大)。16 の .ThreeD と 17 の OrbitCamera も先行。
2. 骨組み: アリーナ (glTF Mesh コライダー) + OrbitCamera + 弾発射 + 薄板ターゲット (03/04/05 の統合が最初に通る縦切り)。
3. 動く的 (09) + 物理小物 + ボーナスゾーン/kill plane + エフェクト/SE。
4. Title/Result + ハイスコア/設定 + BGM。
5. Gallery ストーリー版の play/golden 整備。
6. publish 追加検証 (vk/dx) + Docs 追記。発見した穴は ToDo/ へ。

## 罠・注意

- **ConvexHull の center 補正** ([05](05-physics-mesh-colliders.md)): Bepu は重心原点に平行移動した形状を返す — 描画 transform との対応を忘れると絵と当たりがズレる。
- glTF の座標系/スケール: SceneRenderExtractor と同じ変換を物理にも適用。
- skin 付きメッシュの AABB はアニメで動く — v1 はバインドポーズ AABB × 余裕係数。
- 弾/小物の despawn で Bepu body の解放漏れに注意 (kill plane 経路で必ず Remove)。Mesh の BufferPool 解放経路も確認 ([05](05-physics-mesh-colliders.md) の罠)。
- 命中イベントは「フレーム内で読み切り、持ち越さない」規約 ([04](04-physics-contact-events.md))。
- STA/GPU 遅延生成/Phase 名前衝突は ① と同じ。

## 進捗

### 2026-07-07: スライス 1 — 縦切り (アリーナ + OrbitCamera + CCD 弾 + 薄板ターゲット + 命中スコア)

03 (CCD) + 04 (接触イベント) + 17 (OrbitCamera) の統合を最初に通す縦切り。**済**:
- 新プロジェクト `samples/LuxelRange/LuxelRange.Core` (純ロジック、net10.0、参照 = Luxel.Ecs + Luxel.Physics)。
- `RangeSim`: 床 (静的箱) + 薄板ターゲット ×5 (厚さ 0.15m の静的箱、`RangeTarget{Score}`) を配置。`Fire(origin, dir)` = CCD 球弾 (半径 0.1m、初速 100m/s、`RangeBullet` マーカ)。`StepOnce()` = 固定 1/120 ステップ + `ProcessHits` (Step.ContactEvents の弾×的 Begin → スコア加算、Hit フラグで二重計上防止)。最初の発射まで物理停止 (`Started`) = 初期絵決定的。残弾制。
- 単体テスト `RangeSimTests` 4 (命中で +100・二重計上なし / 空撃ち無得点 / 残弾枯渇で Fire 拒否 / 未発射は物理停止)。
- Gallery ストーリー `Apps/Game/Range` (KnockdownStory 型: `RangeScene : GameScene, IStoryApp`、`OrbitCamera` でドラッグ軌道 + ホイールズーム、クリック→レイキャスト→Fire、ECS→Render3DExtract→cube_forward 描画)。golden = 初期静止アリーナ (床 + 薄板 5 枚)、vk/dx 一致。
- 罠: `OnFixedUpdate` は `OnUpdate` (InitGpu で `_sim` 生成) より先に走る → `_sim is null` ガード必須。
- 検証: 全 826 passed / e2e 71/71 diff 0・vk/dx 一致。

### 2026-07-07 (2): スライス 2 — メッシュアリーナ (05 in-game、起伏地形 + 外周壁)

**済**:
- `RangeTerrain` (Core): 決定的な起伏地形メッシュ (位置 + 解析法線 + 三角形、±15m、N=20)。winding は Q16 の上面法線向き。**同じ頂点を物理 (AddStaticMesh) と描画の両方に使う** = 絵と当たりが一致。
- `RangeSim`: 平床を起伏メッシュ地形に差替 (`Physics.AddStaticMesh`)、外周壁 ×4 (見えない静的箱、場外防止)、薄板ターゲットを地形高さに載せる。地形データを `TerrainPositions/Normals/Indices` で公開。
- Gallery `RangeScene`: 地形を `scene_pbr_lite` で描画 (RangeSim と同一頂点)、的/弾は `cube_forward`。1 パス内で 2 パイプライン描画。golden 更新 (起伏地形 + 地形上の薄板)、vk/dx 一致。
- 単体テスト `RangeSimTests` に winding 検証 1 追加 (低速球が地形上で静定 = 上面衝突向き)。全 827 passed / e2e 71/71 diff 0。
- **既知の制約**: 100m/s の弾を地形へ**真下に**撃つと Bepu の CCD-vs-Mesh が捕捉できず貫通する (静止メッシュへの高速掃引の限界)。ゲームでは的への概ね水平な射撃が主で、地形を外した弾は **kill plane で despawn** する設計 (スライス 3) — トンネリングした弾の落下無限化を防ぐ。

### 2026-07-07 (3): スライス 3a — ConvexHull 物理小物 + kill plane despawn

**済**:
- `PhysicsWorld.RemoveBody(BodyHandle)` / `RemoveStatic` を追加 (Bepu `Simulation.Bodies.Remove`)。**entity 削除と対で呼ぶ** — body を残すと見えない衝突体がリークする (Docs/Physics の既知課題を解消)。
- `RangeSim`: 動的 ConvexHull 小物 ×3 (単位箱の 8 頂点 = `HullCollider.Dynamic`、`RangeProp` マーカ、地形上に配置、描画は単位キューブ = ハルと一致)。**kill plane** = `DespawnFallen` が毎ステップ `KillY=-20` を下回った弾/小物を `RemoveBody` + `DeleteEntity`。`PropCount`/`DespawnedCount` 公開。
- 単体テスト +2: 真下撃ちで地形貫通した弾が KillY 下回りで despawn (スライス 2 の高速弾トンネリング制約の**回収経路を実証**) / 小物が地形上に留まり despawn しない (ConvexHull vs Mesh の静定)。
- Gallery: 小物が単位キューブ (青) として地形上に描画される。golden 更新、vk/dx 一致。全 829 passed / e2e 71/71 diff 0。

### 2026-07-07 (4): スライス 3b — ボーナスゾーン トリガー scoring (04)

**済**:
- `RangeSim`: ボーナスゾーン (静的 `Trigger` collidable + `RangeBonusZone` マーカ、z=-5 の帯) + 装飾床マーカ (黄、コライダー無し)。`RangeProp.Scored` フラグ。`ProcessHits` を拡張し ContactBegin(小物 × ボーナスゾーン) → `BonusScore += 200` (一度だけ)。
- 単体テスト +1: 小物にゾーン方向の速度を与えると +200 を一度だけ (二重計上なし)。
- Gallery: 黄色の床マーカが小物と的の間に描画される。golden 更新、vk/dx 一致。全 830 passed / e2e 71/71 diff 0。
- **物理の知見**: **100m/s の弾で動的小物を吹き飛ばすのは Bepu の対動体 CCD 限界で不確実** (弾が小物をトンネリング、SolverSubsteps を上げても改善せず)。的 (静的箱) への CCD 命中は確実 (スライス 1 で実証済み)。ボーナス機構自体は速度直接付与で決定的にテスト。実プレイでは大きめのゾーンで best-effort。

### 2026-07-07 (5): スライス 3c-1 — 動く的 (キネマティック巡回 + 命中 +300 + ひるみ)

**済**:
- キネマティック体インフラ: `PhysicsWorld.AddKinematic` + `SetBodyPose` + `KinematicBody` component (接触の handle→Entity 逆引きに `BuildEntityMap` へ追加)。**キネマティックは無限質量 = 静的同様に CCD 弾が確実に当たる** (動的小物のトンネリング問題を回避)。
- `RangeSim`: 動く的 Fox = キネマティック箱 proxy を X 方向に巡回 (`FoxAt(phase)`、地形高さに載る)。`UpdateFox` が毎ステップ位相を進め姿勢 + LocalTransform 更新。命中 (`TryResolveFox`) で `FoxScore += 300` + ひるみ 1.5s (巡回停止・再命中で加点しない)。`FoxPosition`/`FoxFlinching`/`TotalScore` 公開。
- 単体テスト +2: Fox 命中で +300 + ひるみ / 巡回で位置が動く。全 832 passed / e2e 71/71 diff 0。
- Gallery: Fox は紫の箱として描画 (キネマティック pose を LocalTransform に反映、Render3DExtract が拾う)。**skin モデル (09 Fox.glb の scene_pbr_skinned 描画) は 3c-2 で差し替え** (FoxPosition へ配置)。golden 更新、vk/dx 一致。

### 2026-07-07 (6): スライス 3c-2 — 動く的を skin モデル (Fox.glb) に差し替え (09 in-game)

**済**:
- `RangeSim`: Fox entity から描画コンポーネント (MeshRef/Color3D) を除去 (物理 proxy + gameplay マーカのみ)。描画は Gallery が担当。
- `RangeScene`: `Fox.glb` を別 world で `SceneBuilder`、歩行アニメ (anim[1]) を毎ステップ sample → `TransformPropagateSystem` → `SkinningSystem` → joint 行列を `RenderBuffer` へ。**skin 頂点 (モデル空間) を instance World = Scale×YawRot×Translate(FoxPosition) で世界に配置** (root node は動かさず instance 変換で置く方式)。`scene_pbr_skinned` で 4 番目の draw。ひるみ中は歩行停止。
- 罠: joint 行列の抽出を「初回のみ」にすると毎フレームの upload が空になり全頂点が原点へ潰れる → **毎フレーム抽出**に修正。Fox モデルは ~155 units (bounds Z[-88,66]) なので scale 0.018。
- golden: 起伏地形 + 薄板 + 小物 + ボーナスゾーン + **歩く Fox** が描画。vk/dx skinning 一致。全 832 passed / e2e 71/71 diff 0。**09 (skin) をゲーム内で検証達成**。

### 2026-07-07 (7): スライス 3d — ゲームイベント層 + SFX 対応表 (演出/SE の駆動元)

**済** (Cavern の SfxDetector と同じ純ロジック方針):
- `RangeEvent { Kind, Position }` + `RangeEventKind {Shot, TargetHit, FoxHit, BonusScored}`。`RangeSim.Events` にそのフレームの出来事 (発射位置/命中位置) を積み、`ClearEvents` で消費。Fire/TryResolveHit/TryResolveFox/TryResolveBonus が発火。
- `RangeSfxDetector.Detect(events, into)` = イベント種別 → `RangeSfx {Fire, Hit, FoxHit, Bonus}` キューへの写像 (exe が実音を鳴らす)。
- 単体テスト +2: Fire/命中でイベント発火 / SFX 対応表の写像。全 834 passed / e2e 71/71 diff 0 (Core のみ、golden 不変)。
- **命中パーティクル (.ThreeD バースト、16) と exe 音 (10) はこのイベントを consume する** — パーティクル描画は frozen golden に映らないため follow-up (装飾常時エミッタで golden 化 or scripted play)。音は RealWindowOnly。

### 2026-07-07 (8): スライス 3d-2 — 命中 .ThreeD パーティクルバースト描画 (16 in-game)

**済**:
- `RangeScene`: `ParticleSystem` (火花 cfg、固定シード) + `ParticleBillboards`。命中イベント (`TargetHit`/`FoxHit`) の位置でバースト放出、毎ステップ `Update`、`OnRender` で `Sync` → RenderGraph パス内で `Draw` (カメラ向きビルボード、5 番目の描画、深度テスト + アルファブレンド)。
- golden: 中央ターゲット位置にデモバーストを 1 発焼いて 15 step 進め、frozen golden で in-game パーティクル描画を確認。**歩く Fox + 火花バースト**が映る。vk/dx 一致。全 834 passed / e2e 71/71 diff 0。**16 (.ThreeD パーティクル) を capstone ゲーム内で検証達成**。

### 2026-07-07 (9): スライス 4 — ゲームフロー (Title/Play/Result) + ハイスコア永続化 (15)

**済** (純ロジック、Cavern の GameFlow + Settings を踏襲):
- `RangeSettings(IFileStore)`: `SettingsStore` 上に HighScore (`Signal<int>`) + 音量 3 つ。AutoSave で即永続化。`SubmitScore` はハイスコア超えのみ更新。**capstone ① の SettingsStore が 2 ゲーム目でも通じることを確認 (15)**。
- `RangeGame(IFileStore)`: `RangeState {Title, Play, Result}` の状態機械。`StartRound` (sim 作り直し = 決定的リセット) → Play、`Fire` は Play のみ、`Step` は弾切れ後 `SettleSeconds`(2s) で Result へ (スコア確定 + `SubmitScore`)。`BackToTitle`。
- 単体テスト +3: Title→Play→hit→Result + ハイスコア更新 / 発射は Play のみ / ハイスコアが別ゲーム跨ぎで永続 (同じ InMemoryFileStore)。全 837 passed / e2e 71/71 diff 0 (Core のみ・golden 不変)。
- **Title/Result の UI 描画 + BGM/SE 実音配線は exe/RealWindowOnly** — スライス 5 (exe) で。

### 2026-07-07 (10): スライス 5-1 — exe 骨組み + publish 3D 検証

**済**:
- 新プロジェクト `samples/LuxelRange/LuxelRange` (WinExe、Cavern exe と同型)。`Program.cs` (STA + `WindowSystem`/`GpuSurface` 提示 + `FramePacer` + `--frames N` スモーク + `LuxelHostBuilder`)、`RangeRealtimeScene : GameScene` (RangeGame を固定 dt 駆動、起伏地形 scene_pbr_lite + 的/小物 cube_forward を Framebuffer へ、**attract 動作** = カメラ自動旋回 + 定期発射)。ハイスコアは `%APPDATA%/LuxelRange/`。
- 罠: `RangeGame` (DI singleton) を scene でも Dispose すると Bepu `Simulation.Dispose` 二重呼び出しでクラッシュ → scene の Dispose を外し `RangeSim.Dispose` を冪等化。
- **検証**: vk/dx とも `--frames` スモーク exit 0。**publish (self-contained win-x64) で全 3D シェーダ (cube_forward/scene_pbr_lite/morph/skinned/billboard の SPIR-V+DXIL) が `shaders/` に同梱**、**リポジトリ外の publish フォルダから vk/dx とも起動 exit 0** (3D 出荷経路 OK)。全 837 passed / e2e 71/71 diff 0。

### 2026-07-07 (11): スライス 5-2 — exe キーボード操作 (OrbitCamera + 発射)

**済**:
- `Program.cs`: `KeyboardSource : IInputSource` (Win32 キー → `InputBus`) を登録・`win.KeyDown/Up` に結線。
- `RangeRealtimeScene`: 入力アクション (`Axis1DAction` orbitH/orbitV = 矢印、`ButtonAction` fire=Space/quit=Esc) を `InputContext` で `_loop.InputStack` に push。`OnFixedUpdate` で矢印カメラ旋回 (+ 無操作時の緩い自動旋回) + Space 押下エッジで画面中央へ CCD 弾発射 + Esc 終了。
- **検証**: vk `--frames` スモーク exit 0 (入力配線後もループ健全)。

**残 (最終スライス 5-3)**: exe に Fox skin/パーティクル描画 (Gallery RangeScene の描画共有/移植) + Title/Result UI オーバーレイ (Rasterizer2D、スコア/ハイスコア表示) + BGM/SE 実音 (10、UseAudio + AudioMixer)。Docs の「配布」節に 3D capstone を追記。**これで 20 の全スライス完了 → 20 MD 削除**。

## スコープ外

- morph のゲーム内使用 (09 のデモストーリーで検証)、2D パーティクルの 3D 空間内使用、深度フェード付きエフェクト (16 スコープ外)。
- 制限時間モード・複数ステージ・武器種・オンラインランキング。
- インストーラ等の配布系 (① と同じ)。
