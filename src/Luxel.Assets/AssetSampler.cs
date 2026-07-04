namespace Luxel.Assets;

/// <summary>テクスチャサンプラ (フィルタ + wrap mode)。</summary>
public sealed class AssetSampler
{
    /// <summary>拡大時のフィルタ。</summary>
    public AssetFilter MagFilter { get; set; } = AssetFilter.Linear;
    /// <summary>縮小時のフィルタ。</summary>
    public AssetFilter MinFilter { get; set; } = AssetFilter.Linear;
    /// <summary>mip level 間のフィルタ。</summary>
    public AssetMipFilter MipFilter { get; set; } = AssetMipFilter.Linear;
    /// <summary>U 方向の wrap mode。</summary>
    public AssetWrapMode WrapU { get; set; } = AssetWrapMode.Repeat;
    /// <summary>V 方向の wrap mode。</summary>
    public AssetWrapMode WrapV { get; set; } = AssetWrapMode.Repeat;
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
}

/// <summary>テクスチャの拡大/縮小フィルタ。</summary>
public enum AssetFilter
{
    /// <summary>最近傍サンプリング。</summary>
    Nearest,
    /// <summary>線形補間。</summary>
    Linear,
}

/// <summary>mip level 間のフィルタ。</summary>
public enum AssetMipFilter
{
    /// <summary>mipmap 不使用。</summary>
    None,
    /// <summary>最近傍の mip level を使用。</summary>
    Nearest,
    /// <summary>隣接 mip level 間を線形補間 (trilinear)。</summary>
    Linear,
}

/// <summary>UV 範囲外のアドレッシング。</summary>
public enum AssetWrapMode
{
    /// <summary>繰り返し (タイル)。</summary>
    Repeat,
    /// <summary>端のピクセルへクランプ。</summary>
    ClampToEdge,
    /// <summary>反転しながら繰り返し。</summary>
    MirroredRepeat,
}
