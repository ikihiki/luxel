using System.Diagnostics;
using Luxel.Graphics.Abstraction;
using Luxel.Graphics.DirectX12;
using Luxel.Graphics.Vulkan;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Silk;
using Luxel.Platform.Windows;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.UI.App;

/// <summary>Environment-aware host for a Luxel widget tree.</summary>
public static class LuxelApp
{
    internal const string BundledFontRelativePath = "assets/fonts/BIZUDGothic-Regular.ttf";
    internal const string BundledFontLicenseRelativePath = "assets/fonts/OFL.txt";
    internal static readonly string[] ShaderRelativePaths =
    [
        "shaders/raster2d_bounds.spv",
        "shaders/raster2d_bin.spv",
        "shaders/raster2d_fine.spv",
        "shaders/raster2d_bounds.wgsl",
        "shaders/raster2d_bin.wgsl",
        "shaders/raster2d_fine.wgsl",
        "shaders/raster2d_bounds.dxil",
        "shaders/raster2d_bin.dxil",
        "shaders/raster2d_fine.dxil",
    ];

    /// <summary>Creates a builder whose window and GPU defaults follow the current environment.</summary>
    public static LuxelAppBuilder CreateBuilder(string[]? args = null) => new(args);

    public static void Run(Func<Widget> rootFactory, LuxelAppOptions? options = null)
        => Run(rootFactory, options ?? new LuxelAppOptions(), new LuxelAppLifecycle());

    internal static void Run(Func<Widget> rootFactory, LuxelAppOptions options, LuxelAppLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(rootFactory);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ValidateOptions(options);
        ValidateAssets(AppContext.BaseDirectory, options.FontFactory is null);

        Exception? failure = null;
        void RunCore()
        {
            try { new EnvironmentLuxelApp(rootFactory, options, lifecycle).Run(); }
            catch (Exception error) { failure = error; }
        }

        if (OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var thread = new Thread(RunCore) { Name = options.Title };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        else
        {
            RunCore();
        }

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static LuxelWindowBackend ResolveWindowBackend(LuxelWindowBackend backend)
        => backend != LuxelWindowBackend.Auto ? backend
            : OperatingSystem.IsWindows() ? LuxelWindowBackend.Win32
            : OperatingSystem.IsLinux() ? LuxelWindowBackend.SilkX11
            : throw new PlatformNotSupportedException("Luxel.UI.App currently supports Windows and Linux/X11.");

    internal static LuxelGraphicsBackend ResolveGraphicsBackend(LuxelGraphicsBackend backend)
        => backend != LuxelGraphicsBackend.Auto ? backend
            : OperatingSystem.IsWindows() ? LuxelGraphicsBackend.Direct3D12
            : OperatingSystem.IsLinux() ? LuxelGraphicsBackend.Vulkan
            : throw new PlatformNotSupportedException("Luxel.UI.App currently supports Direct3D 12 on Windows and Vulkan on Linux.");

    internal static void ValidateOptions(LuxelAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Title))
            throw new ArgumentException("LuxelAppOptions.Title must not be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.UiName))
            throw new ArgumentException("LuxelAppOptions.UiName must not be empty.", nameof(options));
        if (options.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Width, "LuxelAppOptions.Width must be greater than zero.");
        if (options.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Height, "LuxelAppOptions.Height must be greater than zero.");
        if (options.RunFrames is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.RunFrames, "LuxelAppOptions.RunFrames must be null or greater than zero.");
        if (options.RunDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.RunDuration, "LuxelAppOptions.RunDuration must be null or greater than zero.");

        LuxelWindowBackend window = ResolveWindowBackend(options.WindowBackend);
        LuxelGraphicsBackend graphics = ResolveGraphicsBackend(options.GraphicsBackend);
        if (window == LuxelWindowBackend.Win32 && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Win32 window backend is only available on Windows.");
        if (window == LuxelWindowBackend.SilkX11 && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The Silk.NET X11 window backend is only available on Linux.");
        if (graphics == LuxelGraphicsBackend.Direct3D12 && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct3D 12 is only available on Windows.");
        if (window == LuxelWindowBackend.SilkX11 && graphics is not (LuxelGraphicsBackend.Vulkan or LuxelGraphicsBackend.WebGpu))
            throw new PlatformNotSupportedException("The Silk.NET X11 window backend requires Vulkan or WebGPU.");
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

internal sealed class EnvironmentLuxelApp
{
    private readonly Func<Widget> _rootFactory;
    private readonly LuxelAppOptions _options;
    private readonly LuxelAppLifecycle _lifecycle;

    public EnvironmentLuxelApp(Func<Widget> rootFactory, LuxelAppOptions options, LuxelAppLifecycle lifecycle)
    {
        _rootFactory = rootFactory;
        _options = options;
        _lifecycle = lifecycle;
    }

    public void Run()
    {
        LuxelWindowBackend windowBackend = LuxelApp.ResolveWindowBackend(_options.WindowBackend);
        LuxelGraphicsBackend graphicsBackend = LuxelApp.ResolveGraphicsBackend(_options.GraphicsBackend);
        _options.Diagnostic?.Invoke($"Using {windowBackend} windowing with {graphicsBackend} graphics.");

        Theme? previousTheme = null;
        if (_options.Theme is not null)
        {
            previousTheme = UiTheme.Current.Peek();
            UiTheme.Current.Value = _options.Theme;
        }

        using var windows = new WindowSystem(CreateWindowBackend(windowBackend));
        using var clipboard = new Clipboard(CreateClipboardBackend(windowBackend));
        PlatformClipboard.Current = clipboard;
        Window? bootstrapWindow = null;
        try
        {
            var desc = new WindowDesc(_options.Title, _options.Width, _options.Height);
            IGpuBackend backend;
            if (windowBackend == LuxelWindowBackend.SilkX11)
                bootstrapWindow = windows.CreateWindow(desc);

            backend = graphicsBackend switch
            {
                LuxelGraphicsBackend.WebGpu => Luxel.Graphics.WebGPU.WebGpuBackend.Create(),
                LuxelGraphicsBackend.Vulkan when windowBackend == LuxelWindowBackend.SilkX11 => VulkanBackend.Create(new VulkanBackendOptions
                {
                    EnableValidation = _options.EnableValidation,
                    Presentation = VulkanPresentationMode.Window,
                    PresentationSource = WindowGraphicsConnector.CreateVulkanPresentationSource(bootstrapWindow!),
                }),
                LuxelGraphicsBackend.Vulkan => VulkanBackend.Create(new VulkanBackendOptions
                {
                    EnableValidation = _options.EnableValidation,
                    Presentation = VulkanPresentationMode.Win32,
                }),
                LuxelGraphicsBackend.Direct3D12 => D3D12Backend.Create(),
                _ => throw new UnreachableException(),
            };

            using var device = new GpuDevice(backend);
            using VectorFont font = _options.FontFactory?.Invoke()
                ?? LuxelApp.LoadBundledFont(AppContext.BaseDirectory);
            if (font is null) throw new InvalidOperationException("LuxelAppOptions.FontFactory returned null.");
            using var manager = new WindowManager(device, font, windows);
            var runtime = new LuxelAppRuntime(device, font, windows, manager);
            try
            {
                _lifecycle.Configure?.Invoke(runtime);
                Widget root = _rootFactory() ?? throw new InvalidOperationException("The Luxel root widget factory returned null.");
                runtime.MainWindow = bootstrapWindow is null
                    ? manager.CreateUiWindow(desc, _options.UiName, () => root)
                    : manager.AttachUiWindow(bootstrapWindow, _options.UiName, () => root);
                bootstrapWindow = null; // WindowHost owns it now.
                _lifecycle.Started?.Invoke(runtime);
                RunLoop(runtime);
            }
            finally
            {
                runtime.DisposeOwned();
            }
        }
        finally
        {
            bootstrapWindow?.Dispose();
            if (ReferenceEquals(PlatformClipboard.Current, clipboard)) PlatformClipboard.Current = null;
            if (previousTheme is not null) UiTheme.Current.Value = previousTheme;
        }
    }

    private void RunLoop(LuxelAppRuntime runtime)
    {
        var stopwatch = Stopwatch.StartNew();
        double previous = stopwatch.Elapsed.TotalSeconds;
        int frames = 0;
        while (true)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            float dt = _options.RunFrames.HasValue ? 1f / 60f : (float)Math.Min(0.1, now - previous);
            previous = now;
            if (!runtime.WindowManager.RunFrame(dt)) break;
            _lifecycle.Frame?.Invoke(runtime, dt);
            frames++;
            if (_options.RunFrames is int limit && frames >= limit) break;
            if (_options.RunDuration is { } duration && stopwatch.Elapsed >= duration) break;
            if (!runtime.WindowManager.AnyRendered) Thread.Sleep(8);
        }
        _options.Diagnostic?.Invoke($"Luxel UI loop stopped after {frames} rendered frame(s).");
    }

    private static IWindowBackend CreateWindowBackend(LuxelWindowBackend backend) => backend switch
    {
        LuxelWindowBackend.Win32 => Win32WindowBackend.Create(),
        LuxelWindowBackend.SilkX11 => SilkWindowBackend.Create(),
        _ => throw new UnreachableException(),
    };

    private static IClipboardBackend CreateClipboardBackend(LuxelWindowBackend backend) => backend switch
    {
        LuxelWindowBackend.Win32 => Win32WindowBackend.CreateClipboardBackend(),
        LuxelWindowBackend.SilkX11 => SilkWindowBackend.CreateClipboardBackend(),
        _ => throw new UnreachableException(),
    };
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
