using System.Text.Json;
using Luxel.Gallery;
using Luxel.Gallery.Playground;
using Luxel.Gallery.Stories;
using Luxel.Gallery.Site;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;

namespace Luxel.Gallery.Site.Tests;

public sealed class PlaygroundSiteExporterTests
{
    [Fact]
    public void Gallery_shell_loads_playground_through_the_normal_story_route()
    {
        string html = GallerySiteExporter.IndexHtml;
        string script = GallerySiteExporter.ClientScript;

        Assert.Contains("href=\"playground.css\"", html);
        Assert.Contains("src=\"playground.js\"", html);
        Assert.Contains("src=\"playground-site.js\"", html);
        Assert.DoesNotContain("href=\"#playground\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("location.hash==='#playground'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch('playground.html')", script, StringComparison.Ordinal);
        Assert.Contains("LuxelGalleryPlayground?.bindAll(content)", script);
        Assert.Contains("const storyHash=path=>'#story='+encodeURIComponent(path)", script);
    }

    [Fact]
    public void Playground_bridge_recreates_iframe_and_guards_protocol_identity_and_revision()
    {
        string script = GallerySiteExporter.PlaygroundClientScript;

        Assert.Contains("destroy(root);", script);
        Assert.Contains("document.createElement(\"iframe\")", script);
        Assert.Contains("url.searchParams.set(\"instance\"", script);
        Assert.Contains("url.searchParams.set(\"revision\"", script);
        Assert.Contains("session.frame.contentWindow === event.source", script);
        Assert.Contains("event.origin !== location.origin", script);
        Assert.Contains("message.protocolVersion !== Number(root.dataset.playgroundProtocol)", script);
        Assert.Contains("message.instanceId !== session.instanceId", script);
        Assert.Contains("message?.protocol !== protocol", script);
        Assert.Contains("message.revision !== session.revision", script);
        Assert.Contains("message.type === \"ready\"", script);
        Assert.Contains("type: \"run\"", script);
        Assert.Contains("source: session.detail.request.source", script);
        Assert.Contains("message.type === \"runtime-error\"", script);
        Assert.Contains("textContent", script);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("searchParams.set(\"source\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("console.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Playground_bridge_enforces_a_bounded_execution_timeout()
    {
        string script = GallerySiteExporter.PlaygroundClientScript;

        Assert.Contains("playgroundStartupTimeoutMs || 30000", script);
        Assert.Contains("Playground runtime did not become ready within 30 seconds.", script);
        Assert.Contains("playgroundExecutionTimeoutMs || 5000", script);
        Assert.Contains("Script execution exceeded the 5 second timeout.", script);
        Assert.Contains("clearTimeout(session.timeout)", script);
        Assert.Contains("destroy(root);", script);
    }

    [Fact]
    public void Export_without_runtime_writes_playground_as_a_normal_story_with_clear_unavailable_state()
    {
        string output = Temp("without-runtime");
        try
        {
            Export(output, playgroundRoot: null);

            string fragment = ReadPlaygroundFragment(output);
            Assert.Contains("data-playground", fragment);
            Assert.Contains("Button.csx", fragment);
            Assert.Contains("Playground runtime unavailable", fragment);
            Assert.Contains("data-playground-run disabled", fragment);
            Assert.DoesNotContain("samples/luxel-playground/", fragment);
            Assert.False(File.Exists(Path.Combine(output, "playground.html")));
            AssertManifestContainsPlayground(output);
            Assert.True(File.Exists(Path.Combine(output, "playground.css")));
            Assert.True(File.Exists(Path.Combine(output, "playground.js")));
            Assert.True(File.Exists(Path.Combine(output, "playground-site.js")));
            GallerySiteExporter.Validate(output);
        }
        finally { Delete(output); }
    }

    [Fact]
    public void Export_validates_and_copies_playground_runtime_under_relative_safe_path()
    {
        string output = Temp("runtime-output");
        string runtime = CreateRuntimeRoot();
        try
        {
            Export(output, runtime);

            string fragment = ReadPlaygroundFragment(output);
            Assert.Contains("data-playground-runtime-url=\"samples/luxel-playground/\"", fragment);
            AssertManifestContainsPlayground(output);
            Assert.DoesNotContain("src=\"/samples/luxel-playground/", fragment);
            Assert.True(File.Exists(Path.Combine(output, "samples", "luxel-playground", "index.html")));
            Assert.True(File.Exists(Path.Combine(output, "samples", "luxel-playground", "_framework", "dotnet.js")));
            GallerySiteExporter.Validate(output);
        }
        finally { Delete(output); Delete(runtime); }
    }

    [Fact]
    public void Export_fails_when_playground_runtime_is_missing_a_required_file_or_has_wrong_protocol()
    {
        string output = Temp("invalid-output");
        string runtime = CreateRuntimeRoot();
        try
        {
            File.Delete(Path.Combine(runtime, "main.js"));
            FileNotFoundException missing = Assert.Throws<FileNotFoundException>(() => Export(output, runtime));
            Assert.Contains("main.js", missing.Message);

            Delete(output);
            File.WriteAllText(Path.Combine(runtime, "main.js"), "// runtime");
            File.WriteAllText(Path.Combine(runtime, "playground-runtime-manifest.json"), "{\"protocol\":\"luxel-playground\",\"protocolVersion\":2,\"entryUrl\":\"./\"}");
            InvalidDataException protocol = Assert.Throws<InvalidDataException>(() => Export(output, runtime));
            Assert.Contains("protocol 2", protocol.Message);
        }
        finally { Delete(output); Delete(runtime); }
    }

    private static void Export(string output, string? playgroundRoot)
    {
        StoryInfo story = GalleryStoryProject.CreateCatalog().Find(PlaygroundContract.StoryPath)
            ?? throw new InvalidOperationException("The Playground Story was not registered.");
        var builder = new StoryCatalogBuilder();
        builder.Add(story);
        using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
        using var rasterizer = new SkiaRasterizer2D();
        using var host = new GalleryHost(rasterizer, font, builder.Build());
        GallerySiteExporter.Export(host, [story], output, GallerySiteExporter.FindRepositoryRoot(), playgroundBrowserRoot: playgroundRoot);
    }

    private static string ReadPlaygroundFragment(string output)
        => File.ReadAllText(Path.Combine(output, "stories", GallerySiteExporter.Slug(PlaygroundContract.StoryPath) + ".html"));

    private static void AssertManifestContainsPlayground(string output)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
        JsonElement story = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Equal(PlaygroundContract.StoryPath, story.GetProperty("path").GetString());
        Assert.Equal("document", story.GetProperty("status").GetString());
        Assert.Equal("stories/examples-scripting-playground.html", story.GetProperty("fragment").GetString());
    }

    private static string CreateRuntimeRoot()
    {
        string root = Temp("runtime-root");
        Directory.CreateDirectory(Path.Combine(root, "_framework"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><script src=\"main.js\"></script>");
        File.WriteAllText(Path.Combine(root, "main.js"), "// runtime");
        File.WriteAllText(Path.Combine(root, "_framework", "dotnet.js"), "// dotnet");
        File.WriteAllText(Path.Combine(root, "playground-runtime-manifest.json"), "{\"protocol\":\"luxel-playground\",\"protocolVersion\":1,\"entryUrl\":\"./\"}");
        return root;
    }

    private static string Temp(string label) => Path.Combine(Path.GetTempPath(), $"luxel-gallery-playground-{label}-{Guid.NewGuid():N}");
    private static void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
}
