using System.Text.Json;
using System.Security.Cryptography;
using Luxel;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Gallery.Stories;
using Luxel.Gallery.Site;
using Luxel.Graphics.TwoD;
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
        Assert.DoesNotContain('/', GallerySiteExporter.Slug("Reference/Guides/はじめに"));
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

    [Fact]
    public void Story_source_html_is_collapsed_highlightable_and_escaped()
    {
        string html = GallerySiteExporter.StorySourceHtml("[Story(\"X\")]\nstatic Widget X() => Text(\"<tag> & value\");");

        Assert.Contains("<details class=\"story-source\">", html);
        Assert.Contains("<summary>Story source</summary>", html);
        Assert.Contains("<code class=\"language-csharp\">", html);
        Assert.Contains("&lt;tag&gt; &amp; value", html);
        Assert.DoesNotContain(" open", html);
        Assert.Equal("", GallerySiteExporter.StorySourceHtml(null));
        Assert.Equal("", GallerySiteExporter.StorySourceHtml("   "));
    }

    [Fact]
    public void Export_continues_after_document_failure_and_writes_later_pages()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-errors-" + Guid.NewGuid().ToString("N"));
        var broken = new StoryInfo("Test/Broken<Docs>", 400, 240, null, _ =>
            MarkdownDoc.Create(new Signal<string>("# Broken\n\n[Missing](story:Missing/<tag>)"),
                () => Theme.Light, 400, 240));
        var healthy = new StoryInfo("Test/Healthy", 200, 100, null, _ => Luxel.Controls.Kit.Text("healthy"),
            RealWindowOnly: true);

        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            SiteExportReport report = GallerySiteExporter.Export(host, [broken, healthy], output, root);

            Assert.Equal(2, report.Stories);
            Assert.Equal(1, report.Errors);
            Assert.True(File.Exists(Path.Combine(output, "index.html")));
            string brokenHtml = File.ReadAllText(Path.Combine(output, "stories", "test-broken-docs.html"));
            string healthyHtml = File.ReadAllText(Path.Combine(output, "stories", "test-healthy.html"));
            Assert.Contains("capture-error", brokenHtml);
            Assert.Contains("Test/Broken&lt;Docs&gt;", brokenHtml);
            Assert.Contains("healthy", healthyHtml, StringComparison.OrdinalIgnoreCase);

            SiteStory[] manifest = JsonSerializer.Deserialize<SiteStory[]>(
                File.ReadAllText(Path.Combine(output, "manifest.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            Assert.Equal(2, manifest.Length);
            Assert.Equal("error", manifest[0].Status);
            Assert.Equal("unavailable", manifest[1].Status);
            Assert.Contains("Test/Broken<Docs>", manifest[0].SearchText);
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Native_story_source_pane_uses_read_only_highlighted_editor_or_placeholder()
    {
        const string source = "[Story(\"Test/Source\")]\npublic static Widget Source() => Text(\"hello\");";
        var story = new StoryInfo("Test/Source", 100, 100, null,
            _ => Luxel.Controls.Kit.Text("hello"), Source: source);

        TextEditorView editor = Assert.IsType<TextEditorView>(GalleryApp.BuildStorySourcePane(story));
        Assert.True(editor.ReadOnly);
        Assert.True(editor.Fill);
        Assert.True(editor.ShowLineNumbers);
        Assert.NotNull(editor.EditorFont);
        Assert.Contains(editor.Providers, provider => provider is SyntaxHighlightProvider);
        Assert.Equal(source, editor.Value.Get().Value);

        Text placeholder = Assert.IsType<Text>(GalleryApp.BuildStorySourcePane(
            new StoryInfo("Test/Generated", 0, 0, null, _ => Luxel.Controls.Kit.Text("generated"))));
        Assert.Contains("Source unavailable", placeholder.DebugDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Rasterizer_api_reference_exposes_backend_neutral_contracts()
    {
        TypeApi contract = TypeApiRegistry.Find("Luxel.Graphics.TwoD.IRasterizer2D")
            ?? throw new Xunit.Sdk.XunitException("IRasterizer2D was not registered in the API reference.");
        Assert.Equal("interface", contract.Kind);
        Assert.Contains(contract.Members, member => member.Name == "CreateScene");
        Assert.DoesNotContain(contract.Members, member => member.Type.Contains("GpuBuffer", StringComparison.Ordinal)
            || member.Type.Contains("GpuCommandBuffer", StringComparison.Ordinal));

        Assert.NotNull(TypeApiRegistry.Find("Luxel.Diagnostics.EngineDiagnostics"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Framework.FixedTimestep"));
        Assert.Contains("Luxel.Diagnostics", TypeApiRegistry.Namespaces);
        Assert.DoesNotContain("Luxel", TypeApiRegistry.Namespaces);
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Mathematics.OrbitCamera"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Mathematics.Xorshift64"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Typography.VectorFont"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Typography.TwoD.TypographyTwoDExtensions"));
        Assert.Contains("Luxel.Typography.TwoD", TypeApiRegistry.Namespaces);
        Assert.Null(TypeApiRegistry.Find("Luxel.OrbitCamera"));
        Assert.Null(TypeApiRegistry.Find("Luxel.Particles.Xorshift64"));

        Assert.NotNull(TypeApiRegistry.Find("Luxel.Graphics.TwoD.GpuDeviceRasterizer2D"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D"));
    }

    [SkippableFact]
    public void Gpu_retained_session_applies_incremental_canvas_writes()
    {
        using GpuDevice device = CreateDeviceOrSkip();
        using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
        using var host = new GalleryHost(device, font);
        host.SelectWidget(Luxel.Controls.Kit.Text("incremental"), 160, 80);

        RetainedCanvas canvas = host.Canvas
            ?? throw new Xunit.Sdk.XunitException("GalleryHost did not create a retained canvas.");
        UiNode node = Assert.Single(canvas.Root.Children);
        node.Transform = Affine2D.Translate(4, 0);

        host.Step(1f / 60f);

        Assert.False(canvas.LastWasFullRebuild);
        Assert.True(canvas.LastTransformWrites > 0);
    }

    [SkippableFact]
    public void Focused_export_is_complete_and_deterministic()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo story = StoryRegistry.Find("Learn/Rendering/Basics/Shaders")
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
            Assert.True(File.Exists(Path.Combine(a, "vendor", "highlightjs", "highlight.min.js")));
            Assert.True(File.Exists(Path.Combine(a, "vendor", "highlightjs", "github-dark.min.css")));
            Assert.True(File.Exists(Path.Combine(a, "licenses", "highlight.js-LICENSE.txt")));
            Assert.Contains("language-powershell", html);
            string imageFragment = File.ReadAllText(Path.Combine(a, "stories", "controls-button-intents.html"));
            Assert.Contains("src=\"images/controls-button-intents.png\"", imageFragment);
            Assert.Contains("<details class=\"story-source\">", imageFragment);
            Assert.Contains("<code class=\"language-csharp\">", imageFragment);
            Assert.Contains("[Story", imageFragment);
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

        Assert.NotNull(StoryRegistry.Find("Examples/UI/Navigation"));
        Assert.NotNull(StoryRegistry.Find("Controls/NavigationView/Basic"));
        Assert.NotNull(StoryRegistry.Find("Controls/NavigationView/Overview"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.Navigation"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.NavigationHost"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.NavigationPath"));

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

        StoryInfo controls = StoryRegistry.Find("Reference/Guides/Controls")
            ?? throw new InvalidOperationException("Reference/Guides/Controls is missing.");
        TextEditorView controlsDocument = GallerySnapshots.FindDocument(controls.Build(new StoryContext()))
            ?? throw new InvalidOperationException("Reference/Guides/Controls is not a document.");
        Assert.Contains("Controls/NavigationView/Basic", controlsDocument.DocSource);
        Assert.Contains("Examples/UI/Navigation", controlsDocument.DocSource);
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
    public void Old_demo_routes_are_not_kept_as_aliases()
    {
        Assert.Null(StoryRegistry.Find("Demos/2D/CameraRig"));
        Assert.NotNull(StoryRegistry.Find("Examples/2D/CameraRig"));
        Assert.Empty(StoryRegistry.AliasesFor("Examples/2D/CameraRig"));
    }

    [Fact]
    public void Rendering_learn_chain_is_complete_and_has_inline_examples()
    {
        string[] routes =
        [
            "Learn/Rendering/Basics/Overview", "Learn/Rendering/Basics/Environment",
            "Learn/Rendering/Basics/ClearColor", "Learn/Rendering/Basics/FirstTriangle",
            "Learn/Rendering/Basics/BuffersAndBindings", "Learn/Rendering/Basics/Shaders",
            "Learn/Rendering/Basics/FrameLoopAndSynchronization", "Learn/Rendering/ThreeD/Textures",
            "Learn/Rendering/ThreeD/TransformsAndCamera", "Learn/Rendering/ThreeD/DepthCullingLighting",
            "Learn/Rendering/ThreeD/FirstRenderGraph", "Learn/Rendering/ThreeD/StaticGltf",
            "Learn/Rendering/ThreeD/Debugging", "Learn/Rendering/ThreeD/Shipping",
        ];

        for (int i = 0; i < routes.Length; i++)
        {
            string path = routes[i];
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
            "Learn/Rendering/Basics/Overview", "Learn/Rendering/Basics/Environment",
            "Learn/Rendering/Basics/ClearColor", "Learn/Rendering/Basics/FirstTriangle",
            "Learn/Rendering/Basics/BuffersAndBindings", "Learn/Rendering/Basics/Shaders",
            "Learn/Rendering/Basics/FrameLoopAndSynchronization", "Learn/Rendering/ThreeD/Textures",
            "Learn/Rendering/ThreeD/TransformsAndCamera", "Learn/Rendering/ThreeD/DepthCullingLighting",
            "Learn/Rendering/ThreeD/FirstRenderGraph", "Learn/Rendering/ThreeD/StaticGltf",
            "Learn/Rendering/ThreeD/Debugging", "Learn/Rendering/ThreeD/Shipping",
        ];
        StoryInfo[] stories = routes.Select(name => StoryRegistry.Find(name)
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
        string trianglePage = pages["Learn/Rendering/Basics/FirstTriangle"].Text;
        Assert.Contains(expected, trianglePage);
        Assert.DoesNotContain("docs:begin", trianglePage);
        Assert.DoesNotContain("docs:end", trianglePage);

        StoryInfo clearColor = stories.Single(story => story.Path == "Learn/Rendering/Basics/ClearColor");
        Assert.Equal("rendering.clear-color", clearColor.SampleBundle);
        string clearColorPage = pages[clearColor.Path].Text;
        Assert.Contains("samples/ClearColor.cs", clearColorPage);
        Assert.Contains("dotnet run --file samples/ClearColor.cs -- vk", clearColorPage);
        Assert.Contains("clear-color.ppm", clearColorPage);
        Assert.DoesNotContain("WindowSystem", clearColorPage);
        Assert.DoesNotContain("GpuSurface", clearColorPage);
        Assert.DoesNotContain("samples/LuxelTriangle/Program.cs", clearColorPage);
        Assert.DoesNotContain("standalone-frame-loop", clearColorPage);
        string clearColorSource = File.ReadAllText(Path.Combine(root, "samples", "ClearColor.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(clearColorSource.Trim(), clearColorPage);
    }

    [Fact]
    public void Docs_gpu_routes_and_story_source_limitations_are_preserved()
    {
        string[] routes = ["GpuDevice", "TwoD", "RenderGraph", "ThreeD", "Assets", "Ecs", "Physics"];
        foreach (string route in routes)
            Assert.NotNull(StoryRegistry.Find("Reference/Guides/" + route));

        StoryInfo authoring = StoryRegistry.Find("Internals/Authoring")
            ?? throw new InvalidOperationException("Internals/Authoring is missing.");
        Assert.Contains("完全な `[Story]` method宣言", authoring.Source);
        Assert.Contains("下部の **Source** タブ", authoring.Source);
        Assert.Contains("SampleSource(path, region)", authoring.Source);
    }

    [Fact]
    public void Start_courses_and_sample_bundles_are_registered_and_link_clean()
    {
        Assert.Equal("Start", StoryRegistry.All[0].Component);
        Assert.NotNull(StoryRegistry.Find("Start/Welcome"));
        Assert.NotNull(StoryRegistry.Find("Learn/Rendering/TwoD/Overview"));
        Assert.NotNull(StoryRegistry.Find("Learn/Rendering/RasterizerInternals/Overview"));
        foreach (string route in new[] { "Learn/Input/Overview", "Learn/Input/ActionsAndContexts", "Learn/Input/PlatformsAndTesting",
                     "Learn/Audio/Overview", "Learn/Audio/ClipsSourcesAndBuses", "Learn/Audio/SpatialStreamingAndTesting",
                     "Learn/Resources/Overview", "Learn/Resources/PipelinesAndDag", "Learn/Resources/ReloadAndLifetime",
                     "Build/Blocks/Input/Actions", "Build/Blocks/Audio/Tone", "Build/Blocks/Resources/Pipeline" })
            Assert.NotNull(StoryRegistry.Find(route));
        string[] diagnostics = ["InputPaths", "EncodedScene", "Bounds", "TileBins", "Coverage", "Stroke", "Composite", "Dispatch", "RetainedUpdates"];
        foreach (string diagnostic in diagnostics)
            Assert.NotNull(StoryRegistry.Find("Examples/2D/Rasterizer/" + diagnostic));
        Assert.NotNull(StoryRegistry.Find("Build/Recipes/TriangleApp"));
        Assert.Contains(StoryRegistry.All, story => story.Path.StartsWith("Examples/", StringComparison.Ordinal));
        Assert.DoesNotContain(StoryRegistry.All, story => story.Path.StartsWith("Demos/", StringComparison.Ordinal));
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(StoryRegistry.All, resources: null)));
    }

    [Fact]
    public void Sample_bundle_graph_and_files_are_valid()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        HashSet<string> ids = SampleBundleRegistry.All.Select(bundle => bundle.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(ids);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            Assert.True(visiting.Add(id), $"Sample bundle dependency cycle includes '{id}'.");
            SampleBundleInfo bundle = SampleBundleRegistry.Find(id)!;
            foreach (string dependency in bundle.Dependencies ?? [])
            {
                Assert.Contains(dependency, ids);
                if (!visited.Contains(dependency)) Visit(dependency);
            }
            visiting.Remove(id);
            visited.Add(id);
        }

        foreach (SampleBundleInfo bundle in SampleBundleRegistry.All.Where(bundle => !bundle.Id.StartsWith("test.", StringComparison.Ordinal)))
        {
            Visit(bundle.Id);
            foreach (SampleFileInfo file in bundle.Files)
            {
                string sourcePath = Path.Combine(root, file.Path);
                Assert.True(file.EffectiveMode == SampleFileMode.Glob ? Directory.Exists(sourcePath) : File.Exists(sourcePath), file.Path);
                if (file.Kind != SampleFileKind.Asset)
                    _ = Luxel.Gallery.Stories.DocsKit.SampleSource(file.Path, file.Region, file.Language);
            }
        }
        StoryInfo triangle = StoryRegistry.Find("Build/Recipes/TriangleApp")!;
        Assert.Equal("rendering.triangle", triangle.SampleBundle);
        Assert.NotNull(SampleBundleRegistry.Find(triangle.SampleBundle));
    }

    [Fact]
    public void Rendering_overview_follows_catalog_and_build_paths_match_copy_levels()
    {
        Dictionary<string, DocsPage> pages = DocsIndex.Build(
            [StoryRegistry.Find("Learn/Rendering/Basics/Overview")!], resources: null);
        string overview = pages["Learn/Rendering/Basics/Overview"].Text;
        int previous = -1;
        foreach (string route in RenderingCourseCatalog.ApplicationRoute)
        {
            int current = overview.IndexOf("story:" + route, StringComparison.Ordinal);
            Assert.True(current > previous, $"Overview route is missing or out of order: {route}");
            previous = current;
        }
        Assert.True(overview.IndexOf("story:Examples/3D/Triangle", StringComparison.Ordinal) > previous);
        Assert.Contains("独立トラック", overview);

        foreach (StoryInfo story in StoryRegistry.All.Where(story => story.Path.StartsWith("Build/Blocks/", StringComparison.Ordinal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(story.SampleBundle), story.Path);
            Assert.Equal(SampleCopyLevel.Block, SampleBundleRegistry.Find(story.SampleBundle!)!.CopyLevel);
        }
        foreach (StoryInfo story in StoryRegistry.All.Where(story => story.Path.StartsWith("Build/Recipes/", StringComparison.Ordinal)))
        {
            Assert.False(string.IsNullOrWhiteSpace(story.SampleBundle), story.Path);
            Assert.Contains(SampleBundleRegistry.Find(story.SampleBundle!)!.CopyLevel,
                new[] { SampleCopyLevel.Recipe, SampleCopyLevel.StandaloneProject });
        }

        Assert.Null(StoryRegistry.Find("Build/Blocks/ThreeD/Triangle"));
        Assert.Null(StoryRegistry.Find("Build/Recipes/TwoDCanvasApp"));
        Assert.Null(StoryRegistry.Find("Build/Recipes/MiniGame2D"));
        Assert.Null(StoryRegistry.Find("Build/Recipes/Viewer3D"));
        Assert.NotNull(StoryRegistry.Find("Build/Recipes/HeadlessScene2D"));
    }

    [Fact]
    public void Sample_bundle_materializer_expands_dependencies_and_rejects_conflicts()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<string> files = SampleBundleMaterializer.Materialize(root, "rendering.triangle", output);
            Assert.Contains(files, path => path.EndsWith(Path.Combine("samples", "LuxelTriangle", "LuxelTriangle.csproj"), StringComparison.Ordinal));
            Assert.Contains(files, path => path.EndsWith(Path.Combine("samples", "LuxelTriangle", "Program.cs"), StringComparison.Ordinal));
            string abi = File.ReadAllText(Path.Combine(output, "samples", "LuxelTriangle", "TutorialAbi.cs"));
            Assert.Contains("enum TutorialStage", abi);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }

        string clearColorOutput = Path.Combine(Path.GetTempPath(), "luxel-bundle-clear-color-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<string> files = SampleBundleMaterializer.Materialize(root, "rendering.clear-color", clearColorOutput);
            string clearColorPath = Path.Combine(clearColorOutput, "samples", "ClearColor.cs");
            Assert.Contains(files, path => string.Equals(path, clearColorPath, StringComparison.Ordinal));
            string clearColor = File.ReadAllText(clearColorPath);
            Assert.Contains("#:project ../src/Luxel.Graphics/Luxel.Graphics.csproj", clearColor);
            Assert.Contains("clear-color: offline", clearColor);
            Assert.Contains("WritePpm", clearColor);
            Assert.DoesNotContain("Luxel.Platform", clearColor);
            Assert.DoesNotContain("WindowSystem", clearColor);
            Assert.DoesNotContain("GpuSurface", clearColor);
            Assert.False(File.Exists(Path.Combine(clearColorOutput, "samples", "LuxelTriangle", "TriangleRenderer.cs")));
            Assert.False(File.Exists(Path.Combine(clearColorOutput, "samples", "LuxelTriangle", "TutorialAbi.cs")));
        }
        finally
        {
            if (Directory.Exists(clearColorOutput)) Directory.Delete(clearColorOutput, recursive: true);
        }

        string uiOutput = Path.Combine(Path.GetTempPath(), "luxel-bundle-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            SampleBundleMaterializer.Materialize(root, "ui.headless-tree", uiOutput);
            byte[] expectedFont = File.ReadAllBytes(Path.Combine(root, "assets", "fonts", "BIZUDGothic-Regular.ttf"));
            byte[] actualFont = File.ReadAllBytes(Path.Combine(uiOutput, "assets", "fonts", "BIZUDGothic-Regular.ttf"));
            Assert.Equal(SHA256.HashData(expectedFont), SHA256.HashData(actualFont));
        }
        finally
        {
            if (Directory.Exists(uiOutput)) Directory.Delete(uiOutput, recursive: true);
        }

        string conflictOutput = Path.Combine(Path.GetTempPath(), "luxel-bundle-conflict-" + Guid.NewGuid().ToString("N"));
        string conflictId = "test.materializer.conflict." + Guid.NewGuid().ToString("N");
        SampleBundleRegistry.Register(new SampleBundleInfo(conflictId, conflictId, conflictId, "Test", SampleCopyLevel.Recipe,
            [new("README.md", SampleFileKind.Asset, Destination: "same.txt"),
             new("Luxel.slnx", SampleFileKind.Asset, Destination: "same.txt")]));
        try
        {
            Assert.Throws<InvalidOperationException>(() => SampleBundleMaterializer.Materialize(root, conflictId, conflictOutput));
        }
        finally
        {
            if (Directory.Exists(conflictOutput)) Directory.Delete(conflictOutput, recursive: true);
        }
    }

    [Fact]
    public async Task File_based_offline_clear_color_restores_and_builds_from_a_materialized_temp_directory()
    {
        SampleVerificationResult result = await SampleBundleVerifier.VerifyAsync(
            GallerySiteExporter.FindRepositoryRoot(), "rendering.clear-color", runSmoke: false);
        Assert.Equal(Path.Combine("samples", "ClearColor.cs"), result.Project);
        Assert.Contains("ClearColor.cs", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_clear_color_bundle_renders_an_image_without_a_window()
    {
        SampleVerificationResult result = await SampleBundleVerifier.VerifyAsync(
            GallerySiteExporter.FindRepositoryRoot(), "rendering.clear-color");
        Assert.Contains("clear-color: offline", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("DISPLAY", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Triangle_bundle_restores_and_builds_from_a_materialized_temp_directory()
    {
        SampleVerificationResult result = await SampleBundleVerifier.VerifyAsync(
            GallerySiteExporter.FindRepositoryRoot(), "rendering.triangle", runSmoke: false);
        Assert.Contains("Build succeeded", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_headless_bundles_restore_build_and_smoke_from_materialized_temp_directories()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        foreach (string id in new[] { "input.actions", "audio.tone", "resources.pipeline", "rendering.2d" })
        {
            SampleVerificationResult result = await SampleBundleVerifier.VerifyAsync(root, id);
            Assert.Contains(SampleBundleRegistry.Find(id)!.ExpectedStdoutMarker!, result.Stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runtime_sample_bundles_are_connected_to_learn_and_build_pages()
    {
        (string Bundle, string Learn, string Build)[] cases =
        [
            ("input.actions", "Learn/Input/Overview", "Build/Blocks/Input/Actions"),
            ("audio.tone", "Learn/Audio/Overview", "Build/Blocks/Audio/Tone"),
            ("resources.pipeline", "Learn/Resources/Overview", "Build/Blocks/Resources/Pipeline"),
        ];
        foreach (var item in cases)
        {
            SampleBundleInfo bundle = SampleBundleRegistry.Find(item.Bundle)!;
            Assert.Equal(SampleCopyLevel.Block, bundle.CopyLevel);
            Assert.False(string.IsNullOrWhiteSpace(bundle.SmokeCommand));
            Assert.Equal(item.Bundle, StoryRegistry.Find(item.Learn)!.SampleBundle);
            Assert.Equal(item.Bundle, StoryRegistry.Find(item.Build)!.SampleBundle);
        }
    }

    [Fact]
    public void Framework_and_ui_learning_paths_have_clean_consumer_bundles()
    {
        (string Route, string Bundle)[] pages =
        [
            ("Learn/Framework/Overview", "framework.fixed-timestep"),
            ("Learn/Framework/FixedTimestepAndPhases", "framework.fixed-timestep"),
            ("Build/Blocks/Framework/FixedTimestep", "framework.fixed-timestep"),
            ("Learn/UI/WidgetTrees", "ui.headless-tree"),
            ("Learn/UI/Signals", "ui.headless-tree"),
            ("Build/Blocks/UI/HeadlessTree", "ui.headless-tree"),
        ];
        foreach ((string route, string bundle) in pages)
        {
            StoryInfo story = StoryRegistry.Find(route) ?? throw new InvalidOperationException(route);
            Assert.Equal(bundle, story.SampleBundle);
            Assert.NotNull(SampleBundleRegistry.Find(bundle));
        }
        Assert.NotNull(StoryRegistry.Find("Learn/Framework/ScenesAndServices"));
        Assert.NotNull(StoryRegistry.Find("Learn/UI/BuildAndReconciliation"));
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(StoryRegistry.All, resources: null)));
    }

    [Fact]
    public void Domain_and_production_learning_paths_are_registered_and_linked()
    {
        string[] routes =
        [
            "Learn/Assets/Overview", "Learn/Assets/GltfRuntime",
            "Learn/ECSPhysics/Overview", "Learn/ECSPhysics/CollisionsAndGizmos",
            "Learn/AnimationParticles/Overview", "Learn/AnimationParticles/GraphsAndEmitters",
            "Learn/Scripting/Overview", "Learn/Scripting/ReloadAndIsolation",
            "Learn/Production/StudioToPlayer", "Learn/Production/Workbench", "Learn/Production/ValidateAndShip",
            "Build/Recipes/Cavern2D", "Build/Recipes/Range3D", "Build/Blocks/Scripting/HotReload",
        ];
        StoryInfo[] stories = routes.Select(route => StoryRegistry.Find(route)
            ?? throw new InvalidOperationException(route)).ToArray();
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(stories, resources: null)));
    }

    [Fact]
    public void Runtime_examples_are_source_backed_and_bundle_connected()
    {
        (string Route, string Bundle)[] examples =
        [
            ("Examples/Input/Actions", "input.actions"), ("Examples/Input/ContextStack", "input.actions"),
            ("Examples/Input/Bindings", "input.actions"), ("Examples/Audio/WaveformAndVoice", "audio.tone"),
            ("Examples/Audio/Buses", "audio.tone"), ("Examples/Audio/SpatialAttenuation", "audio.tone"),
            ("Examples/Audio/StreamingQueue", "audio.tone"), ("Examples/Resources/Pipeline", "resources.pipeline"),
            ("Examples/Resources/DependencyDag", "resources.pipeline"), ("Examples/Resources/Reload", "resources.pipeline"),
            ("Examples/Resources/Lifetime", "resources.pipeline"),
        ];
        StoryInfo[] stories = examples.Select(item => StoryRegistry.Find(item.Route)!).ToArray();
        Assert.DoesNotContain(stories, story => story is null);
        for (int i = 0; i < examples.Length; i++) Assert.Equal(examples[i].Bundle, stories[i].SampleBundle);
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null);
        Assert.Equal(examples.Length, pages.Count);
        Assert.Empty(DocsIndex.ValidateLinks(pages));
        Assert.All(pages.Values, page => Assert.Contains("コピーして動かす", page.Text));
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
        StoryInfo story = StoryRegistry.Find("Internals/Architecture")
            ?? throw new InvalidOperationException("Internals/Architecture story is missing.");
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-site-mermaid-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var device = CreateDeviceOrSkip();
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var host = new GalleryHost(device, font);
            GallerySiteExporter.Export(host, [story], output, root);
            string html = string.Join('\n', Directory.GetFiles(output, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
            string renderedBody = html[..html.IndexOf("<details class=\"story-source\">", StringComparison.Ordinal)];
            Assert.DoesNotContain("```mermaid", renderedBody);
            Assert.Contains("Static mermaid capture", renderedBody);
            Assert.Contains("```mermaid", html); // generated method source remains visible in the collapsed Source section
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
