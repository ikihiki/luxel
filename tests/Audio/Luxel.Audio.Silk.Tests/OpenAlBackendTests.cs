namespace Luxel.Audio.Silk.Tests;

public sealed class OpenAlBackendTests
{
    [Fact]
    public void OpenAlBackendCanBeConstructed()
    {
        using var backend = new OpenAlAudioBackend();
        Assert.NotNull(backend);
    }
}
