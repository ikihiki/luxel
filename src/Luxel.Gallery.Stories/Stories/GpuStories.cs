using System.Runtime.InteropServices;
using Luxel.AssetsGpu;
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
/// - 3D: story 関数で <c>ctx.ScopedResources</c> から必要なリソースを用意し、
///   <see cref="Luxel.Controls.GpuView"/> の render callback へ渡して offscreen 描画する
/// 時間はすべて Tick の累積秒 — snap の固定ステップ (8 × 1/60s) で決定的。
/// </summary>
public static class GpuStories
{
    private const string ImageUri = "src/Luxel.Gallery/assets/sample-sparkline.png";

    [StructLayout(LayoutKind.Sequential)]
    private struct TexturedDrawArgs
    {
        public uint TextureIndex;
        public uint SamplerIndex;
    }

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
            s.FillCircle(Tw.Amber500, cx, cy, 26);
            s.StrokeRoundedRect(Tw.Slate500, 1, cx - 80, cy - 80, 160, 160, 80);
            float a = t * speed.Value;
            s.FillCircle(Tw.Blue500, cx + MathF.Cos(a) * 80, cy + MathF.Sin(a) * 80, 12);
            float a2 = t * speed.Value * 2.6f;
            s.FillCircle(Tw.Red500, cx + MathF.Cos(a2) * 44, cy + MathF.Sin(a2) * 44, 7);
        }));
    }

    // ---- 3D: story scope のリソースを callback へ渡す ----

    [Story(CanonicalClearColorRecipe.Story, Width = CanonicalClearColorRecipe.Width, Height = CanonicalClearColorRecipe.Height, Order = 119,
        RuntimeBundleId = "webgpu-browser-v1", CapabilityNote = "Specialized browser WebGPU ClearColor route.")]
    public static Widget ClearColor(StoryContext ctx)
        => ctx.Snap(Frame(GpuView(
            CanonicalClearColorRecipe.Width,
            CanonicalClearColorRecipe.Height,
            static (device, surface, _) =>
            {
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null,
                        CanonicalClearColorRecipe.Red, CanonicalClearColorRecipe.Green,
                        CanonicalClearColorRecipe.Blue, CanonicalClearColorRecipe.Alpha)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.SubmitAndWait(command);
            },
            animated: false)));

    [Story(CanonicalTriangleRecipe.Story, Width = CanonicalTriangleRecipe.Width, Height = CanonicalTriangleRecipe.Height, Order = 120,
        RuntimeBundleId = "webgpu-browser-v1", CapabilityNote = "Specialized browser WebGPU validation route.")]
    public static Widget Triangle(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is null || ctx.ScopedResourcesOrNull is not { } resources)
            return BuildOnlyGpuView(ctx, CanonicalTriangleRecipe.Width, CanonicalTriangleRecipe.Height);

        CanonicalTriangleRecipe.Vertex[] vertices = CanonicalTriangleRecipe.CreateVertices();
        ResourceHandle<GpuBuffer> vertexBuffer = resources.CreateBuffer<CanonicalTriangleRecipe.Vertex>(
            "triangle.vertices", vertices.Length);
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "triangle.pipeline",
            GpuShaderCode.Load(CanonicalTriangleRecipe.Shader),
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        WaitFor(vertexBuffer);
        WaitFor(pipeline);
        vertices.CopyTo(vertexBuffer.Value.Span<CanonicalTriangleRecipe.Vertex>(vertices.Length));

        return ctx.Snap(Frame(GpuView(
            CanonicalTriangleRecipe.Width,
            CanonicalTriangleRecipe.Height,
            (device, surface, _) =>
            {
                var args = new CanonicalTriangleRecipe.DrawArgs
                {
                    VertexBufferIndex = vertexBuffer.Value.BindlessIndex,
                };
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(args)
                    .Draw(3)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.SubmitAndWait(command);
            },
            animated: false)));
    }

    [Story("Examples/3D/TexturedQuad", Height = 320, Order = 121)]
    public static Widget TexturedQuad(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is null || ctx.ScopedResourcesOrNull is not { } resources)
            return BuildOnlyGpuView(ctx, 320, 240);

        ResourceHandle<CpuImage> image = resources.Load<CpuImage>(ImageUri);
        image.Ready.GetAwaiter().GetResult();
        CpuImage pixels = image.Value is { Width: > 0 } ready
            ? ready
            : new CpuImage(1, 1, [255, 255, 255, 255]);
        ResourceHandle<GpuTexture> texture = resources.CreateSampledTexture(
            "textured.texture", (uint)pixels.Width, (uint)pixels.Height, pixels.Pixels);
        ResourceHandle<GpuSampler> sampler = resources.CreateSampler(
            "textured.sampler", GpuSamplerFilter.Point);
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "textured.pipeline",
            GpuShaderCode.Load("textured"),
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        WaitFor(texture);
        WaitFor(sampler);
        WaitFor(pipeline);

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (device, surface, _) =>
            {
                var args = new TexturedDrawArgs
                {
                    TextureIndex = texture.Value.BindlessIndex,
                    SamplerIndex = sampler.Value.BindlessIndex,
                };
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0, 0, 0, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(args)
                    .Draw(3)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.SubmitAndWait(command);
            },
            animated: false)));
    }

    private static Widget BuildOnlyGpuView(StoryContext ctx, float width, float height)
        => ctx.Snap(Frame(GpuView(width, height,
            static (_, _, _) => throw new InvalidOperationException(
                "GpuView was realized without a ResourceSystem-backed StoryContext."),
            animated: false)));

    private static void WaitFor<T>(ResourceHandle<T> handle)
        => handle.Ready.GetAwaiter().GetResult();
}
