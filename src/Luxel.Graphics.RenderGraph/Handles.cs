namespace Luxel.Graphics.RenderGraph;

/// <summary>
/// レンダーグラフ内の論理バッファハンドル。Compile 相で物理 <see cref="GpuBuffer"/> に解決される。
/// External (<see cref="RenderGraph.ImportBuffer"/>) と Transient (<see cref="RenderGraph.CreateBuffer"/>) を統一して扱う。
/// 既定値 (<c>Id == 0</c>) は無効ハンドル。
/// </summary>
public readonly record struct BufferHandle(int Id)
{
    public bool IsValid => Id != 0;
    public static BufferHandle Invalid => default;
}

/// <summary>論理テクスチャハンドル (RG-M6 で本格対応)。</summary>
public readonly record struct TextureHandle(int Id)
{
    public bool IsValid => Id != 0;
    public static TextureHandle Invalid => default;
}
