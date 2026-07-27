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
    static GallerySiteExporterTests()
        => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Gallery.Stories.DocsApi).Module.ModuleHandle);

    [Fact]
    public void Slug_is_stable_and_relative_safe()
    {
        Assert.Equal("controls-button-primary", GallerySiteExporter.Slug("Controls/Button/Primary"));
        Assert.DoesNotContain('/', GallerySiteExporter.Slug("Docs/はじめに"));
    }

    [Fact]
    public void Static_markdown_keeps_story_route_when_navigating_toc_anchor()
    {
        string html = GallerySiteExporter.RenderMarkdown(
            "# Reference\n\n- [Widget](#widget)\n\n## Widget\n", "Reference/Luxel.UI");

        Assert.Contains("section:p.get('section')", GallerySiteExporter.ClientScript);
        Assert.Contains("scrollIntoView()", GallerySiteExporter.ClientScript);
        Assert.Contains("id=\"widget\"", html);
        Assert.Contains("href=\"#story=Reference%2FLuxel.UI&amp;section=widget\"", html);
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

    [Fact]
    public void DocString_recognizes_api_tables_for_semantic_export()
    {
        DocString control = $"{Luxel.Controls.Kit.ApiTable("Button", inherited: true)}";
        DocEmbed controlEmbed = Assert.Single(control.Embeds);
        Assert.Equal(DocEmbedKind.ControlApiTable, controlEmbed.Kind);
        Assert.Equal("Button", controlEmbed.Reference);
        Assert.True(controlEmbed.IncludeInherited);

        string typeName = TypeApiRegistry.Namespaces.SelectMany(TypeApiRegistry.InNamespace)
            .Select(api => $"{api.Namespace}.{api.Name}").First();
        DocString type = $"{Luxel.Controls.Kit.TypeApiTable(typeName)}";
        DocEmbed typeEmbed = Assert.Single(type.Embeds);
        Assert.Equal(DocEmbedKind.TypeApiTable, typeEmbed.Kind);
        Assert.Equal(typeName, typeEmbed.Reference);
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
    public void Reference_pages_are_generated_per_namespace_and_control()
    {
        string[] legacy = ["Overview", "Ui", "TwoD", "Core", "Text", "Animation", "ThreeD", "Runtime"];
        foreach (string path in legacy)
            Assert.Null(StoryRegistry.Find("Reference/" + path));

        foreach (string ns in TypeApiRegistry.Namespaces)
        {
            StoryInfo story = StoryRegistry.Find("Reference/" + ns)
                ?? throw new InvalidOperationException($"Namespace reference is missing: {ns}");
            TextEditorView document = GallerySnapshots.FindDocument(story.Build(new StoryContext()))
                ?? throw new InvalidOperationException($"Namespace reference is not a document: {ns}");
            Assert.Contains($"# {ns}", document.DocSource);
            Assert.All(document.DocEmbeds, embed => Assert.Equal(DocEmbedKind.TypeApiTable, embed.Kind));
        }

        string[] requiredNamespaces =
        [
            "Luxel.Controls", "Luxel.Framework.DevTools", "Luxel.NodeGraph", "Luxel.Particles",
            "Luxel.Particles.TwoD", "Luxel.Particles.ThreeD", "Luxel.Particles.UI", "Luxel.Physics.Gizmos",
            "Luxel.Player", "Luxel.SceneEdit", "Luxel.Settings", "Luxel.Scripting", "Luxel.Scripting.Framework",
            "Luxel.Strudel", "Luxel.Graphics.TwoD.Skia", "Luxel.UI.App", "Luxel.Workbench",
        ];
        Assert.All(requiredNamespaces, ns => Assert.Contains(ns, TypeApiRegistry.Namespaces));

        string[] existingCategories = StoryRegistry.All
            .Where(story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                            && !story.Path.EndsWith("/Overview", StringComparison.Ordinal))
            .Select(story => story.Path.Split('/')[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] overviewCategories = StoryRegistry.All
            .Where(story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                            && story.Path.EndsWith("/Overview", StringComparison.Ordinal))
            .Select(story => story.Path.Split('/')[1]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(existingCategories, overviewCategories);

        var mapped = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Button"] = "Button", ["CheckBox"] = "Check", ["Knobs"] = "KnobsTable",
            ["RichText"] = "RichTextView", ["ScrollViewer"] = "Scroll", ["WrapPanel"] = "Wrap",
        };
        foreach ((string category, string apiName) in mapped)
        {
            StoryInfo story = StoryRegistry.Find($"Controls/{category}/Overview")
                ?? throw new InvalidOperationException($"Control overview is missing: {category}");
            TextEditorView document = GallerySnapshots.FindDocument(story.Build(new StoryContext()))
                ?? throw new InvalidOperationException($"Control overview is not a document: {category}");
            Assert.Contains(document.DocEmbeds,
                embed => embed.Kind == DocEmbedKind.ControlApiTable && embed.Reference == apiName);
        }
        foreach (string special in new[] { "Layout", "Kit", "CommandPalette" })
            Assert.NotNull(StoryRegistry.Find($"Controls/{special}/Overview"));

        StoryInfo controls = StoryRegistry.Find("Docs/Controls")
            ?? throw new InvalidOperationException("Docs/Controls is missing.");
        TextEditorView controlsDocument = GallerySnapshots.FindDocument(controls.Build(new StoryContext()))
            ?? throw new InvalidOperationException("Docs/Controls is not a document.");
        Assert.DoesNotContain("Reference/Overview", controlsDocument.DocSource);
        Assert.Contains("Controls/Button/Overview", controlsDocument.DocSource);
    }

    [SkippableFact]
    public void Api_tables_export_as_semantic_html_instead_of_embed_pngs()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo control = StoryRegistry.Find("Controls/Button/Overview")
            ?? throw new InvalidOperationException("Controls/Button/Overview is missing.");
        StoryInfo type = StoryRegistry.Find("Reference/Luxel.UI")
            ?? throw new InvalidOperationException("Reference/Luxel.UI is missing.");
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-api-html-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var device = CreateDeviceOrSkip();
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var host = new GalleryHost(device, font);
            GallerySiteExporter.Export(host, [control, type], output, root);

            string controlHtml = File.ReadAllText(Path.Combine(output, "stories", "controls-button-overview.html"));
            string typeHtml = File.ReadAllText(Path.Combine(output, "stories", "reference-luxel-ui.html"));
            Assert.Contains("<table class=\"api-table\">", controlHtml);
            Assert.Contains("OnClick", controlHtml);
            Assert.Contains("<table class=\"api-table\">", typeHtml);
            Assert.DoesNotContain("Static widget capture", controlHtml + typeHtml);
            Assert.Empty(Directory.GetFiles(Path.Combine(output, "images"), "embed-*.png"));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
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
    public void Rendering_docs_links_metadata_search_and_sample_sources_are_verified()
    {
        string[] routes =
        [
            "Overview", "Environment", "ClearColor", "FirstTriangle", "BuffersAndBindings", "Shaders",
            "Textures", "TransformsAndCamera", "DepthCullingLighting", "FrameLoopAndSynchronization",
            "FirstRenderGraph", "First2DScene", "StaticGltf", "Debugging", "Shipping",
        ];
        StoryInfo[] stories = routes.Select(name => StoryRegistry.Find("Learn/Rendering/" + name)
            ?? throw new InvalidOperationException($"Rendering Learn route is missing: {name}")).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null);

        Assert.Empty(DocsIndex.ValidateLinks(pages));
        for (int i = 0; i < stories.Length; i++)
        {
            string source = pages[stories[i].Path].Text;
            Assert.Contains("**難易度:**", source);
            Assert.Contains("**実行環境:**", source);
            Assert.Contains("**Backend:**", source);
            Assert.Contains("**前提知識:**", source);
            if (i > 0) Assert.Contains("story:" + stories[i - 1].Path, source);
            if (i + 1 < stories.Length) Assert.Contains("story:" + stories[i + 1].Path, source);
        }
        Assert.Contains("story:Apps/Game/Range", pages[stories[^1].Path].Text);

        string overview = pages[stories[0].Path].Text.ToLowerInvariant();
        foreach (string term in new[] { "triangle", "texture", "camera", "render graph", "gltf", "blank screen", "真っ黒" })
            Assert.Contains(term, overview);

        string root = GallerySiteExporter.FindRepositoryRoot();
        string actual = File.ReadAllText(Path.Combine(root, "samples", "LuxelTriangle", "TutorialAbi.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        string expected = ExtractMarkedRegion(actual, "triangle-abi").Trim();
        string trianglePage = pages["Learn/Rendering/FirstTriangle"].Text;
        Assert.Contains(expected, trianglePage);
        Assert.DoesNotContain("docs:begin", trianglePage);
        Assert.DoesNotContain("docs:end", trianglePage);
    }

    [Fact]
    public void Docs_gpu_routes_and_story_source_limitations_are_preserved()
    {
        string[] routes = ["GpuDevice", "TwoD", "RenderGraph", "ThreeD", "Assets", "Ecs", "Physics"];
        foreach (string route in routes)
            Assert.NotNull(StoryRegistry.Find("Docs/" + route));

        StoryInfo authoring = StoryRegistry.Find("Docs/Authoring")
            ?? throw new InvalidOperationException("Docs/Authoring is missing.");
        Assert.Contains("[Story] method本体", authoring.Source);
        Assert.Contains("完全なsource", authoring.Source);
        Assert.Contains("SampleSource(path, region)", authoring.Source);
    }

    [Fact]
    public void Broken_story_and_heading_links_are_reported()
    {
        Assert.Contains("Test/Page: story:Missing/Story",
            DocsIndex.ValidateLinks("Test/Page", "# Title\n[bad](story:Missing/Story)"));
        Assert.Contains("Test/Page: #missing-heading",
            DocsIndex.ValidateLinks("Test/Page", "# Title\n[bad](#missing-heading)"));
        Assert.Empty(DocsIndex.ValidateLinks("Test/Page", "# Existing Heading\n[ok](#existing-heading)"));
    }

    [Fact]
    public void Sample_source_regions_reject_missing_reversed_and_duplicate_markers()
    {
        Assert.Equal("line", Luxel.Gallery.Stories.DocsKit.ExtractRegion(
            "// docs:begin x\nline\n// docs:end x\n", "sample.cs", "x"));
        Assert.Throws<InvalidOperationException>(() => Luxel.Gallery.Stories.DocsKit.ExtractRegion(
            "// docs:begin x\nline\n", "sample.cs", "x"));
        Assert.Throws<InvalidOperationException>(() => Luxel.Gallery.Stories.DocsKit.ExtractRegion(
            "// docs:begin x\n\n// docs:end x\n", "sample.cs", "x"));
        Assert.Throws<InvalidOperationException>(() => Luxel.Gallery.Stories.DocsKit.ExtractRegion(
            "// docs:end x\nline\n// docs:begin x\n", "sample.cs", "x"));
        Assert.Throws<InvalidOperationException>(() => Luxel.Gallery.Stories.DocsKit.ExtractRegion(
            "// docs:begin x\na\n// docs:end x\n// docs:begin x\nb\n// docs:end x\n", "sample.cs", "x"));
    }

    [Fact]
    public void Rendering_samples_are_in_solution_and_pages_ci_builds_solution()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(root, "Luxel.slnx"));
        Assert.Contains("samples/LuxelTriangle/LuxelTriangle.csproj", solution);
        Assert.Contains("samples/LuxelRange/LuxelRange/LuxelRange.csproj", solution);

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy-pages.yml"));
        Assert.Contains("dotnet build Luxel.slnx --no-restore --configuration Release", workflow);
        Assert.Contains("--no-build --configuration Release", workflow);
        Assert.Contains("JamesIves/github-pages-deploy-action@v4.8.0", workflow);
        Assert.Contains("clean-exclude: pr-preview", workflow);
        Assert.Contains("force: false", workflow);

        string preview = File.ReadAllText(Path.Combine(root, ".github", "workflows", "preview-pages.yml"));
        Assert.Contains("pull_request:", preview);
        Assert.Contains("types: [opened, reopened, synchronize, closed]", preview);
        Assert.Contains("rossjrw/pr-preview-action@v1.8.1", preview);
        Assert.Contains("source-dir: artifacts/gallery-site", preview);
        Assert.Contains("wait-for-pages-deployment: true", preview);
        Assert.Contains("pull-requests: write", preview);
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

    private static string ExtractMarkedRegion(string source, string region)
    {
        string begin = $"docs:begin {region}";
        string end = $"docs:end {region}";
        int beginAt = source.IndexOf(begin, StringComparison.Ordinal);
        int contentStart = source.IndexOf('\n', beginAt);
        int endAt = source.IndexOf(end, contentStart, StringComparison.Ordinal);
        int endLineStart = source.LastIndexOf('\n', endAt);
        Assert.True(beginAt >= 0 && contentStart > beginAt && endLineStart > contentStart);
        return source[(contentStart + 1)..endLineStart];
    }

    private static GpuDevice CreateDeviceOrSkip()
    {
        try { return new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()); }
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
