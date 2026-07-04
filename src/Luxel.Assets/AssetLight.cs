using System.Numerics;

namespace Luxel.Assets;

/// <summary>光源 (KHR_lights_punctual 相当)。</summary>
public sealed class AssetLight
{
    public string? Name { get; set; }
    public AssetLightKind Kind { get; set; }
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    /// <summary>point/spot: 光の届く最大距離 (null = 無限)。</summary>
    public float? Range { get; set; }
    /// <summary>spot: cone の inner (fall-off 開始角、ラジアン)。</summary>
    public float InnerConeAngle { get; set; }
    /// <summary>spot: cone の outer (完全に 0 になる角、ラジアン)。</summary>
    public float OuterConeAngle { get; set; } = MathF.PI / 4;
    public Dictionary<string, object>? Extras { get; set; }
}

public enum AssetLightKind { Directional, Point, Spot }
