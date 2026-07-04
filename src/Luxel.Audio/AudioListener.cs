using System.Numerics;

namespace Luxel.Audio;

/// <summary>
/// 3D 空間の「聴取者」。位置 + 前方向 + 上方向を持ち、<see cref="AudioSource3D"/> がこの listener 相対で
/// 距離減衰と pan を計算する。カメラの GlobalTransform と自然に紐付けられる。
/// </summary>
public sealed class AudioListener
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Forward { get; set; } = -Vector3.UnitZ;
    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>右方向 (Forward × Up)。pan 計算に使う。</summary>
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));
}
