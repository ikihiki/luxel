namespace Luxel.Assets;

/// <summary>テクスチャサンプラ (フィルタ + wrap mode)。</summary>
public sealed class AssetSampler
{
    public AssetFilter MagFilter { get; set; } = AssetFilter.Linear;
    public AssetFilter MinFilter { get; set; } = AssetFilter.Linear;
    public AssetMipFilter MipFilter { get; set; } = AssetMipFilter.Linear;
    public AssetWrapMode WrapU { get; set; } = AssetWrapMode.Repeat;
    public AssetWrapMode WrapV { get; set; } = AssetWrapMode.Repeat;
    public string? Name { get; set; }
}

public enum AssetFilter { Nearest, Linear }
public enum AssetMipFilter { None, Nearest, Linear }
public enum AssetWrapMode { Repeat, ClampToEdge, MirroredRepeat }
