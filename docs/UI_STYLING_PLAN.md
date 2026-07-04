# Luxel.UI Styling (関数引数 + 状態別 StateStyle + Tailwind 別アセンブリ) 設計プラン

**ステータス:** TW-M1 完了 (2026-06-30)
**前提:** AN/TR システム完了 (Animation + Transition 全体)
**関連:** [docs/ANIMATION_PLAN.md](ANIMATION_PLAN.md) / [docs/UI_TRANSITION_PLAN.md](UI_TRANSITION_PLAN.md)

---

## 1. 動機 / 哲学

ユーザー要望:
- **UI 関数本体の引数として状態別スタイルを全て指定できる** (CSS の hover/active/focus を引数で渡す)
- **Tailwind 風 utility / トークン** は **別プロジェクト** に分離して上書き層として置く
- **Transition で状態切替を補間**

## 2. 全体アーキテクチャ (3 層)

```
レイヤ 1: Luxel.UI 本体
  ─ StateStyle (一通りのプロパティ、nullable)
  ─ Widget 関数引数: style/hover/pressed/focused/disabled
  ─ Widget 内蔵 Signal<bool> (Hovered/Pressed/Focused) + 外部 override 可
  ─ Theme 型は提供しない (ユーザー定義の record)

レイヤ 2: Luxel.Animation.UI
  ─ TransitionSet (StateStyle と並行構造、tuple 暗黙変換)
  ─ Transition.Animate / SignalTransition.Watch (既存 TR-M1/M2)
  ─ Widget が TransitionSet 引数を受取り、状態切替で自動補間 (TW-M2 で実装)

レイヤ 3: Luxel.UI.Tailwind (新規別アセンブリ、TW-M5 で作成)
  ─ Tw.Blue500/Red500/.../P4/RoundedMd パレット定数
  ─ S.Bg/Px/Py/Rounded/Scale/On(state, ...) utility
  ─ IConfigPart として parts: に渡す
  ─ Widget の引数値を override (CSS specificity 順)
```

## 3. 設計決定 (Open Question 全 8 件)

| # | 質問 | 決定 |
|---|---|---|
| 1 | StateStyle 引数の形 | **個別引数** (style/hover/pressed/focused/disabled) |
| 2 | プロパティ集合の初期スコープ | **一通り全部最初から** (Bg/Fg/Opacity/Scale/Translate/Rotate/Rounded/Border/Padding/Margin/W/H/FontSize) |
| 3 | TransitionSet 形 | **型安全 record + tuple 暗黙変換** |
| 4 | 既存 Theme との関係 | **StateStyle ベースに移行** (大規模 refactor) |
| 5 | 状態保持 | **Widget 内蔵 Signal + 外部 override 可** |
| 6 | 対象 Widget 範囲 | **段階的** (Button → Card → 全 Widget) |
| 7 | Tailwind 指定方法 | **既存 `parts:` 引数で渡す** (Grid.Column と同じ流儀) |
| 8 | テーマトークン | **Theme は値持ち POCO**、ユーザーが record で自由定義、複数種類作成可。Tailwind は数値定義 (`Tw.Blue500`) で別アセンブリ |

## 4. StateStyle / TransitionSet API

### StateStyle (Luxel.UI/Styling/StateStyle.cs)

```csharp
public sealed record StateStyle
{
    // 視覚
    public uint? Background, Foreground;
    public float? Opacity;

    // 変形
    public float? Scale;
    public Vector2? Translate;
    public float? Rotate;          // radians

    // 境界
    public float? Rounded;
    public uint? BorderColor;
    public float? BorderWidth;

    // レイアウト
    public Thickness? Padding, Margin;
    public float? Width, Height;

    // タイポグラフィ
    public float? FontSize;

    public static readonly StateStyle Empty = new();
    public StateStyle MergeWith(StateStyle? other);   // 後勝ち
}
```

### TransitionSet (Luxel.Animation.UI/TransitionSet.cs)

```csharp
public sealed record TransitionSet
{
    public TransitionSpec? Background, Foreground, Opacity;
    public TransitionSpec? Scale, Translate, Rotate;
    public TransitionSpec? Rounded, BorderColor, BorderWidth;
    public TransitionSpec? Padding, Margin, Width, Height, FontSize;
}

// TransitionSpec に暗黙変換 (float / tuple) を追加 (TR-M2 拡張)
TransitionSpec s1 = 0.3f;                          // float → duration のみ
TransitionSpec s2 = (0.3f, EaseInOut);             // tuple → (duration, curve)
TransitionSpec s3 = (0.3f, EaseInOut, 0.1f);       // tuple → (duration, curve, delay)
```

## 5. 利用例 (Button)

### 基本: 引数だけで完結 (Tailwind / CSS 流)

```csharp
using Luxel.UI.Styling;
using static Luxel.UI.UI;

Button("OK", onClick: action,
    style:   new StateStyle { Background = blue,  Foreground = white, Rounded = 10, Width = 180, Height = 60 },
    hover:   new StateStyle { Background = red,   Scale = 1.10f },
    pressed: new StateStyle { Scale = 0.95f },
    disabled: new StateStyle { Opacity = 0.5f });
```

### Transition 補間: TransitionFactory + parts: 方式 (TW-M2)

```csharp
using Luxel.Animation.UI;

var fx = new TransitionFactory(player, clock);   // 事前 setup

Button("OK", onClick: action,
    style: new StateStyle { Background = blue, Width = 200, Height = 100 },
    hover: new StateStyle { Background = red,  Scale = 1.15f },
    parts: [
        fx.Background(0.30f, CubicBezierCurve.EaseInOut),
        fx.Scale     (0.20f, CubicBezierCurve.EaseOut),
    ]);

// または TransitionSet を展開
parts: [.. fx.FromSet(new TransitionSet {
    Background = (0.30f, CubicBezierCurve.EaseInOut),
    Scale      = 0.20f,
})]
```

- すべて関数引数で完結。Theme / Variant / Intent は一切経由しない
- `MergeWith` で「default + 当該状態の override」を合成
- Widget 内部の `Hovered/Pressed/Focused` Signal で自動切替
- `Reactive.Effect` で recolor + transform を部分更新

### ユーザー定義 Theme (record として自由に書く)

```csharp
// 自分のアプリで自由命名 (Luxel は何も強制しない)
public sealed record AppTheme
{
    public required uint Primary { get; init; }
    public required uint PrimaryHover { get; init; }
    public required uint Surface { get; init; }
    public required uint OnSurface { get; init; }
    public required float RoundedMd { get; init; }
}

public static AppTheme MakeLight() => new() {
    Primary       = Tw.Blue500,
    PrimaryHover  = Tw.Blue600,
    Surface       = Tw.Slate50,
    OnSurface     = Tw.Slate900,
    RoundedMd     = 6f,
};
public static AppTheme MakeDark() => new() { /* ... */ };

// 利用
var theme = MakeLight();
Button("OK", onClick: action,
    style: new StateStyle {
        Background = theme.Primary,
        Foreground = theme.OnSurface,
        Rounded    = theme.RoundedMd,
    },
    hover: new StateStyle { Background = theme.PrimaryHover });
```

## 6. マイルストーン

| 段 | 内容 | ステータス |
|---|---|---|
| **TW-M1** | Luxel.UI に StateStyle / TransitionSet 型追加、Button が引数化、サンプル 40 | ✅ 完了 |
| **TW-M2** | TransitionSet 引数で状態切替の自動補間 (TR-M1/M2 と統合)、サンプル 41 | ✅ 完了 |
| **TW-M3** | ユーザー定義 AppTheme record + Signal で Light/Dark 切替、サンプル 42 | ✅ 完了 |
| **TW-M5** | 新規アセンブリ `Luxel.UI.Tailwind` 作成、Tw パレット + S utility (Bg/Px/Py/Rounded/Scale/On) + parts: 経由 override、サンプル 43 | ✅ 完了 |
| **TW-M4** | Text に StateStyle 引数を波及 (Foreground/Opacity/FontSize)、サンプル 44 | ✅ 完了 |
| **TW-M4b** | WidgetState に Checked/Selected 追加、CheckBox 拡張 (StateStyle 引数 + utility + checked: variant)、サンプル 45 | ✅ 完了 |
| **DSL 統一** | 子は `this[...]` indexer 統一、fluent (WithXxx) 全廃止 → factory 引数化、ApplyParts も廃止 | ✅ 完了 |
| **TW-M4c** | Switch / Slider / TextField / Select / SegmentedControl / RadioGroup / Tabs 全対応、サンプル 46-47 | ✅ 完了 |
| **TW-M6 (任意)** | レスポンシブ (Breakpoint) / 文字列パース / CSS animations 連携 | 未着手 |

## 7. TW-M1 実装メモ

### 追加ファイル
- `src/Luxel.UI/Styling/StateStyle.cs` ─ 14 プロパティの nullable record + Empty + MergeWith
- `src/Luxel.Animation.UI/TransitionSet.cs` ─ プロパティ別 TransitionSpec 束ね
- `src/Luxel.Samples/Sample40StateStyleButton.cs` ─ 引数 hover/pressed 切替 demo (vk/dx 一致)

### 変更ファイル
- `src/Luxel.Animation.UI/PTransition.cs` ─ TransitionSpec に float / tuple 暗黙変換を追加
- `src/Luxel.UI/Widgets/Button.cs`
  - StateStyle 引数版コンストラクタ追加 (既存 Variant/Intent 方式と排他)
  - `ResolveCurrent()` で「default + 当該状態」を MergeWith で合成
  - PerformLayout で `Width/Height/Padding/FontSize/Rounded` を default style から拾う
  - Realize の Reactive.Effect で StateStyle ベース recolor + scale (中心 anchor) + opacity (alpha 乗算)
  - 既存 Hovered/Pressed Signal を再利用、Focused Signal を追加
- `src/Luxel.UI/UI.cs` ─ StateStyle 引数版 Button ファクトリ追加

### サンプル 40 結果 (vk/dx 完全一致)
- idle: A=blue (60,120,210), B=green (40,180,110)
- hover A: A=red (230,80,100) + scale 1.10, B=green idle
- hover B: A=blue idle, B=orange + opacity 0.6 → (242,182,130) panel と合成
- idle 復帰: 初期値完全復元

### テスト (175 件, +7)
- StateStyle: Empty/MergeWith override/MergeWith null
- TransitionSet: float 暗黙変換/tuple2/tuple3/record setter

## 8. TW-M2 実装メモ

### 設計方針
- **拡張メソッド (`.WithTransitions`) は使わない**: ユーザー要望で「事前 setup された factory クラスのメソッド呼出しで `parts:` に渡す」方式を採用
- Tailwind 風: utility/トークンを parts に並べて宣言する流儀と整合

### 追加 / 変更
- `src/Luxel.UI/Widgets/Button.cs`
  - `BackgroundSetterFactory` / `ForegroundSetterFactory` / `ScaleSetterFactory` / `OpacitySetterFactory` プロパティ追加 (型: `Func<Action<T>, Action<T>>?`、各プロパティ別に raw setter を「補間付き setter にラップ」する差し替え点)
  - Realize 内の HasStateStyles ブロックを ApplyAll パターンに refactor: `currentBg/currentFg/currentScale/currentOpacity` を closure に保持、各 raw setter は state を更新して ApplyAll を呼ぶ、factory でラップされたものは Transition.Animate 経由で毎フレーム raw setter を呼ぶ
- `src/Luxel.Animation.UI/TransitionFactory.cs` (新規)
  - `new TransitionFactory(player, clock)` を事前構築
  - `fx.Background(dur, curve, delay)` / `fx.Foreground` / `fx.Scale` / `fx.Opacity` が `IConfigPart` を返す
  - 内部の `TransitionSetterPart` (IConfigPart 実装) の `Apply(widget)` で Button の対応 SetterFactory を埋める
  - `fx.FromSet(TransitionSet)` で TransitionSet 全体を IEnumerable&lt;IConfigPart&gt; に展開 (`parts: [.. fx.FromSet(set)]`)
- `src/Luxel.Animation.UI/WidgetTransitions.cs`
  - 旧 `ApplyTransitions` / `WithTransitions` を削除 (TransitionFactory に統合)
  - TR-M2 の `Wrap` / `WrapFromWidget` / `FindSpec` は残置 (静的 spec 用途)
- `src/Luxel.Samples/Sample41StateStyleTransitions.cs`
  - `parts: [ fx.Background(0.30f, EaseInOut), fx.Scale(0.20f, EaseOut) ]` 方式
  - 60fps frame loop で t=0.10/0.20/0.30/0.40 の 4 snapshot
  - R が単調増加 (blue→red の EaseInOut)、t=0.20 で中間色 (191,89,125)、t=0.30 で完了 (red)

### サンプル 41 結果 (vk/dx 完全一致)
- t=0.00 (idle):  (60, 120, 210)
- t=0.10:         (99, 111, 185)   ← EaseInOut の S字、blue 寄りの中間
- t=0.20:         (191, 89, 125)   ← red 寄りの中間
- t=0.30:         (230, 80, 100)   ← 完了 = red
- t=0.40:         (230, 80, 100)   ← 静止

### 設計上の利点
- **Luxel.UI が Luxel.Animation を一切知らない**: Button 自身は `Func<Action<T>, Action<T>>?` factory を持つだけ、補間ロジックは Luxel.Animation.UI が外から埋める
- **既存 TR-M1/M2 の Smooth interrupt が自然に効く**: 補間中に hover が解除されると Transition.Animate が現在値から逆方向に補間しなおす
- **プロパティ別 duration / curve**: Background は 0.30s EaseInOut、Scale は 0.20s EaseOut のように独立指定可

### テスト (175 → 180 件, +5)
- TransitionFactory.Background → SetterFactory 適用
- TransitionFactory.Scale → SetterFactory 適用
- FromSet → 指定 prop のみ IConfigPart を yield
- FromSet zero duration → 除外
- ctor null check

## 9. TW-M3 実装メモ

### 設計方針
- **Luxel 本体に Theme 型を追加しない**: テーマはサンプル内 (= ユーザーアプリ側) でユーザー自由定義の record
- **複数 Theme クラス共存可**: 同じ AppTheme を複数インスタンス (Light/Dark) で持つ、別構造の Theme record も同居可
- **Signal&lt;AppTheme&gt; で動的切替**: 値変化で Reactive.Effect が走り、host.SetRoot が UI 全体を rebuild

### 追加ファイル
- `src/Luxel.Samples/Sample42AppTheme.cs` ─ サンプル内に AppTheme record を直接定義 (Luxel 本体には何も追加しない)

### Sample 42 結果 (vk/dx 完全一致)
- Light: btn=(60, 120, 210), bg=(245, 246, 250)
- Dark:  btn=(120, 175, 255), bg=(24, 26, 32)
- Light 戻し: 初期値完全復元

### Theme record の例 (ユーザー側コード)
```csharp
public sealed record AppTheme
{
    public required uint Primary { get; init; }
    public required uint PrimaryHover { get; init; }
    public required uint Surface { get; init; }
    public required uint OnPrimary { get; init; }
    public required uint OnSurface { get; init; }
    public required float RoundedMd { get; init; }
}

static AppTheme MakeLight() => new() { Primary = ..., ... };
static AppTheme MakeDark()  => new() { Primary = ..., ... };

var theme = new Signal<AppTheme>(MakeLight());
Reactive.Effect(() => {
    var t = theme.Value;
    host.SetRoot(BuildUI(t));
});

theme.Value = MakeDark();   // → 自動 rebuild + recolor
```

### 既存 Luxel.UI.Theme との関係
既存の `Luxel.UI/Theme/Theme.cs` (Background/Surface/Primary 等の固定プロパティ) と
`Luxel.UI/Theme/Styles.cs` (Variant/Intent → VisualStyle 解決) は **そのまま残置** (後方互換):
- 旧 API を使う既存サンプル (10/12/13/15 等) は今まで通り動く
- 新規コードは AppTheme record を自前で定義し StateStyle 引数で使う方を推奨
- TW-M5 で Luxel.UI.Tailwind パレットが追加されたら、ユーザーは「Tw 数値定数 → 自前 AppTheme record」の流れで構築できる

## 10. Border の StateStyle 引数化 (Button と同じ流儀に統一)

Sample 42 を進めるなかで Border が旧 `WithBackground/WithPadding/WithCorner` fluent API のみだったため、
Button と乖離していた。これを統一する形で Border に StateStyle 引数を追加。

### 追加 / 変更
- `src/Luxel.UI/Styling/StyleApply.cs` (新規) ─ `MultiplyAlpha` を共通ヘルパに抽出 (Button/Border 共有)
- `src/Luxel.UI/Widgets/Border.cs`
  - StateStyle 引数 (`style`/`hover`/`pressed`/`focused`/`disabled`) を Button と同じ流儀で追加
  - setter factory 3 種 (`BackgroundSetterFactory`/`ScaleSetterFactory`/`OpacitySetterFactory`) を追加 (Foreground は Border にテキストがないため除外)
  - Realize を ApplyAll パターンに refactor (Button と同じ構造)
  - PerformLayout で `StyleDefault.Padding/Width/Height/Rounded` を反映
- `src/Luxel.UI/Widgets/Button.cs` ─ 内部 `MultiplyAlpha` を削除し `StyleApply.MultiplyAlpha` に統一
- `src/Luxel.UI/UI.cs` ─ Border の StateStyle 引数版 factory 追加
- `src/Luxel.Animation.UI/TransitionFactory.cs` ─ `TransitionSetterPart.Apply` で Button と Border の両方に SetterFactory を適用 (Foreground は Border ではスキップ)
- `src/Luxel.Samples/Sample42AppTheme.cs` ─ `Border(child, style: new StateStyle { Background = t.Surface, Padding = new Thickness(24) })` に書き換え

### 利用例 (統一後)
```csharp
// Button と Border が同じ流儀
Border(
    Grid(...)[child],
    style: new StateStyle { Background = t.Surface, Padding = new Thickness(24), Rounded = 12 })

Button("OK", onClick,
    style: new StateStyle { Background = t.Primary, Width = 200, Height = 60 },
    hover: new StateStyle { Background = t.PrimaryHover, Scale = 1.05f },
    parts: [ fx.Background(0.3f, ease), fx.Scale(0.15f, ease) ])
```

### 後方互換性
旧 Border API (`Border(child).WithBackground(...).WithPadding(...).WithCorner(...)`) はそのまま残置。
Sample 10/12/13/14/15/16/17/18/19/Sample_DevTools 等の既存コードは無修正で動作 (確認済み)。

## 11. TW-M5 実装メモ (Luxel.UI.Tailwind 別アセンブリ)

### 新規プロジェクト
- `src/Luxel.UI.Tailwind/Luxel.UI.Tailwind.csproj` ─ 別アセンブリ、`Luxel` / `Luxel.TwoD` / `Luxel.UI` 参照のみ
- slnx に追加、Samples / Tests プロジェクトに参照を追加

### 新規ファイル
- `src/Luxel.UI/Styling/WidgetState.cs` ─ Default/Hover/Pressed/Focused/Disabled の enum (Luxel.UI 本体に追加、Tailwind 以外も使えるよう Styling 名前空間)
- `src/Luxel.UI.Tailwind/Tw.cs` ─ Tailwind v3 のパレット定数
  - **色**: Slate/Red/Amber/Green/Cyan/Sky/Blue/Indigo/Violet/Pink の主要 50-900 スケール
  - **スペーシング**: P0/P1/P2/P3/P4/P5/P6/P8/P10/P12/P16/P20/P24 (4px 刻み)
  - **角丸**: RoundedNone/Sm/(無印)/Md/Lg/Xl/2xl/3xl/Full
  - White/Black/Transparent
- `src/Luxel.UI.Tailwind/S.cs` ─ utility ファクトリ
  - 視覚: `Bg(uint)` / `Fg(uint)` / `Opacity(float)`
  - 変形: `Scale(float)` / `Rotate(float)`
  - 境界: `Rounded(float)` / `Border(color, width)`
  - レイアウト: `P(n)` / `Px(n)` / `Py(n)` / `W(v)` / `H(v)`
  - タイポ: `FontSize(v)`
  - 状態 variant: **`On(WidgetState, params IConfigPart[])`**
  - 各メソッドが `IConfigPart` (= `StylePart` / `OnVariantPart`) を返し、Widget.ApplyParts で StateStyle スロットを `MergeWith` で更新する
- `src/Luxel.Samples/Sample43Tailwind.cs` ─ 完全 utility-only Button demo

### 動作の流れ (utility は StateStyle スロットを後付け更新)

```
parts: [
    Bg(Tw.Blue500),                                   // StylePart, State=Default
    Fg(Tw.White),                                     // StylePart, State=Default
    Rounded(Tw.RoundedLg),                            // StylePart, State=Default
    W(180), H(80),                                    // StylePart, State=Default
    On(Hover, Bg(Tw.Red500), Scale(1.10f)),           // OnVariantPart → State=Hover の StylePart に再構築
    On(Pressed, Scale(0.92f)),                        // OnVariantPart → State=Pressed の StylePart に再構築
]

↓ Widget.ApplyParts で各 IConfigPart.Apply(widget) が順に走り、
  Button.StyleDefault / StyleHover / StylePressed の各スロットを MergeWith で累積更新する
```

### Tailwind 風 API 完成形
```csharp
using static Luxel.UI.Tailwind.S;
using Luxel.UI.Tailwind;

Button("Tailwind", () => clicked++, parts: [
    Bg(Tw.Blue500), Fg(Tw.White), Rounded(Tw.RoundedLg),
    W(180), H(80),
    On(WidgetState.Hover,   Bg(Tw.Red500), Scale(1.10f)),
    On(WidgetState.Pressed, Scale(0.92f)),
]);

// Border にも適用可
Border(child, parts: [ Bg(Tw.Slate100), P(Tw.P6) ])
```

→ HTML/CSS の `<button class="bg-blue-500 text-white rounded-lg w-[180px] h-[80px] hover:bg-red-500 hover:scale-105 active:scale-95">Tailwind</button>` と等価。

### Sample 43 結果 (vk/dx 完全一致)
- idle:  btn=Tw.Blue500 (59, 130, 246), bg=Tw.Slate100 (241, 245, 249)
- hover: btn=Tw.Red500 (239, 68, 68) + scale 1.10
- idle 戻し: 完全復元

### テスト (180 → 187 件, +7)
- S.Bg / Fg / On(Hover,...) / On(Pressed,...) / 累積 (chained utilities)
- Border への適用
- Tw.Blue500 / Tw.Red500 の RGB 値検証 (Tailwind v3 公式と一致)

## 12. TW-M4 実装メモ (Text への波及)

### 変更ファイル
- `src/Luxel.UI/Widgets/Text.cs`
  - StateStyle 引数 (style/hover/pressed/focused/disabled) と setter factory (Foreground/Opacity) を Button/Border と同じ流儀で追加
  - Hovered/Pressed/Focused Signal も追加 (将来の HitTest 対応に備える)
  - Realize に `HasStateStyles` 分岐: ApplyAll パターンで Foreground/Opacity を反映
  - PerformLayout で StyleDefault.FontSize/Width/Height を反映
- `src/Luxel.UI/UI.cs` ─ Text の StateStyle 引数版 factory 2 つ (string / Func<string> 両方)
- `src/Luxel.UI.Tailwind/S.cs` ─ `StylePart.Apply` に `ApplyToText` 分岐追加
- `src/Luxel.Animation.UI/TransitionFactory.cs` ─ `TransitionSetterPart.Apply` に Text の Foreground/Opacity 対応追加

### サンプル 44 結果 (vk/dx 完全一致)
- 背景 = Tw.Slate100 (241, 245, 249) ✓
- Button = Tw.Blue500 (59, 130, 246) ✓
- Text 3 階層 (Slate900/Slate700/Slate500) + FontSize 28/16/13 + Opacity 1.0/1.0/0.85
- HTML/CSS `<h1 class="text-slate-900 text-2xl">` 等と等価

### Text の制限事項
- Text 自身は HitTest を持たない (Hovered Signal はあるが現状外部からの代入のみ)
- Background プロパティは Text に意味ない (Foreground のみ)
- Scale/Translate も現状未対応 (将来対応可)
- 主用途: 「色とフォントサイズを utility で宣言」が中心

### テスト (187 → 191 件, +4)
- S.Fg が Text.StyleDefault.Foreground を埋める
- S.FontSize が StyleDefault.FontSize を埋める
- S.On(Hover, Fg(...)) が StyleHover を埋める
- TransitionFactory.Foreground が Text.ForegroundSetterFactory を埋める

## 13. TW-M4b 実装メモ (CheckBox + Checked variant)

### 変更ファイル
- `src/Luxel.UI/Styling/WidgetState.cs` ─ `Checked` と `Selected` を enum に追加 (Tailwind の `checked:`/`aria-selected:` 相当)
- `src/Luxel.Controls/CheckBox.cs`
  - StateStyle 引数 (style/hover/pressed/focused/disabled/@checked) を追加
  - setter factory (Background/Foreground/Opacity) 追加
  - `ResolveCurrent` で `_checked.Value` を Effect の依存追跡対象とする
  - Realize の `HasStateStyles` 分岐で ApplyAll パターン (`check.Opacity = _checked.Value ? currentOpacity : 0f`)
  - 旧テーマ駆動 (UiTheme.T.Primary/SurfaceAlt) は後方互換で残置
- `src/Luxel.UI.Tailwind/Luxel.UI.Tailwind.csproj` ─ Luxel.Controls 参照を追加 (Tailwind が Controls Widget もカバー)
- `src/Luxel.UI.Tailwind/S.cs` ─ `StylePart.Apply` に `ApplyToCheckBox` 分岐追加

### 利用例 (Tailwind の `checked:` prefix と等価)
```csharp
var sig = new Signal<bool>(false);
var cb = new Luxel.Controls.CheckBox(sig, "Subscribe");
cb.ApplyParts(new IConfigPart[]
{
    Bg(Tw.Slate300), Fg(Tw.Slate900),
    On(WidgetState.Checked, Bg(Tw.Blue500)),
});
```

HTML/CSS で `<input type="checkbox" class="bg-slate-300 text-slate-900 checked:bg-blue-500">` と等価。

### サンプル 45 結果 (vk/dx 完全一致)
- unchecked: box=(203, 213, 225) = Tw.Slate300 ✓
- checked:   box edge=(59, 130, 246) = Tw.Blue500 ✓ (center は check 印 AA fringe で薄まる)
- unchecked 戻し: 完全復元

### 注意点
- `Luxel.Samples/CheckBox.cs` (sample 11 のカスタムコントロール) と `Luxel.Controls.CheckBox` の名前衝突対策:
  - Sample 45 では `new Luxel.Controls.CheckBox(...)` の完全修飾名で書く
  - `using Luxel.Controls;` を入れても same-namespace の `Luxel.Samples.CheckBox` が優先される (C# name resolution rules)

### テスト (191 → 193 件, +2)
- S.Bg が CheckBox.StyleDefault.Background を埋める
- S.On(Checked, Bg(...)) が StyleChecked を埋める (StyleDefault は触らない)

## 14. DSL 統一 (子 indexer + factory 引数 + ApplyParts 廃止)

### 設計方針
ユーザー要求を反映した最終 DSL:
1. **子 Widget は `this[...]` indexer 統一**: `Border()[child]` / `Grid()[c1, c2]` / `VStack()[c1, c2]`
2. **すべての設定は factory 引数**: fluent (WithXxx) 完全廃止
3. **`parts:` は `params IConfigPart[]` のみ** (Tailwind utility / 添付プロパティ専用)
4. **`Widget.ApplyParts` instance method 廃止 + `ApplyPartsInternal` helper も廃止**: 各 factory 内で `foreach (var p in parts) p.Apply(widget);` を inline

### 削除した API
- `Widget.ApplyParts` / `Widget.ApplyPartsInternal` (factory 内 inline 化)
- `Widget.AddChildWidget` virtual (各 Widget の private AddChild に分離)
- `Border.WithBackground` / `WithPadding` / `WithCorner` / `Clip`
- `StackPanel.WithSpacing`
- `Text.Bind(Func<uint>)` (Text factory の `color:` 引数で代替)
- `Border(Widget child)` コンストラクタ (indexer 統一)
- `Each` / `When` / `Fragment` helper (collection expression で代替)

### 削除した Sample
- Sample 11/12/13/14/15/16/17/18/19/SampleDevTools/Samples.CheckBox: 旧 Luxel.Controls 系 UI、indexer/引数化 refactor 時に削除 (旧 fluent API の使用箇所が多数のため一掃)
- RenderGraph 系 (22-30) / Animation 系 (31-39) / 新 Tailwind 系 (40-45) は維持

### 最終 DSL (例)
```csharp
Border(background: () => panel, padding: new Thickness(16))
[
    Grid(columns: [1, 2], rows: [GridLength.Px(70), GridLength.Star(1)])
    [
        Text(() => $"Count: {count.Value}", 30, color: () => dark,
            parts: [P.Grid.Row(0), P.Grid.ColumnSpan(2)]),
        Button("OK", onClick,
            style: new StateStyle { Background = blue, Foreground = white },
            hover: new StateStyle { Background = blueHi },
            parts: [P.Grid.Column(0), P.Grid.Row(1)])
    ]
]

// Tailwind 風 utility
Button("Tailwind", onClick, parts: [
    Bg(Tw.Blue500), Fg(Tw.White), Rounded(Tw.RoundedLg),
    On(WidgetState.Hover, Bg(Tw.Red500), Scale(1.10f)),
])

// VStack の spacing 引数
VStack(spacing: 12)[Heading(...), Body(...)]
```

### 全 sample 動作確認 (vk/dx 完全一致)
- Sample 10: 宣言的 UI (Grid + signal + Click/hover)
- Sample 40-45: StateStyle / TransitionSet / AppTheme / Tailwind / Text / CheckBox
- テスト 193 件全 pass

## 8. 不変量

- **Luxel.UI 本体に Theme 型を提供しない**: テーマはユーザー定義の record (複数種類 OK)
- **既存 Variant/Intent と新 StateStyle は排他**: いずれかが non-null なら新方式、全部 null なら旧方式 (後方互換)
- **TR-M2 と整合**: TransitionSet は既存 `TransitionSpec` を再利用、tuple 暗黙変換で記述短縮
- **scene-agnostic 維持**: StateStyle 自体は UI 専用、Transition 部は他にも展開可能な形

## 9. 業界参考

| ライブラリ | 概念 |
|---|---|
| Tailwind CSS | utility-first, state prefix (`hover:`/`focus:`), tokens (`theme.colors.primary`) |
| MUI sx prop | object-based utility, theme tokens |
| Flutter WidgetState | `WidgetStateProperty<Color>` で状態別値、関数で動的解決 |
| SwiftUI ViewModifier | implicit animation, .hoverEffect, .scaleEffect |
| React Spring | useSpring + state-driven transition |
| Stitches variants | type-safe variant API in CSS-in-JS |
