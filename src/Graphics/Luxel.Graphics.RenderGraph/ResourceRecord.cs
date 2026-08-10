namespace Luxel.Graphics.RenderGraph;

internal enum ResourceKind { ExternalBuffer, TransientBuffer, ExternalTexture, TransientTexture }

/// <summary>論理リソースの内部表現。External は固定 GpuBuffer/Texture、Transient は Compile 相で物理割当。</summary>
internal sealed class ResourceRecord
{
    public required string Name;
    public required ResourceKind Kind;
    public required int Id;

    // External buffer
    public GpuBuffer? ExternalBuffer;
    // External texture
    public GpuTexture? ExternalTexture;

    // Transient
    public BufferDesc TransientBufferDesc;
    public TextureDesc TransientTextureDesc;

    // Compile 後に解決される物理リソース (External は固定参照)
    public GpuBuffer? PhysicalBuffer;
    public GpuTexture? PhysicalTexture;

    // 寿命解析の結果 (Compile 相で埋まる、未使用なら -1)
    public int FirstWritePass = -1;
    public int LastReadPass = -1;

    // Aliasing 検証用: 同形プール内で割り当てられた論理スロットインデックス (-1=未割当 or External)
    public int PhysicalSlot = -1;

    // Aliasing で他リソースと物理バッファ/テクスチャを共有しているか
    public bool IsAliased;

    public bool IsBuffer => Kind == ResourceKind.ExternalBuffer || Kind == ResourceKind.TransientBuffer;
    public bool IsTexture => Kind == ResourceKind.ExternalTexture || Kind == ResourceKind.TransientTexture;
    public bool IsTransient => Kind == ResourceKind.TransientBuffer || Kind == ResourceKind.TransientTexture;
}
