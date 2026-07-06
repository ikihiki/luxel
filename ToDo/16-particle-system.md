# 16 — 標準パーティクルシステム (2D + 3D ビルボード)

## 概要

エミッタ・寿命・速度/重力・色/サイズカーブを持つ標準の ParticleSystem を Luxel に追加する。現状は BreakoutStory が `Particle struct + List` の ad hoc 実装で自作しており、VFX (爆発・煙・キラキラ・ヒットエフェクト) をゲームごとに再発明している。デモで確立したパターンをエンジン機能へ昇格させるタスク。シミュレーションコアは 2D/3D 共有 (Vector3) で、描画バックエンドとして 2D (RetainedCanvas) と 3D (ビルボード) の両方を提供する。

## 背景と現状

- **Breakout の前例** ([src/Luxel.Gallery/Stories/BreakoutStory.cs](../src/Luxel.Gallery/Stories/BreakoutStory.cs)): パーティクル描画は **ContentColors + ReserveContent の Content 差し替え** (ノードのパス内容を毎フレーム書き換え — Segment 再アップロードはあるが構造再構築はしない)。この描画手法が性能面の答えで、標準化でもこれを踏襲する。
- **RetainedCanvas の増分更新**: 移動 = Transform 書込 / 色 = Style 書込のみ。ただしパーティクルは毎フレーム数が変わる — ノード増減 (Rebuild) を避けるため「**固定容量を先に確保して使い回す**」のが定石 (Knockdown の Slot パターンと同型)。
- **決定性**: 乱数は固定シード xorshift (StrudelKit 方式)。wall-clock 禁止 — 更新は dt 駆動。
- 描画は 2D (compute ラスタライザ上の RetainedCanvas) と 3D (ビルボード) の両対応。コアのシミュレーションは Vector3 で 1 本 (実装方針 1)。

## 実装方針

### 1. コア (新プロジェクト Luxel.Particles + Luxel.Particles.TwoD) — 2026-07-06 決定

- **配置は Luxel.Animation の 3 分割と同型**: `Luxel.Particles` (コア — 依存は Luxel + Luxel.Animation のみ、描画非依存) + `Luxel.Particles.TwoD` (RetainedCanvas 統合) + `Luxel.Particles.ThreeD` (ビルボード)。**`.ThreeD` も本タスクの対象** (3D capstone ゲームのヒットエフェクト等で使う) — 実装順は 2D を先に、.ThreeD を第 2 段として同タスク内で。
- **座標は最初から Vector3** (2D は z=0 で使う) — シミュレーションのコードパスを 1 本にし、.ThreeD を後付けの追加だけで済ませる。

```csharp
public sealed class ParticleSystem
{
    public ParticleSystem(ParticleConfig config, int capacity, ulong seed);
    public void Emit(Vector3 pos, int count);              // バースト
    public void SetEmission(Vector3 pos, float rate);       // 連続放出 (毎秒 rate 個)
    public void Update(float dt);                           // 寿命/速度/重力/カーブ評価
    public int Alive { get; }
    // 描画: 各バックエンド (.TwoD / 将来 .ThreeD) への書き込み口 (下記 2)
}

public record ParticleConfig(
    ParticleValue Life,
    ParticleValue Speed, float SpreadRadians, float BaseAngle,
    float Gravity, float Drag,
    ParticleValue Size,
    ParticleColor Color,                // 寿命で lerp (α含む)
    ParticleShape Shape);                // Quad / Circle 程度から
```

- **パラメータは `ParticleValue` に統一**: Const / Range(min,max) / Easing(start,end,ICurve) / Curve(ICurve) の判別共用体 (Effekseer のパラメータモデル参考)。Min/Max ペアの ad hoc 増殖を避け、カーブ対応を後から全パラメータへ一律に足せる。**v1 の実装は Const/Range (+Size/Color の線形 lerp) だけで良いが、型は最初からこれで切る**。カーブは Luxel.Animation の `ICurve` を使う。
- 内部は **SoA の固定長配列** (pos/vel/life/seed…、capacity で確保しきり、死亡スロットは swap-remove or free-list)。List/クラス配列にしない (GC ゼロ)。**SoA レイアウトは unmanaged struct 配列に保つ** — 将来 GPU バッファへそのまま写すため。
- **積分器は差し替え可能に**: バッファ (SoA) と積分器 (`IParticleSimulator`) を分離し、v1 は CPU 実装のみ。将来 GPU compute シミュレーション (bindless + compute のエンジン本流に合う方向) を同じ外部 API (Emit/Update/Alive) の下で差し込めるよう、**公開面に CPU 読み戻し前提の API を置かない** (統計は Alive 数程度に留める)。
- **フォースフィールド (乱流・引力等) はエンジンに組み込まない** — ゲーム側実装とする。コアは毎ステップ速度を加工できるフック (SoA span を渡すコールバック) だけ用意する。
- 乱数は自前 xorshift (seed 注入) — `Random` 禁止 (決定性 + 割り当て)。

### 2. 描画統合 (Luxel.Particles.TwoD)

- `ParticleNode` (UiNode 1 個 + ReserveContent(capacity × 形状セグメント数)): Update 後に生存パーティクルのパスを Content 差し替え + ContentColors で色書き込み — Breakout の手法をそのまま部品化。
- widget 層のラッパ `ParticleView` ([UiComponent]) も用意し、UI ツリー/ストーリーから使えるように (AddAnimation で Tick 駆動)。ゲーム (ECS) からは system で `ps.Update(dt)` を呼ぶ。
- **Skia CPU バックエンドの制約に注意**: SkiaRenderer は AA が GPU と不一致 — 単体テストは絵ではなくロジック (位置/寿命/生存数) で検証し、絵は e2e golden (GPU) で。

### 2b. 描画統合 (Luxel.Particles.ThreeD — ビルボード)

- Update 後に生存パーティクルを `ParticleInstanceData` (pos/size/rot/color) として `RenderBuffer<T>` に詰め (`MarkDirty` → `FlushImmediate` — Render3DExtractSystem の InstanceData 方式と同型)、RenderGraph の 1 パスでインスタンス描画。
- **ビルボード Slang シェーダを 1 本追加** (カメラの right/up 軸で quad 展開、アルファブレンド)。既存規約に従う: bindless `g_buffers[]`、SPIR-V + DXIL 併存、**vk/dx ピクセル一致**。描画順は発生順 (free-list が順序を保つのでソート不要 — 深度ソートは v1 でやらない、加算/通常ブレンドの割り切りを Docs に明記)。
- 深度フェード (ソフトパーティクル) はやらない — 背景/深度サンプリングはゲーム側の領分 (スコープ外参照)。

### 3. JSON 資産化 + ライブ編集

- `ParticleConfig` を JSON からも読めるようにし、リソース DAG に (ParticleConfig, uri) のパースステップを 1 つ足す (GltfStep/画像と同じ規約)。
- これだけで DAG の watch/reload に乗り、**「JSON 保存 → 実行中のゲームでエフェクトが変わる」ライブ編集**が既存機構のタダ乗りで成立する (Effekseer のネットワーク編集に相当する体験)。検証は「JSON → Config 往復の単体テスト」+ 実窓スモークで (watch は非決定なので golden にしない)。

### 4. テスト + デモ + Docs

- 単体テスト (GPU 不要): 固定シード + 固定 dt で N ステップ → 生存数/座標のゴールデン値一致 (決定性)。capacity 超過 Emit で古いものから捨てる (or 無視 — 仕様を決めて assert)。重力/寿命の境界。ParticleValue の各形態 (Const/Range/Easing/Curve) の評価。
- デモストーリー「Demos/TwoD/Particles」: バースト (クリックで爆発) + 連続放出 (噴水) + knob (rate/gravity)。play: Click → Step(30) → Snap (固定シード + 固定 dt なので golden 決定的)。
- デモストーリー「Demos/ThreeD/Particles」(.ThreeD): 3D 空間でのバースト + OrbitCamera。play は 2D と同じ定石 (Click → Step → Snap)。3D 側は Skia CPU で検証できないので golden は GPU e2e のみ。
- BreakoutStory のパーティクルを新 ParticleSystem に置き換え (dogfooding、golden 差分は許容 — 見た目が同等なら update)。
- Docs/TwoD にパーティクル節を追加。

## 進捗 (2026-07-06)

**コア + .TwoD + .ThreeD + JSON 資産 完了。残り: ParticleView [UiComponent] + Breakout dogfood (最終セッション)。**

### 完了 (第 2 セッション: .ThreeD + JSON)

- **`.ThreeD` ビルボード** ([ParticleBillboards.cs](../src/Luxel.Particles.ThreeD/ParticleBillboards.cs)): 生存を `RenderBuffer<Instance>` (32B) に詰め、
  **`shaders/billboard.slang`** が SV_InstanceID から 6 頂点 quad をカメラ right/up 軸で展開 (ソフト円、straight alpha)。
  深度テストあり・書き込み無し + AlphaBlend、発生順描画 (ソートなし)。`CameraAxes(eye,target)` で OrbitCamera のスクリーン軸。
  球面放出は `ParticleConfig.Spherical` (+Y 軸円錐、π で全球) を追加 — `SpawnOne` に分岐。デモ Demos/3D/Particles (vk golden)。
- **JSON 資産化** ([ParticleConfigJson.cs](../src/Luxel.Particles/ParticleConfigJson.cs)): `FromJson`/`ToJson` 往復 (ParticleValue は数値/const/range/curve、
  色は `#RRGGBBAA`、ease 名) + `ParticleConfigStep` (リソースステップ)。Luxel.Particles に Resources 参照追加。
- テスト +4 (球面放出 / CameraAxes 直交 / JSON 往復 / hex 色)。計 701 passed。Docs/Gpu パーティクル節に 3D/JSON 追記。

### 完了 (第 1 セッション: コア + .TwoD)

### 完了 (この MD はまだ削除しない)

- **コア `Luxel.Particles`** (依存 Luxel + Luxel.Animation): `Xorshift64` (決定的乱数、共通化) /
  `ParticleValue` (Const/Range/Curve 判別共用体、`Sample`=放出時スカラー・`Eval`=寿命補間、float 暗黙変換) /
  `ParticleColor` (start→end を ICurve 補間、α 含む) / `ParticleConfig` (record) / `ParticleShape` (Quad/Circle) /
  `ParticleBuffer` (SoA float[]、生存は発生順で連続) / `IParticleSimulator` + `CpuParticleSimulator` (積分器 seam) /
  `ParticleSystem` (Emit バースト / SetEmission 連続 / Update / `Forces` フック / `Buffer` 公開)。容量超過 Emit は無視 (順序安定)。
- **`Luxel.Particles.TwoD`**: `ParticleNode` (RetainedCanvas 1 ノード + ContentColors + ReserveContent、
  `BuildScene` で生存パーティクルを per-particle 色の絵に、`Sync()` で Content 差し替え — Breakout の手法を部品化)。
- **テスト**: [tests/Luxel.Tests/ParticleTests.cs](../tests/Luxel.Tests/ParticleTests.cs) 18 本 (xorshift 決定性 / ParticleValue 各形態 /
  色補間 / Emit・容量・連続放出 / 速度・重力・抗力積分 / 寿命除去 / 発生順安定 / フォースフック / 決定性再現 / BuildScene パス数)。計 697 passed。
- **デモ + Docs**: [ParticleStory.cs](../src/Luxel.Gallery/Stories/ParticleStory.cs) `Demos/TwoD/Particles`
  (バースト爆発 + 連続噴水、固定シード + 固定 dt で事前ステップ、vk golden)。Docs/Gpu に「パーティクル」節。
- プロジェクトは `Luxel.slnx` + Gallery/Tests に配線済み。

### 残り (最終セッション)

- **`ParticleView` [UiComponent]** (方針 2): AddAnimation で Tick 駆動する widget ラッパ (Reference/Overview golden 更新対象)。
  `ParticleNode` を UI ツリー/ストーリーから使えるようにする薄いラッパ。
- **BreakoutStory の dogfooding 置換**: ad hoc パーティクル (`Particle struct` + `List` + `StepParticles`) を新 `ParticleSystem` +
  `ParticleNode` に置換 ([src/Luxel.Gallery/Stories/BreakoutStory.cs](../src/Luxel.Gallery/Stories/BreakoutStory.cs)、golden 差分は同等なら update)。
- 全ステージ完了で **この MD を削除**。

## 罠・注意

- **golden 決定性が最重要**: シード固定・dt 固定・`Alive` 順序の安定 (swap-remove は順序が変わる — 描画順が変わると絵が変わるので、z 一定 or 発生順を保つ free-list を選ぶ)。
- ReserveContent の容量はセグメント数ベース — 形状 (Quad=1 パス 4 線分等) との掛け算を間違えると途中で Rebuild が走り性能が落ちる (LastSegmentBytesWritten 等の統計で検証できる — サンプル09 の前例)。
- ContentColors=true のノードは色をパス単位で持つ — 色 lerp はここに書く。
- `[UiComponent]` 規約 (ctor 全廃・[UiParam] 宣言順・既定値はフィールド初期化子) に従う。新コンポーネントは Reference/Overview golden の更新対象。

## スコープ外 (2026-07-06 更新)

- **Effekseer 統合はしない (決定)**: ランタイム組み込みも efkefc 読み込みもしない。ネイティブ依存 (C++ shim)・vk/dx ピクセル一致の規律・headless e2e と衝突するため。素材を使いたい場合はエディタのベイク出力 (連番/スプライトシート) → [18](18-sprite-atlas-tilemap.md) のアトラス + flipbook で。
- **フォースフィールド (乱流・引力)・歪み/ソフトパーティクル (背景/深度サンプリング) はゲーム側で実現する**: コアは速度加工フック (前述) を、描画は RenderGraph にゲームが自前パスを足すことで対応。エンジン機能にしない。
- GPU シミュレーション実装 (差し込み口だけ v1 で確保)、3D ビルボードの深度ソート/深度フェード、テクスチャパーティクル (Image シェイプ対応は [18](18-sprite-atlas-tilemap.md) のアトラスと合流してから)、サブエミッタ/トレイル (サブエミッタは将来 — コアは「親パーティクル位置を参照して Emit できる口」を塞がない設計に)。
