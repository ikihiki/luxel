using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 73: SkinningSystem の動作確認 (joint matrix 計算)。
/// 手動で 2 joint の skin を組み、SkinningSystem を走らせて JointMatrices component を検証。
/// </summary>
public static class Sample73SceneSkinning
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 73: SkinningSystem demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var doc = new AssetDocument { SourceFormat = "memory" };
        // 2 joint: joint0 (origin)、joint1 (translation +x)
        doc.Nodes.Add(new AssetNode { Name = "joint0", Translation = new Vector3(0, 0, 0) });
        doc.Nodes.Add(new AssetNode { Name = "joint1", Translation = new Vector3(3, 0, 0) });
        // skinned mesh ノード
        doc.Nodes.Add(new AssetNode { Name = "mesh", SkinIndex = 0 });
        doc.RootNodes.AddRange(new[] { 0, 1, 2 });
        doc.Skins.Add(new AssetSkin
        {
            Name = "skin",
            JointNodeIndices = new[] { 0, 1 },
            InverseBindMatrices = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(-3, 0, 0) },
        });

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        SkinningSystem.Run(world, assets);

        var meshEntity = assets.NodeEntities[2];
        if (!meshEntity.HasComponent<JointMatrices>())
        { Console.Error.WriteLine("FAILED: no JointMatrices"); return 1; }
        var jm = meshEntity.GetComponent<JointMatrices>();
        Console.WriteLine($"  joints: {jm.Matrices.Length}");
        Console.WriteLine($"  joint0: {jm.Matrices[0].M41:F2},{jm.Matrices[0].M42:F2},{jm.Matrices[0].M43:F2}");
        Console.WriteLine($"  joint1: {jm.Matrices[1].M41:F2},{jm.Matrices[1].M42:F2},{jm.Matrices[1].M43:F2}");
        // joint0: identity * identity = identity
        // joint1: invBind(-3) * translation(+3) = identity
        bool ok = jm.Matrices.Length == 2;
        Console.WriteLine(ok ? "OK: SC-M4 (SkinningSystem joint matrix 計算) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
