using Luxel;
using Luxel.Gallery;
using Luxel.Gallery.Site;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;
using Luxel.UI;

string output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "gallery-site");
string? filter = args.SkipWhile(a => a != "--filter").Skip(1).FirstOrDefault();
string? browserWebGpuRoot = args.SkipWhile(a => a != "--browser-webgpu-root").Skip(1).FirstOrDefault();
IReadOnlyList<StoryInfo> stories = filter is null ? StoryRegistry.All
    : StoryRegistry.All.Where(s => s.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

string rasterizerBackend = args.SkipWhile(a => a != "--rasterizer").Skip(1).FirstOrDefault()?.ToLowerInvariant() ?? "gpu";
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
using var host = new GalleryHost(rasterizer, font);
SiteExportReport report = GallerySiteExporter.Export(host, stories, output, GallerySiteExporter.FindRepositoryRoot(), browserWebGpuRoot);
Console.WriteLine($"gallery-site: stories={report.Stories}, images={report.Images}, unavailable={report.Unavailable}, errors={report.Errors}, output={output}");
// Per-story capture failures are exported as validated, explicit error cards; structural validation failures throw.
return 0;
