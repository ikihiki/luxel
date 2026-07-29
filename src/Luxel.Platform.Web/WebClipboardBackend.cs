using System.Runtime.Versioning;
using Luxel.Platform.Abstraction;

namespace Luxel.Platform.Web;

/// <summary>
/// Browser clipboard adapter. The portable API is synchronous but browser clipboard access is asynchronous:
/// GetText returns a cache and starts a non-blocking read refresh; SetText updates the cache immediately and
/// starts a non-blocking write. Permission failures leave the last successful/caller-provided cache unchanged.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class WebClipboardBackend : IClipboardBackend
{
    private string _cache = string.Empty;
    private bool _disposed;

    internal WebClipboardBackend() { }

    public string Name => "Browser Clipboard API (cached)";

    public static async ValueTask<WebClipboardBackend> CreateAsync(
        string moduleUrl = "./luxel-platform-web.js",
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Luxel.Platform.Web can only call browser interop on browser-wasm.");
        await WebInterop.ImportAsync(moduleUrl, cancellationToken);
        return new WebClipboardBackend();
    }

    public string? GetText()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache = WebInterop.ClipboardText();
        WebInterop.RequestClipboardRead();
        return _cache;
    }

    public void SetText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache = text ?? string.Empty;
        WebInterop.SetClipboardText(_cache);
    }

    public void Dispose() => _disposed = true;
}
