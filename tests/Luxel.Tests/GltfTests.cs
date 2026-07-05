using System.Numerics;
using Luxel.Assets;
using Luxel.Gltf;

namespace Luxel.Tests;

public class GltfTests
{
    [Fact]
    public async Task GltfLoader_ParsesMinimalNode()
    {
        var json = """
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [{ "nodes": [0] }],
          "nodes": [{ "name": "root", "translation": [1, 2, 3] }]
        }
        """;
        var tmp = Path.GetTempFileName() + ".gltf";
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var doc = await new GltfLoader().LoadAsync(tmp);
            Assert.Single(doc.Nodes);
            Assert.Equal(new Vector3(1, 2, 3), doc.Nodes[0].Translation);
            Assert.Equal("root", doc.Nodes[0].Name);
            Assert.NotNull(doc.DefaultScene);
            Assert.Single(doc.DefaultScene!.Roots);
            Assert.Same(doc.Nodes[0], doc.DefaultScene!.Roots[0]);
            Assert.Equal("gltf", doc.SourceFormat);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task GltfLoader_ParsesHierarchy()
    {
        var json = """
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [{ "nodes": [0] }],
          "nodes": [
            { "name": "root", "children": [1] },
            { "name": "child", "translation": [0, 5, 0] }
          ]
        }
        """;
        var tmp = Path.GetTempFileName() + ".gltf";
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var doc = await new GltfLoader().LoadAsync(tmp);
            Assert.Equal(2, doc.Nodes.Count);
            Assert.Single(doc.Nodes[0].Children);
            Assert.Same(doc.Nodes[1], doc.Nodes[0].Children[0]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void GltfLoader_RegistersToAssetLoaders()
    {
        AssetLoaders.Clear();
        AssetLoaders.Register(new GltfLoader());
        var loader = new GltfLoader();
        Assert.True(loader.CanLoad("model.gltf"));
        Assert.True(loader.CanLoad("model.glb"));
        Assert.True(loader.CanLoad("MODEL.GLTF"));
        Assert.False(loader.CanLoad("model.fbx"));
        AssetLoaders.Clear();
    }

    private static string? FindKhronosSample(string filename)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", filename),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", filename),
            Path.Combine(AppContext.BaseDirectory, "tools", "khronos-samples", filename),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task GltfLoader_Box_HasMeshAndIndex()
    {
        var path = FindKhronosSample("Box.gltf");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Meshes.Count >= 1);
        Assert.True(doc.Meshes[0].Primitives.Count >= 1);
        var prim = doc.Meshes[0].Primitives[0];
        Assert.True(prim.Attributes.Positions.Length > 0);
        Assert.NotNull(prim.Indices);
        Assert.True(prim.Indices!.Length > 0);
    }

    [Fact]
    public async Task GltfLoader_BoxAnimated_HasAnimation()
    {
        var path = FindKhronosSample("BoxAnimated.glb");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Animations.Count >= 1);
        Assert.True(doc.Animations[0].Channels.Count >= 1);
        Assert.True(doc.Animations[0].Duration > 0);
    }

    [Fact]
    public async Task GltfLoader_Fox_HasSkin()
    {
        var path = FindKhronosSample("Fox.glb");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Skins.Count >= 1, "Fox should have skin");
        Assert.True(doc.Skins[0].Joints.Count > 0);
        Assert.True(doc.Animations.Count >= 1);
    }

    [Fact]
    public async Task GltfLoader_RiggedSimple_HasJointWeights()
    {
        var path = FindKhronosSample("RiggedSimple.glb");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Skins.Count >= 1);
        bool foundSkinned = false;
        foreach (var m in doc.Meshes)
            foreach (var p in m.Primitives)
                if (p.Attributes.Joints0 is not null && p.Attributes.Weights0 is not null) { foundSkinned = true; break; }
        Assert.True(foundSkinned, "RiggedSimple should have JOINTS_0/WEIGHTS_0");
    }

    [Fact]
    public async Task GltfLoader_CesiumMan_LargerScene()
    {
        var path = FindKhronosSample("CesiumMan.glb");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Skins.Count >= 1);
        Assert.True(doc.Skins[0].Joints.Count >= 10);
        Assert.True(doc.Animations.Count >= 1);
    }

    [Fact]
    public async Task GltfLoader_BrainStem_LargeMesh()
    {
        var path = FindKhronosSample("BrainStem.glb");
        if (path is null) return;
        var doc = await new GltfLoader().LoadAsync(path);
        Assert.True(doc.Nodes.Count > 10, $"BrainStem has multiple nodes, got {doc.Nodes.Count}");
        Assert.True(doc.Skins.Count >= 1);
        Assert.True(doc.Animations.Count >= 1);
    }
}
