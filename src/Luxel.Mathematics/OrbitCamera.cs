using System.Numerics;

namespace Luxel.Mathematics;

/// <summary>
/// ターゲットを中心にyaw/pitch/distanceで周回する3D軌道カメラ。
/// 純粋な<see cref="System.Numerics"/>計算で<see cref="ViewProjection"/>を求め、
/// <see cref="Orbit"/>と<see cref="Dolly"/>で姿勢と距離を更新する。
/// </summary>
public struct OrbitCamera
{
    /// <summary>注視点 (ワールド)。</summary>
    public Vector3 Target;
    /// <summary>方位角 (ラジアン、Y 軸まわり)。</summary>
    public float Yaw;
    /// <summary>仰角 (ラジアン、± で上下)。</summary>
    public float Pitch;
    /// <summary>注視点からの距離。</summary>
    public float Distance;
    /// <summary>垂直画角 (ラジアン)。</summary>
    public float FovYRadians;
    /// <summary>アスペクト比 (幅/高さ)。</summary>
    public float Aspect;
    /// <summary>ニアクリップ。</summary>
    public float Near;
    /// <summary>ファークリップ。</summary>
    public float Far;

    public OrbitCamera(Vector3 target, float yaw, float pitch, float distance,
        float fovYRadians, float aspect, float near = 0.1f, float far = 100f)
    {
        Target = target;
        Yaw = yaw;
        Pitch = pitch;
        Distance = distance;
        FovYRadians = fovYRadians;
        Aspect = aspect;
        Near = near;
        Far = far;
    }

    /// <summary>視点 (カメラ位置)。yaw/pitch/distance から算出。</summary>
    public readonly Vector3 Eye => Target + new Vector3(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw)) * Distance;

    /// <summary>ビュー行列 (右手系、上方向 +Y)。</summary>
    public readonly Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);
    /// <summary>透視投影行列。</summary>
    public readonly Matrix4x4 Projection => Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians, Aspect, Near, Far);
    /// <summary>view × proj。シェーダの root args にはこれ (必要なら転置) を渡す。</summary>
    public readonly Matrix4x4 ViewProjection => View * Projection;

    /// <summary>ドラッグで周回する。pitch は真上/真下 (ジンバルロック) の手前で ±<paramref name="pitchLimit"/> にクランプ。</summary>
    public void Orbit(float deltaYaw, float deltaPitch, float pitchLimit = 1.5f)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -pitchLimit, pitchLimit);
    }

    /// <summary>距離をズーム (係数を掛けて <paramref name="min"/>..<paramref name="max"/> にクランプ)。</summary>
    public void Dolly(float factor, float min, float max)
        => Distance = Math.Clamp(Distance * factor, min, max);
}
