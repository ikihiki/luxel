using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace Luxel.Gallery.Browser.E2E.Tests;

internal static class GalleryTestHost
{
    private static readonly Lazy<Task<HostState>> Host = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string BaseUrl => GetState().BaseUrl;

    public static async Task EnsureStartedAsync() => _ = await Host.Value.ConfigureAwait(false);

    public static BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        Locale = "en-US",
        ColorScheme = ColorScheme.Light
    };

    private static HostState GetState()
    {
        if (!Host.IsValueCreated || !Host.Value.IsCompletedSuccessfully)
            throw new InvalidOperationException("GalleryTestHost.EnsureStartedAsync must complete before accessing host settings.");
        return Host.Value.Result;
    }

    private static async Task<HostState> StartAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var artifactRoot = Path.GetFullPath(
            Environment.GetEnvironmentVariable("LUXEL_GALLERY_BROWSER_ROOT")
            ?? Path.Combine(repositoryRoot, "artifacts", "gallery-browser", "wwwroot"));
        if (!Directory.Exists(artifactRoot))
        {
            throw new DirectoryNotFoundException(
                $"Published Gallery root was not found: {artifactRoot}. Run dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release -o artifacts/gallery-browser first.");
        }
        if (!File.Exists(Path.Combine(artifactRoot, "index.html")))
            throw new FileNotFoundException($"Published Gallery index.html was not found under {artifactRoot}.");

        var requestedPort = int.TryParse(Environment.GetEnvironmentVariable("LUXEL_WEBGPU_E2E_PORT"), out var configuredPort)
            ? configuredPort
            : 4193;
        var endpointInUse = await IsListeningAsync(requestedPort).ConfigureAwait(false);
        var reuseServer = string.Equals(
            Environment.GetEnvironmentVariable("LUXEL_WEBGPU_E2E_REUSE_SERVER"),
            "1",
            StringComparison.Ordinal);
        if (endpointInUse && !reuseServer)
        {
            requestedPort = GetAvailablePort();
            endpointInUse = false;
        }

        var baseUrl = $"http://127.0.0.1:{requestedPort}";
        Process? server = null;
        if (!endpointInUse)
            server = StartStaticServer(artifactRoot, requestedPort);

        try
        {
            await ProbeAsync(baseUrl, server).ConfigureAwait(false);
        }
        catch
        {
            StopServer(server);
            throw;
        }

        if (server is not null)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopServer(server);
        return new HostState(baseUrl);
    }

    private static Process StartStaticServer(string artifactRoot, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "python" : "python3",
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
        startInfo.ArgumentList.Add(artifactRoot);
        var server = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Gallery static server.");
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        return server;
    }

    private static async Task ProbeAsync(string baseUrl, Process? server)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (server is { HasExited: true })
                throw new InvalidOperationException($"Gallery static server exited with code {server.ExitCode}.");
            try
            {
                using var response = await client.GetAsync($"{baseUrl}/index.html").ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200).ConfigureAwait(false);
        }
        throw new TimeoutException($"Gallery static server did not become ready at {baseUrl}/index.html.");
    }

    private static void StopServer(Process? server)
    {
        if (server is null)
            return;
        try
        {
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        finally
        {
            server.Dispose();
        }
    }

    private static async Task<bool> IsListeningAsync(int port)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
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

    private sealed record HostState(string BaseUrl);
}
