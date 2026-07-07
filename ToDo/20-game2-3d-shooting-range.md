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

**残 (後続スライス)**: 2 = メッシュアリーナ (05、起伏 + 外周壁) + 物理小物 (ConvexHull)。3 = 動く的 (09 Fox skin) + ボーナスゾーン/kill plane トリガー (04) + パーティクル (16) + SE。4 = Title/Result + ハイスコア/設定 (15) + BGM (10)。5 = exe プレイアブル化 + publish 3D 検証 (glTF/scene_pbr shaders)。**20 MD は全スライス完了まで残す**。

## スコープ外

- morph のゲーム内使用 (09 のデモストーリーで検証)、2D パーティクルの 3D 空間内使用、深度フェード付きエフェクト (16 スコープ外)。
- 制限時間モード・複数ステージ・武器種・オンラインランキング。
- インストーラ等の配布系 (① と同じ)。
