using System.Numerics;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// パーティクル (タスク 16, .TwoD) のデモ — 左: バースト爆発 (全方位放出 + 重力 + 色/α フェード)、
/// 右: 連続放出の噴水 (円パーティクル)。<see cref="ParticleSystem"/> を固定シード + 固定 dt で一定ステップ
/// 事前実行し、<see cref="ParticleNode"/> (RetainedCanvas 統合、per-particle 色) で描く。決定的なので golden 安定。
/// Image と違い塗りなので原理的には Skia でも出るが、GPU/Skia の AA 差を避け golden は vk のみ。
/// </summary>
[StoryMeta("Examples/2D")]
public static class ParticleStories
{
    [Story]
    public static Widget Particles(StoryContext ctx) => ctx.Snap(Frame(GpuSceneBase.View(384, 192, new ParticleScene(), animated: false)));

    private sealed class ParticleScene : GpuSceneBase
    {
        private GpuDeviceRasterizer2D _raster = null!;
        private RetainedCanvas _canvas = null!;
        private IRasterScene2D _rasterScene = null!;

        protected override void OnInit()
        {
            _raster = Track(new GpuDeviceRasterizer2D(Device));
            _canvas = Track(new RetainedCanvas());
            _rasterScene = Track(_raster.CreateScene(_canvas));

            // 暗い背景 (パーティクルを映えさせる)
            UiNode bg = _canvas.AddChild(_canvas.Root);
            bg.Content = new Scene2D().FillRect(Color2D.White, 0, 0, W, H);
            bg.Color = Color2D.Rgba(22, 24, 32);

            // 左: バースト爆発 — 全方位 (spread π) + 重力 + 黄→赤透明フェード
            var burstCfg = new ParticleConfig(
                Life: ParticleValue.Range(0.4f, 0.9f), Speed: ParticleValue.Range(60, 160),
                SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: 260, Drag: 0.6f,
                Size: 5f, Color: new ParticleColor(Color2D.Rgba(255, 230, 120, 255), Color2D.Rgba(230, 60, 40, 0)),
                Shape: ParticleShape.Quad);
            var burst = new ParticleSystem(burstCfg, capacity: 120, seed: 0x2024);
            var burstNode = new ParticleNode(_canvas, _canvas.Root, burst);
            burst.Emit(new Vector3(112, 96, 0), 90);
            for (int f = 0; f < 20; f++) burst.Update(1f / 60);   // 0.33s — 最短寿命 0.4 より前で全生存
            burstNode.Sync();

            // 右: 連続噴水 — 上向き narrow spread + 円パーティクル + シアン→青透明
            var fountainCfg = new ParticleConfig(
                Life: ParticleValue.Range(0.7f, 1.1f), Speed: ParticleValue.Range(120, 170),
                SpreadRadians: 0.32f, BaseAngle: -MathF.PI / 2, Gravity: 300, Drag: 0f,
                Size: 4f, Color: new ParticleColor(Color2D.Rgba(130, 220, 255, 255), Color2D.Rgba(60, 90, 220, 0)),
                Shape: ParticleShape.Circle);
            var fountain = new ParticleSystem(fountainCfg, capacity: 220, seed: 0x1337);
            var fountainNode = new ParticleNode(_canvas, _canvas.Root, fountain);
            fountain.SetEmission(new Vector3(288, 176, 0), rate: 120);
            for (int f = 0; f < 50; f++) fountain.Update(1f / 60);   // 定常状態まで
            fountainNode.Sync();
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, OutBuffer, W, H));
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
