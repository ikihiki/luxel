using System.Security.Cryptography;
using Luxel;
using Luxel.Gallery;
using Luxel.Gallery.Site;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery.Site.Tests;

public sealed class GallerySiteExporterTests
{
    [Fact]
    public void Slug_is_stable_and_relative_safe()
    {
        Assert.Equal("controls-button-primary", GallerySiteExporter.Slug("Controls/Button/Primary"));
        Assert.DoesNotContain('/', GallerySiteExporter.Slug("Docs/はじめに"));
    }

    [Fact]
    public void DocString_preserves_structured_embed_metadata()
    {
        Widget widget = Luxel.Controls.Kit.Text("static metadata test");
        var embed = new Luxel.Controls.DocEmbed(widget, Luxel.Controls.DocEmbedKind.StoryRef, "Controls/Test");
        Luxel.Controls.DocString doc = $"before\n{embed}\nafter";
        Assert.Single(doc.Embeds);
        Assert.Same(widget, doc.Embeds[0].Widget);
        Assert.Equal("Controls/Test", doc.Embeds[0].Reference);
        Assert.Equal(Luxel.Controls.DocEmbedKind.StoryRef, doc.Embeds[0].Kind);
    }

    [SkippableFact]
    public void Focused_export_is_complete_and_deterministic()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo story = StoryRegistry.Find("Controls/Button/Variants")
            ?? StoryRegistry.All.First(s => !s.RealWindowOnly);
        string a = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-a-" + Guid.NewGuid().ToString("N"));
        string b = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-b-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var device = CreateDeviceOrSkip();
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var host = new GalleryHost(device, font);
            GallerySiteExporter.Export(host, [story], a, root);
            GallerySiteExporter.Export(host, [story], b, root);
            GallerySiteExporter.Validate(a);
            string html = string.Join('\n', Directory.GetFiles(a, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.DoesNotContain("language-luxel-ui", html);
            Assert.DoesNotContain("href=\"luxel-ui:", html);
            Assert.Equal(HashTree(a, "*.html"), HashTree(b, "*.html"));
            Assert.Equal(HashTree(a, "manifest.json"), HashTree(b, "manifest.json"));
            Assert.Equal(HashTree(a, "*.png"), HashTree(b, "*.png"));
        }
        finally
        {
            if (Directory.Exists(a)) Directory.Delete(a, true);
            if (Directory.Exists(b)) Directory.Delete(b, true);
        }
    }

    [SkippableFact]
    public void Mermaid_fence_is_exported_as_png()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo story = StoryRegistry.Find("Docs/Architecture")
            ?? throw new InvalidOperationException("Docs/Architecture story is missing.");
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-mermaid-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var device = CreateDeviceOrSkip();
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var host = new GalleryHost(device, font);
            GallerySiteExporter.Export(host, [story], output, root);
            string html = string.Join('\n', Directory.GetFiles(output, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.DoesNotContain("```mermaid", html);
            Assert.Contains("Static mermaid capture", html);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "images"), "mermaid-*.png"));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    private static GpuDevice CreateDeviceOrSkip()
    {
        try { return new GpuDevice(Luxel.Vulkan.VulkanBackend.Create()); }
        catch (Exception e) { Skip.If(true, "Vulkan unavailable: " + e.Message); throw; }
    }

    private static string HashTree(string root, string pattern)
    {
        using var sha = SHA256.Create();
        var bytes = Directory.GetFiles(root, pattern, SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)
            .SelectMany(path => File.ReadAllBytes(path)).ToArray();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
