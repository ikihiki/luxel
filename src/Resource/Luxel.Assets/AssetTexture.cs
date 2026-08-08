namespace Luxel.Assets;

/// <summary>
/// テクスチャ (image 統合)。ピクセルデータ + フォーマット + サンプラを一箇所に集約。
/// 同一 image を複数マテリアルで使いたい場合は同じ <see cref="AssetTexture"/> インスタンスを直接参照する。
/// </summary>
public sealed class AssetTexture
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>幅 (px)。</summary>
    public int Width { get; set; }
    /// <summary>高さ (px)。</summary>
    public int Height { get; set; }
    /// <summary>ピクセルフォーマット (既定は Rgba8)。</summary>
    public AssetImageFormat Format { get; set; } = AssetImageFormat.Rgba8;
    /// <summary>デコード済みピクセルデータ (Format に従うレイアウト)。</summary>
    public byte[] PixelData { get; set; } = Array.Empty<byte>();
    /// <summary>null = デフォルトサンプラ (Linear / Repeat)。</summary>
    public AssetSampler? Sampler { get; set; }
    /// <summary><see cref="PixelData"/> に含まれる mip level 数 (1 = base のみ)。</summary>
    public int MipLevels { get; set; } = 1;
    /// <summary>"image/png" 等。debug / re-encode 用。</summary>
    public string? MimeType { get; set; }
    /// <summary>外部参照元 URI (hot-reload / debug 用)。ピクセルは常に <see cref="PixelData"/> に展開済。</summary>
    public string? SourceUri { get; set; }
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>テクスチャのピクセルフォーマット。</summary>
public enum AssetImageFormat
{
    /// <summary>RGBA 各 8bit unorm。</summary>
    Rgba8,
    /// <summary>RGB 各 8bit unorm。</summary>
    Rgb8,
    /// <summary>R 8bit unorm (単チャンネル)。</summary>
    R8,
    /// <summary>RG 各 8bit unorm。</summary>
    Rg8,
    /// <summary>RGBA 各 16bit float (HDR)。</summary>
    Rgba16Float,
    /// <summary>RGBA 各 32bit float (HDR)。</summary>
    Rgba32Float,
    /// <summary>BC7 圧縮 (高品質 RGBA)。</summary>
    Bc7Unorm,
    /// <summary>BC5 圧縮 (2 チャンネル、ノーマルマップ向け)。</summary>
    Bc5Unorm,
}
