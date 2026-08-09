using System.Numerics;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Resources;

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
            var doc = await DecodeFileAsync(tmp);
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
            var doc = await DecodeFileAsync(tmp);
            Assert.Equal(2, doc.Nodes.Count);
            Assert.Single(doc.Nodes[0].Children);
            Assert.Same(doc.Nodes[1], doc.Nodes[0].Children[0]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task GltfLoader_ParsesMorphTargetsAndDefaultWeights()
    {
        // バッファを C# で組んで base64 埋め込み (positions 3×vec3 / delta 3×vec3 / indices 3×u16)
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        float[] deltas = [0, 0, 0, 0, 0, 0, 0, 0, 1];   // 頂点 2 を +Z へ
        ushort[] indices = [0, 1, 2];
        var bytes = new List<byte>();
        foreach (float f in positions) bytes.AddRange(BitConverter.GetBytes(f));
        foreach (float f in deltas) bytes.AddRange(BitConverter.GetBytes(f));
        foreach (ushort u in indices) bytes.AddRange(BitConverter.GetBytes(u));
        string b64 = Convert.ToBase64String(bytes.ToArray());

        var json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [{ "nodes": [0] }],
          "nodes": [{ "mesh": 0 }],
          "meshes": [{
            "weights": [0.5],
            "primitives": [{
              "attributes": { "POSITION": 0 },
              "indices": 2,
              "targets": [{ "POSITION": 1 }]
            }]
          }],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0,  "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 6 }
          ],
          "buffers": [{ "uri": "data:application/octet-stream;base64,{{b64}}", "byteLength": {{bytes.Count}} }]
        }
        """;
        var tmp = Path.GetTempFileName() + ".gltf";
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var doc = await DecodeFileAsync(tmp);
            var prim = doc.Meshes[0].Primitives[0];
            Assert.NotNull(prim.MorphTargets);
            Assert.Single(prim.MorphTargets!);
            var target = prim.MorphTargets![0];
            Assert.NotNull(target.DeltaPositions);
            Assert.Equal(new Vector3(0, 0, 1), target.DeltaPositions![2]);   // 頂点 2 のデルタ
            // node.Weights は mesh.weights [0.5] からフォールバック
            Assert.NotNull(doc.Nodes[0].Weights);
            Assert.Equal(0.5f, doc.Nodes[0].Weights![0], 5);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void GltfResourceStep_DeclaresGltfExtensions()
    {
        var extensions = new GltfResourceStep().Extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".gltf", extensions);
        Assert.Contains(".glb", extensions);
        Assert.DoesNotContain(".fbx", extensions);
    }

    private static async Task<AssetDocument> DecodeFileAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetDirectoryName(fullPath)!;
        using var resources = ResourceTestSystem.Create(
            sources: [new RootedFileSource(root)],
            configure: (builder, handles) => builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
                .RunOn(handles.CpuDomain).ManagedBy(handles.CpuManager).Register());
        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>(Path.GetFileName(fullPath));
        await handle.Ready;
        return handle.Value;
    }

    private sealed class RootedFileSource(string root) : IResourceSource
    {
        public IEnumerable<string> Schemes => ["file", ""];

        public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context)
            => File.ReadAllBytesAsync(Path.Combine(root, Uri.UnescapeDataString(uri.Path)), context.Token);
    }

    private static string RequireKhronosSample(string sample, string filename)
    {
        string relativePath = Path.Combine(sample, filename);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", relativePath),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", relativePath),
            Path.Combine(AppContext.BaseDirectory, "tools", "khronos-samples", relativePath),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Pinned Khronos fixture was not acquired: {relativePath}");
    }

    private static string? FindOptionalKhronosSample(string filename)
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
        string path = RequireKhronosSample("Box", "Box.gltf");
        var doc = await DecodeFileAsync(path);
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
        string path = RequireKhronosSample("BoxAnimated", "BoxAnimated.glb");
        var doc = await DecodeFileAsync(path);
        Assert.True(doc.Animations.Count >= 1);
        Assert.True(doc.Animations[0].Channels.Count >= 1);
        Assert.True(doc.Animations[0].Duration > 0);
    }

    [Fact]
    public async Task GltfLoader_Fox_HasSkin()
    {
        var path = FindOptionalKhronosSample("Fox.glb");
        if (path is null) return;
        var doc = await DecodeFileAsync(path);
        Assert.True(doc.Skins.Count >= 1, "Fox should have skin");
        Assert.True(doc.Skins[0].Joints.Count > 0);
        Assert.True(doc.Animations.Count >= 1);
    }

    [Fact]
    public async Task GltfLoader_RiggedSimple_HasJointWeights()
    {
        string path = RequireKhronosSample("RiggedSimple", "RiggedSimple.glb");
        var doc = await DecodeFileAsync(path);
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
        var path = FindOptionalKhronosSample("CesiumMan.glb");
        if (path is null) return;
        var doc = await DecodeFileAsync(path);
        Assert.True(doc.Skins.Count >= 1);
        Assert.True(doc.Skins[0].Joints.Count >= 10);
        Assert.True(doc.Animations.Count >= 1);
    }

    [Fact]
    public async Task GltfLoader_BrainStem_LargeMesh()
    {
        var path = FindOptionalKhronosSample("BrainStem.glb");
        if (path is null) return;
        var doc = await DecodeFileAsync(path);
        Assert.True(doc.Nodes.Count > 10, $"BrainStem has multiple nodes, got {doc.Nodes.Count}");
        Assert.True(doc.Skins.Count >= 1);
        Assert.True(doc.Animations.Count >= 1);
    }
}
