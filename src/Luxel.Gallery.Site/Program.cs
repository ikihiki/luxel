using Luxel;
using Luxel.Gallery;
using Luxel.Gallery.Site;
using Luxel.Typography;
using Luxel.UI;

string output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "gallery-site");
string? filter = args.SkipWhile(a => a != "--filter").Skip(1).FirstOrDefault();
IReadOnlyList<StoryInfo> stories = filter is null ? StoryRegistry.All
    : StoryRegistry.All.Where(s => s.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

using var device = new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create());
using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
using var host = new GalleryHost(device, font);
SiteExportReport report = GallerySiteExporter.Export(host, stories, output, GallerySiteExporter.FindRepositoryRoot());
Console.WriteLine($"gallery-site: stories={report.Stories}, images={report.Images}, unavailable={report.Unavailable}, errors={report.Errors}, output={output}");
// Per-story capture failures are exported as validated, explicit error cards; structural validation failures throw.
return 0;
