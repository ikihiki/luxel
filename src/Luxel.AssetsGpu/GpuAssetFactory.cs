using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// stateless な CPU asset → GPU asset の変換ヘルパ。Resource システムを使わない場合 (Sample 直呼び) 用。
/// 呼び出し側が texture/material の dedup cache を渡すことで再アップロードを防ぐ。
/// </summary>
public static class GpuAssetFactory
{
    /// <summary>Mesh の頂点形式 (32B = pos 12 + nrm 12 + uv 8)。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord0;
        public const int Stride = 32;
    }

    /// <summary>Skinned mesh の頂点形式 (56B = pos 12 + nrm 12 + uv 8 + joints 8 + weights 16)。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SkinnedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord0;
        public uint Joints0, Joints1;   // 4x ushort packed
        public Vector4 Weights0;
        public const int Stride = 56;
    }

    public static GpuTexture Upload(AssetTexture tex, GpuDevice device)
    {
        if (tex.Width <= 0 || tex.Height <= 0 || tex.PixelData.Length == 0)
            throw new InvalidOperationException($"AssetTexture '{tex.Name}' は空です");
        return device.CreateTexture((uint)tex.Width, (uint)tex.Height, tex.PixelData, GpuFormat.Rgba8Unorm);
    }

    public static GpuSampler Upload(AssetSampler samp, GpuDevice device)
    {
        var filter = samp.MagFilter == AssetFilter.Nearest ? GpuSamplerFilter.Point : GpuSamplerFilter.Linear;
        var addr = samp.WrapU switch
        {
            AssetWrapMode.Repeat => GpuSamplerAddress.Repeat,
            AssetWrapMode.ClampToEdge => GpuSamplerAddress.Clamp,
            _ => GpuSamplerAddress.Repeat,
        };
        return device.CreateSampler(filter, addr);
    }

    public static GpuMaterial Upload(AssetMaterial mat, GpuDevice device,
        IReadOnlyDictionary<AssetTexture, GpuTexture>? texCache = null,
        IReadOnlyDictionary<AssetSampler, GpuSampler>? sampCache = null,
        GpuSampler? defaultSampler = null)
    {
        var gpu = new GpuMaterial
        {
            Source = mat,
            Name = mat.Name,
            BaseColorFactor = mat.BaseColorFactor,
            MetallicFactor = mat.MetallicFactor,
            RoughnessFactor = mat.RoughnessFactor,
            EmissiveFactor = mat.EmissiveFactor,
            EmissiveStrength = mat.EmissiveStrength,
            AlphaMode = mat.AlphaMode,
            AlphaCutoff = mat.AlphaCutoff,
            DoubleSided = mat.DoubleSided,
        };
        gpu.BaseColorTexture = ResolveTexture(mat.BaseColorTexture?.Texture, device, texCache);
        gpu.MetallicRoughnessTexture = ResolveTexture(mat.MetallicRoughnessTexture?.Texture, device, texCache);
        gpu.NormalTexture = ResolveTexture(mat.NormalTexture?.Texture, device, texCache);
        gpu.OcclusionTexture = ResolveTexture(mat.OcclusionTexture?.Texture, device, texCache);
        gpu.EmissiveTexture = ResolveTexture(mat.EmissiveTexture?.Texture, device, texCache);
        // 最初に見つかった texture の sampler を採用 (どのテクスチャも同じ sampler を想定)
        var samplerAsset = mat.BaseColorTexture?.Texture?.Sampler
                        ?? mat.NormalTexture?.Texture?.Sampler
                        ?? mat.MetallicRoughnessTexture?.Texture?.Sampler;
        gpu.Sampler = samplerAsset is not null
            ? (sampCache != null && sampCache.TryGetValue(samplerAsset, out var s) ? s : Upload(samplerAsset, device))
            : defaultSampler;
        return gpu;
    }

    public static GpuPrimitive Upload(AssetPrimitive prim, GpuDevice device, GpuMaterial? material = null)
    {
        var attr = prim.Attributes;
        bool hasSkinning = attr.Joints0 is not null && attr.Weights0 is not null;
        int n = attr.Positions.Length;
        var indices = prim.Indices ?? Array.Empty<uint>();

        GpuBuffer vbuf; int stride;
        if (!hasSkinning)
        {
            stride = Vertex.Stride;
            vbuf = device.Malloc((ulong)(n * stride), GpuMemoryKind.HostMapped);
            var span = vbuf.Span<Vertex>(n);
            for (int i = 0; i < n; i++)
            {
                span[i] = new Vertex
                {
                    Position = attr.Positions[i],
                    Normal = (attr.Normals is not null && i < attr.Normals.Length) ? attr.Normals[i] : Vector3.UnitZ,
                    TexCoord0 = (attr.TexCoord0 is not null && i < attr.TexCoord0.Length) ? attr.TexCoord0[i] : Vector2.Zero,
                };
            }
        }
        else
        {
            stride = SkinnedVertex.Stride;
            vbuf = device.Malloc((ulong)(n * stride), GpuMemoryKind.HostMapped);
            var span = vbuf.Span<SkinnedVertex>(n);
            var j = attr.Joints0!;
            var w = attr.Weights0!;
            for (int i = 0; i < n; i++)
            {
                ushort j0 = j[i * 4 + 0], j1 = j[i * 4 + 1], j2 = j[i * 4 + 2], j3 = j[i * 4 + 3];
                span[i] = new SkinnedVertex
                {
                    Position = attr.Positions[i],
                    Normal = (attr.Normals is not null && i < attr.Normals.Length) ? attr.Normals[i] : Vector3.UnitZ,
                    TexCoord0 = (attr.TexCoord0 is not null && i < attr.TexCoord0.Length) ? attr.TexCoord0[i] : Vector2.Zero,
                    Joints0 = (uint)(j0 | (j1 << 16)),
                    Joints1 = (uint)(j2 | (j3 << 16)),
                    Weights0 = w[i],
                };
            }
        }

        GpuBuffer? ibuf = null;
        if (indices.Length > 0)
        {
            ibuf = device.Malloc((ulong)(indices.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
            indices.AsSpan().CopyTo(ibuf.Span<uint>(indices.Length));
        }

        return new GpuPrimitive
        {
            Source = prim,
            VertexBuffer = vbuf,
            IndexBuffer = ibuf,
            VertexCount = n,
            IndexCount = indices.Length,
            VertexStride = stride,
            HasSkinning = hasSkinning,
            Material = material,
        };
    }

    public static GpuMesh Upload(AssetMesh mesh, GpuDevice device,
        IReadOnlyDictionary<AssetMaterial, GpuMaterial>? matCache = null)
    {
        var gpu = new GpuMesh { Source = mesh, Name = mesh.Name };
        foreach (var prim in mesh.Primitives)
        {
            GpuMaterial? m = null;
            if (prim.Material is not null && matCache is not null)
                matCache.TryGetValue(prim.Material, out m);
            gpu.Primitives.Add(Upload(prim, device, m));
        }
        return gpu;
    }

    public static GpuSkin Upload(AssetSkin skin, GpuDevice device)
    {
        var rb = new RenderBuffer<Matrix4x4>(device, Math.Max(1, skin.Joints.Count), $"skin:{skin.Name}");
        return new GpuSkin
        {
            Source = skin,
            Name = skin.Name,
            JointCount = skin.Joints.Count,
            JointMatrices = rb,
        };
    }

    private static GpuTexture? ResolveTexture(AssetTexture? asset, GpuDevice device,
        IReadOnlyDictionary<AssetTexture, GpuTexture>? cache)
    {
        if (asset is null) return null;
        if (cache is not null && cache.TryGetValue(asset, out var cached)) return cached;
        return Upload(asset, device);
    }
}
