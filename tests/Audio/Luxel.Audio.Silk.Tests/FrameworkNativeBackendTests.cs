using Luxel.Audio.Windows;
using Luxel.Framework.Game;
using Luxel.Framework.Game.Native;

namespace Luxel.Audio.Silk.Tests;

public sealed class FrameworkNativeBackendTests
{
    [Fact]
    public void DefaultAudioBackendMatchesCurrentOperatingSystem()
    {
        using var backend = LuxelHostBuilderNativeExtensions.CreatePlatformAudioBackend();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) Assert.IsType<OpenAlAudioBackend>(backend);
        else if (OperatingSystem.IsWindows()) Assert.IsType<XAudio2Backend>(backend);
        else Assert.Fail("The native Framework should reject unsupported operating systems.");
    }

    [Fact]
    public void NativeBuilderExtensionsAreAvailable()
    {
        LuxelHostBuilder builder = LuxelHostBuilder.Create()
            .UseVulkan()
            .UseD3D12()
            .UseAudio();
        Assert.NotNull(builder);
    }
}
