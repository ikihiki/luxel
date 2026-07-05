namespace Luxel.RenderGraph;

/// <summary>Transient バッファの記述。Compile 相で同形 (Size, Kind) なリソースをプールから再利用する。</summary>
public readonly record struct BufferDesc(ulong SizeBytes, GpuMemoryKind Kind = GpuMemoryKind.HostMapped);

/// <summary>テクスチャ種別 (RT / Depth)。aliasing グループ判定に使う。</summary>
public enum TextureKind
{
    Color,
    Depth,
}

/// <summary>Transient テクスチャの記述 (RG-M6)。同形 (Width/Height/Format/Kind) で寿命非重複なら aliasing される。</summary>
public readonly record struct TextureDesc(uint Width, uint Height, GpuFormat Format, TextureKind Kind = TextureKind.Color);

/// <summary>テクスチャ用の使用方法。auto-barrier のステージ決定に使う。</summary>
public enum TextureUsage
{
    /// <summary>カラーアタッチメント書込 (BeginRendering の color)。</summary>
    ColorAttachment,
    /// <summary>深度アタッチメント書込 (BeginRendering の depth)。</summary>
    DepthAttachment,
    /// <summary>ピクセルシェーダから sampled として読む。</summary>
    SampledPixel,
    /// <summary>転送ソース (CopyTextureToBuffer など)。</summary>
    CopySource,
    /// <summary>転送デスト (CopyToTexture)。</summary>
    CopyDest,
}

internal static class TextureUsageExtensions
{
    public static bool IsWrite(this TextureUsage u) => u switch
    {
        TextureUsage.ColorAttachment => true,
        TextureUsage.DepthAttachment => true,
        TextureUsage.CopyDest => true,
        _ => false,
    };

    public static GpuStage Stage(this TextureUsage u) => u switch
    {
        TextureUsage.ColorAttachment => GpuStage.ColorOutput,
        TextureUsage.DepthAttachment => GpuStage.DepthStencil,
        TextureUsage.SampledPixel => GpuStage.PixelShader,
        TextureUsage.CopySource => GpuStage.Copy,
        TextureUsage.CopyDest => GpuStage.Copy,
        _ => GpuStage.All,
    };
}

/// <summary>パスを記録するキュー種別。RG-M1 では Graphics / Compute のみ扱う (両方とも MainQueue で実行)。</summary>
public enum PassQueue
{
    Graphics,
    Compute,
    AsyncCompute,  // RG-M2 以降
}

/// <summary>
/// リソースの使用方法。Compile 相が pass 境界の <see cref="GpuStage"/>/<see cref="GpuHazard"/> を計算するための情報源。
/// </summary>
public enum ResourceUsage
{
    /// <summary>未指定 (Texture 系の access で使用)。</summary>
    None = 0,
    /// <summary>compute シェーダから読む (storage buffer read)。</summary>
    StorageBufferRead,
    /// <summary>compute シェーダから書く (storage buffer write)。</summary>
    StorageBufferWrite,
    /// <summary>compute シェーダから読み書きする (同一 pass で R+W)。</summary>
    StorageBufferReadWrite,
    /// <summary>ピクセルシェーダから読む (RG-M2 でテクスチャ系拡張)。</summary>
    SampledInPixelShader,
    /// <summary>頂点/ピクセルから読む uniform 的バッファ。</summary>
    UniformBuffer,
    /// <summary>間接ディスパッチ/描画の引数。</summary>
    IndirectArgs,
    /// <summary>転送 (コピー) ソース。</summary>
    CopySource,
    /// <summary>転送 (コピー) デスティネーション。</summary>
    CopyDest,
}

public static class ResourceUsageExtensions
{
    /// <summary>使用方法から書き込みか否かを判定する。</summary>
    public static bool IsWrite(this ResourceUsage u) => u switch
    {
        ResourceUsage.StorageBufferWrite => true,
        ResourceUsage.StorageBufferReadWrite => true,
        ResourceUsage.CopyDest => true,
        _ => false,
    };

    /// <summary>使用方法から発生するパイプラインステージ。</summary>
    public static GpuStage Stage(this ResourceUsage u) => u switch
    {
        ResourceUsage.StorageBufferRead => GpuStage.ComputeShader,
        ResourceUsage.StorageBufferWrite => GpuStage.ComputeShader,
        ResourceUsage.StorageBufferReadWrite => GpuStage.ComputeShader,
        ResourceUsage.SampledInPixelShader => GpuStage.PixelShader,
        ResourceUsage.UniformBuffer => GpuStage.AllGraphics,
        ResourceUsage.IndirectArgs => GpuStage.DrawIndirect,
        ResourceUsage.CopySource => GpuStage.Copy,
        ResourceUsage.CopyDest => GpuStage.Copy,
        _ => GpuStage.All,
    };
}
