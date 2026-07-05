namespace Luxel.RenderGraph;

/// <summary>
/// パスの execute lambda に渡される実行コンテキスト。物理 GPU リソースの解決と、
/// シェーダの root args 用に <see cref="GpuBuffer.BindlessIndex"/> を引くショートカットを提供する。
/// </summary>
public readonly struct PassContext
{
    private readonly RenderGraph _graph;

    /// <summary>このパスのコマンド記録先。</summary>
    public GpuCommandBuffer Cmd { get; }

    internal PassContext(RenderGraph graph, GpuCommandBuffer cmd)
    {
        _graph = graph;
        Cmd = cmd;
    }

    /// <summary>論理ハンドルを物理 <see cref="GpuBuffer"/> に解決する (Compile 後でのみ有効)。</summary>
    public GpuBuffer Buffer(BufferHandle handle) => _graph.ResolveBuffer(handle);

    /// <summary>論理ハンドルから <see cref="GpuBuffer.BindlessIndex"/> を引く (シェーダの root args に流し込む用)。</summary>
    public uint BindlessIndex(BufferHandle handle) => _graph.ResolveBuffer(handle).BindlessIndex;

    /// <summary>論理ハンドルを物理 <see cref="GpuTexture"/> に解決する (Compile 後でのみ有効)。</summary>
    public GpuTexture Texture(TextureHandle handle) => _graph.ResolveTexture(handle);
}
