using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;

namespace Luxel.Gallery.Native;

/// <summary>Builds the native Gallery E2E runner.</summary>
public sealed class NativeGalleryE2eApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;

    internal NativeGalleryE2eApplicationBuilder(string[] args)
    {
        Args = args;
        _hostBuilder = Host.CreateApplicationBuilder(args);
    }

    internal string[] Args { get; }
    public IServiceCollection Services => _hostBuilder.Services;

    public NativeGalleryE2eApplication Build()
    {
        IHost storyHost = _hostBuilder.Build();
        try
        {
            StoryCatalog catalog = storyHost.Services.GetRequiredService<StoryCatalog>();
            return new NativeGalleryE2eApplication(Args, storyHost, catalog);
        }
        catch
        {
            storyHost.Dispose();
            throw;
        }
    }
}

/// <summary>Configured native Gallery E2E runner.</summary>
public sealed class NativeGalleryE2eApplication : IDisposable
{
    private readonly string[] _args;
    private readonly IHost _storyHost;
    private readonly StoryCatalog _catalog;

    internal NativeGalleryE2eApplication(string[] args, IHost storyHost, StoryCatalog catalog)
    {
        _args = args;
        _storyHost = storyHost;
        _catalog = catalog;
    }

    public static NativeGalleryE2eApplicationBuilder CreateBuilder(string[] args)
        => new(args);

    public int Run()
    {
        string backend = (_args.Length > 0 ? _args[0] : "vk").ToLowerInvariant();
        string rasterizerBackend = ReadOption("--rasterizer")?.ToLowerInvariant() ?? "gpu";
        string? filter = _args.Skip(1)
            .FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)
                && value is not "e2e" and not "snap" and not "gpu" and not "skia");
        bool update = _args.Contains("--update", StringComparer.Ordinal);
        bool times = _args.Contains("--times", StringComparer.Ordinal);
        using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);

        if (rasterizerBackend == "skia")
        {
            Console.WriteLine("=== Luxel.Gallery E2E with SkiaSharp CPU rasterizer ===");
            using var rasterizer = new SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font, _catalog);
            return E2e.Run(host, _catalog.All, "skia", update, filter, times);
        }
        if (rasterizerBackend != "gpu")
            throw new ArgumentException($"未知の2D rasterizer: {rasterizerBackend} (gpu / skia)");

        using GpuDevice device = NativeGalleryGpu.CreateDevice(backend);
        Console.WriteLine($"=== Luxel.Gallery E2E on '{backend}' (device: {device.Name}) ===");
        using var gpuHost = new GalleryHost(device, font, _catalog);
        return E2e.Run(gpuHost, _catalog.All, backend, update, filter, times);
    }

    private string? ReadOption(string name)
    {
        int index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    public void Dispose() => _storyHost.Dispose();
}
