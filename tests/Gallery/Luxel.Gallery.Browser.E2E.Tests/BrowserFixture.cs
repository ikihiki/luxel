using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Gallery browser E2E";
}

public sealed class BrowserFixture : IAsyncLifetime
{
    private static readonly string[] SwiftShaderArgs =
    [
        "--enable-unsafe-webgpu",
        "--use-angle=swiftshader",
        "--enable-features=Vulkan",
        "--disable-vulkan-surface"
    ];

    private Process? _server;
    private IPlaywright? _playwright;

    public string RepositoryRoot { get; private set; } = null!;
    public string ArtifactRoot { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RepositoryRoot = FindRepositoryRoot();
        ArtifactRoot = Path.GetFullPath(
            Environment.GetEnvironmentVariable("LUXEL_GALLERY_BROWSER_ROOT")
            ?? Path.Combine(RepositoryRoot, "artifacts", "gallery-browser", "wwwroot"));
        Assert.True(Directory.Exists(ArtifactRoot),
            $"Published Gallery root was not found: {ArtifactRoot}. Run dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release -o artifacts/gallery-browser first.");
        Assert.True(File.Exists(Path.Combine(ArtifactRoot, "index.html")),
            $"Published Gallery index.html was not found under {ArtifactRoot}.");

        var requestedPort = int.TryParse(Environment.GetEnvironmentVariable("LUXEL_WEBGPU_E2E_PORT"), out var configuredPort)
            ? configuredPort
            : 4193;
        var endpointInUse = await IsListeningAsync(requestedPort);
        var reuseServer = string.Equals(Environment.GetEnvironmentVariable("LUXEL_WEBGPU_E2E_REUSE_SERVER"), "1", StringComparison.Ordinal);
        if (endpointInUse && !reuseServer)
        {
            requestedPort = GetAvailablePort();
            endpointInUse = false;
        }

        BaseUrl = $"http://127.0.0.1:{requestedPort}";
        if (!endpointInUse)
            await StartStaticServerAsync(requestedPort);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !string.Equals(Environment.GetEnvironmentVariable("LUXEL_WEBGPU_E2E_HEADED"), "1", StringComparison.Ordinal),
            Args = SwiftShaderArgs
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
            await _server.WaitForExitAsync();
        }
        _server?.Dispose();
    }

    private async Task StartStaticServerAsync(int port)
    {
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("http.server");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--bind");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--directory");
        startInfo.ArgumentList.Add(ArtifactRoot);
        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Gallery static server.");
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_server.HasExited)
                throw new InvalidOperationException($"Gallery static server exited with code {_server.ExitCode}.");
            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/index.html");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Gallery static server did not become ready at {BaseUrl}/index.html.");
    }

    private static async Task<bool> IsListeningAsync(int port)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250));
            return true;
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException)
        {
            return false;
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Luxel.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root from the test output directory.");
    }
}
