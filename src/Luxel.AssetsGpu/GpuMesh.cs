using Luxel.Assets;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetMesh"/> の GPU ミラー。1 mesh = 複数 primitive を含む。
/// GPU リソース (vertex/index buffer) は <see cref="GpuPrimitive"/> が所有し、Dispose で解放。
/// 手動作成も可能 (<see cref="Source"/> は null OK)。
/// </summary>
public sealed class GpuMesh : IDisposable
{
    /// <summary>元 <see cref="AssetMesh"/> (逆引き用、手動作成では null)。</summary>
    public AssetMesh? Source { get; init; }
    public string? Name { get; set; }
    public List<GpuPrimitive> Primitives { get; } = new();

    public void Dispose()
    {
        foreach (var p in Primitives) p.Dispose();
        Primitives.Clear();
    }
}
