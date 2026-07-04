using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 70: in-memory <see cref="AssetDocument"/> を構築 → <see cref="SceneBuilder.Build"/> で ECS 展開 →
/// TransformPropagateSystem で世界変換計算 → entity 数 / world matrix を検証。
/// AssetDocument 中間表現の動作確認。
/// </summary>
public static class Sample70SceneDocument
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 70: AssetDocument → ECS 展開 demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // 2 階層 (root + child) の最小 AssetDocument を組み立て
        var doc = new AssetDocument { SourceFormat = "memory" };
        doc.Materials.Add(new AssetMaterial { Name = "red", BaseColor = new Vector4(1, 0, 0, 1) });

        var mesh = new AssetMesh { Name = "triangle" };
        var prim = new AssetPrimitive
        {
            Positions = new[] { Vector3.Zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            Normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ },
            Indices = new uint[] { 0, 1, 2 },
            MaterialIndex = 0,
        };
        mesh.Primitives.Add(prim);
        doc.Meshes.Add(mesh);

        doc.Nodes.Add(new AssetNode { Name = "root", Translation = new Vector3(10, 0, 0), MeshIndex = 0 });
        doc.Nodes.Add(new AssetNode { Name = "child", Translation = new Vector3(0, 5, 0), MeshIndex = 0, ParentIndex = 0 });
        doc.Nodes[0].ChildrenIndices.Add(1);
        doc.RootNodes.Add(0);

        // ECS 展開
        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);

        Console.WriteLine($"  entities: {assets.NodeEntities.Count}");
        Console.WriteLine($"  meshes (GPU): {assets.Meshes.Count}");
        Console.WriteLine($"  materials: {assets.Materials.Count}");

        // TransformPropagate で child の world 位置を計算
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var childWorld = assets.NodeEntities[1].GetComponent<Luxel.Ecs.GlobalTransform>().Matrix;
        Vector3 pos = Vector3.Transform(Vector3.Zero, childWorld);
        Console.WriteLine($"  child world pos: {pos} (expect (10, 5, 0))");

        bool ok = Math.Abs(pos.X - 10) < 1e-4 && Math.Abs(pos.Y - 5) < 1e-4
                  && assets.NodeEntities.Count == 2 && assets.Meshes.Count == 1;
        Console.WriteLine(ok ? "OK: SC-M3 (SceneBuilder + Transform 階層) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
