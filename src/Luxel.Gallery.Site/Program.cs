using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel;
using Luxel.Gallery;
using Luxel.Gallery.Site;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;
using Luxel.UI;

string? ReadOption(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

string[] optionsWithValues = ["--filter", "--browser-webgpu-root", "--playground-browser-root", "--rasterizer", "--static-capture"];
string output = args.Select((value, index) => (value, index))
    .FirstOrDefault(item => !item.value.StartsWith("--", StringComparison.Ordinal)
        && (item.index == 0 || !optionsWithValues.Contains(args[item.index - 1], StringComparer.Ordinal))).value
    ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "gallery-site");
string? filter = ReadOption("--filter");
string? browserWebGpuRoot = ReadOption("--browser-webgpu-root");
string? playgroundBrowserRoot = ReadOption("--playground-browser-root");
HostApplicationBuilder storyHostBuilder = Host.CreateApplicationBuilder(args);
storyHostBuilder.Services.AddGalleryStory();
using IHost storyHost = storyHostBuilder.Build();
StoryCatalog catalog = storyHost.Services.GetRequiredService<StoryCatalog>();
IReadOnlyList<StoryInfo> stories = filter is null ? catalog.All
    : catalog.All.Where(s => s.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

StaticCaptureMode captureMode = (ReadOption("--static-capture")?.ToLowerInvariant() ?? "all") switch
{
    "all" => StaticCaptureMode.All,
    "golden-only" => StaticCaptureMode.GoldenOnly,
    "none" => StaticCaptureMode.None,
    string value => throw new ArgumentException($"Unknown static capture mode: {value} (all / golden-only / none)"),
};
string rasterizerBackend = ReadOption("--rasterizer")?.ToLowerInvariant() ?? "gpu";
var environment = new Lazy<CaptureEnvironment>(() => new CaptureEnvironment(rasterizerBackend, catalog));
try
{
    var options = new SiteExportOptions
    {
        StaticCapture = captureMode,
        Incremental = args.Contains("--incremental", StringComparer.Ordinal),
        Log = Console.WriteLine,
    };
    SiteExportReport report = GallerySiteExporter.Export(() => environment.Value.Host, catalog, stories, output,
        GallerySiteExporter.FindRepositoryRoot(), browserWebGpuRoot, playgroundBrowserRoot, options);
    Console.WriteLine($"gallery-site: stories={report.Stories}, images={report.Images}, unavailable={report.Unavailable}, errors={report.Errors}, capture={captureMode}, hostCreated={environment.IsValueCreated}, output={output}");
    return 0;
}
finally
{
    if (environment.IsValueCreated) environment.Value.Dispose();
}

sealed class CaptureEnvironment : IDisposable
{
    private readonly VectorFont _font;
    private readonly GpuDevice? _device;
    private readonly IRasterizer2D _rasterizer;
    public GalleryHost Host { get; }

    public CaptureEnvironment(string backend, StoryCatalog catalog)
    {
        _font = GalleryFonts.Load(GalleryFonts.Regular);
        _device = backend == "gpu" ? new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()) : null;
        _rasterizer = backend switch
        {
            "gpu" => new GpuDeviceRasterizer2D(_device!),
            "skia" => new SkiaRasterizer2D(),
            _ => throw new ArgumentException($"Unknown rasterizer: {backend} (gpu / skia)"),
        };
        Host = new GalleryHost(_rasterizer, _font, catalog, publishFrames: false);
    }

    public void Dispose()
    {
        Host.Dispose();
        _device?.Dispose();
        _font.Dispose();
    }
}
