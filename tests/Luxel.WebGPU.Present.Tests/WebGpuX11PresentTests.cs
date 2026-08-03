using Luxel.Graphics.WebGPU;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Silk;

namespace Luxel.WebGPU.Present.Tests;

public sealed class WebGpuX11PresentTests
{
    [Fact]
    public void Present_and_resize_X11_surface()
    {
        Assert.True(OperatingSystem.IsLinux());
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")));
        using var windows = new WindowSystem(SilkWindowBackend.Create());
        Window window = windows.CreateWindow(new WindowDesc("Luxel WebGPU present", 160, 120));
        SilkWindow nativeWindow = window.RequireBackendWindow<SilkWindow>();
        Assert.NotEqual(0, nativeWindow.X11Display);
        Assert.NotEqual(0UL, nativeWindow.X11Window);
        WebGpuBackend backend = WebGpuBackend.Create();
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateXlibSurface(nativeWindow.X11Display, nativeWindow.X11Window, 160, 120);
        using (GpuBuffer first = Pixels(device, 192, 160, 120, 0xFF2040E0u))
            surface.Present(first, 192, 160, 120);
        window.SetBounds(clientWidth: 224, clientHeight: 144);
        for (int i = 0; i < 5; i++) windows.Pump();
        surface.Resize(224, 144);
        using GpuBuffer second = Pixels(device, 256, 224, 144, 0xFF40C020u);
        surface.Present(second, 256, 224, 144);
        device.MainQueue.WaitIdle();
    }

    private static GpuBuffer Pixels(GpuDevice device, int stride, int width, int height, uint color)
    {
        GpuBuffer buffer = device.Malloc((ulong)(stride * height * 4));
        Span<uint> pixels = buffer.Span<uint>();
        for (int y = 0; y < height; y++) pixels.Slice(y * stride, width).Fill(color);
        using GpuCommandBuffer upload = device.MainQueue.StartCommandRecording();
        upload.Finish();
        device.MainQueue.Submit(upload);
        return buffer;
    }
}
