namespace Luxel.Graphics.RenderGraph;

/// <summary>
/// レンダーグラフ内の論理バッファハンドル。Compile 相で物理 <see cref="GpuBuffer"/> に解決される。
/// External (<see cref="RenderGraph.ImportBuffer"/>) と Transient (<see cref="RenderGraph.CreateBuffer"/>) を統一して扱う。
/// 既定値 (<c>Id == 0</c>) は無効ハンドル。
/// </summary>
public readonly record struct BufferHandle
{
    public BufferHandle(int id)
    {
        Id = id;
        GraphId = 0;
    }

    internal BufferHandle(int id, long graphId)
    {
        Id = id;
        GraphId = graphId;
    }

    public int Id { get; }
    internal long GraphId { get; }
    public bool IsValid => Id != 0;
    public static BufferHandle Invalid => default;
}

/// <summary>論理テクスチャハンドル (RG-M6 で本格対応)。</summary>
public readonly record struct TextureHandle
{
    public TextureHandle(int id)
    {
        Id = id;
        GraphId = 0;
    }

    internal TextureHandle(int id, long graphId)
    {
        Id = id;
        GraphId = graphId;
    }

    public int Id { get; }
    internal long GraphId { get; }
    public bool IsValid => Id != 0;
    public static TextureHandle Invalid => default;
}

/// <summary>RenderGraph contract が定義する論理リソーススロット。</summary>
public readonly record struct RenderResourceSlotId(string Value);

/// <summary>論理リソーススロット内の stable symbolic version。</summary>
public readonly record struct RenderResourceVersionId(RenderResourceSlotId Slot, string Value);

/// <summary>callback 順に依存しない pass の stable symbolic key。</summary>
public readonly record struct RenderPassKey(string Value);
