using System.Runtime.InteropServices;
using Luxel.AssetsGpu;
using Luxel.Resources;
using Luxel.Controls;
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
[StoryMeta("Examples")]
public static class GpuStories
{
    private const string ImageUri = "src/Gallery/Luxel.Gallery/assets/sample-sparkline.png";

    [StructLayout(LayoutKind.Sequential)]
    private struct TexturedDrawArgs
    {
        public uint TextureIndex;
        public uint SamplerIndex;
    }

    // ---- 2D: Scene2D 直描き ----

    [Story]
    public static StoryResult Orbit(StoryContext ctx)
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

    // ---- 3D: browser-safe GpuView stories are owned by this Graphics Gallery. ----

    [Story]
    public static StoryResult TexturedQuad(StoryContext ctx)
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
            new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm)));
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
                .SetRasterizerState(GpuRasterizerState.Default)
                .SetDepthStencilState(GpuDepthStencilState.Default)
                .SetBlendState(GpuBlendState.None)
                    .SetRootArguments(args)
                    .Draw(3)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.SubmitAndWait(command);
                return GpuViewRenderResult.Ready;
            },
            animated: false)));
    }

    private static Widget BuildOnlyGpuView(StoryContext ctx, float width, float height)
        => ctx.Snap(Frame(GpuView(width, height,
            static (_, _, _) => GpuViewRenderResult.Failed,
            animated: false)));

    private static void WaitFor<T>(ResourceHandle<T> handle)
        => handle.Ready.GetAwaiter().GetResult();
}
