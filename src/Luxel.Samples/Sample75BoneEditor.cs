using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Scene.UI;

namespace Luxel.Samples;

/// <summary>
/// Sample 75: <see cref="BoneEditor"/> の動作確認 (Signal で TRS → Apply で ECS LocalTransform に反映)。
/// </summary>
public static class Sample75BoneEditor
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 75: BoneEditor demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // 3 つの bone を持つ scene
        var doc = new AssetDocument { SourceFormat = "memory" };
        doc.Nodes.Add(new AssetNode { Name = "spine",  Translation = new Vector3(0, 1, 0) });
        doc.Nodes.Add(new AssetNode { Name = "head",   Translation = new Vector3(0, 0.5f, 0), ParentIndex = 0 });
        doc.Nodes.Add(new AssetNode { Name = "armL",   Translation = new Vector3(-0.5f, 0, 0), ParentIndex = 0 });
        doc.Nodes[0].ChildrenIndices.AddRange(new[] { 1, 2 });
        doc.RootNodes.Add(0);

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);

        var editor = new BoneEditor(world, assets.NodeEntities);

        // bone 0 (spine) を編集: translation を (5, 0, 0) に
        editor.SelectedIndex.Value = 0;
        editor.TX.Value = 5;
        editor.TY.Value = 0;
        editor.TZ.Value = 0;
        editor.Apply();

        // ECS に反映されたか確認
        var spine = assets.NodeEntities[0];
        var lt = spine.GetComponent<Luxel.Ecs.LocalTransform>();
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Console.WriteLine($"  spine after edit: T={trans}");

        bool ok = Math.Abs(trans.X - 5) < 1e-3;
        Console.WriteLine(ok ? "OK: SC-UI-M2 (BoneEditor Signal → Apply ↔ ECS) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
