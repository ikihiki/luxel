using Luxel.UI;
using static Luxel.Gallery.Story;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Curve、Tween、Clip、Graph、StateMachineを現在の実装契約に沿って学ぶコース。</summary>
[StoryMeta("Learn/Animation")]
public static partial class LearnAnimation
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Animation 学習ガイド

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/Overview", "Beginner", "Standalone / Gallery / Browser", "Backend neutral; adapters write to UI / 2D / ECS", "なし")}}

        Animationは「時間を値へ変換する層」と「値を書き込む層」を分離します。最小経路は **curve → tween → player → setter** です。複数propertyを再利用可能なデータにするときはclip、複数clipを混ぜるときはgraph、eventで状態を切り替えるときはstate machineを選びます。

        ## どの仕組みを選ぶか

        | 目的 | 主な型 | 責務 |
        | --- | --- | --- |
        | 1つの値を補間 | `ICurve` + `ITween<T>` + `Animatable<T>` | progressと値補間を合成 |
        | frame駆動で再生 | `AnimationPlayer` | 絶対時刻、loop、time scale、完了 |
        | 手続き的に組み立てる | `Animate.Tween` / `Sequence` / `Parallel` | commandをplayerへschedule |
        | 複数propertyをまとめる | `AnimationClip` + `Track<T>` | pathごとのkeyframeをsample |
        | clipを混ぜる | `AnimationGraph` | clip / blend / add nodeを評価 |
        | eventで遷移する | `StateMachine` | state、trigger、crossfade |
        | 短命なvisual event | `ParticleSystem` | spawn、寿命、simulation、描画adapter |

        Particleはclipやstate machineの代替ではありません。個体数が多く寿命の短いvisual eventを扱い、sizeやcolorの寿命変化にAnimationの`ICurve`を再利用します。

        ## 最小の値animation

        ```csharp
        var player = new AnimationPlayer();
        var anim = new Animatable<float>
        {
            Duration = 0.4f,
            Curve = CubicBezierCurve.EaseOut,
            Tween = new FloatTween(0f, 100f),
        };

        player.Play(anim, value => x = value);
        player.Update(absoluteTimeSec);
        ```

        `Play()`は直ちに`t=0`の値をsetterへ書きます。以後は累積`dt`ではなく、呼び出し側が渡した絶対時刻とentryの`StartTime`との差で評価します。

        ## コース順

        {{AnimationCourseCatalog.LearningRouteMarkdown()}}

        > [!IMPORTANT]
        > `AnimationPlayer`、`AnimationGraph`、`StateMachine`は自動でframe loopへ接続されません。所有側が毎frame `Update()`または`Tick()`を呼びます。

        ## 典型的な失敗

        - curve、tween、targetの責務を1つのclassへ混ぜ、再利用できなくする。
        - simulationの固定stepと表示用animationの絶対時刻を同じ値だと思い込む。
        - particleで長寿命の状態やsemanticなUI transitionを表現しようとする。
        """;

    [Story]
    public static StoryResult CurvesAndTweens(StoryContext ctx) => $$"""
        # Curves and Tweens

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/CurvesAndTweens", "Beginner", "Standalone / Headless / Browser", "Backend neutral", "Animation overview")}}

        `ICurve`は正規化時間をprogressへ、`ITween<T>`はprogressを型`T`の値へ変換します。`Animatable<T>.Evaluate(timeSec)`は時間を`[0,1]`へclampし、`Curve.Eval()`の結果を`Tween.Lerp()`へ渡す純粋な評価です。

        ## Curveの種類

        | Curve | 用途 |
        | --- | --- |
        | `LinearCurve.Instance` | 等速 |
        | `CubicBezierCurve.Ease` / `EaseIn` / `EaseOut` / `EaseInOut` | UI向けの標準easing |
        | `OutCubicCurve.Instance` / `InOutCubicCurve.Instance` | 三次式のease |
        | `StepsCurve` | 段階的な値 |
        | `SpringCurve` | overshootを含むspring応答 |

        下のサンプルは同じ往復時刻を全curveへ入力します。Bezier presetの加減速、4種類の`StepPosition`のjump位置、springのunderdamped / critical / overdampedを同時に比較できます。

        {{StoryRef("Learn/Animation/CurvesSample")}}

        この比較には`StepsCurve`の`JumpStart` / `JumpEnd` / `JumpBoth` / `JumpNone`と、`SpringCurve`のunderdamped / critical / overdamped設定を含みます。

        ```csharp
        ICurve curve = CubicBezierCurve.EaseInOut;
        float progress = curve.Eval(0.5f);
        ```

        ## 型別Tween

        `FloatTween`、`Vector2Tween`、`Vector3Tween`、`Vector4Tween`は線形補間、`QuaternionTween`はslerp、`RgbaTween`はpacked RGBA channelを補間します。独自型は`ITween<T>`を実装します。

        ```csharp
        var move = new Animatable<Vector2>
        {
            Duration = 0.25f,
            Curve = CubicBezierCurve.EaseOut,
            Tween = new Vector2Tween(new Vector2(0, 0), new Vector2(120, 40)),
        };
        Vector2 value = move.Evaluate(0.125f);
        ```

        ## 境界と失敗

        `Duration <= 0`では終端値`Tween.Lerp(1)`を返します。通常のdurationでは負時刻を0、duration超過を1へclampします。

        > [!NOTE]
        > curveは必ずしも`[0,1]`内に収まるとは限りません。springのovershootを許容できるtween/targetか確認してください。

        - 秒とmillisecondを混在させる。
        - quaternionをcomponentごとに線形補間する。
        - packed colorのbyte orderを確認せず独自tweenを書く。
        """;

    [Story]
    public static StoryResult PlayerAndTiming(StoryContext ctx) => $$"""
        # Player and timing

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/PlayerAndTiming", "Intermediate", "App frame loop / Headless test", "Backend neutral", "Curves and tweens")}}

        `AnimationPlayer`は複数の`TrackEntry`を絶対時刻で更新します。`IClock.TimeSec`を渡すか、同じ基準の`float absoluteTimeSec`を毎frame渡します。

        ## 再生契約

        ```csharp
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        TrackEntry<float> entry = player.Play(anim, v => opacity = v, clock,
            timeScale: 1f, loop: false);

        clock.SetTime(0.2f);
        player.Update(clock);
        ```

        | API | 契約 |
        | --- | --- |
        | `Play()` | `clock.TimeSec`または`LastTime`をstartにし、初期値を即時反映 |
        | `Update()` | 全entryを評価し、完了entryを除去 |
        | `ActiveCount` | 現在残っているentry数 |
        | `Stop(entry)` | entryを除去するがcompletion callbackは呼ばない |
        | `Clear()` | 全entryを即時除去 |

        ## Loop、time scale、completion

        `Loop=true`はdurationで再生位置を循環させます。`TimeScale`はstartとの差へ乗算されます。non-loop entryは終端を一度適用して`Done`となり、playerから除去されます。

        ```csharp
        TrackEntry<float> pulse = player.Play(anim, v => scale = v,
            timeScale: 0.5f, loop: true);
        // later
        player.Stop(pulse); // OnCompleteは発火しない
        ```

        ## 決定的なtest

        wall clockを直接読む代わりに手動clockまたは明示した絶対時刻を使います。同じ時刻列なら評価結果は再現可能です。

        > [!WARNING]
        > `Update(dt)`ではありません。`Update(1f / 60f)`を繰り返すと時刻が進まず、同じ絶対時刻を再評価します。

        - frame loopから`Update`を呼び忘れる。
        - clockを途中で別epochへ切り替える。
        - `Stop`を正常完了通知として使う。
        """;

    [Story]
    public static StoryResult SequenceAndParallel(StoryContext ctx) => $$"""
        # Sequence and Parallel

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/SequenceAndParallel", "Intermediate", "Standalone / Gallery / Browser", "Backend neutral", "Player and timing")}}

        DSLはsetter中心の短いanimationを組み立てます。`Animate.Tween`は型別tween、`Sequence`は開始offsetの直列化、`Parallel`は同時開始を表します。

        ## Commandを作る

        ```csharp
        using static Luxel.Animation.Animate;

        Sequence(
            Tween(v => opacity = v, 0f, 1f, 0.20f),
            Parallel(
                Tween(v => x = v, 0f, 120f, 0.35f),
                Tween(v => scale = v, 0.8f, 1f, 0.35f)))
            .Play(player, clock);
        ```

        `TweenColor`は`uint` RGBA、generic `Tween<T>`は独自`ITween<T>`を受け取ります。各commandにはcurveやcompletionを設定できます。

        ## Schedulingの実装契約

        sequence/parallelは子commandを再生時にすべてscheduleし、各entryの絶対`StartTime`をずらします。遅れて始まるsequence childも`Play()`時に自分の初期値をsetterへ一度書くため、同じpropertyを複数childが共有すると最後にscheduleされた初期値が一時的に見えることがあります。

        | 合成 | StartTime | Duration |
        | --- | --- | --- |
        | `Sequence(a,b)` | bはaのduration後 | 子durationの合計 |
        | `Parallel(a,b)` | 同じstart | 子durationの最大 |
        | delay相当 | setterを変えないcommandまたは開始offset | 指定秒 |

        ## 使い分けと失敗

        {{StoryRef("Learn/Animation/TweenSample")}}

        > [!IMPORTANT]
        > 同じpropertyへ重なるcommandを書かないことが最も単純です。複数sourceの意味あるblendが必要なら`AnimationGraph`を使います。

        - sequenceを「前のcompletion時に次を生成する仕組み」と誤解する。
        - setterが別thread affinityを持つのにplayerをbackground threadで更新する。
        - 長い再利用データまでDSLへ埋め込み、clipとして共有できなくする。
        """;

    [Story]
    public static StoryResult ClipsAndTracks(StoryContext ctx) => $$"""
        # Clips and Tracks

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/ClipsAndTracks", "Intermediate", "Standalone / Gallery / Game", "Backend neutral", "Sequence and parallel")}}

        `AnimationClip`はpathごとのtrackをまとめる再利用可能な時間データです。trackは型付きkeyframeをsampleし、`IAnimationTarget`へpathと値を書きます。

        ## Clipを定義する

        ```csharp
        var clip = new AnimationClip("move", new TrackBase[]
        {
            Tracks.Vector3("marker/translation", InterpolationKind.Linear,
            [
                new Keyframe<Vector3>(0f, new Vector3(20, 48, 0)),
                new Keyframe<Vector3>(1f, new Vector3(196, 48, 0)),
            ]),
            Tracks.Float("marker/opacity", InterpolationKind.Linear,
            [
                new Keyframe<float>(0f, 0f),
                new Keyframe<float>(1f, 1f),
            ]),
        });
        ```

        ## Samplingと補間

        keyframeは呼び出し側が時刻順に渡し、空配列は使えません。範囲外は先頭/末尾へclampされます。`Step`は前値を保持し、`Linear`は隣接値を補間します。現在`CubicSpline`指定はlinearへfallbackします。区間探索はlinear searchです。

        | 要素 | 意味 |
        | --- | --- |
        | `AnimationClip` | 名前、track集合、最大duration |
        | `Track<T>` | property path、keyframe、interpolation |
        | `Keyframe<T>` | 秒単位の時刻と値 |
        | `ClipCommand` / `ClipNode` | player DSL / graphでclipを評価 |

        ## Targetへ再生する

        ```csharp
        Animate.Clip(clip, target).Play(player, clock);
        ```

        サンプルはclipを`EcsAnimationTarget`へ適用し、更新された`LocalTransform`のtranslationを2D markerとして描くだけに絞っています。3D rendererの準備なしで、clip → path → target → componentという利用手順を確認できます。

        {{StoryRef("Learn/Animation/EcsClipSample")}}

        > [!WARNING]
        > pathはcompile-timeに検証されません。target側のbindingと文字列が一致しないtrackは見た目上「再生しているのに動かない」原因になります。

        - keyframeを未sortのまま渡す。
        - `CubicSpline`がtangentを評価すると仮定する。
        - 多数keyframeでlinear searchのcostを無視する。
        """;

    [Story]
    public static StoryResult TargetsAndBindings(StoryContext ctx) => $$"""
        # Targets and bindings

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/TargetsAndBindings", "Intermediate", "UI / 2D retained canvas / ECS", "Adapter specific", "Clips and tracks")}}

        `IAnimationTarget`はclip/graphの評価結果を実際のpropertyへ書く境界です。animation coreはSignal、canvas node、ECS componentを知らず、adapterだけがpath解決と型変換を担当します。

        ## 標準adapter

        | Package | Target | 書き込み先 |
        | --- | --- | --- |
        | `Luxel.Animation.UI` | `SignalAnimationTarget` | 登録した`Signal<T>` |
        | `Luxel.Animation.TwoD` | `RetainedCanvasAnimationTarget` | retained nodeのtransform / opacity等 |
        | `Luxel.Animation.ThreeD` | `EcsAnimationTarget` | entityのtransform component等 |

        ```csharp
        Action<float> setOpacity = SignalAnimationTarget.For(opacitySignal);
        Animate.Tween(setOpacity, 0f, 1f, 0.2f).Play(player, clock);
        ```

        ## Bindingの責務

        targetは`path`、runtime value、value shapeを受けて対応propertyへ適用します。UI transitionのようにSignal変更を起点とする仕組みと、clipが毎frame targetへ書く仕組みは入口が異なります。

        ```text
        Clip / Graph ── path + value ──> IAnimationTarget ──> Signal / Canvas / ECS
        ```

        {{StoryRef("Learn/Animation/EcsClipSample")}}

        ## 診断と失敗

        現在の標準targetはmalformed path、未binding path、未知propertyを黙って無視する場合があり、値は期待型へ直接castします。authoring時にpath一覧を検証し、testでは既知時刻をsampleして書き込み先をassertしてください。

        > [!IMPORTANT]
        > targetのthread affinityと寿命は呼び出し側が管理します。破棄済みUI/ECS objectへplayerやgraphをtickし続けないでください。

        - path typoをanimation timingの問題として調査する。
        - trackの値型とbindingの型を一致させない。
        - 同じpropertyをSignal transitionとclipから同時に駆動する。
        """;

    [Story]
    public static StoryResult GraphsAndBlending(StoryContext ctx) => $$"""
        # Graphs and blending

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/GraphsAndBlending", "Advanced", "Game / Gallery / Headless test", "Backend neutral", "Targets and bindings")}}

        `AnimationGraph`はroot `GraphNode`を毎frame評価し、`GraphEvaluator`へ集めたpath/valueを最後にtargetへflushします。これはsetterを独立にscheduleする`AnimationPlayer`とは別系統です。

        ## Nodeを構成する

        | Node | 役割 |
        | --- | --- |
        | `ClipNode` | clipを時刻でsample |
        | `BlendNode` | 2つの評価結果をweightで補間 |
        | `AddNode` | baseへadditive値をweight付き加算 |
        | custom `GraphNode` | 独自の評価構造 |

        ```csharp
        var blend = new BlendNode(
            new ClipNode(idleClip),
            new ClipNode(runClip),
            weight: 0.35f);
        GraphNode root = blend;
        var graph = new AnimationGraph(root, target)
        {
            StartTime = clock.TimeSec,
            Loop = true,
        };
        graph.Tick(clock);
        ```

        ## 時刻とweight

        graphも絶対時刻を使い、`(now - StartTime) * TimeScale`をrootへ渡します。non-loopはdurationで終端を評価して`Done=true`、loopはdurationでmoduloします。`Reset(startTimeAbs)`で再開します。

        サンプルは上下移動と左右移動の2つの`Vector2` clipを同じ`dot/position` pathへ出力し、`BlendNode.Weight`で混ぜた結果を2Dの丸として描きます。

        {{StoryRef("Learn/Animation/GraphSample")}}

        ## Blendの境界

        pathが両側にあればvalue shapeに応じて補間し、片側だけならその値を保持します。意味の異なるpropertyや型を同じpathへ混ぜないでください。

        > [!NOTE]
        > graphはpose storageそのものではなく、毎tick評価してtargetへ書くDAGです。評価後の値を次frameへ暗黙保持するとは限りません。

        - `StartTime`を設定せず、大きなabsolute timeを最初から渡す。
        - weight sourceを`[0,1]`へ管理しない。
        - additive clipを通常clipと同じ基準値でauthoringする。
        """;

    [Story]
    public static StoryResult StateMachines(StoryContext ctx) => $$"""
        # State machines

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/StateMachines", "Advanced", "Game / UI interaction / Headless test", "Backend neutral", "Graphs and blending")}}

        `StateMachine`は名前付き`State`とtrigger transitionを管理します。各stateはgraphを持ち、transition中はfrom/toを線形crossfadeして同じtargetへflushします。

        ## Stateとtransition

        ```csharp
        var machine = new StateMachine(target);
        var idle = new State("idle", new ClipNode(idleClip));
        var jump = new State("jump", new ClipNode(jumpClip));
        idle.AddTransition("press", jump, crossfadeSec: 0.15f);
        jump.AddTransition("land", idle, crossfadeSec: 0.10f);

        machine.AddState(idle).AddState(jump).SetInitial(idle);
        machine.Start(clock);
        machine.Trigger("press", clock);
        machine.Tick(clock);
        ```

        ## Runtime契約

        | API | 動作 |
        | --- | --- |
        | `SetInitial()` | current stateを指定 |
        | `Start(clock)` | initial未設定なら例外。state start timeを記録 |
        | `Trigger(name, clock)` | current stateの最初に一致したtransitionを開始 |
        | `Tick(clock)` | currentまたはcrossfadeを評価 |
        | `Current` | transition中は遷移先state |
        | `IsTransitioning` | from stateが保持されているか |

        {{StoryRef("Learn/Animation/StateMachineSample")}}

        ## UI transitionとの境界

        `SignalTransition`はSignalの値変化を短く補間するUI向け機構です。semanticな状態、trigger、clip crossfadeが必要なら`StateMachine`、単一propertyのhover/focus変化ならUI transitionを選びます。

        > [!WARNING]
        > trigger queueはありません。一致しないtriggerは何もせず、transition中のtriggerは現在の遷移先stateに対して検索されます。

        - initial stateを設定せず`Start()`する。
        - `Tick()`を呼ばずtriggerだけ送る。
        - crossfade秒をanimation clipの正規化weightと混同する。
        """;

    [Story]
    public static StoryResult ImportAndDebugging(StoryContext ctx) => $$"""
        # Import and debugging

        {{Toc()}}

        {{AnimationCourseCatalog.Meta("Learn/Animation/ImportAndDebugging", "Intermediate", "Tools / Gallery / Headless test", "Backend neutral", "State machines")}}

        `CssKeyframesImporter.Parse()`はCSS keyframesを`AnimationClip`へ変換する入口です。import後も通常のclip/track/target契約で再生されるため、parser、path binding、clock更新を別々に診断できます。

        ## Importする

        ```csharp
        string css = "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }";
        AnimationClip clip = CssKeyframesImporter.Parse(
            css, targetPrefix: "panel", durationSec: 0.4f);
        ```

        {{StoryRef("Learn/Animation/CssKeyframesSample")}}

        ## 診断順

        1. importerが対応するproperty/value構文か確認する。
        2. clipのtrack path、keyframe時刻、interpolationを列挙する。
        3. targetに同じpath/typeのbindingがあるか確認する。
        4. `Play` / `StartTime`とclockのepochを確認する。
        5. frameごとに`Update` / `Tick`されるか確認する。
        6. 既知時刻0、mid、endをheadless testでsampleする。

        | 症状 | 主な原因 |
        | --- | --- |
        | 最初から終端値 | start timeとabsolute timeのepoch不一致 |
        | 全く動かない | update漏れ、path mismatch、未知property |
        | 中間だけ違う | curve/interpolation、CSS非対応入力 |
        | completionが来ない | loop中、Stopで除去、時刻が進んでいない |

        ## 決定的なtest

        ```csharp
        player.Play(anim, v => observed = v);
        player.Update(0.25f);
        Assert.Equal(expectedMid, observed, tolerance);
        player.Update(0.50f);
        Assert.Equal(expectedEnd, observed, tolerance);
        ```

        > [!IMPORTANT]
        > importerが受理しないCSSをbrowserと同じように解釈すると仮定しないでください。対応外入力は小さなfixtureで失敗を固定し、必要ならimport前に正規化します。

        次は[Particles overview](story:Learn/Animation/Particles/Overview)で、curveを短命なvisual eventへ再利用します。
        """;
}
