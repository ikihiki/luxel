using Microsoft.Playwright.Xunit;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class PlaywrightEnvironmentTests : PageTest
{
    [Fact]
    public async Task ChromiumCanRenderAStandalonePage()
    {
        await Page.SetContentAsync("<main data-testid=\"status\">ready</main>");

        await Expect(Page.GetByTestId("status")).ToHaveTextAsync("ready");
    }
}
