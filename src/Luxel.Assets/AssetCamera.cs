namespace Luxel.Assets;

/// <summary>カメラ (perspective / orthographic の union)。</summary>
public sealed class AssetCamera
{
    public string? Name { get; set; }
    public AssetCameraKind Kind { get; set; } = AssetCameraKind.Perspective;

    // Perspective
    public float YFov { get; set; } = MathF.PI / 3;
    /// <summary>null = 動的計算 (viewport から)。</summary>
    public float? AspectRatio { get; set; }
    public float ZNear { get; set; } = 0.1f;
    /// <summary>null = infinite far (reverse-Z 相当)。</summary>
    public float? ZFar { get; set; }

    // Orthographic
    public float OrthoXMag { get; set; }
    public float OrthoYMag { get; set; }

    public Dictionary<string, object>? Extras { get; set; }
}

public enum AssetCameraKind { Perspective, Orthographic }
