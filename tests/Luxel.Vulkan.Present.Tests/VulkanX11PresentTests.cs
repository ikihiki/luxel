using System.Diagnostics;
using Luxel.Abstraction;
using Luxel.Platform.Silk;
using Luxel.Vulkan;

namespace Luxel.Vulkan.Present.Tests;

public sealed class VulkanX11PresentTests
{
    [Fact]
    public void MappedRgbaBuffer_PresentsOneFrame()
    {
        RequireLinuxDisplay();
        using SilkWindowBackend windowBackend = SilkWindowBackend.Create();
        using var windows = new WindowSystem(windowBackend);
        NativeWindow window = windows.CreateWindow(new WindowDesc("Luxel Vulkan present", 160, 120));
        PumpUntil(windows, () => window.Width == 160 && window.Height == 120);

        IVulkanWindowSurface provider = Assert.IsAssignableFrom<IVulkanWindowSurface>(
            window.GetFeature<IVulkanWindowSurface>());
        Assert.Contains("VK_KHR_surface", provider.RequiredInstanceExtensions);

        using var device = new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = true,
            Presentation = VulkanPresentationMode.Window,
            WindowSurface = provider,
        }));
        using GpuSurface surface = window.CreateSwapchain(device);
        using GpuBuffer pixels = CreatePixels(device, 160, 120, 0xFF2040E0u);

        surface.Present(pixels, 160, 160, 120);
        Assert.True(windows.Pump());
    }

    [Fact]
    public void Resize_RecreatesSwapchainAndPresents()
    {
        RequireLinuxDisplay();
        using SilkWindowBackend windowBackend = SilkWindowBackend.Create();
        using var windows = new WindowSystem(windowBackend);
        NativeWindow window = windows.CreateWindow(new WindowDesc("Luxel Vulkan resize", 128, 96));
        PumpUntil(windows, () => window.Width == 128 && window.Height == 96);

        IVulkanWindowSurface provider = Assert.IsAssignableFrom<IVulkanWindowSurface>(
            window.GetFeature<IVulkanWindowSurface>());
        using var device = new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = true,
            Presentation = VulkanPresentationMode.Window,
            WindowSurface = provider,
        }));
        using GpuSurface surface = window.CreateSwapchain(device);
        using (GpuBuffer initial = CreatePixels(device, 128, 96, 0xFF30C050u))
            surface.Present(initial, 128, 128, 96);

        window.SetBounds(clientWidth: 224, clientHeight: 144);
        PumpUntil(windows, () => window.Width == 224 && window.Height == 144);
        surface.Resize((uint)window.Width, (uint)window.Height);
        using GpuBuffer resized = CreatePixels(device, 224, 144, 0xFFF08020u);
        surface.Present(resized, 224, 224, 144);
        Assert.True(windows.Pump());
    }

    private static GpuBuffer CreatePixels(GpuDevice device, uint width, uint height, uint rgba)
    {
        var buffer = device.Malloc(checked((ulong)width * height * 4));
        buffer.Span<uint>(checked((int)(width * height))).Fill(rgba);
        return buffer;
    }

    private static void RequireLinuxDisplay()
    {
        Assert.True(OperatingSystem.IsLinux(), "These integration tests require Linux/X11.");
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")),
            "These integration tests require DISPLAY=:99 (for example from eng/desktop/start.sh).");
    }

    private static void PumpUntil(WindowSystem windows, Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(windows.Pump(), "The test window closed while waiting for an X11 event.");
            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                throw new TimeoutException($"Condition was not reached within {timeoutMilliseconds} ms.");
            Thread.Sleep(5);
        }
    }
}
