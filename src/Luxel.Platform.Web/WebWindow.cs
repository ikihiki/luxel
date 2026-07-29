using System.Runtime.Versioning;
using Luxel.Platform.Abstraction;

namespace Luxel.Platform.Web;

/// <summary>A portable window implementation whose visual surface is an existing HTML canvas.</summary>
[SupportedOSPlatform("browser")]
public sealed class WebWindow : IWindowBackendWindow, IWebCanvasSurfaceProvider
{
    private readonly WebWindowBackend _owner;
    private bool _domStateAlive = true;
    private CursorKind _lastCursor = (CursorKind)(-1);

    internal WebWindow(WebWindowBackend owner, int windowId, string canvasToken, int width, int height, bool visible)
    {
        _owner = owner;
        WindowId = windowId;
        CanvasToken = canvasToken;
        Width = width;
        Height = height;
        IsVisible = visible;
        Scale = 1f;
    }

    internal int WindowId { get; }
    public string CanvasToken { get; }

    /// <summary>Browser canvases have no native pointer handle. Use <see cref="IWebCanvasSurfaceProvider"/>.</summary>
    public nint Handle => 0;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float Scale { get; private set; }
    /// <summary>Canvas windows do not have meaningful screen coordinates.</summary>
    public int X => 0;
    /// <summary>Canvas windows do not have meaningful screen coordinates.</summary>
    public int Y => 0;
    public bool IsClosed { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsFocused { get; private set; }

    public Action<int, int>? Resized { get; set; }
    public Action<int, int>? Moved { get; set; }
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
        ThrowIfClosed();
        WebInterop.SetTitle(WindowId, title ?? string.Empty);
    }

    /// <summary>
    /// X/Y are ignored and remain zero. Width/height are physical backing-store pixels; CSS dimensions
    /// are updated using the current device-pixel ratio.
    /// </summary>
    public void SetBounds(int? x, int? y, int? clientWidth, int? clientHeight)
    {
        ThrowIfClosed();
        if (clientWidth is <= 0) throw new ArgumentOutOfRangeException(nameof(clientWidth));
        if (clientHeight is <= 0) throw new ArgumentOutOfRangeException(nameof(clientHeight));
        int width = clientWidth ?? Width;
        int height = clientHeight ?? Height;
        WebInterop.SetBounds(WindowId, width, height, clientWidth.HasValue, clientHeight.HasValue);
        Width = width;
        Height = height;
    }

    public void Show()
    {
        ThrowIfClosed();
        IsVisible = true;
        WebInterop.ShowWindow(WindowId);
    }

    public void Hide()
    {
        ThrowIfClosed();
        IsVisible = false;
        WebInterop.HideWindow(WindowId);
    }

    public void Focus()
    {
        ThrowIfClosed();
        WebInterop.FocusWindow(WindowId);
    }

    public void Close()
    {
        if (IsClosed) return;
        WebInterop.CloseWindow(WindowId);
    }

    internal void Dispatch(in WebEvent value)
    {
        if (IsClosed) return;
        switch (value.Kind)
        {
            case WebEventKind.Resize:
                Width = Math.Max(1, value.I0);
                Height = Math.Max(1, value.I1);
                Scale = value.A > 0 && double.IsFinite(value.A) ? (float)value.A : 1f;
                Resized?.Invoke(Width, Height);
                break;
            case WebEventKind.Focus:
                IsFocused = value.I0 != 0;
                FocusChanged?.Invoke(IsFocused);
                break;
            case WebEventKind.PointerMove:
                PointerMoved?.Invoke(Pointer(value));
                break;
            case WebEventKind.PointerDown:
                PointerDown?.Invoke(Pointer(value));
                break;
            case WebEventKind.PointerUp:
                PointerUp?.Invoke(Pointer(value));
                break;
            case WebEventKind.Wheel:
                (float x, float y) = Coordinates(value);
                Wheel?.Invoke(new WindowWheelEvent(x, y, (float)value.G, (WindowKeyModifiers)value.I3));
                break;
            case WebEventKind.KeyDown:
                KeyDown?.Invoke(new WindowKeyEvent(MapKey(value.Text), (WindowKeyModifiers)value.I0, value.I1 != 0));
                break;
            case WebEventKind.KeyUp:
                KeyUp?.Invoke(new WindowKeyEvent(MapKey(value.Text), (WindowKeyModifiers)value.I0));
                break;
            case WebEventKind.TextInput:
                if (!string.IsNullOrEmpty(value.Text)) TextInput?.Invoke(value.Text);
                break;
            case WebEventKind.Close:
                IsClosed = true;
                IsFocused = false;
                IsVisible = false;
                Closed?.Invoke();
                break;
        }
    }

    private static WindowPointerEvent Pointer(in WebEvent value)
    {
        (float x, float y) = Coordinates(value);
        return new WindowPointerEvent(x, y, (WindowPointerButton)value.I2, (WindowKeyModifiers)value.I3);
    }

    private static (float X, float Y) Coordinates(in WebEvent value) =>
        WebCoordinateNormalizer.ToBackingPixels(value.A, value.B, value.C, value.D, value.E, value.F, value.I0, value.I1);

    internal void RefreshCursor()
    {
        CursorKind cursor = CursorQuery?.Invoke() ?? CursorKind.Arrow;
        if (cursor == _lastCursor) return;
        _lastCursor = cursor;
        WebInterop.SetCursor(WindowId, (int)cursor);
    }

    internal void DestroyDomState()
    {
        if (!_domStateAlive) return;
        _domStateAlive = false;
        WebInterop.DestroyWindow(WindowId);
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(!_domStateAlive, this);
        if (IsClosed) throw new InvalidOperationException("The canvas window is closed.");
    }

    public void Dispose()
    {
        if (!_domStateAlive) return;
        IsClosed = true;
        IsFocused = false;
        IsVisible = false;
        _owner.Destroy(this);
    }

    private static WindowKey MapKey(string? code)
    {
        if (string.IsNullOrEmpty(code)) return WindowKey.Unknown;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z')
            return WindowKey.A + (code[3] - 'A');
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9')
            return WindowKey.D0 + (code[5] - '0');
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out int function) && function is >= 1 and <= 12)
            return WindowKey.F1 + (function - 1);

        return code switch
        {
            "Tab" => WindowKey.Tab, "Enter" or "NumpadEnter" => WindowKey.Enter,
            "Space" => WindowKey.Space, "Escape" => WindowKey.Escape,
            "Backspace" => WindowKey.Backspace, "Delete" => WindowKey.Delete, "Insert" => WindowKey.Insert,
            "ArrowLeft" => WindowKey.Left, "ArrowRight" => WindowKey.Right,
            "ArrowUp" => WindowKey.Up, "ArrowDown" => WindowKey.Down,
            "Home" => WindowKey.Home, "End" => WindowKey.End,
            "PageUp" => WindowKey.PageUp, "PageDown" => WindowKey.PageDown,
            "Slash" or "NumpadDivide" => WindowKey.Slash,
            "ShiftLeft" => WindowKey.LeftShift, "ShiftRight" => WindowKey.RightShift,
            "ControlLeft" => WindowKey.LeftControl, "ControlRight" => WindowKey.RightControl,
            "AltLeft" => WindowKey.LeftAlt, "AltRight" => WindowKey.RightAlt,
            _ => WindowKey.Unknown,
        };
    }
}
