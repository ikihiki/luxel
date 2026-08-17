using System.Diagnostics;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Silk;
using Luxel.Graphics.Vulkan;

using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;

namespace Luxel.Vulkan.Present.Tests;

public sealed class VulkanLinuxPresentTests
{
    [Fact]
    public void MappedRgbaBuffer_PresentsOneFrame()
    {
        RequireLinuxDisplay();
        using SilkWindowBackend windowBackend = SilkWindowBackend.Create();
        using var windows = new WindowSystem(windowBackend);
        Window window = windows.CreateWindow(new WindowDesc("Luxel Vulkan present", 160, 120));
        PumpUntil(windows, () => window.Width == 160 && window.Height == 120);

        SilkWindow nativeWindow = window.RequireBackendWindow<SilkWindow>();
        VulkanPresentationSource source = CreatePresentationSource(nativeWindow);
        Assert.Contains("VK_KHR_surface", source.RequiredInstanceExtensions);

        VulkanBackend backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = true,
            Presentation = VulkanPresentationMode.Window,
            PresentationSource = source,
        });
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateSurface((uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
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
        Window window = windows.CreateWindow(new WindowDesc("Luxel Vulkan resize", 128, 96));
        PumpUntil(windows, () => window.Width == 128 && window.Height == 96);

        SilkWindow nativeWindow = window.RequireBackendWindow<SilkWindow>();
        VulkanBackend backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = true,
            Presentation = VulkanPresentationMode.Window,
            PresentationSource = CreatePresentationSource(nativeWindow),
        });
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateSurface((uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
        using (GpuBuffer initial = CreatePixels(device, 128, 96, 0xFF30C050u))
            surface.Present(initial, 128, 128, 96);

        window.SetBounds(clientWidth: 224, clientHeight: 144);
        PumpUntil(windows, () => window.Width == 224 && window.Height == 144);
        surface.Resize((uint)window.Width, (uint)window.Height);
        using GpuBuffer resized = CreatePixels(device, 224, 144, 0xFFF08020u);
        surface.Present(resized, 224, 224, 144);
        Assert.True(windows.Pump());
    }

    private static unsafe VulkanPresentationSource CreatePresentationSource(SilkWindow window)
    {
        IVkSurface vkSurface = window.NativeWindow.VkSurface
            ?? throw new PlatformNotSupportedException("Silk.NET did not expose Vulkan surface integration.");
        byte** pointers = vkSurface.GetRequiredExtensions(out uint count);
        if (pointers is null || count == 0)
            throw new PlatformNotSupportedException("Silk.NET did not report Vulkan instance extensions.");
        var extensions = new string[count];
        for (uint i = 0; i < count; i++)
            extensions[i] = SilkMarshal.PtrToString((nint)pointers[i])
                ?? throw new PlatformNotSupportedException("Silk.NET returned an invalid Vulkan extension name.");
        return new VulkanPresentationSource(extensions, instance =>
            vkSurface.Create<byte>(new VkHandle(instance), null).Handle);
    }

    private static GpuBuffer CreatePixels(GpuDevice device, uint width, uint height, uint rgba)
    {
        var buffer = device.Malloc(checked((ulong)width * height * 4));
        buffer.Span<uint>(checked((int)(width * height))).Fill(rgba);
        return buffer;
    }

    private static void RequireLinuxDisplay()
    {
        Assert.True(OperatingSystem.IsLinux(), "These integration tests require Linux.");
        bool hasWayland = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        bool hasX11 = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
        Assert.True(hasWayland || hasX11,
            "These integration tests require WAYLAND_DISPLAY or DISPLAY to name a reachable display.");
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
