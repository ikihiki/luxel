using System.Numerics;
using Luxel;
using Luxel.Graphics.TwoD;

namespace Luxel.Tests;

/// <summary>
/// カメラコントローラ (タスク 17) の GPU 不要・決定的テスト: デッドゾーン / 指数平滑の dt 分割安定性 /
/// 境界クランプ / シェイクの減衰と決定性 / OrbitCamera の viewProj。
/// </summary>
public class CameraRigTests
{
    private static CameraRig2D Rig(float smoothing = 0.15f) => new()
    {
        Smoothing = smoothing,
        ZoomSmoothing = 0f,
        Zoom = 1f,
    };

    [Fact]
    public void Deadzone_Inside_CameraDoesNotMove()
    {
        var rig = Rig(smoothing: 0f);
        rig.Deadzone = new Vector2(100, 100);
        rig.Target = new Vector2(0, 0);
        rig.SnapToTarget();
        rig.Target = new Vector2(30, 20);   // デッドゾーン半径 50 の内側
        rig.Update(1f / 60, 800, 480);
        Assert.Equal(0f, rig.Position.X, 3);
        Assert.Equal(0f, rig.Position.Y, 3);
    }

    [Fact]
    public void Deadzone_Outside_MovesByExcessOnly()
    {
        var rig = Rig(smoothing: 0f);   // 即時追従で goal を直接確認
        rig.Deadzone = new Vector2(100, 0);   // X 半径 50
        rig.Target = new Vector2(0, 0);
        rig.SnapToTarget();
        rig.Target = new Vector2(80, 0);   // 50 を 30 超過 → goal は Target-50 = 30
        rig.Update(1f / 60, 800, 480);
        Assert.Equal(30f, rig.Position.X, 3);
    }

    [Fact]
    public void ExponentialSmoothing_IsFrameRateIndependent()
    {
        // dt=0.1 一発 と dt=0.05 二発 が (goal 一定なら) 厳密に一致する。
        // pos=0 起点にするため Target=0 で Snap してから goal を動かす。
        var one = Rig(smoothing: 0.2f); one.Deadzone = Vector2.Zero; one.Target = Vector2.Zero; one.SnapToTarget();
        var two = Rig(smoothing: 0.2f); two.Deadzone = Vector2.Zero; two.Target = Vector2.Zero; two.SnapToTarget();
        one.Target = new Vector2(100, 0);
        two.Target = new Vector2(100, 0);

        one.Update(0.1f, 800, 480);
        two.Update(0.05f, 800, 480);
        two.Update(0.05f, 800, 480);

        Assert.Equal(one.Position.X, two.Position.X, 3);
        Assert.True(one.Position.X > 0 && one.Position.X < 100);   // 途中まで進む
    }

    [Fact]
    public void WorldBounds_ClampsCameraEdges()
    {
        var rig = Rig(smoothing: 0f);
        rig.Deadzone = Vector2.Zero;
        rig.WorldBounds = new RectF(0, 0, 1000, 1000);
        rig.Zoom = 1f;
        // 可視半幅 = 800/2 /1 = 400 → 中心は [400, 600] に制限される
        rig.Target = new Vector2(9999, 500);
        rig.SnapToTarget();
        rig.Update(1f / 60, 800, 480);
        Assert.Equal(600f, rig.Position.X, 2);   // MaxX(1000) - halfW(400)
    }

    [Fact]
    public void WorldBounds_TinyWorld_CentersAxis()
    {
        var rig = Rig(smoothing: 0f);
        rig.Deadzone = Vector2.Zero;
        rig.WorldBounds = new RectF(0, 0, 100, 100);   // 可視幅 800 より小さい → 中央固定
        rig.Target = new Vector2(9999, 9999);
        rig.SnapToTarget();
        rig.Update(1f / 60, 800, 480);
        Assert.Equal(50f, rig.Position.X, 2);   // CenterX
        Assert.Equal(50f, rig.Position.Y, 2);   // CenterY
    }

    [Fact]
    public void Shake_ReturnsToZero_ExactlyAfterDuration()
    {
        var rig = Rig(smoothing: 0f);
        rig.Target = Vector2.Zero;
        rig.SnapToTarget();
        rig.Shake(amplitude: 20f, duration: 0.5f, seed: 12345);
        // 0.5s ぶん進める
        for (int i = 0; i < 30; i++) rig.Update(1f / 60, 800, 480);
        Assert.False(rig.IsShaking);
        Assert.Equal(Vector2.Zero, rig.ShakeOffset);
    }

    [Fact]
    public void Shake_SameSeed_SameTrajectory()
    {
        var a = Rig(smoothing: 0f); a.Target = Vector2.Zero; a.SnapToTarget();
        var b = Rig(smoothing: 0f); b.Target = Vector2.Zero; b.SnapToTarget();
        a.Shake(15f, 1f, seed: 999);
        b.Shake(15f, 1f, seed: 999);
        for (int i = 0; i < 10; i++)
        {
            a.Update(1f / 60, 800, 480);
            b.Update(1f / 60, 800, 480);
            Assert.Equal(a.ShakeOffset.X, b.ShakeOffset.X, 5);
            Assert.Equal(a.ShakeOffset.Y, b.ShakeOffset.Y, 5);
        }
    }

    [Fact]
    public void Shake_Amplitude_WithinBounds()
    {
        var rig = Rig(smoothing: 0f); rig.Target = Vector2.Zero; rig.SnapToTarget();
        rig.Shake(amplitude: 10f, duration: 1f, seed: 7);
        for (int i = 0; i < 20; i++)
        {
            rig.Update(1f / 60, 800, 480);
            Assert.True(MathF.Abs(rig.ShakeOffset.X) <= 10f + 1e-3f);   // 振幅 × 減衰 ≤ 振幅
            Assert.True(MathF.Abs(rig.ShakeOffset.Y) <= 10f + 1e-3f);
        }
    }

    // ==================== OrbitCamera ====================

    [Fact]
    public void OrbitCamera_Eye_AtExpectedPosition()
    {
        // yaw=0, pitch=0, dist=5, target=origin → eye = (0,0,5)
        var cam = new OrbitCamera(Vector3.Zero, yaw: 0, pitch: 0, distance: 5, fovYRadians: 1f, aspect: 1.5f);
        Assert.Equal(0f, cam.Eye.X, 4);
        Assert.Equal(0f, cam.Eye.Y, 4);
        Assert.Equal(5f, cam.Eye.Z, 4);
    }

    [Fact]
    public void OrbitCamera_ViewProjection_ProjectsTargetToCenter()
    {
        var cam = new OrbitCamera(new Vector3(1, 2, 3), yaw: 0.7f, pitch: 0.3f, distance: 6, fovYRadians: 1.1f, aspect: 1.6f);
        Vector4 clip = Vector4.Transform(new Vector4(cam.Target, 1f), cam.ViewProjection);
        // 注視点はクリップ中心 (x=y=0) に写る
        Assert.Equal(0f, clip.X / clip.W, 3);
        Assert.Equal(0f, clip.Y / clip.W, 3);
        Assert.True(clip.W > 0);   // カメラ前方
    }

    [Fact]
    public void OrbitCamera_Orbit_ClampsPitch()
    {
        var cam = new OrbitCamera(Vector3.Zero, 0, 0, 5, 1f, 1f);
        cam.Orbit(0, 100f, pitchLimit: 1.5f);
        Assert.Equal(1.5f, cam.Pitch, 4);
        cam.Dolly(0.5f, min: 1f, max: 10f);
        Assert.Equal(2.5f, cam.Distance, 4);
    }
}
