# 03 — Physics: CCD (連続衝突検出) の ECS 公開

## 概要

高速に動く剛体が薄い壁をすり抜ける (トンネリング) のを防ぐ CCD を、ECS コンポーネント経由で指定できるようにする。Docs/Physics のロードマップで**「実装コスト低・優先度最高」と自己記載されている**項目 — Bepu 側の機能 (`ContinuousDetection.Continuous`) は存在しており、Luxel.Physics のコンポーネント面に口を開けるだけ。

## 背景と現状

- **Luxel.Physics** (src/Luxel.Physics/): BepuPhysics v2 の統合。
  - `PhysicsWorld.cs` — Simulation の生成・管理
  - `Components.cs` — ECS コンポーネント (RigidBody 等。`RigidBody.Dynamic(initialVelocity)` のようなファクトリがある — KnockdownStory 参照)
  - `PhysicsStepSystem.cs` — 毎 Step で Query して未登録 body を遅延登録 (実行時追加のエンティティも拾う)
  - `Callbacks.cs` — Bepu の narrow phase コールバック
  - `PhysicsSettings.cs`
- **ロードマップの記載場所**: [src/Luxel.Gallery/Stories/Docs/DocsGpu.cs](../src/Luxel.Gallery/Stories/Docs/DocsGpu.cs) の Docs/Physics ページ「## ロードマップ (v1 スコープ外)」節。**着手前にこの節の CCD 項を必ず読むこと** (当時の設計メモが書いてある)。
- **デモの前例**: [src/Luxel.Gallery/Stories/KnockdownStory.cs](../src/Luxel.Gallery/Stories/KnockdownStory.cs) (Game/Knockdown3D) — Bepu 物理 + ECS 3D + クリックで弾を発射。物理は固定 1/120 蓄積器 + 最初のクリックまで停止 (初期絵が snap 決定的)。物理デモストーリーは 3D/PhysicsFalling / 3D/PhysicsPlayground もある。

## 実装方針

1. **コンポーネント面**: RigidBody (または生成記述子) に CCD 指定を足す。Bepu では `BodyDescription.Collidable` の `ContinuousDetection` に `ContinuousDetection.Continuous(sweepConvergence, minimumProgression)` を渡す。API 案:
   - `RigidBody.Dynamic(velocity, ccd: true)` の bool フラグ (既定 false = `ContinuousDetection.Passive`)、または
   - `CcdMode` enum (Discrete/Passive/Continuous) — Bepu の意味論に合わせるならこちら。最小は bool で良い。
2. **登録経路**: PhysicsStepSystem (または PhysicsWorld の body 生成箇所) で、コンポーネントの CCD 指定を BodyDescription に写す。
3. **テスト**: tests/Luxel.Tests/PhysicsTests.cs (既存) に追加。決定的シナリオ: 薄い静的箱 (例: 厚さ 0.1) に向けて高速 (例: 100 m/s) の球を発射し、固定 dt で N ステップ → CCD なしはすり抜ける (z が壁の向こう) / CCD ありは手前で止まる、を両方 assert する (「CCD なしで実際にトンネリングする」ことの確認がテストの信頼性を担保する)。既定は単スレッド = 決定的なので値の assert が安定する。
4. **デモ + Docs**: 3D/PhysicsPlayground か専用ミニストーリーに CCD on/off の比較を追加できると良いが、単体テストで挙動が示せていればデモは任意。Docs/Physics のロードマップ節から CCD 項を削除し、本文に使い方 (1 段落 + コード例) を追記。

## 検証

- `dotnet test` (PhysicsTests — GPU 不要)。
- Docs ページの絵が変わる場合のみ `-- vk e2e --update "Physics"`。

## 罠・注意

- Bepu の CCD は speculative margin と関係する — `ContinuousDetection.Continuous()` の既定引数でまず動かし、テストが不安定なら sweep 引数を明示。
- 固定 1/120 の蓄積器ステップ (KnockdownStory 方式) を単体テストでも踏襲すると、デモと同じ時間解像度で検証できる。
- PhysicsStepSystem.Run は毎回 Query — 既存の遅延登録パターンを壊さない (CCD 指定も登録時に一度だけ適用)。

## スコープ外

- 接触イベント (→ [04](04-physics-contact-events.md))、メッシュコライダー (→ [05](05-physics-mesh-colliders.md))。
