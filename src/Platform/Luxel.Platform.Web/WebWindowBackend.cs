using System.Runtime.Versioning;
using Luxel.Platform.Abstraction;

namespace Luxel.Platform.Web;

/// <summary>
/// Browser window backend backed by existing DOM canvases. Create it asynchronously so ES module loading
/// never blocks the browser main thread, then use it through the portable <see cref="WindowSystem"/> API.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class WebWindowBackend : IWindowBackend
{
    private readonly int _ownerThreadId;
    private readonly WebWindowBackendOptions _options;
    private readonly Dictionary<int, WebWindow> _windows = new();
    private readonly WebEventQueue _events = new();
    private int _nextCanvas;
    private bool _disposed;

    private WebWindowBackend(WebWindowBackendOptions options)
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _options = options;
    }

    public string Name => "Browser canvas";

    /// <summary>Loads the ES module and creates a backend on the current browser main thread.</summary>
    public static async ValueTask<WebWindowBackend> CreateAsync(
        WebWindowBackendOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureBrowser();
        Validate(options);
        await WebInterop.ImportAsync(options.ModuleUrl, cancellationToken);
        return new WebWindowBackend(options);
    }

    /// <summary>Creates a clipboard backend after this backend's module has been loaded.</summary>
    public WebClipboardBackend CreateClipboardBackend()
    {
        VerifyUsable();
        return new WebClipboardBackend();
    }

    public IWindowBackendWindow CreateWindow(in WindowDesc desc)
    {
        VerifyUsable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desc.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desc.Height);
        if (_nextCanvas >= _options.Canvases.Count)
            throw new InvalidOperationException("No unused canvas remains in WebWindowBackendOptions.Canvases.");

        WebCanvasOptions canvas = _options.Canvases[_nextCanvas++];
        int id = WebInterop.CreateWindow(canvas.Selector, desc.Title ?? string.Empty, desc.Width, desc.Height, desc.Visible);
        var window = new WebWindow(this, id, canvas.SurfaceToken ?? canvas.Selector, desc.Width, desc.Height, desc.Visible);
        _windows.Add(id, window);
        return window;
    }

    public bool Pump()
    {
        VerifyUsable();
        DrainInterop();
        while (_events.TryDequeue(out WebEvent value))
        {
            if (_windows.TryGetValue(value.WindowId, out WebWindow? window)) window.Dispatch(value);
        }

        foreach (WebWindow window in _windows.Values)
        {
            if (!window.IsClosed) window.RefreshCursor();
        }

        foreach (int id in _windows.Where(static pair => pair.Value.IsClosed).Select(static pair => pair.Key).ToArray())
        {
            _windows[id].DestroyDomState();
            _windows.Remove(id);
        }
        return _windows.Count != 0;
    }

    private void DrainInterop()
    {
        while (true)
        {
            int kind = WebInterop.DequeueEventKind();
            if (kind == 0) return;
            _events.Enqueue(new WebEvent(
                WebInterop.EventWindowId(),
                (WebEventKind)kind,
                WebInterop.EventNumber(0), WebInterop.EventNumber(1), WebInterop.EventNumber(2),
                WebInterop.EventNumber(3), WebInterop.EventNumber(4), WebInterop.EventNumber(5),
                WebInterop.EventNumber(6),
                WebInterop.EventInteger(0), WebInterop.EventInteger(1),
                WebInterop.EventInteger(2), WebInterop.EventInteger(3),
                WebInterop.EventText()));
        }
    }

    internal void Destroy(WebWindow window)
    {
        VerifyThread();
        if (_windows.Remove(window.WindowId)) window.DestroyDomState();
    }

    private static void Validate(WebWindowBackendOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModuleUrl);
        if (options.Canvases is null || options.Canvases.Count == 0)
            throw new ArgumentException("At least one canvas must be configured.", nameof(options));
        foreach (WebCanvasOptions canvas in options.Canvases)
        {
            if (canvas is null || string.IsNullOrWhiteSpace(canvas.Selector))
                throw new ArgumentException("Every canvas must have a non-empty selector.", nameof(options));
        }
    }

    private static void EnsureBrowser()
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Luxel.Platform.Web can only call browser interop on browser-wasm.");
    }

    private void VerifyUsable()
    {
        EnsureBrowser();
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("WebWindowBackend must be used on the browser thread where it was created.");
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        foreach (WebWindow window in _windows.Values) window.DestroyDomState();
        _windows.Clear();
    }
}
