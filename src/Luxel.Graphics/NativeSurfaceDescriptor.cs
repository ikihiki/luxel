namespace Luxel.Graphics;

/// <summary>Backend-neutral native window-system handles used to create a presentation surface.</summary>
public readonly record struct NativeSurfaceDescriptor
{
    private NativeSurfaceDescriptor(NativeSurfaceKind kind, nint display, ulong window)
    {
        Kind = kind;
        Display = display;
        Window = window;
    }

    public NativeSurfaceKind Kind { get; }
    /// <summary>Xlib Display* for X11; HINSTANCE for Win32.</summary>
    public nint Display { get; }
    /// <summary>X11 Window or HWND.</summary>
    public ulong Window { get; }

    public static NativeSurfaceDescriptor Win32(nint hwnd, nint hinstance = default)
    {
        if (hwnd == 0) throw new ArgumentException("A non-zero HWND is required.", nameof(hwnd));
        return new NativeSurfaceDescriptor(NativeSurfaceKind.Win32, hinstance, unchecked((ulong)hwnd));
    }

    public static NativeSurfaceDescriptor Xlib(nint display, ulong window)
    {
        if (display == 0) throw new ArgumentException("A non-zero Xlib Display pointer is required.", nameof(display));
        if (window == 0) throw new ArgumentException("A non-zero X11 Window is required.", nameof(window));
        return new NativeSurfaceDescriptor(NativeSurfaceKind.Xlib, display, window);
    }
}

public enum NativeSurfaceKind
{
    Win32,
    Xlib,
}

/// <summary>Optional window feature exposing the complete native handles required by graphics APIs.</summary>
public interface INativeSurfaceProvider
{
    NativeSurfaceDescriptor SurfaceDescriptor { get; }
}
