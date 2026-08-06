using Luxel.Audio.Windows;
using Luxel.Framework;

namespace Luxel.Audio.Silk.Tests;

public sealed class FrameworkAudioBackendTests
{
    [Fact]
    public void DefaultAudioBackendMatchesCurrentOperatingSystem()
    {
        using var backend = LuxelHostBuilder.CreatePlatformAudioBackend();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) Assert.IsType<OpenAlAudioBackend>(backend);
        else if (OperatingSystem.IsWindows()) Assert.IsType<XAudio2Backend>(backend);
        else Assert.Fail("The default audio backend should reject unsupported operating systems.");
    }
}
