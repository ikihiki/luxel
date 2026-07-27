using System.Numerics;
using Luxel.Particles;
using Luxel.Particles.ThreeD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// パーティクル (タスク 16, .ThreeD) のデモ — 3D 空間の球状バースト爆発を**カメラ向きビルボード**で描く
/// (<c>billboard.slang</c> がインスタンス quad を right/up 軸で展開)。<see cref="ParticleSystem"/> は
/// <c>Spherical</c> 放出 + 重力で、固定シード + 固定 dt を事前実行。<see cref="OrbitCamera"/> で見下ろす。
/// 深度テストあり・書き込み無し + アルファブレンド。Skia CPU では描けないので golden は vk のみ。
/// docs の Reference/Guides/TwoD 「パーティクル」節から参照される。
/// </summary>
public static class Particle3DStories
{
    [Story("Examples/3D/Particles", Height = 300, Order = 128)]
    public static Widget Particles(StoryContext ctx) => ctx.Snap(Frame(GpuView(256, 256, new Particle3DScene(), animated: false)));

    private sealed class Particle3DScene : GpuSceneBase
    {
        private ParticleBillboards _billboards = null!;
        private GpuTexture _depth = null!;

        protected override void OnInit()
        {
            _depth = Track(Device.CreateDepthTarget(W, H, GpuFormat.D32Float));

            var cfg = new ParticleConfig(
                Life: ParticleValue.Range(0.7f, 1.5f), Speed: ParticleValue.Range(1.5f, 4.0f),
                SpreadRadians: MathF.PI, BaseAngle: 0f, Gravity: -4.5f, Drag: 0.25f,
                Size: 0.13f, Color: new ParticleColor(Color2DRgba(255, 225, 140, 255), Color2DRgba(230, 70, 40, 0)),
                Shape: ParticleShape.Circle, Spherical: true);
            var ps = new ParticleSystem(cfg, capacity: 260, seed: 0xB007);
            _billboards = Track(new ParticleBillboards(Device, ps));

            ps.Emit(new Vector3(0, 0.2f, 0), 220);
            for (int f = 0; f < 28; f++) ps.Update(1f / 60);   // 0.47s — 最短寿命 0.7 より前で全生存
            _billboards.Sync();
        }

        protected override void OnRender(float time)
        {
            var cam = new OrbitCamera(new Vector3(0, 0.4f, 0), yaw: 0.6f, pitch: 0.32f, distance: 6.2f,
                fovYRadians: 0.9f, aspect: (float)W / H);
            (Vector3 right, Vector3 up) = ParticleBillboards.CameraAxes(cam.Eye, cam.Target);

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f);
            _billboards.Draw(cmd, cam.ViewProjection, right, up);
            cmd.EndRendering();
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }

        private static uint Color2DRgba(byte r, byte g, byte b, byte a)
            => (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }
}
