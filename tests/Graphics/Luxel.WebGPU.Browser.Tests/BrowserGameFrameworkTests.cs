using Luxel.Framework.Game.Browser;

namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserGameFrameworkTests
{
    [Fact]
    public void BrowserPlatformIsOwnedByBrowserGameFrameworkAssembly()
        => Assert.Equal("Luxel.Framework.Game.Browser", typeof(BrowserGamePlatform).Assembly.GetName().Name);
}
