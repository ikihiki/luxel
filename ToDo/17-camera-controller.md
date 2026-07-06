# 17 — カメラコントローラ (追従・シェイク・境界クランプ)

## 概要

「ゲームフィール」の中核であるカメラの高レベル機能を追加する: ターゲット追従 (デッドゾーン + スムージング)、画面シェイク、ワールド境界クランプ、ズームのスムーズ遷移。低レベルの Camera2D (ズーム/パン affine) は実装済みで、その上のコントローラ層が無い。

## 背景と現状

- **Camera2D** (src/Luxel.TwoD/Camera2D.cs): `Camera2D.Create(scale, worldCenter, screenW, screenH)` → 2x3 affine。スムーズズームは M4 で対応済み (ズームの数学は信頼できる)。
- **3D カメラ**: RenderGraph サンプルで viewProj 手書き。KnockdownStory に軌道カメラ (ドラッグ) の実装例。glTF カメラ import (AssetCamera: YFov/Aspect/ZNear/ZFar) あり。
- **接続先**: 2D ゲームは RetainedCanvas/Scene2D の camera 引数、3D は RenderGraph に渡す viewProj。コントローラは「毎フレーム Camera2D (または viewProj) を計算して返す純ロジック」にすれば両方に挿せる。
- **実行タイミング**: 追従はターゲットが動いた**後** = LateUpdate フェーズが定位置 (Phase.LateUpdate は実装済み)。

## 実装方針

### 1. CameraRig2D (純ロジック、Luxel.TwoD)

```csharp
public sealed class CameraRig2D
{
    public float Zoom;                     // 目標値。実効値はスムーズ追従
    public (float X, float Y) Target;      // 追従対象 (毎フレーム外から与える)
    public RectF? WorldBounds;             // クランプ範囲 (null = 無制限)
    public (float W, float H) Deadzone;    // 中央デッドゾーン (この中の移動はカメラ不動)
    public float Smoothing;                // 追従の時定数 (指数平滑; 0 = 即時)
    public void Shake(float amplitude, float duration, ulong seed);   // 加算・減衰
    public void Update(float dt);          // ← FixedUpdate でも可変でも動く純関数的更新
    public Camera2D Camera(float screenW, float screenH);   // 現在状態 → affine
}
```

- **追従**: ターゲットがデッドゾーン矩形の外に出た分だけカメラ中心を移動 → 指数平滑 `pos += (goal - pos) * (1 - exp(-dt / tau))` (フレームレート非依存の平滑 — `* 0.1f` 方式は dt 依存なので不可)。
- **シェイク**: 固定シード xorshift のノイズ (毎 Update で 2 値サンプル) × 振幅 × 残り時間の減衰カーブ。**Random/wall-clock 禁止** (golden 決定性)。複数 Shake の重ね掛けは振幅合成。
- **境界クランプ**: ビューポートの世界サイズ (screen/zoom) を考慮して「画面端が WorldBounds を出ない」クランプ。ワールドが画面より小さい軸は中央固定。
- **ズーム**: 目標値への指数平滑 (既存スムーズズームの意味論と揃える)。

### 2. 3D への最小対応

- v1 は 2D を主戦場に。3D は `OrbitCamera` (Knockdown のドラッグ軌道カメラを部品化: yaw/pitch/distance/target → viewProj) の抽出だけ行い、follow/shake の 3D 版はスコープ外。

### 3. テスト + デモ + Docs

- 単体テスト (GPU 不要・全て決定的): デッドゾーン内で不動 / 外に出た分だけ goal が動く / 指数平滑が dt 分割に対して安定 (dt=0.1 一発 ≈ 0.05×2、許容誤差内) / クランプ (境界・ワールド極小) / シェイクが duration 後に厳密に 0 へ戻る / 同シードで同軌跡。
- デモストーリー「Demos/TwoD/CameraRig」: 矢印キーで動くプレイヤー矩形 + タイル状背景、追従 + 境界 + Shake ボタン。play: Key 移動 → Step → Snap (デッドゾーン内は背景不動、外は追従の 2 枚) → Click Shake → Step(固定) → Snap。
- Docs/TwoD (または Docs/Framework) にカメラ節を追加。

## 罠・注意

- **BreakoutStory/KnockdownStory 等の既存デモの golden を壊さない** — 既存デモへの組み込みは任意 (やるなら golden 更新を意図分として)。
- シェイクは位置だけでなく回転を混ぜると効果的だが、Camera2D affine が回転をサポートしているか確認 (affine 2x3 なので数学上は可能 — Create API に回転が無ければ Camera2D 側に小さな追加)。
- Update を FixedUpdate ([14](14-framework-fixedupdate.md)) に載せる場合は補間対象になる — Rig 自体は「どちらでも正しい」純ロジックに保つ (dt を貰って進めるだけ、フレームレート非依存)。

## スコープ外

- 3D の follow/shake、複数カメラのブレンド/カット、シネマティック (スプライン移動)、Cinemachine 的な優先度システム。
