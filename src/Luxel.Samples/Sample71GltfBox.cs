using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 71: Khronos <c>Box.gltf</c> を読み込み → AssetDocument → ECS 展開。
/// glTF JSON パース + buffer/accessor 抽出 + Mesh 構築の E2E 動作確認。
/// </summary>
public static class Sample71GltfBox
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 71: glTF (Box.gltf) → AssetDocument → ECS demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // tools/khronos-samples/Box.gltf を探す
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "Box.gltf"),
            Path.Combine(AppContext.BaseDirectory, "tools", "khronos-samples", "Box.gltf"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "Box.gltf"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Console.Error.WriteLine("FAILED: Box.gltf not found (download via: curl https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Models/main/2.0/Box/glTF/Box.gltf -o tools/khronos-samples/Box.gltf)");
            return 1;
        }

        var loader = new GltfLoader();
        var doc = loader.LoadAsync(path).GetAwaiter().GetResult();
        Console.WriteLine($"  loaded: {path}");
        Console.WriteLine($"  source: {doc.SourceFormat}");
        Console.WriteLine($"  meshes: {doc.Meshes.Count}, materials: {doc.Materials.Count}");
        Console.WriteLine($"  nodes: {doc.Nodes.Count}, root: {doc.RootNodes.Count}");
        Console.WriteLine($"  animations: {doc.Animations.Count}");

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        Console.WriteLine($"  ECS entities: {assets.NodeEntities.Count}, GPU meshes: {assets.Meshes.Count}");
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // 最初の primitive (Box の 24 vertex / 36 indices) を検証
        if (assets.Meshes.Count == 0)
        { Console.Error.WriteLine("FAILED: no GPU mesh"); return 1; }
        var firstMesh = assets.Meshes[0];
        Console.WriteLine($"  first mesh: vtx={firstMesh.VertexCount}, idx={firstMesh.IndexCount}");

        bool ok = doc.Meshes.Count > 0 && doc.Nodes.Count > 0 && firstMesh.VertexCount > 0 && firstMesh.IndexCount > 0;
        Console.WriteLine(ok ? "OK: GLB-M3 (Box.gltf → Scene → ECS) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
