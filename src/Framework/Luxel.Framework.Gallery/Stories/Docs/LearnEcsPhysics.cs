using Luxel.UI;
using static Luxel.Gallery.Story;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>ECS の上に PhysicsWorld と PhysicsStepSystem を接続する学習経路。</summary>
[StoryMeta("Learn/ECS/Physics")]
public static partial class LearnEcsPhysics
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # ECS Physics overview

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/Overview", "Intermediate", "Headless + Gallery", "CPU / BepuPhysics v2", "Diagnostics までの ECS 基礎")}}

        `Luxel.Physics` は BepuPhysics v2 を包む `PhysicsWorld` と、ECS component を body/shape へ接続する `PhysicsStepSystem` を提供します。Physics は独立した scene graph ではなく、ECS の `LocalTransform` を入出力境界として動きます。

        ## データの流れ

        ```text
        ECS components
          → Attach body / static / trigger
          → PhysicsWorld fixed step
          → dynamic pose を LocalTransform へ write-back
          → TransformPropagateSystem
          → render extraction
        ```

        ## 最小構成

        ```csharp
        using var world = new World();
        using var physics = new PhysicsWorld();
        var step = new PhysicsStepSystem(world, physics);

        world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),
            Collider.Box(1, 1, 1),
            RigidBody.Dynamic(mass: 1));

        step.Run(elapsedSeconds);
        ```

        `PhysicsStepSystem` は未 attach の component を発見し、body を一度だけ発行します。その後 `PhysicsWorld.Step` を固定 `FixedDt` で進め、動的 pose を `LocalTransform` へ書き戻します。

        ## 所有権

        `PhysicsWorld` は `Simulation` と `BufferPool` を所有するため dispose が必要です。ECS entity の削除だけでは body は消えません。despawn では `RemoveBody` / `RemoveStatic` を対で呼ぶか、決定的な reset として World と PhysicsWorld をまとめて再構築します。

        {{StoryRef("Learn/ECS/Physics/PhysicsFallingSample")}}
        """;

    [Story]
    public static StoryResult BodiesAndShapes(StoryContext ctx) => $$"""
        # Body と Shape

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/BodiesAndShapes", "Intermediate", "Headless + Gallery", "CPU / BepuPhysics v2", "Physics Overview")}}

        Physics entity は「形状」と「動き方」を別 component で表します。primitive shape は `Collider`、動的 body は `RigidBody`、固定 geometry は `StaticBody` です。

        ## Dynamic body

        ```csharp
        world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 5, 0)),
            Collider.Sphere(radius: 0.5f),
            RigidBody.Dynamic(
                mass: 2f,
                initialVelocity: new Vector3(2, 0, 0),
                ccd: true));
        ```

        `Collider` は Box、Sphere、Capsule を提供します。`RigidBody.Mass` が0以下なら attach 時に1として扱われ、`Continuous` は高速物体向け CCD を一度だけ設定します。

        ## Static body

        ```csharp
        world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(12, 1, 12),
            new StaticBody());
        ```

        static の pose は attach 時の `LocalTransform` で固定されます。後から component の行列だけを変えても Physics 側は移動しません。動く床は `PhysicsWorld.AddKinematic` と `KinematicBody` を使い、所有者が毎 step `SetBodyPose` します。

        ## Shape と見た目

        `Collider.RenderScale` は primitive を Cube mesh で近似表示する寸法です。Box は full size、Sphere は直径、Capsule は `(2r, length + 2r, 2r)` になります。描画 mesh と衝突形状が一致しているか gizmo で確認してください。

        ## 制約

        - `RigidBody` と `StaticBody` を同じ entity に付けない。
        - dynamic body に `Parent` を付けない。Physics pose は world 空間です。
        - CCD は必要な高速物体だけで有効にする。

        {{StoryRef("Learn/ECS/Physics/PhysicsPlaygroundSample")}}
        """;

    [Story]
    public static StoryResult FixedStep(StoryContext ctx) => $$"""
        # Fixed step

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/FixedStep", "Intermediate", "Headless + Frame loop", "CPU / BepuPhysics v2", "BodiesAndShapes、Interpolation")}}

        Physics は可変 frame time を直接 simulation へ渡さず、固定時間で進めます。既定は60Hz、1 unit = 1m、単スレッドです。

        ## Accumulator を PhysicsWorld に任せる

        ```csharp
        var settings = new PhysicsSettings
        {
            FixedDt = 1f / 60f,
            ThreadCount = 0,
        };
        using var physics = new PhysicsWorld(settings);
        var step = new PhysicsStepSystem(world, physics);

        int simulatedSteps = step.Run(elapsedSeconds);
        ```

        `PhysicsWorld.Step` は elapsed を accumulator へ積み、`FixedDt` ごとに0回以上進めます。巨大な elapsed は0.25秒へ clamp され、停止復帰時の spiral of death を抑えます。

        ## IGameScene の fixed loop と統合する

        ```csharp
        while (accumulator >= physics.FixedDt)
        {
            step.StepFixedOnce();
            accumulator -= physics.FixedDt;
        }
        float alpha = accumulator / physics.FixedDt;
        TransformInterpolationSystem.Run(world, alpha);
        ```

        accumulator を外側で持つ場合は `StepFixedOnce` を使い、二重 accumulator にしません。simulation pose を直接描画するか `InterpolatedTransform` へ積むかをアプリ側で一貫させます。

        ## 決定性

        `ThreadCount = 0` では、同じ初期状態と同じ固定 step 列から同じ結果を得る設計です。`ThreadCount > 0` は高速化の opt-in ですが決定性を失い、現在の contact collector も単スレッド前提です。

        ## 実行順

        Physics step は `Phase.Update`、transform 伝搬は `Phase.PostUpdate`、補間と描画抽出は `Phase.PreRender` に置きます。同じ frame で write-back より先に抽出しないでください。
        """;

    [Story]
    public static StoryResult CollisionsAndTriggers(StoryContext ctx) => $$"""
        # Collision と Trigger

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/CollisionsAndTriggers", "Intermediate", "Headless + Gallery", "CPU / BepuPhysics v2", "FixedStep")}}

        collision は接触応答を発生させ、trigger は通過を検知するだけで力を加えません。どちらも Begin / End を entity pair として読み取れます。

        ## Trigger を作る

        ```csharp
        var goal = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 1, 8)),
            Collider.Box(4, 2, 1),
            new Trigger());
        ```

        `Trigger` は static collidable として attach されます。動的 body が触れても反発せず、contact event だけが生成されます。

        ## Event を読む

        ```csharp
        step.StepFixedOnce();
        foreach (ContactEvent contact in step.ContactEvents)
        {
            bool isTrigger = contact.A.HasComponent<Trigger>()
                || contact.B.HasComponent<Trigger>();
            if (isTrigger && contact.Phase == ContactPhase.Begin)
                HandleTriggerEnter(contact.A, contact.B);
        }
        ```

        `ContactEvents` は今 frame の Begin / End だけを持ち、次の `Run` / `StepFixedOnce` 冒頭でクリアされます。後で処理する場合は必要な entity id やゲーム event へ変換して保存してください。

        ## Collision response の設定

        `PhysicsSettings.Friction`、`MaximumRecoveryVelocity`、`ContactSpring` は現在すべての接触に共通です。per-body material はありません。Bepu v2 の「跳ね」は古典的 restitution ではなく、回復速度上限と contact spring で調整します。

        ## よくある失敗

        - event list を次 frame まで保持する。
        - Trigger component の有無を確認せず通常 collision と同じ処理をする。
        - entity を削除したのに対応 body/static を残す。

        {{StoryRef("Learn/ECS/Physics/PhysicsTriggerSample")}}
        """;

    [Story]
    public static StoryResult MeshesAndRaycasts(StoryContext ctx) => $$"""
        # Mesh collider と Raycast

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/MeshesAndRaycasts", "Advanced", "Headless + Gallery", "CPU / BepuPhysics v2", "CollisionsAndTriggers")}}

        terrain や建物のような凹形状は静的な `MeshCollider`、動く実アセットは頂点群から作る動的 `HullCollider` を使います。

        ## Static mesh

        ```csharp
        world.CreateEntity(
            new LocalTransform(Matrix4x4.Identity),
            MeshCollider.Static(vertices, indices, scale: Vector3.One));
        ```

        `MeshCollider` は三角形3頂点ごとの index 配列を Bepu `Mesh` に変換し、static body として attach します。動的な凹 mesh は非対応です。

        ## Dynamic convex hull

        ```csharp
        world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),
            HullCollider.Dynamic(points, mass: 1f, ccd: true));
        ```

        Bepu は hull を重心原点へ recenter します。`PhysicsStepSystem` は `HullCollider.Center` を記録し、write-back で回転済み center を引いて元の mesh 原点へ戻します。凹形状は事前に複数の凸 hull へ分解してください。

        ## Closest raycast

        ```csharp
        Vector3 direction = Vector3.Normalize(target - origin);
        if (physics.RayCast(origin, direction, maximumT: 100f, out PhysicsRayHit hit))
        {
            Vector3 hitPoint = origin + direction * hit.T;
            Console.WriteLine($"normal={hit.Normal}, static={hit.IsStatic}");
        }
        ```

        `PhysicsRayHit` は距離 `T`、normal、static かどうか、raw handle value を返します。entity が必要なら `PhysicsStepSystem` と同様の body/static handle 対応表を所有してください。

        ## 入力検証

        index 数は3の倍数、index は vertices 範囲内、scale は有限値にします。mesh data と shape は `PhysicsWorld` の寿命に属するため、World の reset 時にまとめて再構築するのが安全です。

        {{StoryRef("Learn/ECS/Physics/PhysicsMeshSample")}}
        """;

    [Story]
    public static StoryResult GizmosAndDebugging(StoryContext ctx) => $$"""
        # Gizmo と Physics debugging

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Physics/GizmosAndDebugging", "Intermediate", "Gallery + DevTools", "CPU / 2D overlay", "MeshesAndRaycasts")}}

        Physics の不具合は「見た目の mesh」と「実際の shape」の差から起きやすいため、`PhysicsGizmos` で collider と接触中 pair を可視化します。

        ## Collider を描く

        ```csharp
        PhysicsGizmos.DrawColliders(
            world,
            dynamicColor: 0x44CCFFFF,
            staticColor: 0x88FF88FF,
            ccdColor: 0xFFAA33FF,
            triggerColor: 0xFF44FFFF);
        ```

        `gizmo.physics` category が無効なときは ECS query も allocation も行わず終了します。primitive は `Collider.RenderScale` の外接 wire box として描かれます。

        ## Contact marker を描く

        ```csharp
        step.TrackCurrentContacts = true;
        step.StepFixedOnce();
        PhysicsGizmos.ContactMarkers(step.CurrentContacts, 0xFF3333FF);
        ```

        current contact の収集は既定で無効です。gizmo が必要なときだけ `TrackCurrentContacts = true` にします。現在の marker は詳細な接触点ではなく、2 entity 中心の midpoint に置く接触インジケータです。

        ## 問題を切り分ける

        1. collider 色で dynamic / static / CCD / trigger を確認する。
        2. `Attached` と handle を確認する。
        3. `LocalTransform` と Physics pose のどちらが古いか確認する。
        4. fixed step が0回の frame と複数回の frame を記録する。
        5. contact event は frame 内、current contacts は opt-in という寿命を確認する。

        {{StoryRef("Learn/ECS/Physics/PhysicsGizmosSample")}}

        ECS と Physics、asset、GPU 抽出をまとめた実用構成は Range capstone で確認できます。

        """;
}
