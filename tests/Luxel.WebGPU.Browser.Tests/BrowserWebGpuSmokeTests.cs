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
        string configured = RequireDirectoryEnvironmentVariable("LUXEL_BROWSER_WASM_APPBUNDLE");
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

    [Fact(Timeout = 180_000)]
    public async Task Exported_gallery_composes_triangle_runtime_for_story_and_guide()
    {
        string configured = RequireDirectoryEnvironmentVariable("LUXEL_GALLERY_SITE_ROOT");
        string galleryRoot = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, FindRepositoryRoot());
        Assert.True(File.Exists(Path.Combine(galleryRoot, "index.html")), $"Exported Gallery not found: {galleryRoot}");
        Assert.True(File.Exists(Path.Combine(galleryRoot, "samples", "webgpu-browser", "index.html")),
            $"Browser WebGPU sample was not copied into the Gallery: {galleryRoot}");

        string serverRoot = Path.GetDirectoryName(galleryRoot)
            ?? throw new InvalidOperationException($"Gallery root has no parent directory: {galleryRoot}");
        string prefix = Uri.EscapeDataString(Path.GetFileName(galleryRoot)) + "/";
        int port = GetFreePort();
        using var server = new StaticServer(StartServer(serverRoot, port));
        await WaitForServerAsync(port, prefix);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ChromiumArguments,
        });
        IPage page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1100, Height = 800 },
        });
        var errors = new List<string>();
        var requests = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        page.Request += (_, request) => requests.Add(request.Url);

        foreach (string storyPath in new[]
        {
            "Examples/3D/Triangle",
            "Learn/Rendering/Basics/FirstTriangle",
        })
        {
            string route = Uri.EscapeDataString(storyPath);
            await page.GotoAsync($"http://127.0.0.1:{port}/{prefix}#story={route}",
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            const string runtimeSelector =
                "iframe[src='samples/webgpu-browser/'][data-luxel-runtime-story='Examples/3D/Triangle']";
            ILocator runtime = page.Locator(runtimeSelector);
            await runtime.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            Assert.Equal("samples/webgpu-browser/", await runtime.GetAttributeAsync("src"));

            IFrameLocator frame = page.FrameLocator(runtimeSelector);
            ILocator status = frame.Locator("#status[data-status='pass']");
            await status.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 90_000 });
            string summary = await status.InnerTextAsync();
            Assert.Contains("story=Examples/3D/Triangle", summary);
            Assert.Contains("shader=tutorial_triangle", summary);
            Assert.Contains("vertexSize=32; rootSize=4", summary);
            Assert.Contains("canvas=320x240", summary);
            Assert.Contains("recipe=canonical-triangle-v1", summary);
            Assert.Contains("hash=4c3a36aa594306d963f00f1c0e6c5d7c62b1543748bfc882d72d0de8cf9a2cdd", summary);
            ILocator canvas = frame.Locator("#luxel-canvas");
            Assert.Equal("320", await canvas.GetAttributeAsync("width"));
            Assert.Equal("240", await canvas.GetAttributeAsync("height"));
            await AssertCanvasContainsCanonicalRgbAsync(canvas);
        }

        Assert.Empty(errors);
        Assert.DoesNotContain(requests, url =>
            new Uri(url).AbsolutePath.EndsWith("/images/examples-3d-triangle.png", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertCanvasContainsCanonicalRgbAsync(ILocator canvas)
    {
        byte[] screenshot = await canvas.ScreenshotAsync();
        using Image<Rgba32> image = Image.Load<Rgba32>(screenshot);
        Assert.InRange((double)image.Width / image.Height, 4d / 3d - 0.02, 4d / 3d + 0.02);
        bool red = false, green = false, blue = false;
        for (int y = image.Height / 20; y < image.Height * 19 / 20; y += 3)
        for (int x = image.Width / 20; x < image.Width * 19 / 20; x += 3)
        {
            Rgba32 pixel = image[x, y];
            red |= pixel.R > 90 && pixel.R > pixel.G + 15 && pixel.R > pixel.B + 15;
            green |= pixel.G > 90 && pixel.G > pixel.R + 15 && pixel.G > pixel.B + 15;
            blue |= pixel.B > 90 && pixel.B > pixel.R + 15 && pixel.B > pixel.G + 15;
        }
        Assert.True(red && green && blue, $"Canvas did not contain canonical RGB regions (red={red}, green={green}, blue={blue}).");
    }

    private static string RequireDirectoryEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value),
            $"{name} must point to a published/exported directory; the browser test must not pass without its input.");
        return value!;
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

    private static async Task WaitForServerAsync(int port, string relativePath = "")
    {
        using var client = new HttpClient();
        for (int attempt = 0; attempt < 80; attempt++)
        {
            try { using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/{relativePath}"); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException("Static sample server did not become ready.");
    }
}
