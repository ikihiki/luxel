using System.Numerics;

namespace Luxel.Assets;

/// <summary>光源 (KHR_lights_punctual 相当)。</summary>
public sealed class AssetLight
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>光源の種類 (directional / point / spot)。</summary>
    public AssetLightKind Kind { get; set; }
    /// <summary>光の色 (linear RGB)。</summary>
    public Vector3 Color { get; set; } = Vector3.One;
    /// <summary>光の強度 (directional: lux、point/spot: candela)。</summary>
    public float Intensity { get; set; } = 1.0f;
    /// <summary>point/spot: 光の届く最大距離 (null = 無限)。</summary>
    public float? Range { get; set; }
    /// <summary>spot: cone の inner (fall-off 開始角、ラジアン)。</summary>
    public float InnerConeAngle { get; set; }
    /// <summary>spot: cone の outer (完全に 0 になる角、ラジアン)。</summary>
    public float OuterConeAngle { get; set; } = MathF.PI / 4;
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>光源の種類 (KHR_lights_punctual 相当)。</summary>
public enum AssetLightKind
{
    /// <summary>平行光源 (方向のみ、位置なし)。</summary>
    Directional,
    /// <summary>点光源 (全方位)。</summary>
    Point,
    /// <summary>スポットライト (cone 制限付き)。</summary>
    Spot,
}
