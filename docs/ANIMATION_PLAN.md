# Luxel.Animation 設計プラン (RFC)

**ステータス:** Draft (2026-06-30)
**前提:** [deep-research 報告 (2026-06-30)](https://docs.flutter.dev/ui/animations/overview) — 28 ソース・25 主張検証 (20 確認・5 棄却) で「**3 層 IR + 3 ターゲットアダプタ**」が業界標準と判明。
**関連メモリ:** `luxel-project.md` / `docs/RENDER_GRAPH_PLAN.md`

---

## 1. 動機

Luxel には UI (`Luxel.UI` signals)、2D (`Luxel.TwoD/Retained`)、3D (`Luxel.ThreeD` ECS) という 3 種のシーン表現が共存している。既に `Spinner`/`Accordion` 等で個別の `Tick(dt)` ベースの動きはあるが、

- **多様な入力形式** (glTF アニメ / CSS `@keyframes` / コード DSL Tween / Lottie / Spine / Rive) を統一に扱う基盤がない
- **2D/3D/UI 横断**で値補間 + ターゲット書込みを統一する抽象がない
- **再生制御** (play/pause/seek/speed/reverse/loop/sequence) の共通モデルがない

これを `Luxel.Animation` として整備する。

## 2. 設計の核心結論

業界調査 (Flutter / Bevy / Unreal / Unity / Spine / glTF / Motion) より:

1. **3 層 IR は共通、ターゲット書き込みは分離** (high confidence)。
2. 3 層構造: **Clip (pure data) → Track + Sampler (stateless 関数) → Player / TrackEntry (stateful)**。
3. その上にオプションで **AnimationGraph (Bevy 風 DAG: Clip/Blend/Add)** → **StateMachine** を後付け。
4. 値補間は **時間 t → Curve → progress (0..1) → Tween<T> → 値 T** の 2 段分解 (Flutter Animatable 流)。
5. デフォルト **frame-driven** (Luxel 既存の `UiHost.Tick(dt)` 活用) + 大量データのみ shader-driven (Slang compute) にオプトイン。
6. **ターゲット 3 アダプタ**: UI=Signal<T>、2D=RetainedCanvas SoA、3D=ECS component。共通化しない。

## 3. レイヤ全体図

```
┌────────────────────────────────────────────────────────────────┐
│  入力フォーマット (Importer)                                    │
│  ┌──────────┬────────────┬─────────────┬───────────┬─────────┐ │
│  │ glTF     │ CSS keyfr. │ コード DSL  │ Lottie    │ Spine   │ │
│  │ Importer │ Parser     │ (直接 L2)   │ (専用)    │ (専用)  │ │
│  └──────────┴────────────┴─────────────┴───────────┴─────────┘ │
└─────────────┬──────────────────────────────────────────────────┘
              │ Luxel.Resources の (型, uri) 自動コンポーズで非同期ロード
              ▼
┌────────────────────────────────────────────────────────────────┐
│  L1: AnimationClip (pure data, immutable, 共有可能)             │
│      tracks[] : Track[]                                          │
└─────────────┬──────────────────────────────────────────────────┘
              ▼
┌────────────────────────────────────────────────────────────────┐
│  L2: Track + Sampler (stateless 純粋関数)                       │
│      Curve.Chain(Tween<T>) で関数合成可能 (Flutter Animatable)   │
└─────────────┬──────────────────────────────────────────────────┘
              ▼
┌────────────────────────────────────────────────────────────────┐
│  L3: AnimationPlayer / TrackEntry (stateful)                    │
│      time/loop/alpha/mix/queue (Spine TrackEntry)                │
│      Per-entity (ECS) or per-UiHost (signals)                    │
└─────────────┬──────────────────────────────────────────────────┘
              ▼ (オプション) AnimationGraph (Clip/Blend/Add DAG)
              ▼ (オプション) StateMachine
              ▼ Tick(dt)
┌─────────────────┬─────────────────────┬───────────────────────┐
│  UI Adapter     │ 2D Adapter          │ 3D Adapter            │
│  (Signal<T>.Set)│ (RetainedCanvas.    │ (ECS Set<LocalT> 等)  │
│                 │  SoA 部分更新)       │                        │
└─────────────────┴─────────────────────┴───────────────────────┘
```

**重要不変量:** 各層は domain-agnostic。`AnimationClip` は「3D 専用」「UI 専用」を持たず、target の型情報だけ持つ。

## 4. API スケッチ (C#)

### L2: Animatable<T> / Tween<T> / Curve (核)

```csharp
// 時間 t (秒) → 0..1 progress
public interface ICurve { float Eval(float t01); }

public sealed class LinearCurve : ICurve { public float Eval(float x) => x; }
public sealed class CubicBezierCurve : ICurve { /* (x1,y1)-(x2,y2) で CSS cubic-bezier */ }
public sealed class StepCurve : ICurve { /* CSS steps() */ }
public sealed class SpringCurve : ICurve { /* stiffness/damping/mass */ }

// 0..1 progress → 型 T 値 (begin/end の補間)
public interface ITween<T> { T Lerp(float t01); }

public readonly struct FloatTween : ITween<float> { … }
public readonly struct Vector3Tween : ITween<Vector3> { … }
public readonly struct QuaternionTween : ITween<Quaternion> {   // slerp
    public Quaternion Begin, End;
    public Quaternion Lerp(float t) => Quaternion.Slerp(Begin, End, t);
}
public readonly struct ColorTween : ITween<Color2D> { … }
public readonly struct Affine2DTween : ITween<Affine2D> {  // TRS 分解 → 個別補間 → 合成
}

// 時間 → 値 (純粋関数, stateless)
public interface IAnimatable<T> { T Evaluate(float timeSec); }

// Flutter 風 chain: 時間 → curve → tween → 値
public sealed class Animatable<T> : IAnimatable<T> {
    public ICurve Curve { get; }
    public ITween<T> Tween { get; }
    public float Duration { get; }
    public T Evaluate(float t) {
        float t01 = Math.Clamp(t / Duration, 0, 1);
        return Tween.Lerp(Curve.Eval(t01));
    }
}
```

### L1: AnimationClip (アセット)

```csharp
public readonly record struct PropertyPath(string Path);  // "transform.position" or "$.opacity" etc.

public sealed class Track {
    public PropertyPath Target;     // どこに書くか (アダプタが解釈)
    public Type ValueType;          // float / Vector3 / Quaternion etc.
    public Keyframe[] Keyframes;    // (time, value, interpolation)
}

public readonly record struct Keyframe(float Time, object Value, InterpolationKind Kind);
public enum InterpolationKind { Step, Linear, CubicSpline }

public sealed class AnimationClip {
    public string Name;
    public float Duration;
    public Track[] Tracks;
}
```

### L3: AnimationPlayer / TrackEntry (再生状態)

```csharp
// ECS コンポーネント (3D) or UiHost に持たせる (UI)
public sealed class TrackEntry {
    public AnimationClip Clip;
    public float TrackTime;
    public float TimeScale = 1f;
    public bool Loop;
    public float Alpha = 1f;
    public float MixDuration;       // Spine 風 crossfade
    public TrackEntry? MixingFrom;  // queue/chain
    public Action? OnComplete;
}

public sealed class AnimationPlayer {
    public List<TrackEntry> Tracks;
    public void Tick(float dt) { /* 各 TrackEntry を進めて値を Apply */ }
}
```

### Apply: ターゲットアダプタ (3 種)

```csharp
public interface IAnimationTarget {
    void Apply(PropertyPath path, object value);
}

// UI Adapter
public sealed class SignalAnimationTarget : IAnimationTarget {
    private readonly Dictionary<string, object> _signals = new();
    public void Bind<T>(string path, Signal<T> sig) => _signals[path] = sig;
    public void Apply(PropertyPath p, object value) {
        if (_signals.TryGetValue(p.Path, out var s) && s is Signal<object> sig) sig.Value = value;
    }
}

// 2D Adapter
public sealed class RetainedCanvasAnimationTarget : IAnimationTarget {
    private readonly Dictionary<string, UiNode> _bindings = new();
    public void Bind(string path, UiNode node) => _bindings[path] = node;
    public void Apply(PropertyPath p, object value) {
        // "node1.transform" / "node1.color" などをパース → UiNode.Transform/Color を SoA 書込
    }
}

// 3D Adapter
public sealed class EcsAnimationTarget : IAnimationTarget {
    private readonly World _world;
    private readonly Dictionary<string, Entity> _bindings = new();
    public void Bind(string path, Entity e) => _bindings[path] = e;
    public void Apply(PropertyPath p, object value) {
        // "entity1.LocalTransform" → ECS の Set<LocalTransform>
    }
}
```

### コード DSL (L1/L3 をスキップして L2 を直接構築)

```csharp
// GSAP/DOTween 風 fluent (Luxel.UI signal を直接アニメート)
Animate.Tween(opacity, from: 0f, to: 1f, duration: 0.5f)
       .WithCurve(Curves.EaseOut)
       .OnComplete(() => Console.WriteLine("done"))
       .Play();

Animate.Sequence(
    Animate.Tween(pos, new Vector2(0, 0), new Vector2(100, 0), 0.3f, Curves.EaseInOut),
    Animate.Tween(opacity, 1f, 0f, 0.2f, Curves.Linear)
).Play();
```

### AnimationGraph (オプション、AN-M5)

```csharp
public abstract class GraphNode { public abstract void Evaluate(float t, PoseBuffer dst); }
public sealed class ClipNode  : GraphNode { … }
public sealed class BlendNode : GraphNode { float Weight; GraphNode A, B; }
public sealed class AddNode   : GraphNode { float Weight; GraphNode Base, Additive; }

public sealed class AnimationGraph {
    public GraphNode Root;
    public void EvaluateAt(float time, IAnimationTarget target) { /* DAG bottom-up */ }
}
```

## 5. 入力フォーマット → IR 変換

| フォーマット | Importer の責務 | 落とし先 | AN-M |
|---|---|---|---|
| **コード DSL / Tween** | `Animate.Tween(...)` で `Animatable<T>` を直接構築 | L2 直接 | M1/M2 |
| **glTF 2.0** | `animations[]` → `Channels`(target node/path) → `Samplers`(input/output accessor + LINEAR/STEP/CUBICSPLINE) を `AnimationClip` にマップ | L1 | M3 |
| **CSS `@keyframes`** | パーサ → `Animatable<T>` を直接構築 (Web Animations 仕様準拠) | L2 直接 | M6 (任意) |
| **Lottie Bodymovin** | **subset Importer** (transform/color/opacity のみ) → `AnimationClip`、完全 Lottie ranは外部プラグイン (`IAnimationImporter` 拡張点) | L1 (subset) / 別系統 (full) | M6 (subset) |
| **Spine** | atlas + .json/.skel。**bone hierarchy が必要なため `MESH_PLAN.md` 後に着手**、外部プラグインで実装 | 別系統 (専用 runtime) | 将来 |
| **Rive** | .riv runtime (state machine + inputs)。**Data Binding 詳細仕様が deep-research で棄却**されたため独立 runtime に隔離 | 別系統 (独立 runtime) | 将来 |

**Importer の拡張点** (#4 設計決定):

```csharp
public interface IAnimationImporter {
    bool CanHandle(string uri);
    AnimationClip Import(Stream input, ImportOptions opts);
}
```

本体提供: glTF Importer (AN-M3) + CSS @keyframes Parser (AN-M6) + Lottie subset Importer (AN-M6)。
外部 (将来プラグイン): 完全 Lottie / Rive / Spine。

## 6. Luxel 既存基盤との結線

| 既存 | 結線方法 |
|---|---|
| **Luxel.UI signals** (`Signal<T>`, `ReactiveEffect`) | `SignalAnimationTarget` が `Signal<T>.Value = lerp(...)` を呼ぶ。signals の依存追跡が partial rebuild を起動 |
| **Luxel.TwoD/Retained** (`UiNode` SoA, dirty flag) | `RetainedCanvasAnimationTarget` が `UiNode.Transform`/`UiNode.Color` を書く → 既存の部分更新 (segment 不変、transform/style slot のみ書込) がそのまま走る |
| **Luxel.ThreeD** (`World`, `LocalTransform`/`GlobalTransform`) | `EcsAnimationTarget` が `world.Set(entity, new LocalTransform(...))` を呼ぶ → `TransformPropagateSystem` が GlobalTransform を再計算 → `Render3DExtractor` が bindless buffer 書込 |
| **Luxel.Resources** ((型, uri) 自動コンポーズ) | `glTF` → `AnimationClip` の Importer を `IResourceStep` として登録、`Load<AnimationClip>("scene.gltf#anim0")` で読込 |
| **Luxel.RenderGraph** (scene-agnostic, IRenderExtractor) | RG とは独立 (animation は描画前の値書込フェーズ)。3D Extractor の Extract 時点ですでにアニメ適用済みの GlobalTransform が読まれる |
| **UiHost.Tick(dt)** (既存) | グローバル時間進行に乗せる: `AnimationPlayer.TickAll(dt)` を `UiHost.BeforeFrame` に追加 (#2 設計決定: 完全 frame-driven) |
| **Luxel.DevTools** | `EngineDiagnostics.Emit("Luxel.Animation", DiagAnimation)` で再生中の TrackEntry 一覧を発行、`GET /animation` パネル。Importer 警告は `EngineDiagnostics.Emit("Luxel.Animation.Import", DiagImportWarning)` で `/animation/warnings` パネルに集約 (#6 設計決定) |
| **GPU 駆動システム** (上層) | パーティクル/シェーダエフェクト等が独自に `IAnimationTarget` を実装し、bindless GPU buffer に直接書込み (#3 設計決定: Animation 本体は CPU 駆動、上から拡張) |

## 7. ターゲット抽象を統一すべきか — 根拠付き結論

**結論: 統一しない (IR は共通、ターゲットアダプタは 3 種に分離)。**

| 根拠 | 引用 |
|---|---|
| Unity (property path string), Bevy (per-entity AnimationPlayer + ECS), Flutter (Animation<T> → Widget rebuild), Web (KeyframeEffect on Element) **すべてが書き込みアダプタを分離している** | [Unity Playables](https://docs.unity3d.com/6000.4/Documentation/Manual/Playables.html), [Bevy AnimationGraph](https://docs.rs/bevy/latest/bevy/prelude/struct.AnimationGraph.html), [Flutter](https://docs.flutter.dev/ui/animations/overview) |
| 共通化すると「最小公倍数」になり、signals の reactive 起動、RetainedCanvas の SoA + dirty flag、ECS の component query といった各 domain の最適化が壊れる | 既存 Luxel 設計 |
| 値型ごとのデフォルト interpolation policy も domain で異なる (UI の spring vs 3D の slerp) → アダプタが値型ごとのデフォルトを提供 | [Motion](https://motion.dev/docs/react), [glTF spec](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) |

ただし**最小の共通 IF** (`IAnimationTarget.Apply(path, value)`) は持たせる — Importer から見た「書く先」を抽象化するため。

## 8. マイルストーン

| 段 | 名称 | スコープ | サンプル |
|---|---|---|---|
| **AN-M1** | コア型 (Animatable / Tween / Curve) + UI Adapter | `ICurve`/`ITween<T>`/`Animatable<T>`、`LinearCurve`/`CubicBezierCurve`/`StepCurve`、`FloatTween`/`Vector2Tween`/`Vector3Tween`/`QuaternionTween` (slerp)/`ColorTween`、`SignalAnimationTarget`、`UiHost.BeforeFrame` で tick | **31**: UI signal の opacity/scale アニメ (`Luxel.Controls` の Button に hover/click アニメ追加) |
| **AN-M2** | コード DSL Tween + Sequence/Parallel + 再生制御 | fluent `Animate.Tween(...).WithCurve(...).OnComplete(...)`, `Animate.Sequence(...)`, `Animate.Parallel(...)`, `Play()`/`Pause()`/`Seek()`/`Reverse()`/`Loop`、TrackEntry 再生制御 | **32**: Luxel.UI でメニュー開閉アニメ (slide-in + fade-in を Sequence) |
| **AN-M3** | AnimationClip + glTF Importer + ECS Player | `AnimationClip`/`Track`/`Keyframe`、glTF Importer (`Luxel.Resources` の `IResourceStep` 経由), `EcsAnimationTarget`, `AnimationPlayer` ECS component, `AnimationPlayerSystem.Tick(world, dt)` | **33**: glTF からキューブの回転アニメをロードし ECS で再生 (vk/dx 一致) |
| **AN-M4** | RetainedCanvas 連携 + 2D アニメ | `RetainedCanvasAnimationTarget`、`UiNode.Transform`/`Color` への書込みが既存の部分更新 (transform/style slot のみ) を起動 | **34**: 2D シーンの slide-in + bounce、partial update を確認 |
| **AN-M5** | AnimationGraph (Clip/Blend/Add DAG) | Bevy 風 DAG ノード、`PoseBuffer` 中間表現、`BlendNode`/`AddNode`、weight の階層伝播 | **35**: 2 つの 3D モーション (e.g. idle / wave) を weight でブレンド |
| **AN-M6 (任意)** | 個別 Importer + StateMachine | (a) CSS `@keyframes` パーサ → L2 直接 (b) Lottie **subset** Importer (transform/color/opacity) (c) StateMachine (states + transitions) ─ **GPU 駆動と skinning/morph は別 RFC へ分離** (#3/#5 設計決定) | 個別サンプル |
| **将来 RFC** | `MESH_PLAN.md` (skinned mesh + morph target), `PARTICLES_PLAN.md` (GPU 駆動パーティクル) | Animation の上から `IAnimationTarget` を独自実装して GPU buffer 書込み | 別文書で計画 |

**各段で vk/dx ピクセル一致** + テスト追加 (Luxel の慣習)。

## 9. 検証戦略

- **単体テスト** (`tests/Luxel.Tests/AnimationTests.cs`):
  - `Curve.Eval(t)` のスナップショット (linear / cubic-bezier(0.42, 0, 0.58, 1) / steps / spring)
  - `Tween<T>.Lerp(0.5)` の中間値 (float / Vector3 / Quaternion slerp / Color)
  - `Animatable<T>.Evaluate(t)` の curve × tween の合成
  - `TrackEntry` の time/loop/timeScale/mixDuration 進行
  - `AnimationPlayer.Tick` の複数 TrackEntry 同時処理
  - glTF Importer: keyframe 配列の正しい変換 (`accessor` → `Keyframe[]`)
- **GPU 統合**: サンプル 31-35 が vk/dx 一致 (Luxel の慣習通り)
- **DevTools 統合**: `/animation` で再生中 TrackEntry の JSON 配信、HttpClient テストで検証 (RG-M3 と同様)
- **性能計測**: 大量 (1000+) instance の同時アニメで CPU/GPU 時間を `DiagAnimation` 計測

## 10. Luxel.Animation プロジェクト構成

新規プロジェクト `src/Luxel.Animation` (net9.0, Luxel core のみ参照):

```
src/Luxel.Animation/
├── Luxel.Animation.csproj
├── Curves/                # ICurve 実装 (Linear/CubicBezier/Steps/Spring)
├── Tweens/                # ITween<T> 実装 (Float/Vector*/Quaternion/Color/Affine)
├── Animatable.cs          # Animatable<T> + chain
├── AnimationClip.cs       # L1: pure data
├── Track.cs / Keyframe.cs # L2: stateless
├── AnimationPlayer.cs     # L3: stateful
├── TrackEntry.cs
├── Targets/               # 3 アダプタ
│   ├── IAnimationTarget.cs
│   ├── SignalAnimationTarget.cs  (Luxel.UI 参照)
│   ├── RetainedCanvasAnimationTarget.cs  (Luxel.TwoD 参照)
│   └── EcsAnimationTarget.cs  (Luxel.ThreeD 参照)
├── Dsl/                   # コード DSL
│   ├── Animate.cs         # fluent API
│   ├── Sequence.cs
│   └── Parallel.cs
├── Graph/                 # AnimationGraph (AN-M5)
│   ├── AnimationGraph.cs
│   ├── ClipNode.cs / BlendNode.cs / AddNode.cs
│   └── PoseBuffer.cs
└── Importers/             # 各形式 → AnimationClip
    └── Gltf/              # AN-M3
        └── GltfAnimationImporter.cs
```

参照方向:
- `Luxel.Animation` (core 部分) → `Luxel` のみ
- `Luxel.Animation.UI` (Adapter) → `Luxel.UI`, `Luxel.Animation`
- `Luxel.Animation.TwoD` → `Luxel.TwoD`, `Luxel.Animation`
- `Luxel.Animation.ThreeD` → `Luxel.ThreeD`, `Luxel.Animation`

アダプタを別アセンブリにすることで、core 部分は domain-free (scene-agnostic 哲学を維持)。

## 11. 設計決定 (Resolved)

Open Questions を 1 件ずつレビューし、以下に決定 (2026-06-30):

### #1 PropertyPath の表現 → **ハイブリッド**
Importer (glTF / CSS / Lottie subset) は**文字列パス**、コード DSL は**ラムダ / Signal 直接捕捉**、内部表現は**型付きハンドル** (`PropertyPath(AdapterScope, Path, ValueType)`)。
- Importer (テキストフォーマット) → 文字列で素直に取り込み
- コード DSL → ラムダで型安全
- 内部 → 型付きハンドルに統一して最適化
- 業界例: Unity (path)、DOTween (lambda)、Bevy (`AnimationTargetId` hash) のいいとこ取り

### #2 AnimationGraph と Signal の統合 → **完全 frame-driven** (Bevy 流)
`UiHost.BeforeFrame` で全 `AnimationPlayer.Tick(dt)` → 値計算 → アダプタが Signal/RetainedCanvas/ECS に書込み → 既存 reactive 機構が自動起動。
- 業界 4 実装 (Bevy/Unity/Unreal/Flutter) が frame-driven で収束
- Luxel 既存 `UiHost.Tick(dt)` (Spinner/Accordion) と整合
- アニメ未実行時は `_players.Count == 0` で早期 return (実質ゼロコスト)

### #3 GPU 駆動アニメ → **別 RFC に分離 + Target 拡張点で上から消費可能**
Animation 本体は CPU 駆動 (3 アダプタ: Signal/RetainedCanvas/ECS) で完結。GPU 駆動が必要な領域 (skinned mesh / morph target / パーティクル / シェーダエフェクト) は **`IAnimationTarget` を上から独自実装**し、GPU buffer に直接転送して消費する設計。
- 例: `ParticleAnimationTarget` が `Apply(path, value)` を実装し、bindless GPU buffer に書き込む
- Animation システム本体は `ParticleSystem` を一切知らない (上から拡張)
- skinned mesh / morph target は `MESH_PLAN.md` (将来 RFC) として独立
- 業界も Unity (Animator vs DOTS)、Bevy (animation vs particles) で分離

### #4 Lottie / Rive → **拡張点 + 最小 subset**
本体に **`IAnimationImporter` 拡張点** + 公式 subset Importer (transform/color/opacity のみ)。完全 runtime は外部プラグインに譲る。
- Luxel.TwoD の独自 Vello 風ラスタライザに完全互換は技術的に割が合わない
- deep-research で Lottie/Rive 詳細 (mask/matte/expression/Data Binding) は **0-3 棄却** ─ 仕様検証不十分領域は外に逃がす
- `IAnimationImporter` 拡張点は 3D 側の FBX 等も将来同じ仕組みでサポート可能

### #5 glTF skinned mesh / morph target → **transform only, 別 RFC**
AN-M3 は glTF の `channel.target.path = translation/rotation/scale` のみ対応。`weights` (morph target) と skinning (joints/inverseBindMatrices) は `MESH_PLAN.md` (将来 RFC) として分離。
- transform アニメ単体で実用度高 (キャラ位置/回転、ライト、カメラ、UI 要素の TRS、シーン階層アニメ)
- skinning / morph target は本質的に **mesh format + shader 拡張**で、Animation IR の話とは別レイヤ
- #3 (GPU 駆動別 RFC) と完全整合

### #6 Importer エラー処理 → **Warn + Configurable** (Severity 切替)
デフォルト Warn (未サポート要素は無視、`EngineDiagnostics` で警告イベント発火)、`ImportOptions.Severity = Strict / Warn / Lenient` で切替可能、fatal (JSON 構文不正等) は**常に throw**。
- 警告は `/animation/warnings` DevTools パネルで集約観測 (RG-M3 と同パターン)
- 業界も lenient + warn が主流 (Unity AssetImporter, three.js GLTFLoader, Bevy gltf)

```csharp
public sealed record ImportOptions(
    ImportSeverity Severity = ImportSeverity.Warn,
    bool ThrowOnFatal = true);

public enum ImportSeverity { Strict, Warn, Lenient }
```

### 設計決定の集約

```
入力              内部表現               実行            ターゲット
──────────────    ──────────────────    ─────────    ─────────────────
glTF (path str)    PropertyPath(typed   UiHost.Tick    Signal / Retained
CSS  (path str) →  Adapter+Path+Type) → bottom-up    → Canvas / ECS
Lottie subset      AnimationGraph       全評価         + 上から拡張
コード DSL (λ) →   AnimationPlayer                      (Particle 等)
                   TrackEntry
                   (Warn デフォルト)
```

## 12. ボツになった検討事項 (Refuted)

deep-research の 3 票 adversarial verification で**棄却された主張** (詳細を本プランから除外):

- Spine `MixBlend` enumeration の具体値 (Replace/First/Add 等の網羅)
- Rive State Machine の state 構造詳細 (Exit プレースホルダ等)
- Rive Data Binding の reactive (pull) モデル詳細
- Spine の numbered tracks + alpha layering の合成セマンティクス

→ これらのフォーマットは「独立 sub-system として bundling」が安全 (専用 Player 経由)。

## 13. 参考文献

- glTF 2.0 spec — https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
- Bevy AnimationGraph — https://docs.rs/bevy/latest/bevy/prelude/struct.AnimationGraph.html
- Bevy RFC 51 (Animation Composition) — https://github.com/bevyengine/rfcs/blob/main/rfcs/51-animation-composition.md
- Unreal AnimGraph — https://dev.epicgames.com/documentation/unreal-engine/animgraph
- Unity Playables — https://docs.unity3d.com/6000.4/Documentation/Manual/Playables.html
- Spine Applying Animations — https://esotericsoftware.com/spine-applying-animations
- Spine AnimationState.cs — https://github.com/EsotericSoftware/spine-runtimes/blob/4.1/spine-csharp/src/AnimationState.cs
- Rive State Machines — https://help.rive.app/runtimes/state-machines
- Flutter Animations Overview — https://docs.flutter.dev/ui/animations/overview
- Motion (React) — https://motion.dev/docs/react
- Web Animations Module Level 2 — https://www.w3.org/TR/web-animations-2/
- CSS Animations Level 1 — https://www.w3.org/TR/css-animations-1/

## 14. 次のアクション

1. 本 RFC のレビュー (Open Questions は全て解決済み)
2. `src/Luxel.Animation/` プロジェクト雛形作成 (`net9.0`, `Luxel` core のみ参照)
3. **AN-M1 着手**: `ICurve` / `ITween<T>` / `Animatable<T>` の最小実装 + `SignalAnimationTarget` + サンプル 31 (UI の opacity アニメ)

## 15. 設計決定サマリ (要点 6 件)

| # | 質問 | 決定 |
|---|---|---|
| 1 | PropertyPath の表現 | **ハイブリッド** (Importer=文字列, DSL=ラムダ, 内部=型付きハンドル) |
| 2 | 実行モデル | **完全 frame-driven** (Bevy 流, `UiHost.Tick` で全評価) |
| 3 | GPU 駆動アニメ | **別 RFC + Target 拡張点で上から消費** (Animation 本体は CPU 駆動) |
| 4 | Lottie/Rive | **拡張点 + 最小 subset** (`IAnimationImporter` + transform/color/opacity のみ本体提供) |
| 5 | glTF skin/morph | **transform only, 別 RFC** (`MESH_PLAN.md` で skinning/morph を扱う) |
| 6 | Importer エラー処理 | **Warn デフォルト + Configurable** (Strict/Warn/Lenient 切替, fatal は常に throw) |
