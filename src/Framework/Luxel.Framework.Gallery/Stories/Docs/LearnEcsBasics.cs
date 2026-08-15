using Luxel.UI;
using static Luxel.Gallery.Story;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>ECS の基礎を World から phase 実行まで段階的に説明する。</summary>
[StoryMeta("Learn/ECS")]
public static class LearnEcsBasics
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # ECS 学習ガイド

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Overview", "Beginner", "Headless + Gallery", "CPU / Friflo ECS", "C# の struct と generics")}}

        Entity Component System (ECS) は、ゲーム内の物を継承ツリーではなく、**entity、component data、system**へ分離して扱う設計です。Luxel の `World` は Friflo `EntityStore` を包み、描画や Physics に依存しない純粋な状態としてテストできます。

        ## この章で理解すること

        - entity は識別子、component は値、tag は値を持たない印です。
        - query は必要な component の組を持つ entity だけを処理します。
        - system は `PreUpdate` から `Render` までの phase に置きます。
        - transform、補間、保存、diagnostics を ECS のデータ境界として扱います。
        - Physics は ECS 基礎の上に載るため、後半の `Physics` サブカテゴリで学びます。

        ## 推奨ルート

        {{EcsCourseCatalog.RouteMarkdown()}}

        ## 最小の World

        ```csharp
        using var world = new World();
        var entity = world.CreateEntity(
            new LocalTransform(Matrix4x4.Identity),
            new Color3D(new Vector4(1, 0.4f, 0.2f, 1)),
            new MeshRef(MeshRef.Cube));
        ```

        `World` が所有するのは component data と system group です。GPU resource や window は component に直接埋め込まず、描画前に query して別の runtime 層へ抽出します。

        {{StoryRef(ctx, "Examples/3D/EcsCubes")}}

        ## 完成形への入口

        ECS、Physics、asset、GPU 抽出を統合した例は Advanced の capstone です。基礎ページを終えてから参照してください。

        """;

    [Story]
    public static StoryResult WorldAndEntities(StoryContext ctx) => $$"""
        # World と Entity

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/WorldAndEntities", "Beginner", "Headless", "CPU / Friflo ECS", "Overview")}}

        `World` は `EntityStore` の寿命と query/system の入口をまとめます。entity 自体は軽量な handle で、意味は保持する component の組み合わせによって決まります。

        ## Entity を作る

        ```csharp
        using var world = new World();
        var empty = world.CreateEntity();
        var named = world.CreateEntity(new DebugName("Player"));
        var visibleCube = world.CreateEntity(
            new LocalTransform(Matrix4x4.Identity),
            new Visible(true),
            new MeshRef(MeshRef.Cube));
        ```

        `World.CreateEntity` の convenience overload は component 3個までです。4個以上を同時に渡す場合は `world.Store.CreateEntity(...)` を使用できます。

        ## Component を追加・更新する

        ```csharp
        entity.AddComponent(new DebugName("Enemy"));
        ref LocalTransform transform = ref entity.GetComponent<LocalTransform>();
        transform.Matrix *= Matrix4x4.CreateTranslation(1, 0, 0);
        ```

        `Entity` は `World.Store` によって管理されます。別 World の entity を component 参照として混ぜず、削除後の handle を再利用しないでください。

        ## 削除と resource の境界

        ```csharp
        entity.DeleteEntity();
        ```

        entity の削除は ECS data を消しますが、外部 GPU resource や Physics body を自動破棄するとは限りません。外部 resource を発行した system は、despawn 時の対になる解放処理を持つか、World と runtime をまとめて再構築します。

        ## よくある失敗

        - component を持つ前に `GetComponent<T>()` を呼ぶ。
        - query 列挙中に entity の archetype を変更する。
        - ECS entity の寿命と外部 resource の寿命を同じだと思い込む。
        """;

    [Story]
    public static StoryResult ComponentsAndTags(StoryContext ctx) => $$"""
        # Component と Tag

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/ComponentsAndTags", "Beginner", "Headless", "CPU / Friflo ECS", "WorldAndEntities")}}

        component は `IComponent` を実装する struct で、entity の状態を保持します。tag は `ITag` を実装し、値を持たず query の分類に使います。

        ## Component は純データにする

        ```csharp
        public struct Health : IComponent
        {
            public int Current;
            public int Maximum;
        }

        var player = world.CreateEntity(new Health { Current = 100, Maximum = 100 });
        ```

        Luxel の標準 component には `LocalTransform`、`GlobalTransform`、`Parent`、`Color3D`、`MeshRef`、`Visible` があります。処理は system 側へ置き、component 自身に frame loop や service 参照を持たせません。

        ## Tag で状態を分類する

        ```csharp
        entity.AddTag<Enabled>();
        entity.AddTag<Selected>();

        var enabled = world.Query<LocalTransform>()
            .AllTags(Tags.Get<Enabled>());
        ```

        `Enabled`、`Selected`、`Dirty` は値を持たない marker です。頻繁に変わる数値は component、単純な所属や処理対象フラグは tag にすると意図が明確になります。

        ## 保存対象を決める

        `DebugName` は観測専用で `[ComponentKey(null)]` が付いているため `WorldSave` から除外されます。GPU handle、callback、service のような再構築可能な runtime 値も保存対象 component へ混ぜないでください。

        ## よくある失敗

        - class を component として使い、所有権と共有状態を曖昧にする。
        - tag に値を持たせようとする。
        - render resource の handle を永続データとして保存する。
        """;

    [Story]
    public static StoryResult Queries(StoryContext ctx) => $$"""
        # Query

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Queries", "Beginner", "Headless", "CPU / Friflo ECS", "ComponentsAndTags")}}

        query は必要な component の組を宣言し、一致する archetype だけを走査します。entity ごとの仮想 method 呼び出しではなく、data の組に対する処理として考えます。

        ## 1 component を更新する

        ```csharp
        world.Query<Health>().ForEachEntity(
            (ref Health health, Entity entity) =>
            {
                health.Current = Math.Min(health.Current + 1, health.Maximum);
            });
        ```

        ## 複数 component を結合する

        ```csharp
        world.Query<LocalTransform, Velocity>().ForEachEntity(
            (ref LocalTransform transform, ref Velocity velocity, Entity entity) =>
            {
                transform.Matrix *= Matrix4x4.CreateTranslation(velocity.Value * dt);
            });
        ```

        `World.Query<T1,T2,T3>()` は 3 component までを直接公開します。より高度な filter は `world.Store` の Friflo query API を使用します。

        ## 構造変更は収集してから適用する

        ```csharp
        var expired = new List<Entity>();
        world.Query<Lifetime>().ForEachEntity(
            (ref Lifetime life, Entity entity) =>
            {
                life.Seconds -= dt;
                if (life.Seconds <= 0) expired.Add(entity);
            });
        foreach (Entity entity in expired) entity.DeleteEntity();
        ```

        query 中の component 値の変更はできますが、component の追加・削除や entity 削除は archetype を変えます。Luxel の system と同様に「収集 → 適用」の2段階にすると安全です。

        {{StoryRef(ctx, "Examples/3D/EcsCubes")}}
        """;

    [Story]
    public static StoryResult SystemsAndPhases(StoryContext ctx) => $$"""
        # System と Phase

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/SystemsAndPhases", "Beginner", "Headless + Frame loop", "CPU / Friflo ECS", "Queries")}}

        system は query と状態更新をひとまとまりにし、phase は1フレーム内の実行順を固定します。Luxel の規約は `PreUpdate`、`Update`、`PostUpdate`、`PreRender`、`Render` です。

        ## Action を phase に登録する

        ```csharp
        using var world = new World();
        world.AddSystem(Phase.Update, () => MoveEntities(world, dt));
        world.AddSystem(Phase.PostUpdate, () => TransformPropagateSystem.Run(world));

        world.RunPhase(Phase.Update, new UpdateTick { deltaTime = dt });
        world.RunPhase(Phase.PostUpdate, new UpdateTick { deltaTime = dt });
        ```

        `AddSystem` は `Action` または Friflo `BaseSystem` を受け取ります。未登録 phase の `RunPhase` は no-op です。

        ## 標準 schedule を使う

        ```csharp
        SystemRoot root = ScheduleRoot.Create(world);
        root.GetGroup(Phase.Update).Add(new MovementSystem());
        root.Update(new UpdateTick { deltaTime = dt });
        ```

        `ScheduleRoot.Create` は5つの group を順に登録します。入力を `PreUpdate`、ゲームロジックを `Update`、transform 伝搬を `PostUpdate`、描画用抽出を `PreRender`、RenderGraph 実行を `Render` に置くのが基本です。

        ## 実行時間を観測する

        ```csharp
        world.EnableSystemPerfMonitor();
        world.RunPhase(Phase.Update, tick);
        foreach (var timing in world.CollectSystemTimings(Phase.Update))
            Console.WriteLine($"{timing.Name}: {timing.LastMs:F3} ms");
        ```

        順序に依存する system を同じ phase へ無計画に追加せず、data の producer と consumer がどの phase に属するかを決めてください。
        """;
}
