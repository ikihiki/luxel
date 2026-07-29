using Luxel.Graphics.Vulkan;

namespace Luxel.Vulkan.Tests;

public sealed class VulkanHeadlessTests
{
    [Fact]
    public void Win32Presentation_IsRejectedOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Throws<PlatformNotSupportedException>(() =>
            VulkanBackend.Create(new VulkanBackendOptions
            {
                EnableValidation = false,
                Presentation = VulkanPresentationMode.Win32,
            }));
    }

    [Fact]
    public void AutoPresentation_IsHeadlessOutsideWindows()
    {
        if (OperatingSystem.IsWindows()) return;

        using var backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = false,
            Presentation = VulkanPresentationMode.Auto,
        });

        Assert.False(backend.SupportsPresentation);
    }

    [Fact]
    public void DisabledPresentation_CreatesDeviceAndBuffer()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = false,
            Presentation = VulkanPresentationMode.Disabled,
        });

        Assert.False(backend.SupportsPresentation);
        using var buffer = backend.CreateBuffer(4096, GpuMemoryKind.HostMapped);
        Assert.Equal(4096UL, buffer.Size);
        Assert.NotEqual(0UL, buffer.DeviceAddress);

        var error = Assert.Throws<InvalidOperationException>(() =>
            backend.CreateSurface(NativeSurfaceDescriptor.Win32(1), 1, 1));
        Assert.Contains("headless", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
