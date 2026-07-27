using System.Diagnostics;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Silk;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Graphics.Vulkan;

namespace Luxel.UI.App;

/// <summary>One-call Linux/X11 host for a single Luxel widget tree.</summary>
public static class LuxelApp
{
    internal const string BundledFontRelativePath = "assets/fonts/BIZUDGothic-Regular.ttf";
    internal const string BundledFontLicenseRelativePath = "assets/fonts/OFL.txt";
    internal static readonly string[] ShaderRelativePaths =
    [
        "shaders/raster2d_bounds.spv",
        "shaders/raster2d_bin.spv",
        "shaders/raster2d_fine.spv",
    ];

    public static void Run(Func<Widget> rootFactory, LuxelAppOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rootFactory);
        options ??= new LuxelAppOptions();
        ValidateOptions(options);
        ValidateAssets(AppContext.BaseDirectory, options.FontFactory is null);

        Theme? previousTheme = null;
        if (options.Theme is not null)
        {
            previousTheme = UiTheme.Current.Peek();
            UiTheme.Current.Value = options.Theme;
        }

        try
        {
            Widget root = rootFactory() ?? throw new InvalidOperationException("The Luxel root widget factory returned null.");
            new LinuxLuxelApp(root, options).Run();
        }
        finally
        {
            if (previousTheme is not null) UiTheme.Current.Value = previousTheme;
        }
    }

    internal static void ValidateOptions(LuxelAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Title))
            throw new ArgumentException("LuxelAppOptions.Title must not be empty.", nameof(options));
        if (options.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Width, "LuxelAppOptions.Width must be greater than zero.");
        if (options.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Height, "LuxelAppOptions.Height must be greater than zero.");
        if (options.RunFrames is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.RunFrames, "LuxelAppOptions.RunFrames must be null or greater than zero.");
    }

    internal static void ValidateAssets(string baseDirectory, bool requireBundledFont)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        foreach (string relativePath in ShaderRelativePaths)
        {
            string path = Path.Combine(baseDirectory, relativePath);
            if (!File.Exists(path))
                throw MissingAsset(relativePath, path, "Build or publish the app so Luxel.UI.App content files are copied.");
        }

        if (requireBundledFont)
        {
            string path = Path.Combine(baseDirectory, BundledFontRelativePath);
            if (!File.Exists(path))
                throw MissingAsset(BundledFontRelativePath, path,
                    "Build or publish the app so the bundled BIZ UDGothic font is copied, or set LuxelAppOptions.FontFactory.");
        }

        string licensePath = Path.Combine(baseDirectory, BundledFontLicenseRelativePath);
        if (!File.Exists(licensePath))
            throw MissingAsset(BundledFontLicenseRelativePath, licensePath,
                "Build or publish the app so the bundled font's SIL Open Font License is copied.");
    }

    internal static VectorFont LoadBundledFont(string baseDirectory)
        => VectorFont.Load(Path.Combine(baseDirectory, BundledFontRelativePath));

    private static FileNotFoundException MissingAsset(string relativePath, string resolvedPath, string remediation)
        => new($"Luxel.UI.App required asset '{relativePath}' was not found at '{resolvedPath}'. {remediation}", resolvedPath);
}

internal sealed class LinuxLuxelApp
{
    private readonly Widget _root;
    private readonly LuxelAppOptions _options;

    public LinuxLuxelApp(Widget root, LuxelAppOptions options)
    {
        _root = root;
        _options = options;
    }

    public void Run()
    {
        _options.Diagnostic?.Invoke("Creating Silk.NET X11 window.");
        using var windows = new WindowSystem(SilkWindowBackend.Create());
        using var clipboard = new Clipboard(SilkWindowBackend.CreateClipboardBackend());
        PlatformClipboard.Current = clipboard;
        Window window = windows.CreateWindow(new WindowDesc(_options.Title, _options.Width, _options.Height));

        IVulkanWindowSurface provider = window.GetFeature<IVulkanWindowSurface>()
            ?? throw new PlatformNotSupportedException("The Silk window did not provide a Vulkan surface. Luxel.UI.App requires Linux/X11 Vulkan presentation.");

        _options.Diagnostic?.Invoke("Creating Vulkan device and swapchain.");
        using var device = new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
        {
            EnableValidation = _options.EnableValidation,
            Presentation = VulkanPresentationMode.Window,
            WindowSurface = provider,
        }));
        using GpuSurface surface = device.CreateSurface(window.Handle, (uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
        using var rasterizer = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(rasterizer);
        using VectorFont font = _options.FontFactory?.Invoke()
            ?? LuxelApp.LoadBundledFont(AppContext.BaseDirectory);
        if (font is null)
            throw new InvalidOperationException("LuxelAppOptions.FontFactory returned null.");

        var theme = new Signal<Theme>(_options.Theme ?? Theme.Light);
        using var host = new UiHost(canvas, font, Math.Max(1, window.Width), Math.Max(1, window.Height), theme);
        host.SetRoot(_root);
        WireInput(window, host);

        GpuBuffer? framebuffer = null;
        int width = Math.Max(0, window.Width);
        int height = Math.Max(0, window.Height);
        int stride = 0;
        bool resizePending = true;
        window.Resized += (w, h) => { width = Math.Max(0, w); height = Math.Max(0, h); resizePending = true; };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            double previous = stopwatch.Elapsed.TotalSeconds;
            int renderedFrames = 0;
            while (windows.Pump())
            {
                if (resizePending)
                {
                    device.MainQueue.WaitIdle();
                    surface.Resize((uint)width, (uint)height);
                    framebuffer?.Dispose();
                    framebuffer = null;
                    stride = 0;
                    if (width > 0 && height > 0)
                    {
                        stride = Align(width, 64);
                        framebuffer = device.Malloc(checked((ulong)stride * (uint)height * 4), GpuMemoryKind.HostMapped);
                        host.Resize(width, height);
                    }
                    resizePending = false;
                }

                if (width == 0 || height == 0 || framebuffer is null)
                {
                    Thread.Sleep(10);
                    continue;
                }

                double now = stopwatch.Elapsed.TotalSeconds;
                float dt = _options.RunFrames.HasValue ? 1f / 60f : (float)Math.Min(0.1, now - previous);
                previous = now;
                host.Tick(dt);
                using (GpuCommandBuffer command = device.MainQueue.StartCommandRecording())
                {
                    canvas.Render(command, Camera2D.Pixels, (uint)stride, (uint)height, framebuffer);
                    command.Finish();
                    device.MainQueue.SubmitAndWait(command);
                }
                surface.Present(framebuffer, (uint)stride, (uint)width, (uint)height);
                host.EmitTree();
                renderedFrames++;

                if (_options.RunFrames is int limit && renderedFrames >= limit)
                {
                    window.Close();
                    windows.Pump();
                    break;
                }
            }
            _options.Diagnostic?.Invoke($"Luxel UI loop stopped after {renderedFrames} rendered frame(s).");
        }
        finally
        {
            if (ReferenceEquals(PlatformClipboard.Current, clipboard)) PlatformClipboard.Current = null;
            device.MainQueue.WaitIdle();
            framebuffer?.Dispose();
        }
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static void WireInput(Window window, UiHost host)
    {
        window.PointerMoved += input => host.PointerMove(input.X, input.Y, LuxelInput.MapModifiers(input.Modifiers));
        window.PointerDown += input =>
        {
            if (LuxelInput.TryMapButton(input.Button, out PointerButton button) && input.Button != WindowPointerButton.Right)
                host.PointerDown(input.X, input.Y, button, LuxelInput.MapModifiers(input.Modifiers));
        };
        window.PointerUp += input =>
        {
            KeyModifiers modifiers = LuxelInput.MapModifiers(input.Modifiers);
            if (input.Button == WindowPointerButton.Right)
                host.ContextClick(input.X, input.Y, modifiers);
            else if (LuxelInput.TryMapButton(input.Button, out PointerButton button))
                host.PointerUp(input.X, input.Y, button, modifiers);
        };
        window.Wheel += input => host.Wheel(input.X, input.Y, input.Delta * 40f);
        window.KeyDown += input =>
        {
            Key key = LuxelInput.MapKey(input.Key);
            if (key != Key.None)
            {
                KeyModifiers modifiers = LuxelInput.MapModifiers(input.Modifiers);
                host.KeyDown(key,
                    (modifiers & KeyModifiers.Shift) != 0,
                    (modifiers & KeyModifiers.Ctrl) != 0,
                    (modifiers & KeyModifiers.Alt) != 0);
            }
        };
        window.TextInput += host.Char;
        window.CursorQuery = () => host.CurrentCursor;
    }
}

internal static class LuxelInput
{
    public static Key MapKey(WindowKey key) => key switch
    {
        >= WindowKey.A and <= WindowKey.Z when Enum.TryParse(key.ToString(), out Key letter) => letter,
        >= WindowKey.D0 and <= WindowKey.D9 when Enum.TryParse(key.ToString(), out Key digit) => digit,
        >= WindowKey.F1 and <= WindowKey.F12 when Enum.TryParse(key.ToString(), out Key function) => function,
        WindowKey.Tab => Key.Tab,
        WindowKey.Enter => Key.Enter,
        WindowKey.Space => Key.Space,
        WindowKey.Escape => Key.Escape,
        WindowKey.Backspace => Key.Backspace,
        WindowKey.Delete => Key.Delete,
        WindowKey.Left => Key.Left,
        WindowKey.Right => Key.Right,
        WindowKey.Up => Key.Up,
        WindowKey.Down => Key.Down,
        WindowKey.Home => Key.Home,
        WindowKey.End => Key.End,
        WindowKey.PageUp => Key.PageUp,
        WindowKey.PageDown => Key.PageDown,
        WindowKey.Slash => Key.Slash,
        _ => Key.None,
    };

    public static KeyModifiers MapModifiers(WindowKeyModifiers modifiers)
    {
        KeyModifiers result = KeyModifiers.None;
        if ((modifiers & WindowKeyModifiers.Control) != 0) result |= KeyModifiers.Ctrl;
        if ((modifiers & WindowKeyModifiers.Shift) != 0) result |= KeyModifiers.Shift;
        if ((modifiers & WindowKeyModifiers.Alt) != 0) result |= KeyModifiers.Alt;
        if ((modifiers & WindowKeyModifiers.Meta) != 0) result |= KeyModifiers.Meta;
        return result;
    }

    public static bool TryMapButton(WindowPointerButton button, out PointerButton mapped)
    {
        mapped = button switch
        {
            WindowPointerButton.Left => PointerButton.Left,
            WindowPointerButton.Right => PointerButton.Right,
            WindowPointerButton.Middle => PointerButton.Middle,
            _ => default,
        };
        return button is WindowPointerButton.Left or WindowPointerButton.Right or WindowPointerButton.Middle;
    }
}
