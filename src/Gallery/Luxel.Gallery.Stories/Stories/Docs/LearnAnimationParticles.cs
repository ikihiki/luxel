using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Animation curveと接続しつつparticle simulationと描画adapterを学ぶコース。</summary>
public static class LearnAnimationParticles
{
    [Story("Learn/Animation/Particles/Overview", Order = 9, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Particles overview

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/Overview", "Beginner", "Standalone / Gallery / Browser", "CPU simulation + 2D / UI / 3D adapters", "Animation import and debugging")}}

        Particleは短命なvisual eventを多数生成する仕組みです。`ParticleSystem`がemissionとsimulationを所有し、`ParticleBuffer`がSoA形式の生存particleを保持し、2D/UI/3D adapterが同じbufferを描画へ変換します。

        ## 全体構成

        ```text
        ParticleConfig ──> ParticleSystem ──> ParticleBuffer
              │                 │                    │
              └─ ICurve         ├─ IParticleSimulator
                                └─ ParticleForce     ├─ ParticleNode / ParticleView
                                                     └─ ParticleBillboards
        ```

        | 要素 | 責務 |
        | --- | --- |
        | `ParticleConfig` | life、speed、spread、gravity、drag、size、color、shape |
        | `ParticleSystem` | burst/continuous emission、seed、update順、capacity |
        | `ParticleBuffer` | position、velocity、age等のSoA storage |
        | `CpuParticleSimulator` | 寿命除去、gravity/drag、Euler integration |
        | adapter | bufferを2D path、UI widget、3D billboardへ同期 |

        ## 最小のburst

        ```csharp
        var particles = new ParticleSystem(config, capacity: 512, seed: 42);
        particles.Emit(new Vector3(120, 80, 0), count: 40);

        particles.Update(1f / 60f);
        renderer.Sync();
        ```

        {{StoryRef(ctx, "Examples/2D/ParticleView")}}

        ## Animationとの境界

        sizeとcolorは寿命を`[0,1]`へ正規化して`ICurve`で評価できます。一方、particleはsemanticな状態、clip path、graph transitionを持ちません。それらは[Animation本編](story:Learn/Animation/Overview)を使います。

        ## Particlesサブコース

        {{AnimationCourseCatalog.ParticleRouteMarkdown()}}

        > [!IMPORTANT]
        > 現在の標準simulationはCPUです。`IParticleSimulator`はextension seamですが、標準GPU simulatorが提供されているという意味ではありません。
        """;

    [Story("Learn/Animation/Particles/ValuesAndConfiguration", Order = 10, Toc = true)]
    public static StoryResult ValuesAndConfiguration(StoryContext ctx) => $$"""
        # Particle values and configuration

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/ValuesAndConfiguration", "Beginner", "Standalone / Headless", "CPU configuration", "Particles overview")}}

        `ParticleValue`は定数、spawn時range、寿命curveの3種類です。どの時点でrandom sampleし、どの時点で正規化寿命を評価するかが重要です。

        ## Const、Range、Curve

        | Kind | spawn時`Sample()` | 描画時`Eval(t01)` |
        | --- | --- | --- |
        | `Const(v)` | v | v |
        | `Range(min,max)` | `[min,max)`の一様乱数 | min |
        | `Curved(from,to,curve)` | from | curveでfrom→to |

        `Life`、`Speed`、初期`Size`はspawn時にsampleされます。range sizeは個体ごとに固定され、curve sizeだけが寿命中に変化します。`Eval()`は`t01`をclampし、curveがnullならlinearです。

        ```csharp
        var config = new ParticleConfig(
            Life: ParticleValue.Range(0.4f, 0.9f),
            Speed: ParticleValue.Range(60f, 160f),
            SpreadRadians: MathF.PI,
            BaseAngle: -MathF.PI / 2f,
            Gravity: 260f,
            Drag: 0.6f,
            Size: ParticleValue.Curved(5f, 0f, CubicBezierCurve.EaseOut),
            Color: new ParticleColor(
                Color2D.Rgba(255, 230, 120, 255),
                Color2D.Rgba(230, 60, 40, 0)),
            Shape: ParticleShape.Quad);
        ```

        ## Colorとtint

        `ParticleColor`はRGBA全channelを寿命curveで補間します。`Emit` / `SetEmission`の個体tintは、評価済みconfig colorへchannelごとに`a * b / 255`で乗算されます。packed literalより`Color2D.Rgba`を使うとbyte orderを明示できます。

        ## Emission shape

        通常はXY平面で`BaseAngle ± SpreadRadians`へ放射し、Z velocityは0です。`Spherical=true`では+Y軸周りのconeになり、spreadはhalf-angle、`MathF.PI`でfull sphereです。この場合`BaseAngle`は使いません。

        {{StoryRef(ctx, "Examples/2D/Particles")}}

        > [!NOTE]
        > sampled lifeは最低`1e-4`秒へclampされますが、speedとsizeは同じclampを受けません。2D adapterは負sizeをゼロへclampしますが、3D instanceには値がそのまま渡ります。
        """;

    [Story("Learn/Animation/Particles/EmissionAndSimulation", Order = 11, Toc = true)]
    public static StoryResult EmissionAndSimulation(StoryContext ctx) => $$"""
        # Emission and simulation

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/EmissionAndSimulation", "Intermediate", "Game loop / Headless test", "CPU simulator v1", "Particle values and configuration")}}

        burstは`Emit()`、連続放出は`SetEmission()`を使います。`Update(dt)`は固定stepを強制しないため、game側が適切な`dt`列を選びます。

        ## Burstとcontinuous emission

        ```csharp
        particles.Emit(position, count: 32, tint: Color2D.Rgba(255, 180, 80, 255));
        particles.SetEmission(position, rate: 120f);

        particles.Update(1f / 60f);
        particles.StopEmission(); // 生存particleは残る
        ```

        rateはparticles/secで、負値は0へclampされ、端数はupdate間で蓄積されます。

        ## Update順

        ```text
        1. continuous emissionを蓄積してspawn
        2. Forcesを現在の全生存particleへ適用
        3. simulator: age加算 → 寿命除去 → gravity → drag → position積分
        4. survivorをspawn順のままstable compaction
        ```

        新しく連続放出された個体も同じupdateでforceを受け、ageが進み、移動します。`Age >= LifeMax`になった個体はそのstepの移動前に除去されます。

        ## Capacityとbuffer

        capacityは構築時に固定され、0以下は例外です。`Emit`が満杯を超えた分は黙って無視され、古い個体をevictしません。continuous emission中に満杯になるとそのupdateのspawnを止め、fractional accumulatorを0へ戻します。

        | API | 動作 |
        | --- | --- |
        | `Alive` / `Capacity` | 生存数 / 固定上限 |
        | `Buffer` | `[0, Alive)`のSoA storage |
        | `Clear()` | 配列を再利用して生存数を0にする |
        | `Config` | 次のspawn/evaluationに使う設定 |

        {{StoryRef(ctx, "Examples/2D/Particles")}}

        > [!WARNING]
        > overflowは例外になりません。effectの欠落を診断したい場合は`Alive == Capacity`をtelemetryへ記録してください。
        """;

    [Story("Learn/Animation/Particles/ForcesAndDeterminism", Order = 12, Toc = true)]
    public static StoryResult ForcesAndDeterminism(StoryContext ctx) => $$"""
        # Forces and determinism

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/ForcesAndDeterminism", "Intermediate", "Headless test / Fixed-step game loop", "CPU SoA", "Emission and simulation")}}

        `ParticleForce`はsimulation前にSoA spanを直接変更するhookです。同じseed、同じemit call順、同じ`dt`列を使えばCPU結果を決定的に再現できます。

        ## Force hook

        ```csharp
        particles.Forces = (spans, dt) =>
        {
            for (int i = 0; i < spans.Count; i++)
            {
                spans.VelX[i] += windX * dt;
                spans.VelZ[i] += windZ * dt;
            }
        };
        ```

        `ParticleSpans`はposition、velocity、age、lifeの生存rangeを公開します。sizeやtintはこのhookから直接変更できません。

        ## CPU Euler v1

        ```csharp
        age += dt;
        if (age >= lifeMax) removeParticle();
        velY += gravity * dt;
        float dragFactor = MathF.Max(0f, 1f - drag * dt);
        velocity *= dragFactor;
        position += velocity * dt;
        ```

        dragは指数dampingではなく、stepごとの線形factorです。そのため可変`dt`では軌跡が変わります。

        ## 再現可能なtest

        ```csharp
        var a = new ParticleSystem(config, 64, seed: 1234);
        var b = new ParticleSystem(config, 64, seed: 1234);
        a.Emit(Vector3.Zero, 8);
        b.Emit(Vector3.Zero, 8);
        for (int i = 0; i < 30; i++)
        {
            a.Update(1f / 60f);
            b.Update(1f / 60f);
        }
        ```

        stable compactionはsurvivorのspawn順を維持するため、2D painter orderと3D instance orderも固定seed/update列で安定します。

        > [!IMPORTANT]
        > `Update`はnegative/巨大`dt`をvalidationしません。production loopでpositiveな上限付き固定stepを渡してください。

        - random seedだけ揃え、emit順や`dt`列を変える。
        - force内でframe wall timeや非決定的randomを読む。
        - dragをframerate非依存な指数式だと仮定する。
        """;

    [Story("Learn/Animation/Particles/Rendering2DAndUI", Order = 13, Toc = true)]
    public static StoryResult Rendering2DAndUI(StoryContext ctx) => $$"""
        # Rendering particles in 2D and UI

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/Rendering2DAndUI", "Intermediate", "Retained 2D / UI / Browser", "Graphics.TwoD + UI", "Forces and determinism")}}

        `ParticleNode`は生存particleを1個ずつabsolute-color pathへ変換します。`ParticleView`はそのnodeをwidgetへ埋め込み、必要ならanimation callbackで`Update → Sync`を所有します。

        ## ParticleNode

        ```csharp
        var node = new ParticleNode(canvas, canvas.Root, particles, circleSegments: 12);
        particles.Emit(new Vector3(120, 80, 0), 40);

        particles.Update(1f / 60f);
        node.Sync();
        ```

        X/Yを使いZは無視します。quadはaxis-aligned square、circleは指定segment数のregular polygonです。color/tintとcurve sizeを寿命で評価し、buffer順に描画します。

        ## ParticleView

        ```csharp
        Widget view = ParticleView(
            particles,
            viewWidth: 320,
            viewHeight: 180,
            animated: true,
            circleSegments: 12);
        ```

        `animated:true`は各UI tickで`ParticleSystem.Update(dt)`の後に`ParticleNode.Sync()`します。`animated:false`は初回Syncだけで、呼び出し側がsimulationと同期を管理します。

        {{StoryRef(ctx, "Examples/2D/ParticleView")}}
        {{StoryRef(ctx, "Examples/2D/Particles")}}

        ## Retained reservation

        path数は構築時の`system.Capacity`、segment reservationは構築時のshapeで決まります。quadは4 segment、circleは`circleSegments`です。

        > [!WARNING]
        > 構築後に`Config.Shape`をquadからcircleへ変えると、元のsegment予約を超える可能性があります。shapeまたはcapacityのlayout条件を変える場合はadapter/nodeを再作成してください。

        - `Update`後の`Sync`を忘れる。
        - `animated:true`のviewとgame loopの両方で同じsystemをupdateする。
        - UI local座標とworld座標を混同する。
        """;

    [Story("Learn/Animation/Particles/Rendering3D", Order = 14, Toc = true)]
    public static StoryResult Rendering3D(StoryContext ctx) => $$"""
        # Rendering particles in 3D

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/Rendering3D", "Advanced", "Native / Browser GPU", "Graphics.ThreeD particle billboards", "Rendering particles in 2D and UI")}}

        `ParticleBillboards`は全particleをcamera-facing instanced quadとして描画します。simulation後に`Sync()`し、cameraのright/upとview-projectionを`Draw()`へ渡します。

        ## 同期とdraw

        ```csharp
        using var billboards = new ParticleBillboards(device, particles);

        particles.Update(dt);
        billboards.Sync();
        (Vector3 right, Vector3 up) =
            ParticleBillboards.CameraAxes(camera.Eye, camera.Target);

        cmd.BeginRendering(...);
        billboards.Draw(cmd, camera.ViewProjection, right, up);
        cmd.EndRendering();
        ```

        `Sync()`はposition、寿命評価済みsize、color/tintをinstance bufferへ書き、現在の`InstanceCount`を記録します。`Draw()`は0 instanceなら何もしません。

        ## Pipeline契約

        | 項目 | 現在の動作 |
        | --- | --- |
        | geometry | 6 vertexのcamera-facing quad |
        | shape | `ParticleShape`を参照しない |
        | depth test | enabled |
        | depth write | disabled |
        | blend | alpha blend enabled |
        | order | spawn order、未sort |

        {{StoryRef(ctx, "Examples/3D/Particles")}}

        ## Camera axesと透明度

        `CameraAxes`はright-handed look-at basisを作ります。eyeとtargetが同一点、またはviewがworld +Yへ平行な退化ケースのfallbackはありません。透明particleはdepth sortされないため、重なりの正しいalpha合成は保証されません。

        > [!NOTE]
        > 3D adapterでは`Circle`もquad billboardです。円形に見せるにはtexture/shader側のalpha shapeが必要です。

        - simulation後に`Sync`せず古いinstanceをdrawする。
        - camera axesを別のhandednessで渡す。
        - unsorted transparencyをopaqueと同じ結果だと期待する。
        """;

    [Story("Learn/Animation/Particles/ResourcesAndDebugging", Order = 15, Toc = true)]
    public static StoryResult ResourcesAndDebugging(StoryContext ctx) => $$"""
        # Particle resources and debugging

        {{AnimationCourseCatalog.Meta("Learn/Animation/Particles/ResourcesAndDebugging", "Intermediate", "Resources / Tools / Headless test", "CPU resource step + optional render adapters", "Rendering particles in 3D")}}

        `ParticleConfigStep`はJSON bytesを`ParticleConfig`へ変換するCPU resource stepです。configをURIで共有・reloadするときは`ResourceSystem`へ明示登録します。

        ## Stepを登録してloadする

        ```csharp
        resources.AddStep<byte[], ParticleConfig>(new ParticleConfigStep());
        using ResourceHandle<ParticleConfig> handle =
            resources.Load<ParticleConfig>("effects/explosion.particle.json");
        await handle.Ready;

        particles.Config = handle.Value;
        ```

        ```json
        {
          "life": { "range": [0.4, 0.9] },
          "speed": { "range": [60, 160] },
          "spread": 3.14159,
          "angle": -1.5708,
          "gravity": 260,
          "drag": 0.6,
          "size": { "curve": [5, 0], "ease": "easeOut" },
          "color": { "start": "#FFE678FF", "end": "#E63C2800" },
          "shape": "quad"
        }
        ```

        ## JSONの制限

        | 制限 | 現在の結果 |
        | --- | --- |
        | `Spherical` | read/writeされない。JSON loadは常にfalse |
        | 未知のease名 | errorではなくnullになりlinear評価 |
        | 任意`ICurve` | built-in preset以外は`ease`をserializeできずlinearへround-trip |
        | shape未知値 | `Quad` |
        | range/curve配列 | 先頭2値だけ使用、2個未満は不正 |

        `#RRGGBB`はalpha FF、`#RRGGBBAA`も利用できます。対応easeは`linear`、`ease`、`easeIn`、`easeOut`、`easeInOut`で、hyphenは無視されます。

        ## Reloadと診断

        reload時に`particles.Config`を差し替えても、既存particleのsample済みlife/speed/sizeは作り直されません。次のspawnと、configから毎frame評価するcurve/colorへ新設定が影響します。shape/capacityに依存するrender reservationを変える場合はadapterを再作成します。

        ```text
        診断: Alive/Capacity → emission rate → fixed dt → force → Sync → blend/depth
        ```

        gizmoでemission position/directionを可視化し、seedと固定`dt`を記録すると再現しやすくなります。

        > [!IMPORTANT]
        > JSONへ`"spherical": true`を書いても現在は反映されません。3D spherical effectはC#側で`Spherical: true`を設定してください。

        - reload後に既存個体まで再sampleされると思う。
        - unknown easeをvalidation errorとして扱う。
        - arbitrary custom curveがJSON round-tripすると仮定する。
        """;
}
