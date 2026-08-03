using System.Runtime.InteropServices;
using Luxel.Controls;
using Luxel.Resources;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// 2D/3D システムの描画結果をストーリーにする実例 (GS)。
/// - 2D: <see cref="Luxel.Controls.Canvas2D"/> — Scene2D を直接描く (UI と同じ保持型キャンバスの 1 ノード)
/// - 3D: <see cref="Luxel.Controls.GpuView"/> + render callback — offscreen へ自前レンダ →
///   image プリミティブでゼロコピー合成。リソースは <c>ctx.Resources</c> (ホスト所有) から
/// 時間はすべて Tick の累積秒 — snap の固定ステップ (8 × 1/60s) で決定的。
/// </summary>
public static class GpuStories
{
    // ---- 2D: Scene2D 直描き ----

    [Story("Examples/2D/Shapes", Height = 300, Order = 110)]
    public static Widget Shapes(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(384, 220, draw: s =>
    {
        s.FillRoundedRect(Tw.Blue500, 16, 16, 140, 90, 14);
        s.FillCircle(Tw.Amber500, 230, 60, 44);
        s.StrokeRoundedRect(Tw.Green500, 3, 290, 20, 80, 80, 10);
        s.BeginFill(Tw.Red500).MoveTo(60, 130).LineTo(150, 200).LineTo(20, 200).Close().End();
        s.StrokePolyline(Tw.Slate500, 2,
            new System.Numerics.Vector2(180, 200), new System.Numerics.Vector2(230, 140),
            new System.Numerics.Vector2(280, 190), new System.Numerics.Vector2(370, 130));
    })));

    [Story("Examples/2D/Orbit", Height = 300, Order = 111)]
    public static Widget Orbit(StoryContext ctx)
    {
        Signal<float> speed = ctx.Signal("speed", 1f, "軌道アニメの速度倍率");
        ctx.Play(static d => d.Snap());
        return Frame(Canvas2D(384, 220, animate: (s, t) =>
        {
            const float cx = 192, cy = 110;
            s.FillCircle(Tw.Amber500, cx, cy, 26);                       // 恒星
            s.StrokeRoundedRect(Tw.Slate500, 1, cx - 80, cy - 80, 160, 160, 80);   // 軌道
            float a = t * speed.Value;
            s.FillCircle(Tw.Blue500, cx + MathF.Cos(a) * 80, cy + MathF.Sin(a) * 80, 12);
            float a2 = t * speed.Value * 2.6f;
            s.FillCircle(Tw.Red500, cx + MathF.Cos(a2) * 44, cy + MathF.Sin(a2) * 44, 7);
        }));
    }

    // ---- 3D: offscreen 自前レンダ → image 合成 ----

    [Story(CanonicalClearColorRecipe.Story, Width = CanonicalClearColorRecipe.Width, Height = CanonicalClearColorRecipe.Height, Order = 119,
        RuntimeBundleId = "webgpu-browser-v1", CapabilityNote = "Specialized browser WebGPU ClearColor route.")]
    public static Widget ClearColor(StoryContext ctx)
        => ctx.Snap(Frame(GpuSceneBase.View(CanonicalClearColorRecipe.Width, CanonicalClearColorRecipe.Height, new ClearColorScene(), animated: false)));

    [Story(CanonicalTriangleRecipe.Story, Width = CanonicalTriangleRecipe.Width, Height = CanonicalTriangleRecipe.Height, Order = 120,
        RuntimeBundleId = "webgpu-browser-v1", CapabilityNote = "Specialized browser WebGPU validation route.")]
    public static Widget Triangle(StoryContext ctx)
        => ctx.Snap(Frame(GpuSceneBase.View(CanonicalTriangleRecipe.Width, CanonicalTriangleRecipe.Height, new TriangleScene())));

    [Story("Examples/3D/TexturedQuad", Height = 320, Order = 121)]
    public static Widget TexturedQuad(StoryContext ctx)
        => ctx.Snap(Frame(GpuSceneBase.View(320, 240, new TexturedScene(ctx.Resources), animated: false)));

    /// <summary>The native Gallery path for the shared canonical ClearColor recipe.</summary>
    private sealed class ClearColorScene : GpuSceneBase
    {
        protected override void OnInit() { }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer command = Device.MainQueue.StartCommandRecording();
            command.BeginRendering(Target, null,
                    CanonicalClearColorRecipe.Red, CanonicalClearColorRecipe.Green,
                    CanonicalClearColorRecipe.Blue, CanonicalClearColorRecipe.Alpha)
                .EndRendering();
            Surface.CopyColorToFramebuffer(command);
            command.Finish();
            Device.MainQueue.SubmitAndWait(command);
        }
    }

    /// <summary>The native Gallery path for the shared canonical first-triangle recipe.</summary>
    private sealed class TriangleScene : GpuSceneBase
    {
        private GpuBuffer _vertices = null!;
        private GpuPipeline _pipeline = null!;

        protected override void OnInit()
        {
            CanonicalTriangleRecipe.Vertex[] vertices = CanonicalTriangleRecipe.CreateVertices();
            _vertices = Track(Device.Malloc(checked((ulong)vertices.Length * CanonicalTriangleRecipe.VertexSize), GpuMemoryKind.HostMapped));
            vertices.CopyTo(_vertices.Span<CanonicalTriangleRecipe.Vertex>(vertices.Length));
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load(CanonicalTriangleRecipe.Shader),
                GpuRasterDesc.Default(GpuFormat.Rgba8Unorm)));
        }

        protected override void OnRender(float time)
        {
            var args = new CanonicalTriangleRecipe.DrawArgs { VertexBufferIndex = _vertices.BindlessIndex };
            using GpuCommandBuffer command = Device.MainQueue.StartCommandRecording();
            command.BeginRendering(Target, null, 0.055f, 0.07f, 0.11f, 1)
                .SetGraphicsPipeline(_pipeline)
                .SetRootArguments(args)
                .Draw(3)
                .EndRendering();
            Surface.CopyColorToFramebuffer(command);
            command.Finish();
            Device.MainQueue.SubmitAndWait(command);
        }
    }

    /// <summary>テクスチャ付きフルスクリーン三角形 (サンプル 03 の移植)。テクスチャは
    /// <c>ctx.Resources</c> から PNG をロード (初回ロードの publish は Pump 不要なので Init で待てる)。</summary>
    private sealed class TexturedScene(ResourceSystem resources) : GpuSceneBase
    {
        private const string ImageUri = "src/Luxel.Gallery/assets/sample-sparkline.png";

        [StructLayout(LayoutKind.Sequential)]
        private struct DrawArgs { public uint TextureIndex; public uint SamplerIndex; }

        private GpuTexture _texture = null!;
        private GpuSampler _sampler = null!;
        private GpuPipeline _pipeline = null!;

        protected override void OnInit()
        {
            ResourceHandle<CpuImage> image = Track(resources.Load<CpuImage>(ImageUri));
            try { image.Ready.Wait(5000); } catch { /* 失敗時は 1x1 白で続行 */ }
            CpuImage pixels = image.IsReady && image.Value is { Width: > 0 } ready
                ? ready : new CpuImage(1, 1, [255, 255, 255, 255]);
            _texture = Track(Device.CreateTexture((uint)pixels.Width, (uint)pixels.Height, pixels.Pixels));
            _sampler = Track(Device.CreateSampler(GpuSamplerFilter.Point));
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("textured"),
                GpuRasterDesc.Default(GpuFormat.Rgba8Unorm)));
        }

        protected override void OnRender(float time)
        {
            var args = new DrawArgs { TextureIndex = _texture.BindlessIndex, SamplerIndex = _sampler.BindlessIndex };
            using GpuCommandBuffer command = Device.MainQueue.StartCommandRecording();
            command.BeginRendering(Target, null, 0, 0, 0, 1)
                .SetGraphicsPipeline(_pipeline)
                .SetRootArguments(args)
                .Draw(3)
                .EndRendering();
            Surface.CopyColorToFramebuffer(command);
            command.Finish();
            Device.MainQueue.SubmitAndWait(command);
        }
    }
}
