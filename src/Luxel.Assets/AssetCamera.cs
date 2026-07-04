namespace Luxel.Assets;

/// <summary>カメラ (perspective / orthographic の union)。</summary>
public sealed class AssetCamera
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>投影の種類 (perspective / orthographic)。</summary>
    public AssetCameraKind Kind { get; set; } = AssetCameraKind.Perspective;

    // Perspective
    /// <summary>perspective: 垂直視野角 (ラジアン)。</summary>
    public float YFov { get; set; } = MathF.PI / 3;
    /// <summary>null = 動的計算 (viewport から)。</summary>
    public float? AspectRatio { get; set; }
    /// <summary>near クリップ面距離。</summary>
    public float ZNear { get; set; } = 0.1f;
    /// <summary>null = infinite far (reverse-Z 相当)。</summary>
    public float? ZFar { get; set; }

    // Orthographic
    /// <summary>orthographic: 水平方向の半径 (glTF の xmag)。</summary>
    public float OrthoXMag { get; set; }
    /// <summary>orthographic: 垂直方向の半径 (glTF の ymag)。</summary>
    public float OrthoYMag { get; set; }

    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}

/// <summary>カメラの投影の種類。</summary>
public enum AssetCameraKind
{
    /// <summary>透視投影。</summary>
    Perspective,
    /// <summary>平行投影。</summary>
    Orthographic,
}
