using Luxel.Audio;
using Luxel.Framework.Game;

namespace Luxel.Framework.Game.Native;

/// <summary>Desktop-native GPU and audio backend composition for <see cref="LuxelHostBuilder"/>.</summary>
public static class LuxelHostBuilderNativeExtensions
{
    /// <summary>Creates and owns a Vulkan GPU device through the host container.</summary>
    public static LuxelHostBuilder UseVulkan(this LuxelHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseGpu(static () => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()));
    }

    /// <summary>Creates and owns a Direct3D 12 GPU device through the host container.</summary>
    public static LuxelHostBuilder UseD3D12(this LuxelHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseGpu(static () => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()));
    }

    /// <summary>
    /// Selects the desktop audio backend for the current operating system:
    /// XAudio2 on Windows and OpenAL Soft through Silk.NET on Linux or macOS.
    /// </summary>
    public static LuxelHostBuilder UseAudio(this LuxelHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseAudio(CreatePlatformAudioBackend);
    }

    /// <summary>Creates the default desktop audio backend for the current operating system.</summary>
    public static IAudioBackend CreatePlatformAudioBackend()
    {
        if (OperatingSystem.IsWindows()) return new Luxel.Audio.Windows.XAudio2Backend();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new Luxel.Audio.Silk.OpenAlAudioBackend();
        throw new PlatformNotSupportedException(
            "Native audio supports Windows (XAudio2) and Linux/macOS (OpenAL Soft via Silk.NET). " +
            "Use LuxelHostBuilder.UseAudio(factory) for another platform.");
    }
}
