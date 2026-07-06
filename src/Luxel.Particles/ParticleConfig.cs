using Luxel.Animation;

namespace Luxel.Particles;

/// <summary>パーティクルの描画形状 (v1)。</summary>
public enum ParticleShape
{
    /// <summary>軸並行の四角 (1 パス 4 線分)。</summary>
    Quad,
    /// <summary>円 (n 角形近似)。</summary>
    Circle,
}

/// <summary>
/// 寿命に沿って start→end を補間する RGBA (α 含む)。<see cref="ICurve"/> 省略時は線形。
/// 色は RGBA8 (R が下位バイト、<c>Luxel.TwoD.Color2D</c> と同じ並び)。
/// </summary>
public readonly record struct ParticleColor(uint Start, uint End, ICurve? Curve = null)
{
    public static ParticleColor Const(uint c) => new(c, c);

    /// <summary>寿命 t01∈[0,1] での色 (各チャンネル線形補間、α 含む)。</summary>
    public uint Eval(float t01)
    {
        float k = (Curve ?? LinearCurve.Instance).Eval(Math.Clamp(t01, 0f, 1f));
        return LerpRgba(Start, End, k);
    }

    private static uint LerpRgba(uint a, uint b, float k)
    {
        byte L(int shift) => (byte)(((a >> shift) & 0xFF) + (((int)((b >> shift) & 0xFF) - (int)((a >> shift) & 0xFF)) * k));
        return (uint)(L(0) | (L(8) << 8) | (L(16) << 16) | (L(24) << 24));
    }
}

/// <summary>
/// パーティクルシステムの設定。放出方向は <see cref="BaseAngle"/> ± <see cref="SpreadRadians"/> (ラジアン、XY 平面)。
/// <see cref="Gravity"/> は Y 速度への加速度 (2D 画面座標では +Y が下方向)。<see cref="Drag"/> は毎秒の速度減衰率。
/// </summary>
public sealed record ParticleConfig(
    ParticleValue Life,
    ParticleValue Speed,
    float SpreadRadians,
    float BaseAngle,
    float Gravity,
    float Drag,
    ParticleValue Size,
    ParticleColor Color,
    ParticleShape Shape = ParticleShape.Quad);
