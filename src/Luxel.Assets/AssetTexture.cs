namespace Luxel.Assets;

/// <summary>
/// テクスチャ (image 統合)。ピクセルデータ + フォーマット + サンプラを一箇所に集約。
/// 同一 image を複数マテリアルで使いたい場合は同じ <see cref="AssetTexture"/> インスタンスを直接参照する。
/// </summary>
public sealed class AssetTexture
{
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public AssetImageFormat Format { get; set; } = AssetImageFormat.Rgba8;
    public byte[] PixelData { get; set; } = Array.Empty<byte>();
    /// <summary>null = デフォルトサンプラ (Linear / Repeat)。</summary>
    public AssetSampler? Sampler { get; set; }
    public int MipLevels { get; set; } = 1;
    /// <summary>"image/png" 等。debug / re-encode 用。</summary>
    public string? MimeType { get; set; }
    /// <summary>外部参照元 URI (hot-reload / debug 用)。ピクセルは常に <see cref="PixelData"/> に展開済。</summary>
    public string? SourceUri { get; set; }
    public Dictionary<string, object>? Extras { get; set; }
}

public enum AssetImageFormat
{
    Rgba8,
    Rgb8,
    R8,
    Rg8,
    Rgba16Float,
    Rgba32Float,
    Bc7Unorm,
    Bc5Unorm,
}
