using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.Ecs;

namespace Luxel.AssetRuntime;

/// <summary>
/// <see cref="AssetDocument"/> を Luxel.Ecs.World へ展開する builder。
/// direct-ref モデル: index lookup は使わず、AssetNode/AssetMesh/AssetMaterial の直接参照を辿る。
///
/// 1) Materials → GPU (bindless material array へ)
/// 2) Meshes → GPU (primitive 単位で vertex/index buffer を確保)
/// 3) Nodes を tree walk して Entity 展開 + Parent + LocalTransform + AssetMeshRef/AssetMaterialRef/AssetSkinRef
/// </summary>
public static class SceneBuilder
{
    /// <summary>Mesh vertex 形式 (32B = pos 12 + nrm 12 + uv 8)。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord0;
        public const int Stride = 32;
    }

    /// <summary>Skinned vertex 形式 (56B = pos 12 + nrm 12 + uv 8 + joints 8 + weights 16)。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SkinnedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord0;
        public uint Joints0;
        public uint Joints1;
        public Vector4 Weights0;
        public const int Stride = 56;
    }

    public static SceneAssets Build(Luxel.Ecs.World world, AssetDocument doc, GpuDevice device)
    {
        var assets = new SceneAssets();

        // === 既定サンプラ ===
        assets.DefaultSampler = device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);

        // === Materials → GPU (bindless array 用に順序を確定) ===
        foreach (var m in doc.Materials)
        {
            var gpu = new SceneMaterialGpu { BaseColor = m.BaseColorFactor };
            var baseTex = m.BaseColorTexture?.Texture;
            if (baseTex is not null)
                gpu.BaseColorTexture = UploadTexture(device, baseTex);
            assets.MaterialIndex[m] = assets.MaterialArray.Count;
            assets.MaterialArray.Add(gpu);
        }

        // === Skins index の割り当て ===
        foreach (var skin in doc.Skins)
        {
            assets.SkinIndex[skin] = assets.SkinArray.Count;
            assets.SkinArray.Add(skin);
        }

        // === Meshes → GPU (primitive 単位) ===
        foreach (var mesh in doc.Meshes)
        {
            var primList = new List<AssetPrimitive>();
            foreach (var prim in mesh.Primitives)
            {
                var gpu = BuildPrimitiveGpu(device, prim, assets.MaterialIndex);
                assets.Primitives[prim] = gpu;
                primList.Add(prim);
                assets.OrderedPrimitives.Add(prim);
            }
            assets.MeshPrimitives[mesh] = primList;
        }

        // === Nodes → Entity (tree walk) ===
        // 1 pass: 全 node に対して entity を確保 (Parent 解決の for 2nd pass)
        foreach (var scene in doc.Scenes)
            foreach (var root in scene.Roots)
                EnsureEntities(world, root, assets);
        // どの scene にも属さない standalone node (rare) にも対応
        foreach (var n in doc.Nodes)
            if (!assets.NodeEntities.ContainsKey(n))
                EnsureEntities(world, n, assets);

        // 2 pass: component を設定 (Parent 参照が全 node 揃ってから)
        foreach (var (node, entity) in assets.NodeEntities)
            ConfigureNode(world, node, entity, assets);

        return assets;
    }

    private static void EnsureEntities(Luxel.Ecs.World world, AssetNode node, SceneAssets assets)
    {
        if (assets.NodeEntities.ContainsKey(node)) return;
        assets.NodeEntities[node] = world.CreateEntity();
        foreach (var c in node.Children) EnsureEntities(world, c, assets);
    }

    private static void ConfigureNode(Luxel.Ecs.World world, AssetNode node, Entity entity, SceneAssets assets)
    {
        entity.AddComponent(new Luxel.Ecs.LocalTransform(node.LocalMatrix));
        if (!string.IsNullOrEmpty(node.Name))
            entity.AddComponent(new AssetNodeName(node.Name));

        // Children → 各 child entity に Parent(entity) を設定
        foreach (var c in node.Children)
            if (assets.NodeEntities.TryGetValue(c, out var childEntity))
                childEntity.AddComponent(new Luxel.Ecs.Parent(entity));

        // Mesh 参照
        if (node.Mesh is AssetMesh mesh)
        {
            var prims = mesh.Primitives;
            if (prims.Count > 0)
            {
                entity.AddComponent(new AssetMeshRef(mesh));
                var firstMat = prims[0].Material;
                if (firstMat is not null) entity.AddComponent(new AssetMaterialRef(firstMat));

                // primitive 1..N-1 は child entity として展開
                for (int p = 1; p < prims.Count; p++)
                {
                    var child = world.CreateEntity();
                    child.AddComponent(new Luxel.Ecs.LocalTransform(Matrix4x4.Identity));
                    child.AddComponent(new Luxel.Ecs.Parent(entity));
                    // Note: multi-primitive の child は Mesh 全体ではなく単一 primitive を指したいが、
                    // 現在は Mesh 単位の component しかないため mesh を保持し extractor 側で primitive[p] を選ぶ
                    child.AddComponent(new AssetMeshRef(mesh));
                    var pmat = prims[p].Material;
                    if (pmat is not null) child.AddComponent(new AssetMaterialRef(pmat));
                    if (!string.IsNullOrEmpty(node.Name))
                        child.AddComponent(new AssetNodeName($"{node.Name}/prim{p}"));
                }
            }
        }

        // Skin 参照
        if (node.Skin is AssetSkin skin)
            entity.AddComponent(new AssetSkinRef(skin));
    }

    private static ScenePrimitiveGpu BuildPrimitiveGpu(GpuDevice device, AssetPrimitive prim, Dictionary<AssetMaterial, int> matIdx)
    {
        var attr = prim.Attributes;
        bool hasSkinning = attr.Joints0 is not null && attr.Weights0 is not null;
        int n = attr.Positions.Length;
        int materialIndex = prim.Material is not null && matIdx.TryGetValue(prim.Material, out var mi) ? mi : -1;
        var indices = prim.Indices ?? Array.Empty<uint>();

        if (!hasSkinning)
        {
            var vbuf = device.Malloc((ulong)(n * Vertex.Stride), GpuMemoryKind.HostMapped);
            var vspan = vbuf.Span<Vertex>(n);
            for (int i = 0; i < n; i++)
            {
                vspan[i] = new Vertex
                {
                    Position = attr.Positions[i],
                    Normal = (attr.Normals is not null && i < attr.Normals.Length) ? attr.Normals[i] : Vector3.UnitZ,
                    TexCoord0 = (attr.TexCoord0 is not null && i < attr.TexCoord0.Length) ? attr.TexCoord0[i] : Vector2.Zero,
                };
            }
            var ibuf = device.Malloc((ulong)Math.Max(4, indices.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
            if (indices.Length > 0) indices.AsSpan().CopyTo(ibuf.Span<uint>(indices.Length));
            return new ScenePrimitiveGpu
            {
                VertexBuffer = vbuf,
                IndexBuffer = ibuf,
                VertexCount = n,
                IndexCount = indices.Length,
                VertexStride = Vertex.Stride,
                MaterialIndex = materialIndex,
                HasSkinning = false,
            };
        }
        else
        {
            var vbuf = device.Malloc((ulong)(n * SkinnedVertex.Stride), GpuMemoryKind.HostMapped);
            var vspan = vbuf.Span<SkinnedVertex>(n);
            var joints = attr.Joints0!;
            var weights = attr.Weights0!;
            for (int i = 0; i < n; i++)
            {
                ushort j0 = joints[i * 4 + 0];
                ushort j1 = joints[i * 4 + 1];
                ushort j2 = joints[i * 4 + 2];
                ushort j3 = joints[i * 4 + 3];
                vspan[i] = new SkinnedVertex
                {
                    Position = attr.Positions[i],
                    Normal = (attr.Normals is not null && i < attr.Normals.Length) ? attr.Normals[i] : Vector3.UnitZ,
                    TexCoord0 = (attr.TexCoord0 is not null && i < attr.TexCoord0.Length) ? attr.TexCoord0[i] : Vector2.Zero,
                    Joints0 = (uint)(j0 | (j1 << 16)),
                    Joints1 = (uint)(j2 | (j3 << 16)),
                    Weights0 = weights[i],
                };
            }
            var ibuf = device.Malloc((ulong)Math.Max(4, indices.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
            if (indices.Length > 0) indices.AsSpan().CopyTo(ibuf.Span<uint>(indices.Length));
            return new ScenePrimitiveGpu
            {
                VertexBuffer = vbuf,
                IndexBuffer = ibuf,
                VertexCount = n,
                IndexCount = indices.Length,
                VertexStride = SkinnedVertex.Stride,
                MaterialIndex = materialIndex,
                HasSkinning = true,
            };
        }
    }

    private static GpuTexture? UploadTexture(GpuDevice device, AssetTexture tex)
    {
        if (tex.Width <= 0 || tex.Height <= 0 || tex.PixelData.Length == 0) return null;
        return device.CreateTexture((uint)tex.Width, (uint)tex.Height, tex.PixelData, GpuFormat.Rgba8Unorm);
    }
}
