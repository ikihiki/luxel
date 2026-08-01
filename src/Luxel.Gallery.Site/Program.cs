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

string[] optionsWithValues = ["--filter", "--browser-webgpu-root", "--playground-browser-root", "--rasterizer"];
string output = args.Select((value, index) => (value, index))
    .FirstOrDefault(item => !item.value.StartsWith("--", StringComparison.Ordinal)
        && (item.index == 0 || !optionsWithValues.Contains(args[item.index - 1], StringComparer.Ordinal))).value
    ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "gallery-site");
string? filter = ReadOption("--filter");
string? browserWebGpuRoot = ReadOption("--browser-webgpu-root");
string? playgroundBrowserRoot = ReadOption("--playground-browser-root");
StoryCatalog catalog = GalleryStoryProject.CreateCatalog();
IReadOnlyList<StoryInfo> stories = filter is null ? catalog.All
    : catalog.All.Where(s => s.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

string rasterizerBackend = ReadOption("--rasterizer")?.ToLowerInvariant() ?? "gpu";
using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
using GpuDevice? device = rasterizerBackend == "gpu"
    ? new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create())
    : null;
using IRasterizer2D rasterizer = rasterizerBackend switch
{
    "gpu" => new GpuDeviceRasterizer2D(device!),
    "skia" => new SkiaRasterizer2D(),
    _ => throw new ArgumentException($"Unknown rasterizer: {rasterizerBackend} (gpu / skia)"),
};
using var host = new GalleryHost(rasterizer, font, catalog);
SiteExportReport report = GallerySiteExporter.Export(host, stories, output, GallerySiteExporter.FindRepositoryRoot(), browserWebGpuRoot, playgroundBrowserRoot);
Console.WriteLine($"gallery-site: stories={report.Stories}, images={report.Images}, unavailable={report.Unavailable}, errors={report.Errors}, output={output}");
// Per-story capture failures are exported as validated, explicit error cards; structural validation failures throw.
return 0;
