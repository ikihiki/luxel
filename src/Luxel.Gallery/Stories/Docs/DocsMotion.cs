using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — モーション章 (Animation / Transitions)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsMotion
{
    [Story("Docs/Animation", Order = 30)]
    public static Widget Animation(StoryContext ctx) => DocNew(ctx, $$"""
        # アニメーション (Luxel.Animation)

        glTF / CSS / コード DSL など様々な形式を統一的に扱うアニメーションシステムです。中核は **3 層の IR** で、書き込み先 (UI / 2D / 3D) はアダプタに分離されています。

        ## 設計ノート: 3 層 IR は共通、ターゲットは 3 アダプタ

        業界 (Flutter / Bevy / Unreal / Unity / Spine / glTF) の実装は同じ構造に収束しています:

        - **L1: AnimationClip** — pure data (Track の束)。immutable で共有可能
        - **L2: Track + Sampler** — stateless な純粋関数 (キーフレーム補間)
        - **L3: AnimationPlayer / TrackEntry** — stateful (time / loop / mix)

        ターゲットを共通化しないのも業界共通です — 共通化すると「最小公倍数」になり、signals のリアクティブ起動、RetainedCanvas の SoA 部分更新、ECS の component query という各ドメインの最適化が壊れます。Luxel のアダプタは 3 つ: `SignalAnimationTarget` (UI) / `RetainedCanvasAnimationTarget` (2D) / `EcsAnimationTarget` (3D)。

        ## Curve × Tween の 2 段分解

        値の補間は Flutter Animatable 流の 2 段分解です:

        ```
        時間 t → Curve.Eval(t01) → progress → Tween.Lerp(p) → 値 T → Setter
        ```

        Curve は Linear / CubicBezier / Steps / Spring、Tween は Float / Vector2/3 / Rgba / Quaternion (slerp)。時間の合成 (イージング) と値の合成 (型) が直交します。

        ## コード DSL — Sequence / Parallel

        ```csharp
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);

        Animate.Sequence(
            Animate.Parallel(
                Animate.Tween(SignalAnimationTarget.For(x), -150f, 30f, 0.4f)
                       .WithCurve(CubicBezierCurve.EaseOut),
                Animate.Tween(SignalAnimationTarget.For(opacity), 0f, 1f, 0.4f)),
            Animate.Tween(SignalAnimationTarget.For(x2), 300f, 130f, 0.4f)
        ).Play(player, clock);

        // 毎フレーム: clock を進めて player.Update(clock) — 絶対時刻モデル
        ```

        実物 (2 枚のカードが slide-in + fade-in): {{StoryRef(ctx, "Demos/Animation/Tween")}}

        ## AnimationClip と Importer

        キーフレームの束は `AnimationClip` に正規化されます。コードで組む (`Tracks.Vector3(path, kind, keyframes)`) ほか、**Importer** が外部形式を落とし込みます:

        - **CSS @keyframes** — `CssKeyframesImporter.Parse(css)` → opacity / translate / color の Track 群 ([Demos/Animation/CssKeyframes](story:Demos/Animation/CssKeyframes))
        - **glTF** — animations[] → translation/rotation/scale の Track。**skin (スケルタル)** — JOINTS_0/WEIGHTS_0 + inverse bind matrix から GPU 頂点スキニング ({{StoryRef(ctx, "Demos/3D/GltfSkinned")}})。**morph target (ブレンドシェイプ)** — 位置/法線デルタを weights channel の重みで頂点加算 ({{StoryRef(ctx, "Demos/3D/GltfMorph")}})。どちらも GPU 頂点シェーダで解決
        - 拡張点は `IAnimationImporter` — Lottie subset 等はここに載せます。パース警告は既定 Warn (Strict / Lenient に切替可)

        Track の `TargetPath` ("card/opacity" 等) をアダプタの `Bind(name, 対象)` が解決します。

        ## Graph と StateMachine

        Clip の上には合成レイヤが載ります:

        - **AnimationGraph** — Clip / Blend / Add の DAG。`BlendNode.Weight` で 2 クリップを混合 ([Demos/Animation/Graph](story:Demos/Animation/Graph) — weight は knob)
        - **StateMachine** — Trigger 名で状態切替、crossfade 秒指定 ([Demos/Animation/StateMachine](story:Demos/Animation/StateMachine) — ボタンで Trigger)
        - ECS への適用は [Demos/Animation/EcsClip](story:Demos/Animation/EcsClip) — Clip → LocalTransform → propagate → extract → 描画

        ## 絶対時刻モデル (frame-driven)

        実行は完全 frame-driven です。`IClock` (絶対時刻) を進めて `player.Update(clock)` を呼ぶ — `FixedFrameClock` は frame × (1/fps) を毎回計算するので累積誤差ゼロ、snap / bench の固定ステップでピクセルまで決定的になります。

        > [!TIP]
        > wall-clock は使いません。ストーリーやアプリのフレームループが持つ累積秒から clock を組み立てるのが規約です (Gallery のデモは全てこの形)。

        次: [Docs/Transitions](story:Docs/Transitions) — 「値を変えるだけで補間される」層へ。
        """, toc: true);

    [Story("Docs/Transitions", Order = 31)]
    public static Widget Transitions(StoryContext ctx) => DocNew(ctx, $$"""
        # UI トランジション

        CSS の `transition` 相当 — **プロパティ値を変えるだけで**古い値から新しい値へ自動補間される層です。明示的アニメ (Clip) や状態機械とは起動のしかたが違います:

        | モード | 起動 | 用途 |
        | --- | --- | --- |
        | CSS animation (Clip) | 明示的 @keyframes + 開始呼び出し | scripted motion |
        | State machine | Trigger 名で状態切替 | 離散的な状態遷移 |
        | **CSS transition (このページ)** | **値変化を検知**して自動補間 | implicit interpolation |

        ## Transition.Animate — setter ラッパー

        最小のプリミティブは「setter を包む」ことです。Signal / UiNode / 任意の書き込み先に等しく適用できます (scene-agnostic):

        ```csharp
        // setter ラッパー: 値を「変えるだけで」古い値から自動補間される
        Action<uint> animated = Transition.Animate<uint>(
            v => node.Color = v, player, clock,
            duration: 0.3f, curve: CubicBezierCurve.EaseInOut);

        animated(Color2D.Red);    // 初回は即時適用
        animated(Color2D.Blue);   // 0.3s で Red→Blue

        // Signal と組み合わせるなら Effect で
        using var e = new ReactiveEffect(() =>
            animated(hovered.Value ? Color2D.Red : Color2D.Blue));
        ```

        ## 設計ノート: smooth interrupt

        A→B の補間中に C へ変更されたら、**現在の表示値 X を起点に X→C をフル duration で** 進めます (Framer Motion / React Spring の fromCurrent と同じ)。値がジャンプせず、hover→pressed→normal と連打しても連続です。この意味論は状態機械 (PropertyStateMachine) にも組み込まれています — プロパティ毎に独立進行するので、色 300ms / Scale 120ms が並走できます。

        ## 宣言 API — When + Transition

        コントロールでは状態別の値 (`When`) と遷移の速さ (`Transition` 系) を分けて宣言します:

        ```csharp
        Button(onClick, "Save",
                background: Tw.Blue500)
            .When(WidgetState.Hover, background: Tw.Red500, scaleX: 1.1f)
            .When(WidgetState.Pressed, background: Tw.Green500)
            .Transition(0.4f, CubicBezierCurve.EaseInOut, ButtonProps.Background)  // プロパティ既定
            .TransitionTo(WidgetState.Hover, 0.08f)                                // enter は素早く
            .TransitionBetween(WidgetState.Pressed, WidgetState.Hover, 0f);        // 離した瞬間は即時
        ```

        解決は TransitionTable の (from, to, prop) ルールで、**具体的な指定が勝ちます** (pair > to > from > プロパティ既定 > 全体既定。to = enter が from = leave より優先 — CSS の「遷移先のルールが適用される」慣習と同じ)。実物は [Demos/Animation/Transitions](story:Demos/Animation/Transitions) へ。

        ## 動的状態 — スクロールも選択移動も同じ機械で

        ListView の選択行 (10 万行) やスクロールオフセット (任意 float) のような**非有界の状態空間**は、`Goto("selected", clock, 値)` の動的状態で表します。状態名が TransitionTable の解決キー、値は都度の目標 — 「ホイールは滑走 / サムドラッグは即時」も "wheel" / "drag" という状態名と table ルールだけで表現できます。

        ## コントロール標準モーション

        | コントロール | モーション |
        | --- | --- |
        | Switch | つまみスライド (OutCubic 140ms) + トラック色クロスフェード |
        | Segmented / Tabs / Radios | 選択ハイライトのスライド (160ms) |
        | ListView | 選択ハイライトの移動 (120ms) + スクロール慣性 (時定数 ~80ms) |
        | Dialog / Drawer / Toast | フェード + スライド/スケール (180〜220ms) + scrim |
        | Button / MenuRow | hover 色フェード (80ms) |

        設計原則: **アニメは変化時のみ、初期状態は瞬時** (Realize 直後は静止値 — snap の golden が揺れない)。静定後は signal 書き込みゼロ (アイドルで再描画 0)。動きは全て transform / opacity / color の部分更新に乗ります。
        """, toc: true);
}
