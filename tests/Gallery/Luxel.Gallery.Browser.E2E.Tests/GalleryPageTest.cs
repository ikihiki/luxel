using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace Luxel.Gallery.Browser.E2E.Tests;

public abstract class GalleryPageTest : PageTest
{
    public override async Task DisposeAsync()
    {
        try
        {
            if (!Page.IsClosed)
            {
                string directory = Path.Combine(FindRepositoryRoot(), "test-results", "screenshots");
                Directory.CreateDirectory(directory);
                string fileName = $"{GetType().Name}-{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.png";
                await Page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(directory, fileName),
                    FullPage = true
                });
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to capture Gallery Browser E2E teardown screenshot: {error.Message}");
        }

        await base.DisposeAsync();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Luxel.slnx")))
                return directory.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
