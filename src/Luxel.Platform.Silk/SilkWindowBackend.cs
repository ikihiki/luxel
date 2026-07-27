using Luxel.Platform.Abstraction;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace Luxel.Platform.Silk;

/// <summary>Linux/X11 window backend implemented with Silk.NET Windowing and GLFW.</summary>
public sealed unsafe class SilkWindowBackend : IWindowBackend
{
    private readonly int _ownerThreadId;
    private readonly List<SilkWindow> _windows = new();
    private readonly Dictionary<CursorKind, nint> _cursors = new();
    private bool _disposed;

    private SilkWindowBackend()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;

        // Silk 2.23's managed enum predates GLFW 3.4's platform-selection constants, while
        // Ultz.Native.GLFW 3.4 implements them. Select X11 before the singleton is initialized.
        if (!GlfwProvider.GLFW.IsValueCreated)
        {
            const int glfwPlatformHint = 0x00050003;
            const int glfwPlatformX11 = 0x00060004;
            GlfwProvider.UninitializedGLFW.Value.InitHint((InitHint)glfwPlatformHint, glfwPlatformX11);
        }

        GlfwWindowing.Use();
    }

    /// <summary>Creates a GLFW/X11 backend on the current thread.</summary>
    /// <exception cref="PlatformNotSupportedException">The process is not Linux or has no X11 display.</exception>
    public static SilkWindowBackend Create()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Luxel.Platform.Silk currently supports Linux/X11 only. Use the Win32 backend on Windows.");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            throw new PlatformNotSupportedException(
                "Luxel.Platform.Silk requires an X11 display. Set DISPLAY to a reachable X server " +
                "(for example DISPLAY=:99 after eng/desktop/start.sh, or run under xvfb-run). " +
                "Native Wayland is not supported by this backend yet.");
        }

        try
        {
            return new SilkWindowBackend();
        }
        catch (Exception ex)
        {
            throw new PlatformNotSupportedException(
                "Failed to load the Silk.NET GLFW runtime required by Luxel.Platform.Silk. " +
                "Verify that the application output contains libglfw.so.3.3 and that its Linux/X11 dependencies are available.",
                ex);
        }
    }

    public string Name => "Silk.NET GLFW/X11";

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
            if (desc.X.HasValue || desc.Y.HasValue)
            {
                Vector2D<int> defaultPosition = options.Position;
                options.Position = new Vector2D<int>(desc.X ?? defaultPosition.X, desc.Y ?? defaultPosition.Y);
            }

            IWindow created = Window.Create(options);
            native = created;
            if (!GlfwWindowing.IsViewGlfw(created))
            {
                throw new PlatformNotSupportedException(
                    "Silk.NET did not select its GLFW window platform. The Luxel Silk backend requires GLFW on Linux/X11.");
            }

            created.Initialize();
            var platformWindow = created.Native
                ?? throw new PlatformNotSupportedException("Silk.NET created a window without native platform handles.");
            if (platformWindow.X11 is not { } x11)
            {
                string selected = platformWindow.Wayland is not null ? "Wayland" : platformWindow.Kind.ToString();
                throw new PlatformNotSupportedException(
                    $"GLFW created a {selected} window, but Luxel.Platform.Silk currently requires X11. " +
                    "Run with a valid DISPLAY/X11 server (for example DISPLAY=:99); native Wayland support is not implemented yet.");
            }
            nint x11Handle = checked((nint)x11.Window);

            Glfw glfw = GlfwWindowing.GetExistingApi(created)
                ?? throw new PlatformNotSupportedException("Silk.NET created a window without an accessible GLFW API.");
            WindowHandle* handle = GlfwWindowing.GetHandle(created);
            if (handle is null)
            {
                throw new PlatformNotSupportedException("Silk.NET created a GLFW window without a native GLFW handle.");
            }

            var window = new SilkWindow(this, created, glfw, handle, x11Handle);
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
            throw new PlatformNotSupportedException(
                "Failed to create a GLFW/X11 window. Verify that DISPLAY names a reachable X server and that the " +
                "Silk.NET GLFW native library and X11 runtime libraries are installed.", ex);
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
            Glfw.ThrowExceptions();
            throw new InvalidOperationException($"GLFW failed to create the standard {kind} cursor.");
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
            foreach (nint cursor in _cursors.Values) glfw.DestroyCursor((Cursor*)cursor);
        }
        _cursors.Clear();
    }
}
