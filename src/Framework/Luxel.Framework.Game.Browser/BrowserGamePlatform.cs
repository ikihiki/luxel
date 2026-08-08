using System.Runtime.Versioning;
using Luxel.Audio.Browser;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;

namespace Luxel.Framework.Game.Browser;

/// <summary>Browser canvas, WebGPU, optional Web Audio, and animation-frame pacing options.</summary>
public sealed class BrowserGamePlatformOptions
{
    public required WebWindowBackendOptions WindowBackend { get; init; }
    public WindowDesc Window { get; init; } = new("Luxel", 1280, 720);
    public required Func<CancellationToken, Task> WaitFrame { get; init; }
    public bool UseAudio { get; init; }
}

/// <summary>
/// Browser-owned platform services that can be attached to the portable
/// <see cref="LuxelHostBuilder"/> without adding browser dependencies to <c>Luxel.Framework.Game</c>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserGamePlatform : IDisposable
{
    private readonly Func<CancellationToken, Task> _waitFrame;
    private bool _audioTransferred;
    private bool _disposed;

    private BrowserGamePlatform(
        WebWindowBackend windowBackend,
        WindowSystem windows,
        Window window,
        BrowserWebGpuBackend graphicsBackend,
        GpuDevice device,
        BrowserAudioBackend? audio,
        Func<CancellationToken, Task> waitFrame)
    {
        WindowBackend = windowBackend;
        Windows = windows;
        Window = window;
        GraphicsBackend = graphicsBackend;
        Device = device;
        Audio = audio;
        _waitFrame = waitFrame;
    }

    public WebWindowBackend WindowBackend { get; }
    public WindowSystem Windows { get; }
    public Window Window { get; }
    public BrowserWebGpuBackend GraphicsBackend { get; }
    public GpuDevice Device { get; }
    public BrowserAudioBackend? Audio { get; }

    public static async Task<BrowserGamePlatform> CreateAsync(
        BrowserGamePlatformOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.WindowBackend);
        ArgumentNullException.ThrowIfNull(options.WaitFrame);

        WebWindowBackend? windowBackend = null;
        WindowSystem? windows = null;
        Window? window = null;
        BrowserWebGpuBackend? graphicsBackend = null;
        GpuDevice? device = null;
        BrowserAudioBackend? audio = null;
        try
        {
            windowBackend = await WebWindowBackend.CreateAsync(options.WindowBackend, cancellationToken);
            windows = new WindowSystem(windowBackend);
            window = windows.CreateWindow(options.Window);
            windows.Pump();
            graphicsBackend = await BrowserWebGpuBackend.CreateAsync(cancellationToken);
            device = new GpuDevice(graphicsBackend);
            if (options.UseAudio) audio = await BrowserAudioBackend.CreateAsync(cancellationToken);
            return new BrowserGamePlatform(windowBackend, windows, window, graphicsBackend, device, audio, options.WaitFrame);
        }
        catch
        {
            audio?.Dispose();
            device?.Dispose();
            if (device is null) graphicsBackend?.Dispose();
            if (windows is not null) windows.Dispose();
            else windowBackend?.Dispose();
            throw;
        }
    }

    /// <summary>Attaches borrowed browser GPU services and pacing to a portable game host builder.</summary>
    public LuxelHostBuilder Configure(LuxelHostBuilder builder)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseGpuDevice(Device).UseFrameWaiter(_waitFrame);
        if (Audio is not null)
        {
            if (_audioTransferred)
                throw new InvalidOperationException("Browser audio has already been attached to a host builder.");
            builder.UseAudio(() => Audio);
            _audioTransferred = true;
        }
        return builder;
    }

    /// <summary>Creates a WebGPU presentation surface for the configured canvas.</summary>
    public GpuSurface CreateSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string token = GetCanvasToken(WindowBackend, Window);
        return GraphicsBackend.CreateCanvasSurface(token, (uint)Window.Width, (uint)Window.Height);
    }

    public bool Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Windows.Pump();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_audioTransferred) Audio?.Dispose();
        Device.Dispose();
        Windows.Dispose();
    }

    private static string GetCanvasToken(WebWindowBackend backend, Window window)
    {
        if (window.BackendWindow is IWebCanvasSurfaceProvider canvas) return canvas.CanvasToken;
        throw new InvalidOperationException($"Window backend '{backend.Name}' did not expose a browser canvas token.");
    }
}
