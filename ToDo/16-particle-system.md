# 16 — 標準パーティクルシステム (2D)

## 概要

エミッタ・寿命・速度/重力・色/サイズカーブを持つ標準の ParticleSystem を Luxel に追加する。現状は BreakoutStory が `Particle struct + List` の ad hoc 実装で自作しており、VFX (爆発・煙・キラキラ・ヒットエフェクト) をゲームごとに再発明している。デモで確立したパターンをエンジン機能へ昇格させるタスク。

## 背景と現状

- **Breakout の前例** ([src/Luxel.Gallery/Stories/BreakoutStory.cs](../src/Luxel.Gallery/Stories/BreakoutStory.cs)): パーティクル描画は **ContentColors + ReserveContent の Content 差し替え** (ノードのパス内容を毎フレーム書き換え — Segment 再アップロードはあるが構造再構築はしない)。この描画手法が性能面の答えで、標準化でもこれを踏襲する。
- **RetainedCanvas の増分更新**: 移動 = Transform 書込 / 色 = Style 書込のみ。ただしパーティクルは毎フレーム数が変わる — ノード増減 (Rebuild) を避けるため「**固定容量を先に確保して使い回す**」のが定石 (Knockdown の Slot パターンと同型)。
- **決定性**: 乱数は固定シード xorshift (StrudelKit 方式)。wall-clock 禁止 — 更新は dt 駆動。
- 3D パーティクルは今回スコープ外 (2D compute ラスタライザ上で完結させる)。

## 実装方針

### 1. コア (Luxel.TwoD または新 Luxel.Particles — 配置は Controls 非依存の層に)

```csharp
public sealed class ParticleSystem
{
    public ParticleSystem(ParticleConfig config, int capacity, ulong seed);
    public void Emit(float x, float y, int count);        // バースト
    public void SetEmission(float x, float y, float rate); // 連続放出 (毎秒 rate 個)
    public void Update(float dt);                          // 寿命/速度/重力/カーブ評価
    public int Alive { get; }
    // 描画: RetainedCanvas ノードへの書き込み口 (下記 2)
}

public record ParticleConfig(
    float LifeMin, float LifeMax,
    float SpeedMin, float SpeedMax, float SpreadRadians, float BaseAngle,
    float Gravity, float Drag,
    float SizeStart, float SizeEnd,
    uint ColorStart, uint ColorEnd,     // 寿命で lerp (α含む)
    ParticleShape Shape);                // Quad / Circle 程度から
```

- 内部は **SoA の固定長配列** (pos/vel/life/seed…、capacity で確保しきり、死亡スロットは swap-remove or free-list)。List/クラス配列にしない (GC ゼロ)。
- 乱数は自前 xorshift (seed 注入) — `Random` 禁止 (決定性 + 割り当て)。
- カーブは v1 では線形 lerp のみ (start→end)。AnimationCurve 統合は将来。

### 2. 描画統合

- `ParticleNode` (UiNode 1 個 + ReserveContent(capacity × 形状セグメント数)): Update 後に生存パーティクルのパスを Content 差し替え + ContentColors で色書き込み — Breakout の手法をそのまま部品化。
- widget 層のラッパ `ParticleView` ([UiComponent]) も用意し、UI ツリー/ストーリーから使えるように (AddAnimation で Tick 駆動)。ゲーム (ECS) からは system で `ps.Update(dt)` を呼ぶ。
- **Skia CPU バックエンドの制約に注意**: SkiaRenderer は AA が GPU と不一致 — 単体テストは絵ではなくロジック (位置/寿命/生存数) で検証し、絵は e2e golden (GPU) で。

### 3. テスト + デモ + Docs

- 単体テスト (GPU 不要): 固定シード + 固定 dt で N ステップ → 生存数/座標のゴールデン値一致 (決定性)。capacity 超過 Emit で古いものから捨てる (or 無視 — 仕様を決めて assert)。重力/寿命の境界。
- デモストーリー「Demos/TwoD/Particles」: バースト (クリックで爆発) + 連続放出 (噴水) + knob (rate/gravity)。play: Click → Step(30) → Snap (固定シード + 固定 dt なので golden 決定的)。
- BreakoutStory のパーティクルを新 ParticleSystem に置き換え (dogfooding、golden 差分は許容 — 見た目が同等なら update)。
- Docs/TwoD にパーティクル節を追加。

## 罠・注意

- **golden 決定性が最重要**: シード固定・dt 固定・`Alive` 順序の安定 (swap-remove は順序が変わる — 描画順が変わると絵が変わるので、z 一定 or 発生順を保つ free-list を選ぶ)。
- ReserveContent の容量はセグメント数ベース — 形状 (Quad=1 パス 4 線分等) との掛け算を間違えると途中で Rebuild が走り性能が落ちる (LastSegmentBytesWritten 等の統計で検証できる — サンプル09 の前例)。
- ContentColors=true のノードは色をパス単位で持つ — 色 lerp はここに書く。
- `[UiComponent]` 規約 (ctor 全廃・[UiParam] 宣言順・既定値はフィールド初期化子) に従う。新コンポーネントは Reference/Overview golden の更新対象。

## スコープ外

- 3D パーティクル (ビルボード/GPU シミュレーション)、AnimationCurve 統合、テクスチャパーティクル (Image シェイプ対応は [18](18-sprite-atlas-tilemap.md) のアトラスと合流してから)、サブエミッタ/トレイル。
