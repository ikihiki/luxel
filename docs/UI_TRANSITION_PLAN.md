# Luxel.UI Transitions 設計プラン (RFC)

**ステータス:** Draft (2026-06-30)
**前提:** AN-M1〜M6 完了済 (Animation system 全体)、`Luxel.UI` の signals + reactive、`Luxel.Animation` の `Curve` / `Tween` / `AnimationPlayer` / `IClock`。
**関連:** [docs/ANIMATION_PLAN.md](ANIMATION_PLAN.md)

---

## 1. 動機

CSS の `transition` 相当の機能を UI に組み込みたい:

```css
.button {
  background: blue;
  transform: scale(1);
  transition: background 0.3s ease, transform 0.15s ease-out;
}
.button:hover {
  background: red;
  transform: scale(1.05);
}
```

→ プロパティ値を**変えるだけで**、古い値から新しい値へ自動的に補間される。

Luxel での同等表現を**既存基盤の組み合わせ**で実現する:
- 既存: `Signal<T>` リアクティブ値 (Luxel.UI) + `ReactiveEffect` 依存追跡 + `AnimationPlayer.Update(IClock)` (AN-M1) + `Curves` (AN-M1)
- 新規: 「値変化を検知して `Animate.Tween(old, new, dur, curve)` を自動投入する」**ラッパー**

これは **AN-M6** で実装した `Animate.Sequence/Parallel/Clip` (明示的アニメ) や `StateMachine` (状態切替) とは性格が違う:

| モード | 起動 | 用途 |
|---|---|---|
| **CSS animation** (AN-M6a) | 明示的 `@keyframes` + 開始呼び出し | scripted motion |
| **State machine** (AN-M6b) | Trigger 名で状態切替 | discrete state transitions |
| **CSS transition** (本プラン) | **プロパティ変化を検知**して自動補間 | implicit interpolation |

## 2. 設計の核心結論

**「Setter ラッパー」が最小実装**:

```csharp
Action<T> animated = Transition.Animate<T>(
    realSetter: v => node.Color = v,    // 真の書込先 (UiNode の Color など)
    player, clock,
    duration: 0.3f, curve: CubicBezierCurve.EaseInOut);

animated(Color2D.Red);   // 初回は即時 Apply
animated(Color2D.Blue);  // 0.3s で Red→Blue を補間 (TweenCommand を自動 Play)
```

Signal と組み合わせるには `ReactiveEffect`:

```csharp
var hovered = new Signal<bool>(false);
var animatedColor = Transition.Animate<uint>(v => node.Color = v, player, clock, 0.3f, ease);
using var e = new ReactiveEffect(() =>
    animatedColor(hovered.Value ? Color2D.Red : Color2D.Blue));
```

これだけで「`hovered = true` にすると 0.3s で Red→Blue」の transition が成立する。

### 3 つの設計選択 (比較)

| 案 | 内容 | 利点 | 欠点 |
|---|---|---|---|
| **A. Setter ラッパー** | `Transition.Animate(setter, ...)` が新 `Action<T>` を返す。setter を呼ぶごとに前回値からの補間を起動 | 既存 Signal/Widget を変えない / 任意の setter (UiNode 直接書込み等) に適用可 / scope agnostic | Signal と組み合わせるには `ReactiveEffect` を明示的に書く |
| **B. Signal ラッパー** | `TransitionedSignal<T>(source, dur, curve)` を作り、`.Value = x` で自動補間 | Signal の使い心地そのまま (`sig.Value = x`) | 既存 `Signal<T>` API と別型 / Computed と組み合わせる際の表現が増える |
| **C. Widget モディファイア** | `.WithTransition("opacity", 0.3f, ease)` で Widget の特定プロパティを自動補間 (SwiftUI 流) | 宣言的 / Flutter `AnimatedContainer` 風 | Widget 側に property name → setter のマッピングが必要 / 拡張が limited |

**結論: 案 A をベース、案 B / C は薄い砂糖として上に積む**。
- 案 A は scene-agnostic で、Signal/UiNode/任意の setter に等しく適用可能
- 案 B / C は便利な糖衣として後付け

これは AN-M3 の **scene-agnostic IR + ターゲット分離**の哲学と整合します。

## 3. API スケッチ

### TR-M1: Setter ラッパー (核心)

```csharp
namespace Luxel.Animation;

public static class Transition
{
    /// <summary>
    /// Setter をラップし、値変化時に古い値から新しい値への補間を自動再生する。
    /// 初回 (値が未設定) は即時 Apply、以降は前回値からの Tween を Player に Play。
    /// 進行中の Tween は新値で interrupt し、現在の値を始点として新たに開始 (smooth interrupt)。
    /// </summary>
    public static Action<T> Animate<T>(
        Action<T> setter,
        AnimationPlayer player,
        IClock clock,
        float duration,
        ICurve? curve = null,
        float delay = 0f);

    /// <summary>同様だが Signal を直接受け取る糖衣 (案 B 的)。内部で ReactiveEffect を作って依存追跡。</summary>
    public static IDisposable Watch<T>(
        Signal<T> source,
        Action<T> animatedSetter);   // = `new ReactiveEffect(() => animatedSetter(source.Value))`
}
```

**Smooth interrupt の動作**:
- `animated(A)` → setter(A) 即時
- `animated(B)` → Tween A→B が走る
- 途中で `animated(C)` → 現在の補間値 X を始点として X→C を新たに開始
  - 古い TrackEntry は `Finish()` でなく `Stop()` (OnComplete は発火しない)
  - 新しい TrackEntry が現在値からスタート → 視覚的に瞬断しない

### TR-M2: Signal ラッパー (案 B、糖衣)

```csharp
namespace Luxel.UI;

/// <summary>Signal の API を保ちつつ、値変化を自動補間で反映する。</summary>
public sealed class TransitionedSignal<T> : ISignalSource
{
    public TransitionedSignal(T initial, AnimationPlayer player, IClock clock,
                               float duration, ICurve? curve = null);

    /// <summary>Set すると古い表示値から新値へ補間が始まる。Get は補間後の **表示値**。</summary>
    public T Value { get; set; }

    /// <summary>真の目標値 (即時)。</summary>
    public T TargetValue { get; }
}
```

利用例:
```csharp
var bg = new TransitionedSignal<uint>(Color2D.Blue, player, clock,
    duration: 0.3f, curve: CubicBezierCurve.EaseInOut);
bg.Value = Color2D.Red;     // 自動で 0.3s 補間
// 内部: 値が変わるたびに Transition.Animate(setter) 経由で AnimationPlayer に Play
```

### TR-M3: Widget モディファイア (案 C、糖衣)

```csharp
// Luxel.UI.Widget に拡張 (Luxel.Animation.UI に置く)
Button("OK", onClick: ...)
    .WithColors(Background: Color2D.Blue)
    .Transition("background", duration: 0.3f, curve: CubicBezierCurve.EaseInOut)
    .Transition("scale",      duration: 0.15f, curve: CubicBezierCurve.EaseOut);
```

内部: Widget が `Realize` 時に該当 signal を `TransitionedSignal` でラップ、または該当 setter に `Transition.Animate` を被せる。

### TR-M4 (任意): CSS `transition:` 構文サポート

```csharp
// "background 0.3s ease, transform 0.15s ease-out" をパース → 各 property に対する Transition を構成
var spec = CssTransitionSpec.Parse("background 0.3s ease, transform 0.15s ease-out");
widget.ApplyTransitionSpec(spec);
```

## 4. 既存基盤との結線

| 既存 | 結線方法 |
|---|---|
| `Signal<T>` (Luxel.UI) | `Transition.Watch(signal, animatedSetter)` で値変化を観測、ReactiveEffect が依存追跡 |
| `ReactiveEffect` | 案 A の `Watch` ヘルパー内部で生成、Dispose で購読解除 |
| `AnimationPlayer.Update(clock)` (AN-M1) | フレーム駆動はユーザー責任で既存通り。`Transition.Animate` が返す setter はその player に Play する |
| `IClock` (FixedFrameClock/WallClock) | Transition のスタート時刻 = `clock.TimeSec` |
| `Curves` (AN-M1) | `curve` 引数で渡す。デフォルトは `LinearCurve` (ただし UI 用途では `CubicBezierCurve.EaseInOut` を推奨) |
| `Tween` (AN-M1) | 内部で型ごとに自動選択 (float→FloatTween, Vector2→Vector2Tween, uint→RgbaTween 等)。type-dispatch は AN-M3 の `TrackValue` と同パターン |
| `UiNode` (Luxel.TwoD/Retained) | Widget 側で `node.Color =`/`node.Transform =`/`node.Opacity =` を直接 setter に渡す or `RetainedCanvasAnimationTarget.Bind` 経由 |
| `Widget` (Luxel.UI) | TR-M3 でモディファイア追加 |

## 5. Smooth Interrupt の詳細仕様

連続変化 (`A → B` 進行中に `C` への変更) の挙動:

```
時刻 t0: animated(A) → setter(A) を即時呼出 (初回)
時刻 t1: animated(B) → Tween(A, B) を Play、TrackEntry1 起動
時刻 t1 + 0.1s: animated(C)
   現在の表示値 = X (= A から t1 までの補間結果)
   - 既存 TrackEntry1 を Stop() (OnComplete 非発火、Player から除外)
   - 新たに Tween(X, C) を Play、TrackEntry2 起動 (X = 現在値からスタート)
   - duration は新たに動かす残り時間 (デフォルト = 元の `duration` をフル)
```

CSS の動作は実装によりばらつくが、**「X から C へフル duration かけて」** が最も自然 (Framer Motion / React Spring の "fromCurrent" モード)。

代替: 「残り時間 = duration × (1 - 経過比率)」 = カットオフ。これは値の進み方が一定でない。フルで進める方が予測しやすい。

## 6. マイルストーン

| 段 | 名称 | スコープ | サンプル |
|---|---|---|---|
| **TR-M1** | Setter ラッパー (核心) | `Transition.Animate<T>(setter, player, clock, dur, curve)` / `Transition.Watch(signal, animated)` / 型ごとの自動 Tween 選択 / smooth interrupt | **38**: hover 時に色と scale が補間される Button (Signal を Watch) |
| **TR-M2** | Signal ラッパー (糖衣) | `TransitionedSignal<T>(initial, player, clock, dur, curve)` / `.Value` set で内部的に Animate / `TargetValue` で即時値も読める | **39**: TransitionedSignal で複数プロパティを宣言、`.Value =` だけで動かす |
| **TR-M3** | Widget モディファイア | `widget.WithTransition(property, dur, curve)` 拡張、Widget が Realize 時に該当 signal を自動 transition 化 | **40**: Luxel.Controls の Button に `.WithTransition` を追加し、hover/press のアニメを宣言的に |
| **TR-M4 (任意)** | CSS `transition:` 構文パーサ | `CssTransitionSpec.Parse("a 0.3s ease, b 0.2s ease-out")` を AN-M6a と同じ流儀で実装 | サンプル既存への追加 |

各段で vk/dx ピクセル一致 + テスト追加 (Luxel 慣習通り)。

## 7. 検証戦略

- **単体テスト** (`tests/Luxel.Tests/TransitionTests.cs`):
  - 初回 Set は即時 Apply
  - 2 回目以降は補間 (t=Duration 後に終端値)
  - Smooth interrupt: 進行中に新値 → 現在値から新値へ
  - 型ごとに自動 Tween: float/Vector2/Vector3/Quaternion(slerp)/uint(RgbaTween)
  - `Transition.Watch(signal, ...)` が ReactiveEffect で依存追跡し、Dispose で停止
- **GPU 統合**: サンプル 38-40 が vk/dx 完全一致 (CPU 駆動なので backend 非依存)
- **DevTools 統合** (任意): `EngineDiagnostics.Emit("Luxel.UI.Transition", DiagTransition)` で active transition 一覧を発火

## 8. 設計決定 (Resolved)

Open Questions を 1 件ずつレビューし、以下に決定 (2026-06-30):

### #1 配置 → **3 層に分散** (Transition 本体は Animation core、Signal/添付プロパティは Animation.UI、UI 本体は無変更)
- `Luxel.Animation/Transition.cs` ─ `Transition.Animate<T>(setter, player, clock, dur, curve, delay)` scene-agnostic 核心
- `Luxel.Animation.UI/SignalTransition.cs` ─ `Transition.Watch(Signal<T>, ...)` reactive 連携
- `Luxel.Animation.UI/PTransition.cs` ─ `P.Transition.*` 添付プロパティ宣言 (`extension(PRoot)` で UI 本体無変更で拡張、Luxel.Controls と同パターン)
- **Luxel.UI 本体は一切変更しない**

### #2 Smooth interrupt → **フル duration、現在値起点** (Framer Motion 流)
- 進行中 entry を `Stop()` で凍結 (OnComplete 非発火)
- 新 entry を現在値からフル duration で再開
- 連続変更でも一貫した速度感

### #3 delay → **TR-M1 から組込み** (CSS spec 互換)
- `Transition.Animate(setter, player, clock, duration, curve, delay = 0f)`
- `P.Transition.Color(0.3f, ease, delay: 0.1f)` で staggered animation 可

### #4 複数プロパティ → **別 setter で分割** (Grid.Column と同じ流儀)
- `P.Transition.Color(...)` + `P.Transition.Scale(...)` を別個に AttachedPart として宣言
- Widget Realize で各 part を見て独立した `Transition.Animate` ラッパーを生成
- 同じ `AnimationPlayer` に複数 TrackEntry が並列 (AN-M2 Parallel と同じ哲学)

### #5 TransitionedSignal → **後回し** (マイルストーンから削除)
- 添付プロパティ + `Transition.Animate(setter)` で 90% カバー
- 必要になったら後付け (破壊変更なし)
- TR-M2 (TransitionedSignal) をマイルストーンから外す

### #6 デフォルト curve → **`CubicBezierCurve.EaseInOut`** (モダン UI 体験)
- CSS spec の `ease` ではなく Framer Motion / SwiftUI のモダンなデフォルト
- CSS パーサ (TR-M3) では `ease`/`linear`/`ease-in`/`ease-out`/`ease-in-out` キーワードを明示的にマップ

### #7 キャンセル時 → **現在値で凍結** (`Stop` 流, AN-M1 と整合)
- `player.Stop(entry)` / `ReactiveEffect.Dispose` で進行中 entry を除外、setter は呼ばれなくなる
- 表示値は最後に書かれた値で固定
- 終端値が必要なら明示的に setter(targetValue) を呼ぶ

### #8 Reduce Motion → **将来 RFC** (本プランから外す)
- OS 設定読み出しは Luxel.Platform 等の上位レイヤ範疇
- 必要時に別 RFC として議論

### 設計決定サマリ

```
入力                内部                   実行                   ターゲット
─────────────────  ──────────────────    ──────────────────    ─────────────
animated(newValue) → Transition.Animate → AnimationPlayer +     → setter
                     wrap (scene-agnostic) TrackEntry (per prop)   (Signal/Node/ECS)
P.Transition.*    →  AttachedPart       → Widget Realize で 
attached property    in Luxel.UI         上記 Animate を生成
                     (extension method)
```

## 9. 改訂マイルストーン (Open Questions 反映後)

| 段 | 名称 | スコープ | サンプル |
|---|---|---|---|
| **TR-M1** | Setter ラッパー (scene-agnostic 核心) | `Luxel.Animation/Transition.cs`: `Transition.Animate<T>(setter, player, clock, dur, curve, delay)` / smooth interrupt (フル duration) / 型ごとの自動 Tween / デフォルト curve = EaseInOut + `Transition.Watch(Signal<T>, animated)` (Animation.UI) | **38**: Signal を `Watch` で結線、hover で色 + scale が補間される簡素デモ |
| **TR-M2** | 添付プロパティ + Widget Realize 連携 | `Luxel.Animation.UI/PTransition.cs`: `extension(PRoot) { public TransitionDecl Transition { get; } }` / `P.Transition.Color(dur, curve, delay)` / `Translation` / `Scale` / `Opacity` / Widget Realize で AttachedPart を読んで該当 setter を `Transition.Animate` で wrap | **39**: Luxel.Controls の Button に `P.Transition.Color`/`P.Transition.Scale` を添付、hover/press で宣言的アニメ |
| **TR-M3 (任意)** | CSS `transition:` 構文パーサ | `CssTransitionSpec.Parse("a 0.3s ease, b 0.2s ease-out 0.1s")` ─ AN-M6a と同じ流儀、`ease`/`linear`/`ease-in`/`ease-out` キーワード対応 | サンプル既存への追加 |
| **(削除)** | ~~TR-M2 TransitionedSignal~~ | #5 で後回し | ─ |
| **(将来 RFC)** | Reduce Motion | #8 で本プラン外 | ─ |

## 10. 設計決定要約

| # | 質問 | 決定 |
|---|---|---|
| 1 | 配置 | **3 層** ─ Animation core / Animation.UI / UI 本体 無変更 |
| 2 | Smooth interrupt | **フル duration、現在値起点** (Framer Motion 流) |
| 3 | delay | **TR-M1 から** (CSS spec 互換) |
| 4 | 複数プロパティ | **別 setter で分割** (Grid.Column 流) |
| 5 | TransitionedSignal | **後回し** (マイルストーンから削除) |
| 6 | デフォルト curve | **CubicBezierCurve.EaseInOut** (モダン UI) |
| 7 | キャンセル時 | **Stop で凍結** (AN-M1 整合) |
| 8 | Reduce Motion | **将来 RFC** (本プラン外) |

## 9. プロジェクト配置

新規ファイルは **`src/Luxel.Animation.UI/`** (既存) に追加:

```
src/Luxel.Animation.UI/
├── Luxel.Animation.UI.csproj
├── SignalAnimationTarget.cs   (既存)
├── AnimationUiBridge.cs        (既存)
├── Transition.cs               (新規, TR-M1)
├── TransitionedSignal.cs       (新規, TR-M2)
└── WidgetTransitions.cs        (新規, TR-M3)
```

参照方向は既存と同じ: `Luxel.UI + Luxel.Animation`。`Luxel.Animation` core は domain-free を維持。

## 10. 設計上の不変量

- **scene-agnostic 維持**: `Transition.Animate(setter, ...)` の setter は `Action<T>` のみ要求。`Signal<T>` / `UiNode.Color = ` / 任意のターゲットに使える
- **既存基盤の薄い積み上げ**: `Curve` / `Tween` / `AnimationPlayer` / `IClock` の組合せで実装。新概念は最小限
- **frame-driven**: AnimationPlayer の Update 経由で進行。`Transition` 自体は別 scheduler を持たない (#2 設計決定との整合)
- **scene 横断**: 2D `UiNode` でも 3D ECS でも同じ `Transition.Animate(setter)` が使える

## 11. 参考文献

- CSS Transitions Level 1 — https://www.w3.org/TR/css-transitions-1/
- Web Animations Module Level 2 — https://www.w3.org/TR/web-animations-2/ (currentTime + reverse)
- Flutter `AnimatedContainer` / `AnimatedOpacity` 等 — https://api.flutter.dev/flutter/widgets/AnimatedContainer-class.html
- SwiftUI implicit animation (`withAnimation { state.toggle() }`) — Apple Developer
- Framer Motion `animate` prop / spring physics — https://motion.dev/docs/react
- React Spring `useSpring` — https://www.react-spring.dev/

## 12. 次のアクション

1. ✅ Open Questions 全 8 件解決済
2. **TR-M1 着手**: `src/Luxel.Animation/Transition.cs` に `Transition.Animate<T>` 核心 + `src/Luxel.Animation.UI/SignalTransition.cs` に `Transition.Watch<T>` + サンプル 38 (Signal の値変化を補間)
3. **TR-M2**: 添付プロパティ `P.Transition.*` + Widget Realize 連携 + サンプル 39 (Button hover/press)
4. **TR-M3 (任意)**: CSS `transition:` 構文パーサ

## 13. AN-M シリーズとの関係

このプランは **AN シリーズの拡張ではなく、UI 寄りのラッパー** として位置付けます:

- AN-M1〜M6 は **「明示的にアニメを Play する」モデル** (timeline + clip)
- 本プラン (TR-M1〜M4) は **「値変化を検知して暗黙的に補間する」モデル** (implicit transition)
- 両者は同じ `AnimationPlayer` / `IClock` / `Curves` / `Tween` を共有し、競合しない

UI 用途で 90% のケースをカバーする「implicit transition」を提供することで、`Animate.Tween(...)` を毎回書かずに `signal.Value = newValue` だけで動くアニメ UI が実現できます。
