using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Framework.UI;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Platform;
using Luxel.Typography.TwoD;
using Luxel.UI;

namespace Luxel.Gallery.Native;

/// <summary>Builds the native Gallery application.</summary>
public sealed class NativeGalleryApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;

    internal NativeGalleryApplicationBuilder(string[] args)
    {
        Args = args;
        _hostBuilder = Host.CreateApplicationBuilder(args);
    }

    internal string[] Args { get; }
    public IServiceCollection Services => _hostBuilder.Services;

    public NativeGalleryApplication Build()
    {
        IHost storyHost = _hostBuilder.Build();
        try
        {
            StoryCatalog catalog = storyHost.Services.GetRequiredService<StoryCatalog>();
            return new NativeGalleryApplication(Args, storyHost, catalog);
        }
        catch
        {
            storyHost.Dispose();
            throw;
        }
    }
}

/// <summary>Configured native Gallery application.</summary>
public sealed class NativeGalleryApplication : IDisposable
{
    private readonly string[] _args;
    private readonly IHost _storyHost;
    private readonly StoryCatalog _catalog;
    private bool _disposed;

    internal NativeGalleryApplication(string[] args, IHost storyHost, StoryCatalog catalog)
    {
        _args = args;
        _storyHost = storyHost;
        _catalog = catalog;
    }

    public static NativeGalleryApplicationBuilder CreateBuilder(string[] args)
        => new(args);

    public int Run()
    {
        string backend = (_args.Length > 0 ? _args[0] : "auto").ToLowerInvariant();
        string rasterizerBackend = ReadOption("--rasterizer")?.ToLowerInvariant() ?? "gpu";
        if (rasterizerBackend is not ("gpu" or "skia"))
            throw new ArgumentException($"未知の2D rasterizer: {rasterizerBackend} (gpu / skia)");

        if (_args.Length > 2 && _args[1] == "bench")
            return RunBenchmark(backend, rasterizerBackend);

        int port = _args.Length > 1 && int.TryParse(_args[1], out int p) ? p : 5180;
        int seconds = _args.Length > 2 && int.TryParse(_args[2], out int s) ? s : 0;
        var gallery = new GalleryApp(_catalog);
        GpuGlyphMaskRenderer2D? glyphMasks = null;
        bool storyRegistered = false;
        LuxelAppBuilder builder = LuxelApp.CreateBuilder(_args);
        builder.Options.Title = NativeGalleryLabels.WindowTitle;
        builder.Options.UiName = "gallery";
        builder.Options.Width = 1280;
        builder.Options.Height = 840;
        builder.Options.Theme = gallery.ShellTheme;
        builder.Options.FontFactory = () => GalleryFonts.Load(GalleryFonts.Regular);
        builder.Options.RunDuration = seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
        builder.Options.GraphicsBackend = ParseGraphicsBackend(backend);
        builder.ConfigureRuntime(runtime =>
        {
            runtime.Own(gallery);
            gallery.HostGpu = (runtime.Device, runtime.Font);
            glyphMasks = runtime.Own(new GpuGlyphMaskRenderer2D(runtime.Device, new SkiaGlyphMaskRasterizer()));
            runtime.Own(GlyphMaskRendering.Register(runtime.Font, glyphMasks));
        });
        builder.OnStarted(runtime =>
        {
            glyphMasks!.RenderScale = runtime.MainWindow.Window.Scale;
            Console.WriteLine($"=== Luxel.Gallery app (device: {runtime.Device.Name}) ===");
            if (runtime.MainWindow.Content is UiContent content)
                content.Host.RegisterShortcut(new KeyGesture(Key.D, Ctrl: true), gallery.ToggleTheme);
            gallery.SelectByPath("Start/Welcome");
            runtime.Commands.Register("story.select", value =>
            {
                if (value is System.Text.Json.JsonElement element && element.TryGetProperty("id", out var id))
                    gallery.SelectByPath(id.GetString() ?? "");
            });

            DevToolsListener listener = runtime.Own(new DevToolsListener(runtime.Commands));
            var server = runtime.Own(new DebugServer(listener, port, windows: runtime.WindowManager));
            server.Start();
            Console.WriteLine($"Gallery URL: {server.Url} (stories: {_catalog.All.Count})");
        });
        builder.OnFrame((runtime, _) =>
        {
            glyphMasks!.RenderScale = runtime.MainWindow.Window.Scale;
            gallery.Update();
            if (runtime.MainWindow.Content is not UiContent content) return;
            gallery.SetWindowSize(content.Host.Width, content.Host.Height);
            if (gallery.ConsumeDirty()) content.Host.SetRoot(gallery.BuildRoot());
            if (!storyRegistered && gallery.StoryHost is { } storyHost)
            {
                runtime.WindowManager.UiRegistry.Register("story", storyHost);
                storyRegistered = true;
            }
        });

        LuxelUiApplication app = builder.Build();
        app.MapScreen("/", gallery.BuildRoot);
        app.Run();
        Console.WriteLine("gallery: shutting down");
        return 0;
    }

    private int RunBenchmark(string backend, string rasterizerBackend)
    {
        if (rasterizerBackend != "gpu")
            throw new NotSupportedException("既存benchはGPU upload/readbackを計測するため --rasterizer gpu 専用です。");
        using GpuDevice device = NativeGalleryGpu.CreateDevice(backend);
        Console.WriteLine($"=== Luxel.Gallery bench on '{backend}' (device: {device.Name}) ===");
        using var font = GalleryFonts.Load(GalleryFonts.Regular);
        using var host = new GalleryHost(device, font, _catalog);
        int frames = _args.Length > 3 && int.TryParse(_args[3], out int f) ? f : 300;
        (float x, float y)? click = null;
        int ci = Array.IndexOf(_args, "--click");
        if (ci >= 0 && ci + 2 < _args.Length
            && float.TryParse(_args[ci + 1], out float cx) && float.TryParse(_args[ci + 2], out float cy))
            click = (cx, cy);
        float wheel = 0;
        int wi = Array.IndexOf(_args, "--wheel");
        if (wi >= 0 && wi + 1 < _args.Length && float.TryParse(_args[wi + 1], out float wd)) wheel = wd;
        return Bench.Run(host, _args[2], frames, _args.Contains("--type"), click, wheel);
    }

    private string? ReadOption(string name)
    {
        int index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    private static LuxelGraphicsBackend ParseGraphicsBackend(string backend) => backend switch
    {
        "auto" => LuxelGraphicsBackend.Auto,
        "vk" or "vulkan" => LuxelGraphicsBackend.Vulkan,
        "dx" or "d3d12" => LuxelGraphicsBackend.Direct3D12,
        "webgpu" or "wgpu" => LuxelGraphicsBackend.WebGpu,
        _ => throw new ArgumentException($"未知のバックエンド: {backend} (auto / vk / dx / webgpu)"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _storyHost.Dispose();
    }
}

internal static class NativeGalleryGpu
{
    public static GpuDevice CreateDevice(string backend) => backend switch
    {
        "auto" when OperatingSystem.IsWindows() => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
        "auto" => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()),
        "vk" or "vulkan" => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()),
        "dx" or "d3d12" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
        "webgpu" or "wgpu" => new GpuDevice(Luxel.Graphics.WebGPU.WebGpuBackend.Create()),
        _ => throw new ArgumentException($"未知のバックエンド: {backend} (vk / dx / webgpu)"),
    };
}
