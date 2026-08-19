using Luxel.Platform.Abstraction;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Luxel.Platform.Silk;

public sealed unsafe class SilkWindow : IWindowBackendWindow
{
    private readonly SilkWindowBackend _backend;
    private readonly IWindow _window;
    private readonly Glfw _glfw;
    private WindowHandle* _handle;
    private readonly SilkWindowPlatform _platform;
    private readonly nint _nativeDisplay;
    private readonly nint _nativeSurface;

    // GLFW stores unmanaged function pointers. Keep every delegate rooted until the native window is destroyed.
    private readonly GlfwCallbacks.CursorPosCallback _cursorPosCallback;
    private readonly GlfwCallbacks.MouseButtonCallback _mouseButtonCallback;
    private readonly GlfwCallbacks.ScrollCallback _scrollCallback;
    private readonly GlfwCallbacks.KeyCallback _keyCallback;
    private readonly GlfwCallbacks.CharCallback _charCallback;

    private bool _programmaticCloseAccepted;
    private bool _closed;
    private bool _nativeDisposed;
    private bool _closedNotified;
    private CursorKind? _currentCursor;

    internal SilkWindow(SilkWindowBackend backend, IWindow window, Glfw glfw, WindowHandle* handle,
        SilkWindowPlatform platform, nint nativeDisplay, nint nativeSurface)
    {
        _backend = backend;
        _window = window;
        _glfw = glfw;
        _handle = handle;
        _platform = platform;
        _nativeDisplay = nativeDisplay;
        _nativeSurface = nativeSurface;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Move += OnMove;
        _window.FocusChanged += OnFocusChanged;
        _window.Closing += OnClosing;

        _cursorPosCallback = OnCursorPosition;
        _mouseButtonCallback = OnMouseButton;
        _scrollCallback = OnScroll;
        _keyCallback = OnKey;
        _charCallback = OnChar;

        _glfw.SetCursorPosCallback(_handle, _cursorPosCallback);
        _glfw.SetMouseButtonCallback(_handle, _mouseButtonCallback);
        _glfw.SetScrollCallback(_handle, _scrollCallback);
        _glfw.SetKeyCallback(_handle, _keyCallback);
        _glfw.SetCharCallback(_handle, _charCallback);
        Glfw.ThrowExceptions();
        SilkWindowBackend.RegisterClipboardWindow(_handle);
    }

    public nint Handle
    {
        get
        {
            _backend.VerifyThread();
            return _nativeSurface;
        }
    }

    /// <summary>The Silk.NET window implementation. It remains owned by this window.</summary>
    public IWindow NativeWindow => _window;

    public SilkWindowPlatform Platform => _platform;

    /// <summary>The X11 display selected by the built-in Silk backend.</summary>
    public nint X11Display { get { VerifyPlatform(SilkWindowPlatform.X11); return _nativeDisplay; } }

    /// <summary>The X11 window selected by the built-in Silk backend.</summary>
    public ulong X11Window { get { VerifyPlatform(SilkWindowPlatform.X11); return unchecked((ulong)_nativeSurface); } }

    /// <summary>The Wayland display selected by the built-in Silk backend.</summary>
    public nint WaylandDisplay { get { VerifyPlatform(SilkWindowPlatform.Wayland); return _nativeDisplay; } }

    /// <summary>The Wayland surface selected by the built-in Silk backend.</summary>
    public nint WaylandSurface { get { VerifyPlatform(SilkWindowPlatform.Wayland); return _nativeSurface; } }

    public int Width
    {
        get { _backend.VerifyThread(); return _closed ? 0 : _window.FramebufferSize.X; }
    }

    public int Height
    {
        get { _backend.VerifyThread(); return _closed ? 0 : _window.FramebufferSize.Y; }
    }

    public float Scale
    {
        get
        {
            _backend.VerifyThread();
            if (_closed) return 1f;
            Vector2D<int> logical = _window.Size;
            Vector2D<int> framebuffer = _window.FramebufferSize;
            return logical.X > 0 ? framebuffer.X / (float)logical.X : 1f;
        }
    }

    public int X
    {
        get { _backend.VerifyThread(); return _closed || _platform == SilkWindowPlatform.Wayland ? 0 : _window.Position.X; }
    }

    public int Y
    {
        get { _backend.VerifyThread(); return _closed || _platform == SilkWindowPlatform.Wayland ? 0 : _window.Position.Y; }
    }

    public bool IsClosed
    {
        get { _backend.VerifyThread(); return _closed; }
    }

    public bool IsVisible
    {
        get { _backend.VerifyThread(); return !_closed && _window.IsVisible; }
    }

    public bool IsFocused
    {
        get
        {
            _backend.VerifyThread();
            return !_closed && _glfw.GetWindowAttrib(_handle, WindowAttributeGetter.Focused);
        }
    }

    public Action<int, int>? Resized { get; set; }
    public Action<int, int>? Moved { get; set; }
    public Func<bool>? Closing { get; set; }
    public Action? Closed { get; set; }
    public Action<bool>? FocusChanged { get; set; }
    public Action<WindowPointerEvent>? PointerMoved { get; set; }
    public Action<WindowPointerEvent>? PointerDown { get; set; }
    public Action<WindowPointerEvent>? PointerUp { get; set; }
    public Action<WindowWheelEvent>? Wheel { get; set; }
    public Action<WindowKeyEvent>? KeyDown { get; set; }
    public Action<WindowKeyEvent>? KeyUp { get; set; }
    public Action<string>? TextInput { get; set; }
    public Func<CursorKind>? CursorQuery { get; set; }

    public void SetTitle(string title)
    {
        VerifyUsable();
        _window.Title = title;
    }

    public void SetBounds(int? x, int? y, int? clientWidth, int? clientHeight)
    {
        VerifyUsable();
        if (clientWidth is <= 0) throw new ArgumentOutOfRangeException(nameof(clientWidth));
        if (clientHeight is <= 0) throw new ArgumentOutOfRangeException(nameof(clientHeight));

        // Wayland deliberately leaves global placement to the compositor.
        if (_platform == SilkWindowPlatform.X11 && (x.HasValue || y.HasValue))
        {
            Vector2D<int> position = _window.Position;
            _window.Position = new Vector2D<int>(x ?? position.X, y ?? position.Y);
        }
        if (clientWidth.HasValue || clientHeight.HasValue)
        {
            Vector2D<int> size = _window.Size;
            float scale = Scale;
            int width = clientWidth.HasValue ? Math.Max(1, (int)MathF.Round(clientWidth.Value / scale)) : size.X;
            int height = clientHeight.HasValue ? Math.Max(1, (int)MathF.Round(clientHeight.Value / scale)) : size.Y;
            _window.Size = new Vector2D<int>(width, height);
        }
    }

    public void Show()
    {
        VerifyUsable();
        _window.IsVisible = true;
    }

    public void Hide()
    {
        VerifyUsable();
        _window.IsVisible = false;
    }

    public void Focus()
    {
        VerifyUsable();
        // Wayland clients cannot request global focus; activation is compositor-controlled.
        if (_platform == SilkWindowPlatform.X11) _window.Focus();
    }

    public void Close()
    {
        _backend.VerifyThread();
        if (_closed || !AcceptCloseRequest(Closing)) return;
        _programmaticCloseAccepted = true;
        try { _window.Close(); }
        catch
        {
            _programmaticCloseAccepted = false;
            throw;
        }
    }

    internal static bool AcceptCloseRequest(Func<bool>? closing) => closing?.Invoke() != false;

    internal void PumpEvents()
    {
        _backend.VerifyThread();
        if (!_closed) _window.DoEvents();
    }

    internal void RefreshCursor()
    {
        if (_closed) return;
        CursorKind requested = CursorQuery?.Invoke() ?? CursorKind.Arrow;
        if (_currentCursor == requested) return;
        _glfw.SetCursor(_handle, _backend.GetCursor(requested, _glfw));
        _currentCursor = requested;
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (!_closed && size.X >= 0 && size.Y >= 0) Resized?.Invoke(size.X, size.Y);
    }

    private void OnMove(Vector2D<int> position)
    {
        if (!_closed && _platform == SilkWindowPlatform.X11) Moved?.Invoke(position.X, position.Y);
    }

    private void OnFocusChanged(bool focused)
    {
        if (!_closed) FocusChanged?.Invoke(focused);
    }

    private void OnClosing()
    {
        if (_closed) return;
        if (_programmaticCloseAccepted)
        {
            _programmaticCloseAccepted = false;
            NotifyClosed();
            return;
        }
        if (!AcceptCloseRequest(Closing)) { _window.IsClosing = false; return; }
        NotifyClosed();
    }

    private void OnCursorPosition(WindowHandle* window, double x, double y)
    {
        if (_closed) return;
        float scale = Scale;
        PointerMoved?.Invoke(new WindowPointerEvent(
            (float)x * scale, (float)y * scale, WindowPointerButton.None, CurrentModifiers()));
        RefreshCursor();
    }

    private void OnMouseButton(WindowHandle* window, MouseButton button, InputAction action, KeyModifiers modifiers)
    {
        if (_closed || action is not (InputAction.Press or InputAction.Release)) return;
        WindowPointerButton mappedButton = SilkInput.MapButton(button);
        if (mappedButton == WindowPointerButton.None) return;
        _glfw.GetCursorPos(_handle, out double x, out double y);
        float scale = Scale;
        var input = new WindowPointerEvent(
            (float)x * scale, (float)y * scale, mappedButton, SilkInput.MapModifiers(modifiers));
        if (action == InputAction.Press) PointerDown?.Invoke(input);
        else PointerUp?.Invoke(input);
    }

    private void OnScroll(WindowHandle* window, double xOffset, double yOffset)
    {
        if (_closed) return;
        _glfw.GetCursorPos(_handle, out double x, out double y);
        float scale = Scale;
        float delta = (float)(yOffset != 0 ? yOffset : xOffset);
        Wheel?.Invoke(new WindowWheelEvent((float)x * scale, (float)y * scale, delta, CurrentModifiers()));
    }

    private void OnKey(WindowHandle* window, Keys key, int scanCode, InputAction action, KeyModifiers modifiers)
    {
        if (_closed || action is not (InputAction.Press or InputAction.Release or InputAction.Repeat)) return;
        var input = new WindowKeyEvent(
            SilkInput.MapKey(key), SilkInput.MapModifiers(modifiers), SilkInput.IsRepeat(action));
        if (action == InputAction.Release) KeyUp?.Invoke(input);
        else KeyDown?.Invoke(input);
    }

    private void OnChar(WindowHandle* window, uint codePoint)
    {
        if (_closed) return;
        string? text = SilkInput.CodePointToString(codePoint);
        if (text is not null) TextInput?.Invoke(text);
    }

    private WindowKeyModifiers CurrentModifiers()
    {
        WindowKeyModifiers modifiers = WindowKeyModifiers.None;
        if (IsPressed(Keys.ControlLeft) || IsPressed(Keys.ControlRight)) modifiers |= WindowKeyModifiers.Control;
        if (IsPressed(Keys.ShiftLeft) || IsPressed(Keys.ShiftRight)) modifiers |= WindowKeyModifiers.Shift;
        if (IsPressed(Keys.AltLeft) || IsPressed(Keys.AltRight)) modifiers |= WindowKeyModifiers.Alt;
        if (IsPressed(Keys.SuperLeft) || IsPressed(Keys.SuperRight)) modifiers |= WindowKeyModifiers.Meta;
        return modifiers;
    }

    private bool IsPressed(Keys key) => _glfw.GetKey(_handle, key) == (int)InputAction.Press;

    private void NotifyClosed()
    {
        if (_closedNotified) return;
        _closedNotified = true;
        _closed = true;
        Closed?.Invoke();
    }

    private void VerifyPlatform(SilkWindowPlatform expected)
    {
        VerifyUsable();
        if (_platform != expected)
            throw new PlatformNotSupportedException($"This Silk window uses {_platform}, not {expected}.");
    }

    private void VerifyUsable()
    {
        _backend.VerifyThread();
        ObjectDisposedException.ThrowIf(_closed || _nativeDisposed, this);
    }

    internal void DisposeNative()
    {
        _backend.VerifyThread();
        if (_nativeDisposed) return;
        _nativeDisposed = true;
        SilkWindowBackend.UnregisterClipboardWindow(_handle);
        _window.Dispose();
        _handle = null;
    }

    public void Dispose()
    {
        _backend.VerifyThread();
        if (_nativeDisposed) return;
        NotifyClosed();
        DisposeNative();
    }
}
