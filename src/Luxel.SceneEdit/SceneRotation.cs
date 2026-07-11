using System.Numerics;

namespace Luxel.SceneEdit;

/// <summary>
/// クォータニオン ↔ オイラー角 (度) の変換 — インスペクタの Quat 表示/入力用 (ADR-0015:
/// **保存形式は Quat のまま**、オイラーは編集時の見せ方だけ)。規約は
/// <see cref="Quaternion.CreateFromYawPitchRoll"/> と同じ YXZ (yaw=Y → pitch=X → roll=Z)。
/// 度数の Vector3 は (X=pitch, Y=yaw, Z=roll)。ジンバル特異点 (pitch=±90°) 近傍では
/// 分解が一意でない — 往復は「同じ回転」を保証する (角度表現の一致ではない)。
/// </summary>
public static class SceneRotation
{
    /// <summary>オイラー角 (度、X=pitch/Y=yaw/Z=roll) からクォータニオンを作る。</summary>
    public static Quaternion FromEulerDegrees(Vector3 degrees)
        => Quaternion.CreateFromYawPitchRoll(
            degrees.Y * MathF.PI / 180f,
            degrees.X * MathF.PI / 180f,
            degrees.Z * MathF.PI / 180f);

    /// <summary>クォータニオンをオイラー角 (度、X=pitch/Y=yaw/Z=roll) に分解する。</summary>
    public static Vector3 ToEulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        // YXZ (yaw→pitch→roll) の分解
        float sinP = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinP) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinP) : MathF.Asin(sinP);
        float yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
    }
}
