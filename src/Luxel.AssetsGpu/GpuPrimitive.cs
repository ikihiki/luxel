using Luxel.Assets;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetPrimitive"/> の GPU ミラー。1 draw call 単位。
/// vertex/index buffer を所有 (Dispose で解放)、material は借用参照 (別途所有)。
/// </summary>
public sealed class GpuPrimitive : IDisposable
{
    public AssetPrimitive? Source { get; init; }
    public GpuBuffer VertexBuffer { get; init; } = null!;
    public GpuBuffer? IndexBuffer { get; init; }
    public int VertexCount { get; init; }
    public int IndexCount { get; init; }
    /// <summary>頂点構造体の byte stride (32=Vertex, 56=SkinnedVertex 等、shader が使う)。</summary>
    public int VertexStride { get; init; }
    /// <summary>skinning 有効 (JOINTS_0/WEIGHTS_0 頂点属性を持つ)。shader variant 選択用。</summary>
    public bool HasSkinning { get; init; }
    /// <summary>マテリアル直接参照 (nullable)。</summary>
    public GpuMaterial? Material { get; set; }

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
    }
}
