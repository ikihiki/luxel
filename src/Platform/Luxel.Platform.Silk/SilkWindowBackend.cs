using Luxel.Platform.Abstraction;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace Luxel.Platform.Silk;

/// <summary>Native Linux window platform selected for the Silk.NET/GLFW backend.</summary>
public enum SilkWindowPlatform
{
    /// <summary>Prefer Wayland when WAYLAND_DISPLAY is available, otherwise use X11.</summary>
    Auto,
    X11,
    Wayland,
}

/// <summary>Process-level GLFW clipboard backend using the most recently created live Silk window.</summary>
public sealed unsafe class SilkClipboardBackend : IClipboardBackend
{
    private string? _ownedText;

    public string Name => "Silk.NET GLFW";
    public string? GetText()
    {
        WindowHandle* window = SilkWindowBackend.GetClipboardWindow();
        return GlfwProvider.GLFW.Value.GetClipboardString(window) ?? _ownedText;
    }

    public void SetText(string text)
    {
        _ownedText = text ?? string.Empty;
        WindowHandle* window = SilkWindowBackend.GetClipboardWindow();
        GlfwProvider.GLFW.Value.SetClipboardString(window, _ownedText);
    }

    public void Dispose() => _ownedText = null;
}

/// <summary>Linux window backend implemented with Silk.NET Windowing and GLFW.</summary>
public sealed unsafe class SilkWindowBackend : IWindowBackend
{
    private const int GlfwPlatformHint = 0x00050003;
    private const int GlfwPlatformX11 = 0x00060004;
    private const int GlfwPlatformWayland = 0x00060003;

    private static readonly object InitializationGate = new();
    private static SilkWindowPlatform? InitializedPlatform;
    private static readonly object ClipboardWindowsGate = new();
    private static readonly List<nint> ClipboardWindows = new();

    private readonly int _ownerThreadId;
    private readonly List<SilkWindow> _windows = new();
    private readonly Dictionary<CursorKind, nint> _cursors = new();
    private bool _disposed;

    private SilkWindowBackend(SilkWindowPlatform platform)
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Platform = platform;

        // Silk 2.23's managed enum predates GLFW 3.4's platform-selection constants, while
        // Ultz.Native.GLFW 3.4 implements them. Select the requested platform before initialization.
        lock (InitializationGate)
        {
            if (InitializedPlatform is { } initialized && initialized != platform)
            {
                throw new PlatformNotSupportedException(
                    $"GLFW is already initialized for {initialized}; the same process cannot also select {platform}.");
            }
            if (!GlfwProvider.GLFW.IsValueCreated)
            {
                int hint = platform == SilkWindowPlatform.Wayland ? GlfwPlatformWayland : GlfwPlatformX11;
                GlfwProvider.UninitializedGLFW.Value.InitHint((InitHint)GlfwPlatformHint, hint);
            }
            else if (InitializedPlatform is null)
            {
                throw new PlatformNotSupportedException(
                    "GLFW was initialized before Luxel selected its Linux window platform. " +
                    "Create SilkWindowBackend before using other GLFW services.");
            }

            GlfwWindowing.Use();
            InitializedPlatform = platform;
        }
    }

    /// <summary>Creates a GLFW backend on the current thread, preferring Wayland when available.</summary>
    public static SilkWindowBackend Create() => Create(SilkWindowPlatform.Auto);

    /// <summary>Creates a GLFW backend for the requested native Linux window platform.</summary>
    /// <exception cref="PlatformNotSupportedException">The process is not Linux or the requested display is unavailable.</exception>
    public static SilkWindowBackend Create(SilkWindowPlatform platform)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Luxel.Platform.Silk is available only on Linux. Use the Win32 backend on Windows.");
        }

        platform = ResolvePlatform(platform,
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"));

        try
        {
            return new SilkWindowBackend(platform);
        }
        catch (Exception ex)
        {
            string displayVariable = platform == SilkWindowPlatform.Wayland ? "WAYLAND_DISPLAY" : "DISPLAY";
            throw new PlatformNotSupportedException(
                $"Failed to initialize the Silk.NET GLFW {platform} backend. Verify that {displayVariable} names a reachable display " +
                "and that the application output contains the bundled GLFW native library and its runtime dependencies.", ex);
        }
    }

    internal static SilkWindowPlatform ResolvePlatform(SilkWindowPlatform requested, string? waylandDisplay, string? x11Display)
    {
        if (requested == SilkWindowPlatform.Auto)
        {
            if (!string.IsNullOrWhiteSpace(waylandDisplay)) return SilkWindowPlatform.Wayland;
            if (!string.IsNullOrWhiteSpace(x11Display)) return SilkWindowPlatform.X11;
            throw new PlatformNotSupportedException(
                "Luxel.Platform.Silk requires a Wayland or X11 display. Set WAYLAND_DISPLAY for native Wayland, " +
                "or DISPLAY for X11/Xwayland.");
        }

        string? display = requested == SilkWindowPlatform.Wayland ? waylandDisplay : x11Display;
        if (string.IsNullOrWhiteSpace(display))
        {
            string variable = requested == SilkWindowPlatform.Wayland ? "WAYLAND_DISPLAY" : "DISPLAY";
            throw new PlatformNotSupportedException(
                $"The Silk.NET {requested} backend requires {variable} to name a reachable display.");
        }
        return requested;
    }

    /// <summary>Creates the process-level GLFW clipboard backend.</summary>
    public static IClipboardBackend CreateClipboardBackend() => new SilkClipboardBackend();

    public SilkWindowPlatform Platform { get; }
    public string Name => $"Silk.NET GLFW/{Platform}";

    public IWindowBackendWindow CreateWindow(in WindowDesc desc)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desc.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(desc.Height);

        IWindow? native = null;
        try
        {
            WindowOptions options = WindowOptions.DefaultVulkan;
            options.Title = desc.Title;
            options.Size = new Vector2D<int>(desc.Width, desc.Height);
            options.IsVisible = desc.Visible;
            options.IsEventDriven = false;
            options.ShouldSwapAutomatically = false;
            if (Platform == SilkWindowPlatform.X11 && (desc.X.HasValue || desc.Y.HasValue))
            {
                Vector2D<int> defaultPosition = options.Position;
                options.Position = new Vector2D<int>(desc.X ?? defaultPosition.X, desc.Y ?? defaultPosition.Y);
            }

            IWindow created = global::Silk.NET.Windowing.Window.Create(options);
            native = created;
            if (!GlfwWindowing.IsViewGlfw(created))
            {
                throw new PlatformNotSupportedException(
                    "Silk.NET did not select its GLFW window platform. The Luxel Silk backend requires GLFW on Linux.");
            }

            created.Initialize();
            var platformWindow = created.Native
                ?? throw new PlatformNotSupportedException("Silk.NET created a window without native platform handles.");
            nint display;
            nint surface;
            if (Platform == SilkWindowPlatform.Wayland && platformWindow.Wayland is { } wayland)
            {
                display = wayland.Display;
                surface = wayland.Surface;
                if (display == 0 || surface == 0)
                    throw new PlatformNotSupportedException("Silk.NET created a Wayland window without wl_display/wl_surface handles.");
            }
            else if (Platform == SilkWindowPlatform.X11 && platformWindow.X11 is { } x11)
            {
                display = x11.Display;
                surface = checked((nint)x11.Window);
                if (display == 0 || surface == 0)
                    throw new PlatformNotSupportedException("Silk.NET created an X11 window without Display/Window handles.");
            }
            else
            {
                throw new PlatformNotSupportedException(
                    $"GLFW created a {platformWindow.Kind} window while Luxel requested {Platform}.");
            }

            Glfw glfw = GlfwWindowing.GetExistingApi(created)
                ?? throw new PlatformNotSupportedException("Silk.NET created a window without an accessible GLFW API.");
            WindowHandle* handle = GlfwWindowing.GetHandle(created);
            if (handle is null)
            {
                throw new PlatformNotSupportedException("Silk.NET created a GLFW window without a native GLFW handle.");
            }

            var window = new SilkWindow(this, created, glfw, handle, Platform, display, surface);
            _windows.Add(window);
            native = null;
            return window;
        }
        catch (PlatformNotSupportedException)
        {
            native?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            native?.Dispose();
            string displayVariable = Platform == SilkWindowPlatform.Wayland ? "WAYLAND_DISPLAY" : "DISPLAY";
            throw new PlatformNotSupportedException(
                $"Failed to create a GLFW/{Platform} window. Verify that {displayVariable} names a reachable display and that the " +
                "Silk.NET GLFW native library and platform runtime libraries are installed.", ex);
        }
    }

    public bool Pump()
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);

        SilkWindow? eventSource = _windows.FirstOrDefault(window => !window.IsClosed);
        eventSource?.PumpEvents();
        foreach (SilkWindow window in _windows)
        {
            if (!window.IsClosed) window.RefreshCursor();
        }

        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].IsClosed) continue;
            _windows[i].DisposeNative();
            _windows.RemoveAt(i);
        }

        return _windows.Count > 0;
    }

    internal static void RegisterClipboardWindow(WindowHandle* window)
    {
        if (window is null) return;
        lock (ClipboardWindowsGate) ClipboardWindows.Add((nint)window);
    }

    internal static void UnregisterClipboardWindow(WindowHandle* window)
    {
        if (window is null) return;
        lock (ClipboardWindowsGate) ClipboardWindows.Remove((nint)window);
    }

    internal static WindowHandle* GetClipboardWindow()
    {
        lock (ClipboardWindowsGate)
        {
            if (ClipboardWindows.Count == 0)
                throw new InvalidOperationException("The Silk.NET clipboard requires a live GLFW window.");
            return (WindowHandle*)ClipboardWindows[^1];
        }
    }

    internal Cursor* GetCursor(CursorKind kind, Glfw glfw)
    {
        VerifyThread();
        if (_cursors.TryGetValue(kind, out nint cursorHandle)) return (Cursor*)cursorHandle;

        CursorShape shape = kind switch
        {
            CursorKind.IBeam => CursorShape.IBeam,
            CursorKind.Hand => CursorShape.Hand,
            CursorKind.ResizeH => CursorShape.HResize,
            CursorKind.ResizeV => CursorShape.VResize,
            _ => CursorShape.Arrow,
        };
        Cursor* cursor = glfw.CreateStandardCursor(shape);
        if (cursor is null)
        {
            // Some minimal Wayland environments do not install an Xcursor theme, so GLFW can fail
            // to create individual standard cursors. Clear its pending error and pass null to
            // glfwSetCursor, which restores the platform default instead of crashing the app.
            try { Glfw.ThrowExceptions(); }
            catch { }
        }
        _cursors.Add(kind, (nint)cursor);
        return cursor;
    }

    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"SilkWindowBackend must be used on its creation thread (managed thread {_ownerThreadId}); " +
                $"the current managed thread is {Environment.CurrentManagedThreadId}.");
        }
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;

        foreach (SilkWindow window in _windows) window.Dispose();
        _windows.Clear();

        Glfw? glfw = null;
        try
        {
            glfw = GlfwProvider.GLFW.IsValueCreated ? GlfwProvider.GLFW.Value : null;
        }
        catch
        {
            // Initialization failures have no cursor resources to release.
        }

        if (glfw is not null)
        {
            foreach (nint cursor in _cursors.Values)
                if (cursor != 0) glfw.DestroyCursor((Cursor*)cursor);
        }
        _cursors.Clear();
    }
}
