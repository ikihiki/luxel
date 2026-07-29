using System.Diagnostics;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserWebGpuSmokeTests
{
    private static readonly string[] ChromiumArguments =
    [
        "--enable-features=Vulkan", "--use-angle=swiftshader", "--use-vulkan=swiftshader",
        "--use-webgpu-adapter=swiftshader", "--disable-vulkan-surface", "--enable-unsafe-webgpu",
    ];

    [Fact(Timeout = 120_000)]
    public async Task Published_sample_runs_compute_renders_and_presents_to_canvas()
    {
        string? configured = Environment.GetEnvironmentVariable("LUXEL_BROWSER_WASM_APPBUNDLE");
        if (string.IsNullOrWhiteSpace(configured)) return;
        string appBundle = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, FindRepositoryRoot());
        Assert.True(File.Exists(Path.Combine(appBundle, "index.html")), $"Published AppBundle not found: {appBundle}");

        int port = GetFreePort();
        using var server = new StaticServer(StartServer(appBundle, port));
        await WaitForServerAsync(port);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ChromiumArguments,
        });
        IPage page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1100, Height = 800 } });
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync($"http://127.0.0.1:{port}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.True(await page.EvaluateAsync<bool>("() => !!navigator.gpu"), "navigator.gpu was unavailable in Chromium.");
        await page.Locator("#luxel-canvas").HoverAsync(new LocatorHoverOptions { Position = new Position { X = 30, Y = 30 } });
        await page.Locator("#luxel-canvas").ClickAsync();
        await page.Keyboard.PressAsync("KeyA");
        await page.WaitForSelectorAsync("#status[data-status='pass']", new PageWaitForSelectorOptions { Timeout = 90_000 });
        string summary = await page.Locator("#status").InnerTextAsync();
        Assert.Contains("compute=0xc0ffee42", summary);
        Assert.Contains("pointer=", summary);
        Assert.Contains("key=", summary);

        byte[] screenshot = await page.Locator("#luxel-canvas").ScreenshotAsync();
        using Image<Rgba32> image = Image.Load<Rgba32>(screenshot);
        Rgba32 background = image[4, 4];
        bool foundDifferent = false;
        for (int y = image.Height / 4; y < image.Height * 3 / 4 && !foundDifferent; y += 4)
        for (int x = image.Width / 4; x < image.Width * 3 / 4; x += 4)
        {
            Rgba32 pixel = image[x, y];
            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) > 60)
            { foundDifferent = true; break; }
        }
        Assert.True(foundDifferent, "Canvas screenshot contained only the background color.");
        Assert.Empty(errors);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root.");
    }

    private sealed class StaticServer(Process process) : IDisposable
    {
        public void Dispose()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }

    private static Process StartServer(string directory, int port)
    {
        var start = new ProcessStartInfo("python3")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-m"); start.ArgumentList.Add("http.server"); start.ArgumentList.Add(port.ToString());
        start.ArgumentList.Add("--bind"); start.ArgumentList.Add("127.0.0.1"); start.ArgumentList.Add("--directory"); start.ArgumentList.Add(directory);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the static HTTP server.");
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static async Task WaitForServerAsync(int port)
    {
        using var client = new HttpClient();
        for (int attempt = 0; attempt < 80; attempt++)
        {
            try { using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/"); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException("Static sample server did not become ready.");
    }
}
