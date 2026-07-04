# アニメーション統合計画 (AS: Animation States)

目標: ①QP-M1 で Luxel.UI に入れてしまったアニメ計算を **Luxel.Animation へ一本化 (依存の逆転を解消)**
②**プロパティ状態機械**をアニメーションプロジェクトに実装 — 遷移途中でも現在値起点で滑らかに次の
アニメへ繋がる ③UI の状態レイヤ (状態ごとのプロパティ値 — 既存) に **from / to / from→to 毎 ×
プロパティ選択のトランジション設定**を宣言できるようにする。

**方針 (ユーザー決定)**: transform 系の連続値アニメ (Switch つまみ/Tabs 下線/オーバーレイ開閉/
スクロール等) も「状態間のアニメ」として**状態機械に統一管理**する — UI 側に AnimatedValue のような
並行機構を残さない。全アニメが同じ機械 + TransitionTable + AnimationPlayer の上で動く。

## 現状分析

### 既にあるもの

| 場所 | 資産 |
|---|---|
| Luxel.Animation (→ Luxel のみ) | `ICurve` (CubicBezier/Linear/Spring/Steps)、`ITween<T>` (Float/Vector2-4/Quaternion/**Rgba**)、Clip/Track/Keyframe、Graph (Clip/Blend/Add)、DSL (Sequence/Parallel/Tween)、**StateMachine (クリップグラフ用 — Trigger + crossfade)**、**`Transition.Animate` (setter ラップ、smooth interrupt = 現在値起点フル duration、UI_TRANSITION_PLAN の設計決定済み)**、`AnimationPlayer` (絶対時刻 `IClock` 駆動 — dt 累積の丸め誤差なし)、CssKeyframesImporter |
| Luxel.Animation.UI (→ Animation + UI) | `TransitionFactory` (**`Widget.SetSetterWrap` 連携済み** — parts で宣言)、`TransitionSet` (プロパティ別 spec 束)、`WidgetTransitions`、`PTransition`、`SignalAnimationTarget` |
| Luxel.UI | **状態→プロパティ値は完備**: `WidgetState` + `Bindable<T>.SetState` (状態レイヤ、`StatePriority` = Disabled > Pressed > Hover > Focused > Checked > Selected で解決) + `Widget.IsStateActive` + `SetSetterWrap`/`WrapSetter` (補間差し替え点) |

### 問題 (このプランで直すもの)

1. **計算の重複 + 依存の逆転**: QP-M1 の `Luxel.UI/Motion.cs` (Easing/AnimatedValue/LerpColor) は
   Luxel.Animation の ICurve/FloatTween/RgbaTween/Transition.Animate の再発明。Luxel.UI が
   Luxel.Animation を参照していないために UI 側へ実装された — 参照を張って計算を Animation へ返す
   (Animation は Luxel core のみ依存なので循環しない)。
2. **状態機械の欠落**: 既存 StateMachine はクリップグラフ (Rive/Mecanim 型) 用。UI が要るのは
   「状態 = プロパティ値の集合」で遷移する **variants 型** (Framer Motion 相当) — これが無い。
3. **トランジション設定の粒度**: TransitionFactory/TransitionSet は「プロパティ × 単一 spec」のみ。
   from / to / from→to 毎の設定 (hover は素早く入りゆっくり抜ける等) ができない。

## 設計

### 時刻モデルの橋渡し

UI 側は `ctx.AddAnimation(dt)` (dt 累積)、Animation 側は `IClock` (絶対時刻)。
**UiHost 毎に `AnimationPlayer` + `ManualClock`** を持つ `UiAnimationHub` (Animation.UI) を新設し、
`ctx.AddAnimation(dt => { clock.Advance(dt); player.Update(clock); return false; })` で駆動する
(snap/bench は固定 dt なので決定的)。

### TransitionTable — from/to/pair × プロパティの解決

```csharp
// Luxel.Animation (UI 非依存 — 状態名は string、プロパティ名も string)
public sealed class TransitionTable
{
    public void Add(string? from, string? to, string? prop, TransitionSpec spec);  // null = ワイルドカード
    public TransitionSpec? Resolve(string from, string to, string prop);
}
```

解決優先度 (具体的なものが勝つ):

| 優先 | from | to | prop |
|---|---|---|---|
| 1 | ✓ | ✓ | ✓ |
| 2 | ✓ | ✓ | * |
| 3 | * | ✓ | ✓ |
| 4 | * | ✓ | * |
| 5 | ✓ | * | ✓ |
| 6 | ✓ | * | * |
| 7 | * | * | ✓ |
| 8 | * | * | * (既定) |

to 指定 (enter) が from 指定 (leave) より優先 — CSS の「遷移先のルールが適用される」慣習に一致。

### PropertyStateMachine — 途中遷移の連続性

```csharp
// Luxel.Animation/StateMachine (第二の機械 — クリップグラフ用と並置)
var m = new PropertyStateMachine(table);
m.AddState("normal", new() { ["Background"] = blue,  ["Scale"] = 1.0f });
m.AddState("hover",  new() { ["Background"] = red,   ["Scale"] = 1.1f });
m.Start("normal", clock);   // 初期状態は瞬時適用 (snap 不変の原則を機械の意味論に)
m.Goto("hover", clock);     // 各プロパティが「現在のアニメ値」起点で新目標へ (per-prop tween)
m.Tick(clock, (prop, value) => apply(prop, value));
```

- **smooth interrupt を機械の意味論に**: 遷移中の `Goto` は各プロパティを現在値起点で再スタート
  (`Transition.Animate` と同じ決定 = UI_TRANSITION_PLAN #2 を継承)。from は「離れようとしていた
  目標状態」— 途中で hover→pressed→normal と連打しても値はジャンプしない。
- プロパティ毎に独立進行 (クリップ機械の crossfade と違い、色 300ms / Scale 120ms が並走できる)。
- 片側の状態にしか無いプロパティ: 目標側に無ければ「既定状態の値」へ戻す (CSS と同じ)。
- 値型は既存 `ITween<T>` 自動選択 (float/Vector*/Quaternion/uint=Rgba、不明型 Step)。

### 動的状態 — 連続値アニメも状態遷移として統一する鍵

登録済み状態 (on/off 等) だけでは、**状態空間が非有界**なアニメ (ListView の選択行 = 10 万行、
スクロールオフセット = 任意 float、Tabs の選択 index = 実体化時に決まる) を表せない。
そこで `Goto` に**値をその場で与える動的状態**を許す:

```csharp
m.Goto("selected", clock, new() { ["y"] = row * rowH });   // 状態名は from/to 解決に、値は都度供給
m.Goto("wheel",    clock, new() { ["offset"] = target });  // スクロール: 状態名でチャネルを区別
m.Goto("drag",     clock, new() { ["offset"] = target });  // → table 側 (*, "drag", *) = 0ms で即時
```

- **状態名 = TransitionTable の解決キー、値 = 都度の目標**。同名状態への再 Goto でも値が違えば
  現在値起点で再 tween (ListView の選択移動、ホイール連打の目標加算がこれ)。
- スクロールの「ホイールは滑走 / サムドラッグは即時」は **"wheel"/"drag" という状態名の使い分け +
  table ルール**で表現 — instant フラグのような側路が要らなくなる。
- 登録済み状態は「値も固定の動的状態」の糖衣にすぎない (実装は一本化)。

### UI 宣言 API (Animation.UI)

状態ごとの**値**は既存の `On(WidgetState.Hover, Bg(red))` のまま。**遷移**を parts で重ねる:

```csharp
Button(onClick, "Save",
    On(WidgetState.Hover,   Bg(red), Scale(1.1f)),
    On(WidgetState.Pressed, Bg(darkRed)),
    // プロパティ既定
    P.Transition.On("Background", 0.3f, CubicBezierCurve.EaseInOut),
    // to (enter): hover へ入るときは全プロパティ素早く
    P.Transition.To(WidgetState.Hover, 0.08f),
    // from (leave): hover から抜けるときはゆっくり — prop 指定付き
    P.Transition.From(WidgetState.Hover, "Background", 0.3f),
    // from→to pair: pressed→hover は即時 (離した瞬間の手応え)
    P.Transition.Between(WidgetState.Pressed, WidgetState.Hover, 0f));
```

配線は既存の **`SetSetterWrap` 差し替え点**をそのまま使う。ラッパは setter 呼び出し時に widget の
**支配状態** (StatePriority 順で最初にアクティブな状態、なければ Default) を評価し、
(前回の支配状態 → 今回, prop) で TransitionTable を解決して補間する。状態が変わらない値変化
(テーマ切替等) は prop 既定 spec、無ければ即時。

## マイルストーン

- **AS-M1: 依存逆転 — 計算の一本化**。Luxel.UI → Luxel.Animation 参照を追加。
  `Easing` を廃止し Curves へ (OutCubic/InOutCubic は多項式 curve として Animation に移設 —
  決定論のため式は不変)。`Motion.LerpColor` → RgbaTween。`AnimatedValue` は公開 API を保ったまま
  内部を Animation の計算に委譲する**暫定ブリッジ**とし、AS-M3 完了時に削除する (呼び出し側の
  段階的移行のため 2 段階に分ける)。MotionTests を計算部 (Animation) とブリッジ部 (UI) に分割。
  ゲート: テスト + snap 51/51 不変 (「初回は瞬時」原則は維持)。
- **AS-M2: PropertyStateMachine + TransitionTable (Luxel.Animation)**。上記設計 —
  登録状態 + **動的状態 (`Goto(name, values)`)**、Start は瞬時適用、静定後は sink 書き込みゼロ
  (アイドル dirty ゼロの原則を機械に内蔵)。ヘッドレステスト: 8 段優先度、途中 Goto の連続性
  (ジャンプなし)、同名動的状態の値変更 retarget、per-prop 並走、片側欠落プロパティ、delay。
- **AS-M3: UI 統合 — 全アニメを状態機械へ統一移行**。
  - `UiAnimationHub` (UiHost 毎 player+clock、ctx.AddAnimation 駆動、`UiBuildContext.Host` 経由)。
  - `ctx.States(table)` (Animation.UI): PropertyStateMachine + signal sink (prop → tracked 読み)。
    effect で `sm.Value("t")` を transform/色に束縛する — AnimatedValue の使用感を機械の上で再現。
  - 状態スタイル配線: `P.Transition.On/To/From/Between` parts → SetSetterWrap
    (支配状態スナップショットで from/to 識別)。QP-M2 の手書き hover lerp (Button/MenuRow) を移行。
  - **transform 系連続値の統一移行 (ユーザー決定)**: Switch つまみ+トラック色 = "on"/"off" 状態、
    オーバーレイ開閉 = "open"/"closed"、Accordion = "expanded"/"collapsed"、
    Tabs/Segmented/RadioGroup = index 状態 (実体化時に登録)、ListView 選択 = 動的状態 "selected"、
    スクロール = 動的状態 "wheel"/"drag" (drag は table で 0ms)。**on→off と off→on で別 duration を
    設定できる**ようになるのが統一の実利。移行完了後に `AnimatedValue`/`MotionExtensions` を削除。
  - ゲート: snap 51/51、状態強制フェード ≤100ms 規約は既定 spec で維持、
    bench --wheel 再構築 0% 維持 (スクロール移行の回帰確認)。
- **AS-M4: デモ + E2E + ドキュメント**。Gallery「Transitions/States」ストーリー
  (enter 80ms / leave 300ms / pressed→hover pair 0ms / 色とScale の並走 / on→off・off→on の
  非対称 duration を実演)。実窓 E2E: hover in/out 中間フレーム、hover 中に press→release の
  途中遷移連続性、スクロール滑走の回帰。

## リスク / 注意

- **from/to の識別タイミング**: Bindable の状態解決は「値の切替」で、setter は変化後の値しか
  知らない。支配状態のスナップショット比較で from→to を復元する — 状態 signal 書き込み → effect →
  setter の同期実行順なので一致する (複数状態が 1 フレームで同時に変わると中間状態は畳まれる。仕様とする)。
- **カーブ互換**: Easing.OutCubic と CSS ease-out は別物。式を変えず多項式 curve として移設し、
  中間フレームの見た目も不変にする (golden は静止のみなのでゲートには影響しない)。
- **AnimationPlayer の絶対時刻**: ManualClock の dt 累積で駆動 — snap の固定 dt で決定的。
  丸め誤差が問題になったら FixedFrameClock に切替可能な構造にしておく。
- TransitionSet (プロパティ × 単一 spec) は TransitionTable の劣化形として残すか、
  `Table.Add(null, null, prop, spec)` の糖衣に置き換えて撤去 (AS-M3 で判断)。
- **スクロールの状態化は意味論が薄い** (本質は連続 retarget) が、統一管理の決定に従い
  動的状態 + table ルールで表す。使用感が悪化する場合は AS-M3 で報告して判断を仰ぐ。
- ListView は再バインド/クリック判定が表示オフセット (旧 shown) に依存 — 移行後は
  `sm.Value("offset")` を同じ役割で読む (仮想化の「滑走中も行が欠けない」不変条件を E2E で再確認)。

## 進捗

**AS-M1〜M4 全完了 (2026-07-04)** — テスト 399 (+PropertyStateMachineTests 13)、snap 52/52
(vk/dx ピクセル不変、Transitions/States 追加)、bench --wheel 再構築 0% 維持、実窓 E2E 済み。

- AS-M1: Luxel.UI → Luxel.Animation 参照。`Easing` → `OutCubicCurve`/`InOutCubicCurve`
  (Curves/PolynomialCurves.cs、式不変)。`Motion.LerpColor` → `RgbaTween`。
  `TransitionSpec` を Animation.UI → Luxel.Animation へ移設。
- AS-M2: `TransitionTable` (8 段優先度) + `PropertyStateMachine` (登録状態 + 動的状態
  `Goto(name, values)` / 単一 prop 糖衣、Start 瞬時、途中 Goto は現在値起点、静定中 sink 書き込みゼロ、
  欠落 prop は base へ、型別 ITween 自動選択)。ヘッドレステスト 13 本。
- AS-M3: `UiHost.Clock` (ManualClock、Tick 頭で加算) + `UiStates` (`ctx.States` — signal 束縛と
  駆動だけの薄いブリッジ)。**全コントロール移行完了・AnimatedValue/MotionExtensions 削除**:
  Switch (on/off)、FocusRing (focus/blur)、Segmented/Tabs (index 動的状態、Tabs は x+w 並走)、
  RadioGroup (行毎 on/off)、Accordion (expanded/collapsed)、Button/MenuRow hover (normal/hover)、
  オーバーレイ (open/closed、UiHost 内で UiStates 直使用)、ScrollViewer/ListView
  (動的状態 "wheel"/"drag"、drag は table 0ms — instant フラグ廃止)。
  **状態スタイル配線**: `P.Transition.Default/On/To/From/Between` (Animation.UI parts) →
  widget 添付 TransitionTable → `Widget.Realize` が `SetSetterWrapFallback`
  (TransitionWiring.Provider = プロパティ毎 PropertyStateMachine、from/to は支配状態
  StatePriority 先頭アクティブのスナップショット) を自動登録。
- AS-M4: Transitions/States ストーリー (Button: enter 80ms / leave 400ms EaseInOut /
  To(Pressed) 0ms / Between(Pressed→Hover) 0ms / Background のみ対象・Scale は瞬時)。
  実窓 E2E (ピクセル判定): enter 40ms でほぼ赤 (高速)、leave 150ms でブレンド (低速)、
  強制 pressed = 純緑即時、pressed 解除 +150ms = **純赤 (pair 0ms が prop 既定 400ms に勝つ)**。

既知の注意:
- Button は入力からの Pressed 追跡を持たない (状態強制トグルのみ) — pair の実マウス検証は
  Pressed 追跡を Button に足したときに再確認。
- Gallery の状態強制は `Hovered` を 1 フレーム false→true と揺らす (Enabled 再評価ハック) —
  同値 Goto は no-op なので状態機械には無害。

## 後続 (GN, 2026-07-04 完了): 型付き When + fluent Transition

DSL 検討 (ユーザー決定: Stateable 明示フラグ / When は値のみ / トランジションは対象プロパティ群を
指定する Transition 系に集約) を実装:

- **`[UiParam(Stateable = true)]`**: 状態レイヤに出してよい表示系プロパティの明示宣言
  (effect で毎回解決されるもののみ — レイアウト系は単一パスで反映されないため対象外)。
- **生成 `When(state, ...)`**: ジェネレーターが Stateable フィールドから partial に焼き込み。
  **引数はファクトリと同名・同型** (Bindable 既定値 = 未設定判定)。IntelliSense がそのコントロールで
  状態可変なものだけを見せる — 「コントロール毎にプロパティが異なる」問題の型付き解。
- **生成 `{Class}Props` 定数** (sibling クラス — `Button.Props` はファクトリ関数名と CS0119 衝突するため)。
- **fluent `Transition/TransitionTo/TransitionFrom/TransitionBetween(spec, params props)`**
  (Luxel.UI、ジェネリック this で具象型チェーン)。props 省略 = 全プロパティ。
  parts (P.Transition.*) と同じ `TransitionWiring.AddRule` に合流。
- 新構文 (Transitions/States ストーリー):
  ```csharp
  Button(onClick, "Hover / Press", background: Tw.Blue500, ...)
      .When(WidgetState.Hover, background: Tw.Red500, scale: 1.06f)
      .When(WidgetState.Pressed, background: Tw.Green500)
      .Transition(0.4f, EaseInOut, ButtonProps.Background)
      .TransitionTo(WidgetState.Hover, 0.08f, ButtonProps.Background)
      .TransitionBetween(WidgetState.Pressed, WidgetState.Hover, 0f);
  ```
- Stateable 付与済み: Button/Switch/MenuRow/Segmented/RadioGroup/Tabs/ListView/Accordion/
  Spinner/ScrollViewer の表示系 (色/opacity/scale)。
- ゲート: テスト 399、snap 52/52 (vk/dx — 意味は同一なので golden 不変)、
  実窓ピクセル判定 (initial Blue500 → hover Red500 → 復帰)。

## 後続 (EV, 2026-07-04 完了): [UiEvent] — コールバックの UI パラメータ化

- **`UiEvent` / `UiEvent<T>` / `UiEvent<T1,T2>`** (Luxel.UI): Action ラッパ struct。
  `Action` からの暗黙変換、`HasHandler`、ハンドラなし Invoke は no-op。
  Bindable の対 (値ではなく動作) — 状態レイヤ/アニメ/DevTools 値編集の対象にはしない。
- **`[UiEvent]` フィールド** をジェネレーターが収集し:
  - ファクトリの省略可能引数 (`Action? onClick = null`) として **ctor 引数の直後**に出す —
    `Button(() => ..., "text")` の位置引数互換が保たれる (Button/MenuRow は ctor から
    コールバックを撤去しても全呼び出し側が無修正でビルドが通った)
  - 引数なしイベントは **`InvokeEvent(string name)` override** を生成 (テスト/リモート駆動用)
- 移行済み: Button.OnClick / MenuRow.OnClick (ctor 引数廃止)、ListView.OnSelect/OnReorder
  (プロパティ → [UiEvent] フィールド、ファクトリ引数にも出るようになった)。
- **注意: lambda の直接代入は不可** (`lv.OnSelect = i => ...` — lambda→ユーザー定義変換は連鎖しない)。
  ファクトリ引数で渡すか、`Action<int> h = ...; lv.OnSelect = h;` と変数経由で。
  自己参照ハンドラ (ハンドラ内で自 widget を触る) は後者のパターン。
- ゲート: テスト 403 (+UiEventTests 4)、snap 52/52、実窓 E2E (Counter クリック ×3 → 3)。

## 後続 (EV2+TF, 2026-07-04 完了): 全コールバック移行 + transform 成分の共通 UI パラメータ化

- **EV2**: Splitter.OnResized を [UiEvent] へ (ctor は vertical のみ)。残る Action は移行対象外:
  Dropdown の items (行データの一部)、TableBlock/LiveCodeBlock の commit (embed ホストプロトコル)、
  FocusTarget/HitTarget のハンドラ (入力配線)。**widget の ctor コールバックはゼロになった**。
- **TF (ユーザー決定: 共通 UI パラメータ)**: Widget 基底に
  `[UiParam(Stateable = true)] TranslateX / TranslateY / ScaleX / ScaleY / Rotate` —
  基底フィールドはジェネレーターが継承収集するため **When に常に出る + 全ファクトリの共通引数**が
  自動で成立。Transition の対象指定は共通定数 **`Transform.ScaleX`** 等 (Luxel.UI/Transform.cs)。
  行列は直接アニメしない (CSS の translate/rotate/scale 独立プロパティと同じ判断)。
- **適用点 = `Widget.WireTransform(ctx, node)`**: root ノードに Offset + 成分を中心基準で
  Translate → Rotate → Scale 合成。setter は WrapSetter 経由 = トランジション自動適用。
  全成分が既定値なら従来どおり `Translate(Offset)` (恒等パス — snap 不変の証明)。
  戻り値 `TransformHandle.SetExtraScale` でコントロール固有の一様スケール (Button/Border の
  Scale) を同じ行列に合成 — 手書きの中心スケール transform を撤去。
- **共通化 (Widget 側)**: `Widget.CreateRoot(ctx, parent, worldOrigin[, out TransformHandle])` —
  root ノード生成の 3 行イディオム (AddChild → Transform=Translate(Offset) → SetWorldPos) を
  1 行に畳み、transform 配線を内蔵。**全 27 コントロール + コンテナ (Grid/StackPanel/WrapPanel) を
  一括移行済み** — transform 成分はどのコントロール/パネルでも効く。world 座標は呼出し後に
  `WorldPos` を読む。完全自動 (Realize が暗黙配線) にしなかった理由: 自前ノードを持たず子を直接
  実体化する widget では「自分の root」を機械的に識別できず、子の transform を壊すため。
- デモ (Transitions/States): squash & stretch — hover で scaleX 1.12 (120ms) /
  scaleY 0.94 (300ms EaseInOut) / rotate 0.03rad (瞬時) が並走。**X と Y で別カーブ**の実例。
- ゲート: テスト 403、snap 52/52 (恒等パスで不変)、実窓 E2E (hover で伸び・潰れ・傾きを確認)。

## 後続 (EV3, 2026-07-04 完了): sender-first コールバック + ListView items UI パラメータ化

- **sender-first 規約 (ユーザー決定)**: UiEvent の第一引数は**発火元の UI 自身**。
  arity-0 の `UiEvent` を廃止し `UiEvent<TSender>` / `UiEvent<TSender, T>` /
  `UiEvent<TSender, T1, T2>` に統一 (`Invoke(sender, ...)`)。
  ハンドラは `onSelect: (lv, i) => lv.ScrollTo(i)` のように発火元へ型付きアクセスできる —
  EV2 で残っていた**自己参照ハンドラの Action 変数経由パターンは不要になった**
  (Reorder ストーリーはファクトリ引数一発で書ける)。
- **ジェネレーター**: ファクトリ引数は `Action<TSender[, ...]>?`。InvokeEvent(name) の生成対象は
  「引数が sender 1 つだけ」のイベント (`ArgTypesFq is [w.TypeFq]`) → `E.Invoke(this)`。
- **ListView.SetItems 廃止 (ユーザー決定)**: `[UiParam] Bindable<IReadOnlyList<string>> Items` —
  **Signal を渡して値を入れ直すと反映される** (`items: sig` → `sig.Value = next`)。
  Realize 内の effect が Items を tracked 読みし、**参照が変わった時だけ**旧 SetItems 相当
  (選択解除 + ReservePool + オフセット clamp + _version++ 再バインド) を実行。
  再実体化しても items は同じ signal から復元される (フィールド保持が不要になった)。
- 呼び出し側スイープ: `Button(() =>` → `Button(_ =>` 等 (perl 一括 + メソッドグループ 2 箇所は
  `_ => Run()` 形へ)。GalleryApp Log / DevToolsUi の 4 リスト + JsonPanel は
  `Signal<IReadOnlyList<string>>` フィールド + ctor でファクトリ呼び (**フィールド初期化子は
  他のインスタンスフィールドを参照できない** — signal を渡すファクトリ呼びは ctor で行う)。
- ゲート: テスト 404 (+items signal 反映 1)、snap 52/52 (vk/dx)、実窓 E2E
  (ListView/Basic = 40 行表示 → 行クリック → gallery Log (items signal) が 1 行 /
  Button/Counter ×3 クリック → " 3 ")。
- **E2E 小ネタ**: `/cmd` は `{"op":"click","ui":"story","x":..,"y":..}` で **story ホストへ
  論理座標のまま**打てる (SurfaceView 越しの物理座標換算が不要)。args はトップレベルに置く
  (`{"op":..,"id":..}` — `args` 入れ子は無視される)。

## 後続 (BC, 2026-07-04 完了): Bindable class 化 — UI パラメータの値書き換え専用化

- **動機 (ユーザー要望)**: UI パラメータは「Bindable の中の値を書き換える」ものにし、
  **Bindable インスタンス自体は差し替えられない**ようにする。struct 時代は
  `w.Background = Tw.Red500;` (public mutable struct フィールドへの再代入) が通ってしまい、
  積んだ状態レイヤ (When) や DevTools override が丸ごと消える事故が可能だった。
- **`Bindable<T>` / `BindableString` を struct → sealed class 化**:
  - [UiParam] フィールドは `readonly Bindable<T> Foo = new();` で宣言 (**`= new()` 必須** —
    忘れると NRE)。再代入は CS0191 でコンパイルエラー。
  - 書き込み API は従来どおり: **SetBase** (基底値差し替え — 状態レイヤ/override 維持、
    値/Signal/Func を暗黙変換で受ける) / **SetState** (状態レイヤ) / **SetOverride** (DevTools)。
    class 化で SetBase の「override/states を退避して復元する」struct ダンスが消えた。
  - 「未設定」は `default(Bindable<T>)` (struct) から **`new Bindable<T>()`** に。
    引数の未指定は **nullable (`Bindable<T>? x = null`)** で表す。
- **ジェネレーター**: ファクトリ/When の引数を `Bindable<T>?`/`BindableString?` `= null` に
  (Length だけ値型のまま `.IsSet`)、書き込みは `w.F = pn` から **`w.F.SetBase(pn)`** へ。
  SetProp/SetDebugProp/PropWriter/Codec から `ref`/`in` を除去 (class 参照渡し)。
  readonly スキップ (`f.IsReadOnly`) は撤廃 — 全フィールドが readonly になったため。
- **修正が要った既存コード** (コンパイラが検出): 派生 ctor での基底フィールド代入 ×2
  (Center の `HAlign = Stretch` / Spacer の `Width = ...` → SetBase)、テストの直接代入・
  `default` 使用 ×4。**それ以外の全コードは無修正** — 読み (Get/Or) と生成コード経由の
  書きに統一されていたため。
- **知見**: C# の暗黙変換 (`T`→`Bindable<T>`) は class でも機能する
  (`background: Tw.Blue500` / `items: signal` / `$"..."` handler ctor も class で可)。
  interpolated string handler は class でも OK。alloc 増 (widget 1 個あたり Bindable
  フィールド数 × ~40B) は構築時のみでホットパス (タイプ/スクロール) には乗らない。
- ゲート: テスト 404、snap 52/52 (vk/dx ピクセル不変)、実窓 E2E (Transitions/States で
  hover 中 (633,201) = RGB(239,68,68) Red500 / 離脱後 = RGB(59,130,246) Blue500 —
  When 状態レイヤと SetBase 基底が class 化後も正しく解決)。
