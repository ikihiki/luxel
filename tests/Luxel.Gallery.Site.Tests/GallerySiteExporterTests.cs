using System.Security.Cryptography;
using Luxel;
using Luxel.Controls;
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
        StoryInfo story = StoryRegistry.Find("Learn/Rendering/Shaders")
            ?? StoryRegistry.All.First(s => !s.RealWindowOnly);
        StoryInfo imageStory = StoryRegistry.Find("Controls/Button/Intents")
            ?? StoryRegistry.All.First(s => !s.RealWindowOnly && s.Path != story.Path);
        string a = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-a-" + Guid.NewGuid().ToString("N"));
        string b = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-b-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var device = CreateDeviceOrSkip();
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var host = new GalleryHost(device, font);
            GallerySiteExporter.Export(host, [story, imageStory], a, root);
            GallerySiteExporter.Export(host, [story, imageStory], b, root);
            GallerySiteExporter.Validate(a);
            string html = string.Join('\n', Directory.GetFiles(a, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
            string index = File.ReadAllText(Path.Combine(a, "index.html"));
            string script = File.ReadAllText(Path.Combine(a, "site.js"));
            Assert.Contains("vendor/highlightjs/highlight.min.js", index);
            Assert.Contains("vendor/highlightjs/github-dark.min.css", index);
            Assert.Contains("Highlight.js license", index);
            Assert.Contains("highlight(content)", script);
            Assert.Contains("slang:'cpp'", script);
            Assert.Contains("powershell:'shell'", script);
            Assert.Contains("treeFor(filtered)", script);
            Assert.Contains("details.tree-folder", script);
            Assert.Contains("localStorage.setItem(openKey", script);
            Assert.Contains("renderLevel(child,path,open,expandAll)", script);
            Assert.Contains("aria-current','page'", script);
            Assert.Contains("!hasSavedOpen()&&!prefix", script);
            Assert.Contains("if(!expandAll)details.addEventListener", script);
            Assert.Contains("(x.aliases||[]).includes(requested)", script);
            Assert.True(File.Exists(Path.Combine(a, "vendor", "highlightjs", "highlight.min.js")));
            Assert.True(File.Exists(Path.Combine(a, "vendor", "highlightjs", "github-dark.min.css")));
            Assert.True(File.Exists(Path.Combine(a, "licenses", "highlight.js-LICENSE.txt")));
            Assert.Contains("language-powershell", html);
            string imageFragment = File.ReadAllText(Path.Combine(a, "stories", "controls-button-intents.html"));
            Assert.Contains("src=\"images/controls-button-intents.png\"", imageFragment);
            Assert.DoesNotContain("src=\"../images/", imageFragment);
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

    [Fact]
    public void Legacy_two_d_routes_resolve_without_duplicate_sidebar_entries()
    {
        string[] names = ["CameraRig", "Sprites", "Tilemap", "Particles", "ParticleView", "Gizmos2D"];
        foreach (string name in names)
        {
            StoryInfo canonical = StoryRegistry.Find($"Demos/2D/{name}")
                ?? throw new InvalidOperationException($"Canonical 2D story is missing: {name}");
            Assert.Same(canonical, StoryRegistry.Find($"Demos/TwoD/{name}"));
            Assert.Contains($"Demos/TwoD/{name}", StoryRegistry.AliasesFor(canonical.Path));
        }
        Assert.DoesNotContain(StoryRegistry.All, story => story.Path.StartsWith("Demos/TwoD/", StringComparison.Ordinal));
    }

    [Fact]
    public void Rendering_learn_chain_is_complete_and_has_inline_examples()
    {
        string[] routes =
        [
            "Overview", "Environment", "ClearColor", "FirstTriangle", "BuffersAndBindings", "Shaders",
            "Textures", "TransformsAndCamera", "DepthCullingLighting", "FrameLoopAndSynchronization",
            "FirstRenderGraph", "First2DScene", "StaticGltf", "Debugging", "Shipping",
        ];

        for (int i = 0; i < routes.Length; i++)
        {
            string path = "Learn/Rendering/" + routes[i];
            StoryInfo story = StoryRegistry.Find(path)
                ?? throw new InvalidOperationException($"Rendering Learn route is missing: {path}");
            Widget root = story.Build(new StoryContext());
            TextEditorView document = GallerySnapshots.FindDocument(root)
                ?? throw new InvalidOperationException($"Rendering Learn route is not a document: {path}");
            Assert.Contains("**難易度:**", document.DocSource);
            if (i > 0)
                Assert.Contains("```", document.DocSource); // The page remains understandable without opening sample files.
        }
    }

    [Fact]
    public void Validator_rejects_non_png_capture_files()
    {
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-invalid-png-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(output, "images"));
            File.WriteAllText(Path.Combine(output, "manifest.json"), "[]");
            File.WriteAllBytes(Path.Combine(output, "images", "broken.png"), new byte[24]);
            Assert.Throws<InvalidDataException>(() => GallerySiteExporter.Validate(output));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
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
