using Luxel.UI;
using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>ECS transform hierarchy と fixed-step 描画補間。</summary>
[StoryMeta("Learn/ECS")]
public static partial class LearnEcsTransforms
{
    [Story]
    public static StoryResult TransformHierarchy(StoryContext ctx) => $$"""
        # Transform hierarchy

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/TransformHierarchy", "Intermediate", "Headless + 3D", "CPU / AssetRuntime", "SystemsAndPhases")}}

        `LocalTransform` は親空間、`GlobalTransform` は world 空間の行列です。`Parent` を持つ entity は親の global 行列を掛けて world pose を得ます。

        ## 親子を作る

        ```csharp
        var parent = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(4, 0, 0)));
        var child = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 2, 0)),
            new Parent(parent));

        TransformPropagateSystem.Run(world);
        Matrix4x4 childWorld = child.GetComponent<GlobalTransform>().Matrix;
        ```

        Luxel は row-vector の合成規約を使い、子の global は `local.Matrix * parent.GlobalTransform.Matrix` です。`TransformPropagateSystem` は `GlobalTransform` が無い entity へ追加し、深い階層を最大8回反復して解決します。

        ## Phase の位置

        local pose を変える system の後、描画抽出の前に実行します。

        ```csharp
        world.AddSystem(Phase.Update, UpdateLocalTransforms);
        world.AddSystem(Phase.PostUpdate, () => TransformPropagateSystem.Run(world));
        world.AddSystem(Phase.PreRender, ExtractRenderInstances);
        ```

        ## 契約と制約

        - `Parent.ParentEntity` は同じ World の有効な entity を指す必要があります。
        - 循環参照を作らないでください。
        - 8階層を超える hierarchy は完全には伝搬しない可能性があります。
        - Physics の `RigidBody` は world-space pose を所有するため、`Parent` 付き entity では未定義動作です。

        {{StoryRef("Learn/ECS/EcsCubesTransformSample")}}
        """;

    [Story]
    public static StoryResult Interpolation(StoryContext ctx) => $$"""
        # Fixed-step の描画補間

        {{Toc()}}

        {{EcsCourseCatalog.Meta("Learn/ECS/Interpolation", "Intermediate", "Headless + Frame loop", "CPU", "TransformHierarchy、固定タイムステップ")}}

        simulation を固定間隔で進めても、display frame の時刻は step 境界と一致しません。`InterpolatedTransform` は前回と現在の TRS を保持し、描画直前に `alpha` で補間して `LocalTransform` へ書きます。

        ## FixedUpdate で状態を積む

        ```csharp
        ref InterpolatedTransform pose =
            ref entity.GetComponent<InterpolatedTransform>();
        pose.Push(nextPosition, nextRotation, nextScale);
        ```

        `Push` は current を previous へ送り、新しい current を保存します。固定 step 1回につき1回だけ呼びます。

        ## PreRender で sample する

        ```csharp
        float alpha = accumulator / fixedDt;
        TransformInterpolationSystem.Run(world, alpha);
        TransformPropagateSystem.Run(world);
        ```

        `TransformInterpolationSystem.Run` は `InterpolatedTransform` を query し、補間した `Scale * Rotation * Translation` を `LocalTransform` へ反映します。`LocalTransform` が無ければ追加します。

        ## Teleport と初期化

        ```csharp
        var pose = new InterpolatedTransform(position, rotation, Vector3.One);
        entity.AddComponent(pose);

        ref InterpolatedTransform current =
            ref entity.GetComponent<InterpolatedTransform>();
        current.Teleport(respawnPosition, Quaternion.Identity, Vector3.One);
        ```

        spawn や teleport で previous と current を別位置にすると、存在しない軌跡を1 frame 補間します。両方を同じ TRS にそろえて飛びを消してください。`alpha` は通常 `[0,1)` に保ちます。
        """;
}
