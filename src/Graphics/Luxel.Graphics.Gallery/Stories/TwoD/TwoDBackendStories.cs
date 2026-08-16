using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>同じScene2DをGPU/Skiaへ渡し、どちらもGpuView内に表示するbackend比較。</summary>
public static class TwoDBackendStories
{
    private const uint Width = 256;
    private const uint Height = 160;

    public static StoryResult Backends(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device)
            return ctx.Snap(Muted("GPU device is required to present the backend comparison."));

        Scene2D Scene()
        {
            var scene = new Scene2D();
            scene.FillRect(Color2D.Rgba(20, 27, 40), 0, 0, Width, Height);
            scene.FillRoundedRect(Color2D.Rgba(47, 111, 237), 18, 18, 102, 64, 14);
            scene.FillCircle(Color2D.Rgba(245, 180, 55), 178, 54, 34);
            scene.BeginFill(Color2D.Rgba(236, 72, 100))
                .MoveTo(48, 104).LineTo(102, 146).LineTo(14, 146).Close().End();
            scene.StrokeLine(Color2D.Rgba(90, 210, 150), 5, 132, 132, 236, 100);
            return scene;
        }

        var gpuRasterizer = new GpuDeviceRasterizer2D(device);
        IRasterScene2D gpuScene = ((IRasterizer2D)gpuRasterizer).CreateScene(Scene());
        Widget gpu = GpuView(Width, Height, (_, surface, _) =>
        {
            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            gpuScene.Render(Camera2D.Pixels,
                new GpuRasterTarget2D(command, surface.Framebuffer, surface.Width, surface.Height));
            command.Finish();
            device.MainQueue.Submit(command);
            return GpuViewRenderResult.Ready;
        }, animated: false, dispose: () =>
        {
            gpuScene.Dispose();
            gpuRasterizer.Dispose();
        });

        Widget cpuContract = Border(
            background: Bind.From(() => UiTheme.T.Surface),
            padding: new Thickness(16),
            width: Width,
            height: Height)
        [VStack(8)[
            Heading("CPU backend contract", 3),
            Text("SkiaRasterizer2D consumes the same Scene2D and writes a synchronous RGBA target.", 13,
                width: Width - 32),
            Text("The browser-safe Gallery documents that native backend without loading Skia. Native pixel parity is validated in backend tests.", 12,
                color: Bind.From(() => UiTheme.T.TextMuted), width: Width - 32)
        ]];

        return ctx.Snap(HStack(12)[
            VStack(4)[Muted("GPU — live browser-safe rasterizer"), Frame(gpu)],
            VStack(4)[Muted("Skia — documented native contract"), Frame(cpuContract)]
        ]);
    }
}
