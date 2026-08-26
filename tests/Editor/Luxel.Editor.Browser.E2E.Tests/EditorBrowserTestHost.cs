using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Luxel.Editor.Browser.E2E.Tests;

internal static class EditorBrowserTestHost
{
    private const string NestedPrefix = "/nested/products/editor/";
    private static readonly Lazy<Task<HostState>> Host = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string BaseUrl => GetState().BaseUrl;
    public static string NestedBaseUrl => GetState().BaseUrl + NestedPrefix;

    public static async Task EnsureStartedAsync() => _ = await Host.Value.ConfigureAwait(false);

    private static HostState GetState()
    {
        if (!Host.IsValueCreated || !Host.Value.IsCompletedSuccessfully)
            throw new InvalidOperationException("EditorBrowserTestHost.EnsureStartedAsync must complete before accessing host settings.");
        return Host.Value.Result;
    }

    private static async Task<HostState> StartAsync()
    {
        string repositoryRoot = FindRepositoryRoot();
        string artifactRoot = Path.GetFullPath(
            Environment.GetEnvironmentVariable("LUXEL_EDITOR_BROWSER_ROOT")
            ?? Path.Combine(repositoryRoot, "artifacts", "editor-browser", "wwwroot"));
        if (!Directory.Exists(artifactRoot))
        {
            throw new DirectoryNotFoundException(
                $"Published Editor demo root was not found: {artifactRoot}. Run dotnet publish samples/LuxelEditorBrowser/LuxelEditorBrowser.csproj -c Release -o artifacts/editor-browser first.");
        }
        if (!File.Exists(Path.Combine(artifactRoot, "index.html")))
            throw new FileNotFoundException($"Published Editor demo index.html was not found under {artifactRoot}.");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cancellation = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, artifactRoot, cancellation.Token);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            cancellation.Cancel();
            listener.Stop();
        };

        string baseUrl = $"http://127.0.0.1:{port}";
        await ProbeAsync(baseUrl + "/index.html").ConfigureAwait(false);
        await ProbeAsync(baseUrl + NestedPrefix + "index.html").ConfigureAwait(false);
        return new HostState(baseUrl);
    }

    private static async Task AcceptLoopAsync(TcpListener listener, string artifactRoot, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client, artifactRoot), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static async Task ServeAsync(TcpClient client, string artifactRoot)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            try
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (requestLine is null) return;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync().ConfigureAwait(false))) { }

                string[] parts = requestLine.Split(' ', 3);
                if (parts.Length < 2 || parts[0] != "GET")
                {
                    await WriteResponseAsync(stream, HttpStatusCode.MethodNotAllowed, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed")).ConfigureAwait(false);
                    return;
                }

                string path = Uri.UnescapeDataString(new Uri("http://localhost" + parts[1]).AbsolutePath);
                if (path.StartsWith(NestedPrefix, StringComparison.Ordinal)) path = path[NestedPrefix.Length..];
                else path = path.TrimStart('/');
                if (path.Length == 0 || path.EndsWith('/')) path += "index.html";
                string fullPath = Path.GetFullPath(Path.Combine(artifactRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                string rootPrefix = artifactRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) || !File.Exists(fullPath))
                {
                    await WriteResponseAsync(stream, HttpStatusCode.NotFound, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found")).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, HttpStatusCode.OK, ContentType(fullPath), await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpStatusCode status, string contentType, byte[] body)
    {
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".wasm" => "application/wasm",
        ".dll" => "application/octet-stream",
        ".dat" => "application/octet-stream",
        ".pdb" => "application/octet-stream",
        ".svg" => "image/svg+xml",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream"
    };

    private static async Task ProbeAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(100).ConfigureAwait(false);
        }
        throw new TimeoutException($"Editor browser static server did not become ready at {url}.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Luxel.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root from the E2E test output directory.");
    }

    private sealed record HostState(string BaseUrl);
}
