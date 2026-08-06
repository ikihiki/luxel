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
    private static readonly StoryCatalog Catalog = GalleryStoryProject.CreateCatalog();

    static GallerySiteExporterTests()
        => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Gallery.Stories.DocsApi).Module.ModuleHandle);

    [Fact]
    public void Gallery_story_project_registers_an_explicit_isolated_catalog()
    {
        StoryCatalog catalog = GalleryStoryProject.CreateCatalog();
        var empty = new StoryCatalogBuilder().Build();

        Assert.NotNull(catalog.Find("Start/Welcome"));
        Assert.NotNull(catalog.Find("Controls/Button/Primary"));
        Assert.NotNull(catalog.Find("Controls/Button/Overview"));
        Assert.NotNull(catalog.Find("Examples/Scripting/Playground"));
        Assert.Equal("webgpu-browser-v1", catalog.Find("Examples/3D/ClearColor")?.RuntimeBundleId);
        Assert.Equal("webgpu-browser-v1", catalog.Find("Examples/3D/Triangle")?.RuntimeBundleId);
        Assert.Equal("webgpu-browser-v1", catalog.Find("Controls/Button/Counter")?.RuntimeBundleId);
        StoryInfo textureStory = Assert.IsType<StoryInfo>(catalog.Find("Examples/3D/Textures"));
        Assert.Contains("CreateCheckerboard(textureWidth, textureHeight)", textureStory.Source);
        Assert.Contains("resources.CreateSampledTexture(", textureStory.Source);
        Assert.Contains("resources.CreateSampler(", textureStory.Source);
        Assert.Contains("ctx.Observe(texture)", textureStory.Source);
        Assert.Contains("texture.Value.BindlessIndex", textureStory.Source);
        Assert.DoesNotContain("device.CreateTexture(", textureStory.Source);
        Assert.DoesNotContain("private static byte[] CreateCheckerboard", textureStory.Source);
        Assert.DoesNotContain("for (uint y", textureStory.Source);
        Assert.Null(empty.Find("Start/Welcome"));
    }

    [Fact]
    public void Playground_is_a_buildable_native_story()
    {
        StoryInfo story = Assert.IsType<StoryInfo>(Catalog.Find("Examples/Scripting/Playground"));
        var context = new StoryContext();
        context.SetServices(GalleryServices.Provider);

        Widget widget = story.Build(context);

        Assert.NotNull(widget);
        Assert.Equal("Examples/Scripting/Playground", story.Path);
        (widget as IDisposable)?.Dispose();
    }

    [Fact]
    public void Browser_webgpu_bundle_contains_publishable_source_and_gallery_run_link()
    {
        SampleBundleInfo bundle = Assert.IsType<SampleBundleInfo>(SampleBundleRegistry.Find("rendering.webgpu-browser"));
        Assert.Equal(SampleCopyLevel.StandaloneProject, bundle.CopyLevel);
        Assert.Contains(bundle.Files, file => file.Path == "samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj");
        Assert.Contains(bundle.Files, file => file.Path == "samples/LuxelWebGpuBrowser/wwwroot/main.js");
        Assert.Contains(bundle.Files, file => file.Path.EndsWith("compute.wgsl", StringComparison.Ordinal));
        Assert.Contains(bundle.Files, file => file.Path.EndsWith("triangle.wgsl", StringComparison.Ordinal));
        Assert.Contains("dotnet publish", bundle.RunCommand);

    }

    [Fact]
    public void Static_gallery_includes_an_accessible_ipad_review_workspace()
    {
        string html = GallerySiteExporter.IndexHtml;
        string css = GallerySiteExporter.SiteCss;

        Assert.Contains("id=\"review-panel\"", html);
        Assert.Contains("aria-label=\"ギャラリーフィードバック\"", html);
        Assert.Contains("id=\"review-comment\"", html);
        Assert.Contains("id=\"review-import\"", html);
        Assert.Contains("role=\"toolbar\"", html);
        Assert.Contains("class=\"icon-button\"", html);
        Assert.Contains("aria-label=\"全件をコピー\"", html);
        Assert.Contains("class=\"review-comment-label\" for=\"review-comment\">フィードバック</label>", html);
        Assert.True(html.IndexOf("class=\"review-actions\"", StringComparison.Ordinal)
            < html.IndexOf("id=\"review-comment\"", StringComparison.Ordinal));
        Assert.Contains("viewport-fit=cover", html);
        Assert.Contains("@media(max-width:820px),(orientation:portrait)", css);
        Assert.Contains("grid-template-columns:310px minmax(0,1fr) minmax(320px,390px)", css);
        Assert.Contains(".review-actions{display:flex;flex-wrap:nowrap", css);
        Assert.Contains(".review-actions>.icon-button{flex:1 1 0}", css);
        Assert.Contains(".review-comment-label{display:block;margin:3px 0 2px", css);
        Assert.DoesNotContain("Start/Welcome", html);
        Assert.DoesNotContain("現在のストーリーを開く", html);
        Assert.DoesNotContain("この端末に自動保存します。", html);
        Assert.DoesNotContain("下書きはこのSafari内だけに保存され、端末間では同期されません。", html);
        Assert.Contains("env(safe-area-inset-bottom)", css);
        Assert.Contains("prefers-reduced-motion", css);
    }

    [Fact]
    public void Review_panel_stays_fixed_while_the_complete_story_remains_scrollable()
    {
        string css = GallerySiteExporter.SiteCss;

        Assert.Contains("html,body{height:100%;overflow:hidden", css);
        Assert.Contains("main{min-width:0;height:100vh;height:100dvh", css);
        Assert.Contains("overflow-y:auto;overscroll-behavior:contain", css);
        Assert.Contains("#review-panel{display:none;position:sticky", css);
        Assert.Contains("height:100dvh;overflow:hidden", css);
        Assert.Contains("body.review-open #review-panel{display:flex;flex-direction:column}", css);
        Assert.Contains("#review-comment{flex:1 1 160px;min-height:80px", css);
        Assert.Contains("bottom:var(--keyboard-inset,0px)", css);
        Assert.Contains("height:min(44dvh,340px)", css);
        Assert.Contains("body.review-keyboard #review-panel{height:min(34vh,220px)", css);
        Assert.Contains("var(--visual-viewport-height,100dvh)*.38", css);
        string script = GallerySiteExporter.ClientScript;
        Assert.Contains("function syncVisualViewport()", script);
        Assert.Contains("document.body.classList.add('review-keyboard')", script);
        Assert.Contains("window.visualViewport?.addEventListener('resize',syncVisualViewport)", script);
        Assert.DoesNotContain("reviewComment.focus", script);
        Assert.DoesNotContain("#review-panel{display:none;position:sticky;top:0;height:100vh;height:100dvh;overflow:auto", css);
    }

    [Fact]
    public void Review_drafts_are_path_scoped_exportable_and_do_not_embed_credentials()
    {
        string script = GallerySiteExporter.ClientScript;

        Assert.Contains("reviewKey='luxel-gallery-review:v1:'+location.pathname", script);
        Assert.Contains("stories[activeReviewPath]", script);
        Assert.Contains("addEventListener('pagehide',saveCurrentReview)", script);
        Assert.Contains("function reviewMarkdown()", script);
        Assert.Contains("luxel-gallery-feedback.json", script);
        Assert.Contains("JSON.parse(await file.text())", script);
        Assert.Contains("encodeURIComponent(body)", script);
        Assert.Contains("https://github.com/ikihiki/luxel/issues/new", script);
        Assert.DoesNotContain("github_pat_", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Slug_is_stable_and_relative_safe()
    {
        Assert.Equal("controls-button-primary", GallerySiteExporter.Slug("Controls/Button/Primary"));
        Assert.DoesNotContain('/', GallerySiteExporter.Slug("Reference/はじめに"));
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
    public void Gallery_api_tables_use_explicit_semantic_doc_embeds()
    {
        var controlTable = Luxel.Gallery.UI.Kit.ApiTable("Button", inherited: true);
        DocString control = $"{new DocEmbed(controlTable, DocEmbedKind.ControlApiTable, "Button", IncludeInherited: true)}";
        DocEmbed controlEmbed = Assert.Single(control.Embeds);
        Assert.Equal(DocEmbedKind.ControlApiTable, controlEmbed.Kind);
        Assert.Equal("Button", controlEmbed.Reference);
        Assert.True(controlEmbed.IncludeInherited);

        string typeName = TypeApiRegistry.Namespaces.SelectMany(TypeApiRegistry.InNamespace)
            .Select(api => $"{api.Namespace}.{api.Name}").First();
        var typeTable = Luxel.Gallery.UI.Kit.TypeApiTable(typeName);
        DocString type = $"{new DocEmbed(typeTable, DocEmbedKind.TypeApiTable, typeName)}";
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
    public void Runtime_story_export_realizes_the_main_story_only_once()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-single-realization-" + Guid.NewGuid().ToString("N"));
        var story = new StoryInfo("Test/SingleRealization/" + Guid.NewGuid().ToString("N"), 160, 80, null,
            _ => Luxel.Controls.Kit.Text("single realization"));
        StoryRegistry.Register(story);
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font, publishFrames: false);

            SiteExportReport report = GallerySiteExporter.Export(host, [story], output, root);

            Assert.Equal(1, report.Stories);
            Assert.Equal(1, host.StorySelectionCount);
            Assert.True(File.Exists(Path.Combine(output, "images", GallerySiteExporter.Slug(story.Path) + ".png")));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Static_capture_none_exports_native_story_as_unavailable_without_creating_host()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-no-capture-" + Guid.NewGuid().ToString("N"));
        var story = new StoryInfo("Test/NoCapture", 160, 80, null,
            _ => throw new InvalidOperationException("native story must not be realized"), Source: "static Widget NoCapture() => Text(\"source\");");
        var builder = new StoryCatalogBuilder();
        builder.Add(story);
        StoryCatalog catalog = builder.Build();
        int hostCreations = 0;
        try
        {
            SiteExportReport report = GallerySiteExporter.Export(
                () => { hostCreations++; throw new InvalidOperationException("host must remain lazy"); },
                catalog, [story], output, root, options: new SiteExportOptions { StaticCapture = StaticCaptureMode.None });

            Assert.Equal(0, hostCreations);
            Assert.Equal(0, report.Images);
            Assert.Equal(1, report.Unavailable);
            Assert.Equal(1, report.Metrics?.PolicySkips);
            SiteStory entry = Assert.Single(JsonSerializer.Deserialize<SiteStory[]>(
                File.ReadAllText(Path.Combine(output, "manifest.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!);
            Assert.Equal("unavailable", entry.Status);
            Assert.Null(entry.Image);
            Assert.Empty(entry.ImageSha256);
            string fragment = File.ReadAllText(Path.Combine(output, "stories", "test-nocapture.html"));
            Assert.Contains("Static preview was disabled", fragment);
            Assert.Contains("Story source", fragment);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Golden_only_exports_semantic_document_and_skips_native_story_without_creating_host()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-golden-host-free-" + Guid.NewGuid().ToString("N"));
        var document = new StoryInfo("Test/Document", 0, 0, null,
            _ => throw new InvalidOperationException("document widget must not be realized"),
            ResultBuild: static _ => StoryResult.FromMarkdown("# Semantic document"));
        var native = new StoryInfo("Test/Native", 160, 80, null,
            _ => throw new InvalidOperationException("native story must not be realized"));
        var builder = new StoryCatalogBuilder();
        builder.Add(document);
        builder.Add(native);
        StoryCatalog catalog = builder.Build();
        int hostCreations = 0;
        try
        {
            SiteExportReport report = GallerySiteExporter.Export(
                () => { hostCreations++; throw new InvalidOperationException("host must remain lazy"); },
                catalog, [document, native], output, root,
                options: new SiteExportOptions { StaticCapture = StaticCaptureMode.GoldenOnly });

            Assert.Equal(0, hostCreations);
            Assert.Equal(0, report.Metrics?.NativeRealization.TotalMilliseconds);
            Assert.Equal(1, report.Metrics?.DocumentStories);
            Assert.Equal(1, report.Unavailable);
            SiteStory[] entries = JsonSerializer.Deserialize<SiteStory[]>(
                File.ReadAllText(Path.Combine(output, "manifest.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            Assert.Equal("document", Assert.Single(entries, entry => entry.Path == document.Path).Status);
            Assert.Equal("unavailable", Assert.Single(entries, entry => entry.Path == native.Path).Status);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Incremental_semantic_export_reuses_unchanged_files_without_creating_host()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-incremental-" + Guid.NewGuid().ToString("N"));
        StoryResult BuildResult(StoryContext _) => StoryResult.FromMarkdown("# Incremental\n\nSemantic document.");
        var story = new StoryInfo("Test/Incremental", 0, 0, null,
            _ => throw new InvalidOperationException("native story must not be realized"), ResultBuild: BuildResult);
        var builder = new StoryCatalogBuilder();
        builder.Add(story);
        StoryCatalog catalog = builder.Build();
        var options = new SiteExportOptions { StaticCapture = StaticCaptureMode.None, Incremental = true };
        try
        {
            GallerySiteExporter.Export(() => throw new InvalidOperationException("host must remain lazy"),
                catalog, [story], output, root, options: options);
            string fragment = Path.Combine(output, "stories", "test-incremental.html");
            DateTime firstWrite = File.GetLastWriteTimeUtc(fragment);
            Thread.Sleep(20);

            SiteExportReport second = GallerySiteExporter.Export(
                () => throw new InvalidOperationException("host must remain lazy"),
                catalog, [story], output, root, options: options);

            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(fragment));
            Assert.True(second.Metrics?.FilesReused > 0);
            Assert.Equal(0, second.Images);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Markdown_story_references_detect_cycles_without_native_realization()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-markdown-cycle-" + Guid.NewGuid().ToString("N"));
        StoryInfo a = new("Test/Markdown/A", 0, 0, null, _ => Luxel.Controls.Kit.Text("native A"),
            ResultBuild: static _ => ReferenceMarkdown("A", "Test/Markdown/B"));
        StoryInfo b = new("Test/Markdown/B", 0, 0, null, _ => Luxel.Controls.Kit.Text("native B"),
            ResultBuild: static _ => ReferenceMarkdown("B", "Test/Markdown/A"));
        var builder = new StoryCatalogBuilder();
        builder.Add(a).Add(b);
        StoryCatalog catalog = builder.Build();
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font, catalog);

            SiteExportReport report = GallerySiteExporter.Export(host, [a, b], output, root);

            Assert.True(report.Errors >= 2);
            string fragment = File.ReadAllText(Path.Combine(output, "stories", "test-markdown-a.html"));
            Assert.Contains("Story reference cycle detected", fragment);
            Assert.Contains("story-reference-markdown", fragment);
            Assert.Equal(0, host.StorySelectionCount);
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Button_overview_exports_semantic_html_with_only_its_reference_as_an_iframe()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-button-overview-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryCatalog catalog = GalleryStoryProject.CreateCatalog();
        StoryInfo overview = Assert.IsType<StoryInfo>(catalog.Find("Controls/Button/Overview"));
        Assert.NotNull(overview.ResultBuild);
        StoryInfo basic = Assert.IsType<StoryInfo>(catalog.Find("Controls/Button/Basic"));
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font, catalog);

            SiteExportReport report = GallerySiteExporter.Export(host, [overview, basic], output, root, browserRoot);

            Assert.Equal(2, report.Stories);
            string fragment = File.ReadAllText(Path.Combine(output, "stories", "controls-button-overview.html"));
            Assert.Contains("<h1 id=\"button\">Button</h1>", fragment);
            Assert.Contains("## Implementation", overview.BuildResult(new StoryContext()).Markdown, StringComparison.Ordinal);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(fragment, "<iframe").Cast<System.Text.RegularExpressions.Match>());
            Assert.Contains("story=Controls%2FButton%2FBasic", fragment);
            Assert.Contains("class=\"args-panel\"", fragment);
            Assert.Contains("<table class=\"args-table\">", fragment);
            Assert.Contains("data-arg-control=\"text\"", fragment);
            Assert.Contains("role=\"tablist\"", fragment);
            Assert.Contains("data-runtime-tab=\"args\"", fragment);
            Assert.Contains("data-runtime-tab=\"output\"", fragment);
            Assert.Contains("class=\"output-list\"", fragment);
            Assert.Contains("aria-labelledby=\"controls-button-basic-", fragment);
            Assert.True(fragment.IndexOf("class=\"runtime-frame\"", StringComparison.Ordinal)
                < fragment.IndexOf("class=\"args-panel\"", StringComparison.Ordinal),
                "The embedded preview must appear above its args table.");
            Assert.DoesNotContain("Static capture — not interactive", fragment);
            Assert.Equal(0, host.StorySelectionCount);
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void Duplicate_runtime_references_have_distinct_stable_instance_and_accessible_args_ids()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-duplicate-runtime-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryInfo basic = Assert.IsType<StoryInfo>(CoreUiStoryProject.CreateCatalog().Find("Controls/Button/Basic"));
        var document = new StoryInfo("Test/DuplicateRuntimeRefs", 0, 0, null,
            _ => throw new InvalidOperationException("semantic document must not build a native Widget"),
            ResultBuild: static _ => StoryResult.FromMarkdown(
                "# Duplicate runtime refs\n\n```luxel-story\n0\n```\n\n```luxel-story\n1\n```",
                StoryReference.To("Controls/Button/Basic", new { text = "First" }),
                StoryReference.To("Controls/Button/Basic", new { text = "Second" })));
        var builder = new StoryCatalogBuilder();
        builder.Add(document).Add(basic);
        StoryCatalog catalog = builder.Build();
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font, catalog);

            GallerySiteExporter.Export(host, [document, basic], output, root, browserRoot);
            string first = File.ReadAllText(Path.Combine(output, "stories", "test-duplicateruntimerefs.html"));
            string[] firstIds = System.Text.RegularExpressions.Regex.Matches(first,
                    "data-luxel-runtime-instance=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            Assert.Equal(2, firstIds.Length);
            Assert.All(firstIds, id =>
            {
                Assert.Contains($"aria-labelledby=\"{id}-args-tab\"", first, StringComparison.Ordinal);
                Assert.Contains($"id=\"{id}-args-panel\"", first, StringComparison.Ordinal);
                Assert.Contains($"aria-labelledby=\"{id}-output-tab\"", first, StringComparison.Ordinal);
                Assert.Contains($"id=\"{id}-output-panel\"", first, StringComparison.Ordinal);
            });
            Assert.Contains("args=%7B%22", first, StringComparison.Ordinal);

            GallerySiteExporter.Export(host, [document, basic], output, root, browserRoot);
            string second = File.ReadAllText(Path.Combine(output, "stories", "test-duplicateruntimerefs.html"));
            string[] secondIds = System.Text.RegularExpressions.Regex.Matches(second,
                    "data-luxel-runtime-instance=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            Assert.Equal(firstIds, secondIds);
            Assert.Equal(0, host.StorySelectionCount);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void Client_protocol_is_bidirectional_revisioned_source_checked_and_hash_persistent()
    {
        string script = GallerySiteExporter.ClientScript;

        Assert.Contains("const runtimeProtocolVersion=2", script, StringComparison.Ordinal);
        Assert.Contains("type:'set-args'", script, StringComparison.Ordinal);
        Assert.Contains("message.type==='args-changed'", script, StringComparison.Ordinal);
        Assert.Contains("message.type==='event'", script, StringComparison.Ordinal);
        Assert.Contains("appendRuntimeEvent(section,message.entry)", script, StringComparison.Ordinal);
        Assert.Contains("source.hidden=name!=='source'", script, StringComparison.Ordinal);
        Assert.Contains("initRuntimePanelResize(section)", script, StringComparison.Ordinal);
        Assert.Contains("setRuntimePanelHeight(section", script, StringComparison.Ordinal);
        Assert.Contains("handle.addEventListener('pointerdown'", script, StringComparison.Ordinal);
        Assert.Contains("lostpointercapture", script, StringComparison.Ordinal);
        Assert.Contains("event.shiftKey?48:16", script, StringComparison.Ordinal);
        Assert.Contains("event.key==='ArrowUp'", script, StringComparison.Ordinal);
        Assert.Contains("activateRuntimeTab(section,next.dataset.runtimeTab,true)", script, StringComparison.Ordinal);
        Assert.Contains("candidate.contentWindow===event.source", script, StringComparison.Ordinal);
        Assert.Contains("event.origin!==location.origin", script, StringComparison.Ordinal);
        Assert.Contains("message.protocolVersion!==runtimeProtocolVersion", script, StringComparison.Ordinal);
        Assert.Contains("message.revision<Number(section.dataset.luxelRuntimeRevision", script, StringComparison.Ordinal);
        Assert.Contains("section.dataset.luxelRuntimeRequest!==message.requestId", script, StringComparison.Ordinal);
        Assert.Contains("params.set('args',JSON.stringify(top))", script, StringComparison.Ordinal);
        Assert.Contains("params.set('embeds',JSON.stringify(embeds))", script, StringComparison.Ordinal);
        Assert.Contains("history.replaceState(null,'','#'+params.toString())", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Examples/3D/ClearColor", "examples-3d-clearcolor.html")]
    [InlineData("Examples/3D/Triangle", "examples-3d-triangle.html")]
    [InlineData("Controls/Button/Counter", "controls-button-counter.html")]
    public void Browser_stories_export_as_runtime_without_native_realization_or_static_capture(string storyPath, string fragmentName)
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-runtime-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryInfo descriptor = storyPath.StartsWith("Examples/3D/", StringComparison.Ordinal)
            ? Assert.IsType<StoryInfo>(Catalog.Find(storyPath))
            : Assert.IsType<StoryInfo>(CoreUiStoryProject.CreateCatalog().Find(storyPath));
        StoryInfo story = descriptor with
        {
            Build = _ => throw new InvalidOperationException("runtime story must not be realized"),
            ResultBuild = null,
        };
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            SiteExportReport report = GallerySiteExporter.Export(host, [story], output, root, browserRoot);

            Assert.Equal(1, report.Stories);
            Assert.Equal(0, report.Images);
            Assert.Equal(0, host.StorySelectionCount);
            SiteStory entry = Assert.Single(JsonSerializer.Deserialize<SiteStory[]>(
                File.ReadAllText(Path.Combine(output, "manifest.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!);
            Assert.Equal("runtime", entry.Status);
            Assert.Null(entry.Image);
            Assert.Empty(entry.ImageSha256);

            string fragment = File.ReadAllText(Path.Combine(output, "stories", fragmentName));
            Assert.Contains($"<iframe src=\"samples/webgpu-browser/?story={Uri.EscapeDataString(storyPath)}&amp;args=", fragment);
            Assert.Contains("&amp;instance=", fragment);
            Assert.Contains($"data-luxel-runtime-story=\"{storyPath}\"", fragment);
            Assert.Contains("<article class=\"story runtime-page\">", fragment);
            Assert.Contains("allow=\"webgpu; clipboard-read; clipboard-write\"", fragment);
            Assert.Contains("role=\"tablist\"", fragment);
            Assert.Contains("data-runtime-tab=\"args\"", fragment);
            Assert.Contains("data-runtime-tab=\"output\"", fragment);
            Assert.Contains("class=\"output-list\"", fragment);
            if (storyPath.StartsWith("Examples/3D/", StringComparison.Ordinal))
                Assert.Contains("This story has no configurable args.", fragment);
            if (storyPath == "Controls/Button/Counter")
            {
                Assert.Contains("class=\"args-panel\"", fragment);
                Assert.Contains("data-arg-control=\"count\"", fragment);
                Assert.Single(entry.Args);
                Assert.Equal("count", entry.Args[0].Name);
                Assert.Equal(0, entry.Args[0].DefaultValue.GetInt32());
            }
            Assert.DoesNotContain("Runtime WebAssembly — interactive", fragment);
            Assert.DoesNotContain("Static capture — not interactive", fragment);
            Assert.DoesNotContain("<header>", fragment);
            Assert.DoesNotContain("runtime-caption", fragment);
            Assert.DoesNotContain("src=\"/samples/webgpu-browser/", fragment);
            Assert.Empty(Directory.GetFiles(Path.Combine(output, "images"), "*.png"));
            Assert.True(File.Exists(Path.Combine(output, "samples", "webgpu-browser", "index.html")));
            Assert.True(File.Exists(Path.Combine(output, "samples", "webgpu-browser", "main.js")));
            Assert.True(File.Exists(Path.Combine(output, "samples", "webgpu-browser", "_framework", "dotnet.js")));
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void ClearColor_story_ref_uses_the_browser_runtime_instead_of_a_static_capture()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-clear-color-runtime-embed-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryInfo story = Catalog.Find("Learn/Graphics/ClearColor")
            ?? throw new InvalidOperationException("ClearColor story is missing.");
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            GallerySiteExporter.Export(host, [story], output, root, browserRoot);

            string fragment = File.ReadAllText(Path.Combine(output, "stories", "learn-graphics-clearcolor.html"));
            Assert.Contains("<iframe src=\"samples/webgpu-browser/?story=Examples%2F3D%2FClearColor&amp;args=%7B%7D&amp;instance=", fragment);
            Assert.Contains("data-luxel-runtime-story=\"Examples/3D/ClearColor\"", fragment);
            Assert.Contains("runtime-story-embedded", fragment);
            Assert.Contains("data-runtime-tab=\"source\">Source</button>", fragment);
            Assert.Contains("class=\"source-panel\"", fragment);
            Assert.Contains("public static Widget ClearColor", fragment);
            Assert.Contains("data-runtime-panel-resizer", fragment);
            Assert.Contains("title=\"Interactive ClearColor\"", fragment);
            Assert.False(File.Exists(Path.Combine(output, "images", "examples-3d-clearcolor.png")));
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void First_triangle_story_ref_uses_the_browser_runtime_instead_of_a_triangle_capture()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-runtime-embed-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryInfo story = Catalog.Find("Learn/Graphics/FirstTriangle")
            ?? throw new InvalidOperationException("FirstTriangle story is missing.");
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            GallerySiteExporter.Export(host, [story], output, root, browserRoot);

            string fragment = File.ReadAllText(Path.Combine(output, "stories", "learn-graphics-firsttriangle.html"));
            Assert.Contains("<iframe src=\"samples/webgpu-browser/?story=Examples%2F3D%2FTriangle&amp;args=%7B%7D&amp;instance=", fragment);
            Assert.Contains("data-luxel-runtime-story=\"Examples/3D/Triangle\"", fragment);
            Assert.Contains("runtime-story-embedded", fragment);
            Assert.Contains("Load&lt;Vertex&gt;(vertexId * 32)", fragment);
            Assert.Contains("VSOut vsMain(uint vertexId : SV_VertexID)", fragment);
            Assert.Contains("float4 psMain(VSOut input) : SV_Target", fragment);
            Assert.DoesNotContain("&amp;gt;", fragment);
            Assert.DoesNotContain("&amp;lt;", fragment);
            Assert.DoesNotContain("&amp;quot;", fragment);
            Assert.Contains("title=\"Interactive Triangle\"", fragment);
            Assert.DoesNotContain("runtime-caption", fragment);
            Assert.DoesNotContain("Static embedded story capture", fragment);
            Assert.False(File.Exists(Path.Combine(output, "images", "examples-3d-triangle.png")));
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void RenderGraph_overview_uses_the_browser_runtime_for_the_Blur_story()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-rendergraph-runtime-" + Guid.NewGuid().ToString("N"));
        string browserRoot = CreateBrowserRuntimeRoot();
        StoryInfo story = Catalog.Find("Learn/Graphics/RenderGraph/Overview")
            ?? throw new InvalidOperationException("RenderGraph Overview story is missing.");
        try
        {
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            GallerySiteExporter.Export(host, [story], output, root, browserRoot);

            string fragment = File.ReadAllText(Path.Combine(output, "stories", "learn-graphics-rendergraph-overview.html"));
            Assert.Contains("<iframe src=\"samples/webgpu-browser/?story=Examples%2FRenderGraph%2FBlur&amp;args=%7B%7D&amp;instance=", fragment);
            Assert.Contains("data-luxel-runtime-story=\"Examples/RenderGraph/Blur\"", fragment);
            Assert.Contains("runtime-story-embedded", fragment);
            Assert.Contains("title=\"Interactive Blur\"", fragment);
            Assert.DoesNotContain("Static embedded story capture", fragment);
            Assert.False(File.Exists(Path.Combine(output, "images", "examples-rendergraph-blur.png")));
            GallerySiteExporter.Validate(output);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(browserRoot)) Directory.Delete(browserRoot, true);
        }
    }

    [Fact]
    public void Runtime_triangle_requires_a_complete_copied_browser_application()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string output = Path.Combine(Path.GetTempPath(), "luxel-gallery-runtime-required-" + Guid.NewGuid().ToString("N"));
        string incomplete = Path.Combine(Path.GetTempPath(), "luxel-browser-incomplete-" + Guid.NewGuid().ToString("N"));
        var story = new StoryInfo("Examples/3D/Triangle", 320, 240, null,
            _ => throw new InvalidOperationException("runtime story must not be realized"));
        try
        {
            Directory.CreateDirectory(incomplete);
            File.WriteAllText(Path.Combine(incomplete, "index.html"), "<!doctype html>");
            using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
            using var rasterizer = new Luxel.Graphics.TwoD.Skia.SkiaRasterizer2D();
            using var host = new GalleryHost(rasterizer, font);

            GallerySiteExporter.Export(host, [story], output, root);
            SiteStory fallback = Assert.Single(JsonSerializer.Deserialize<SiteStory[]>(
                File.ReadAllText(Path.Combine(output, "manifest.json")),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!);
            Assert.NotEqual("runtime", fallback.Status);
            Assert.DoesNotContain("data-luxel-runtime-story", File.ReadAllText(Path.Combine(output, fallback.Fragment)));
            int fallbackRealizations = host.StorySelectionCount;
            Assert.True(fallbackRealizations > 0);

            FileNotFoundException error = Assert.Throws<FileNotFoundException>(
                () => GallerySiteExporter.Export(host, [story], output, root, incomplete));
            Assert.Contains("bundle manifest is missing", error.Message);
            Assert.Equal(fallbackRealizations, host.StorySelectionCount);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (Directory.Exists(incomplete)) Directory.Delete(incomplete, true);
        }
    }

    [Fact]
    public void Runtime_story_css_fills_the_available_gallery_viewport()
    {
        string css = GallerySiteExporter.SiteCss;
        Assert.Contains(".runtime-story{display:flex;flex-direction:column;width:100%;height:100%;margin:0}", css);
        Assert.Contains(".runtime-frame,.runtime-frame iframe{display:block;width:100%;height:100%;margin:0;padding:0;border:0", css);
        Assert.Contains(".runtime-page{width:100%;max-width:none;height:100%;margin:0}", css);
        Assert.Contains(".runtime-story-embedded{height:auto", css);
        Assert.Contains(".runtime-story-embedded .runtime-frame{flex:none;height:500px;min-height:500px}", css);
        Assert.Contains("body.runtime-active main{padding:0;overflow:hidden}", css);
        Assert.Contains(".runtime-panels{flex:none", css);
        Assert.Contains(".runtime-tabs{display:flex", css);
        Assert.Contains(".args-panel,.output-panel,.source-panel", css);
        Assert.Contains(".runtime-panel-resizer", css);
        Assert.Contains("height:var(--runtime-panel-height,180px)", css);
        Assert.Contains("cursor:ns-resize", css);
        Assert.Contains(".source-panel pre", css);
        Assert.Contains(".output-list", css);
        Assert.Contains("border-top:1px solid var(--line)", css);
        Assert.Contains(".args-table", css);
        Assert.DoesNotContain("aspect-ratio:4/3", css);
    }

    [Fact]
    public void Native_story_source_pane_uses_read_only_highlighted_editor_or_placeholder()
    {
        const string source = "[Story(\"Test/Source\")]\npublic static Widget Source() => Text(\"hello\");";
        var story = new StoryInfo("Test/Source", 100, 100, null,
            _ => Luxel.Controls.Kit.Text("hello"), Source: source);

        TextEditorView editor = Assert.IsType<TextEditorView>(GalleryStorySourcePane.Build(story));
        Assert.True(editor.ReadOnly);
        Assert.True(editor.Fill);
        Assert.True(editor.ShowLineNumbers);
        Assert.NotNull(editor.EditorFont);
        Assert.Contains(editor.Providers, provider => provider is SyntaxHighlightProvider);
        Assert.Equal(source, editor.Value.Get().Value);

        Text placeholder = Assert.IsType<Text>(GalleryStorySourcePane.Build(
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
        StoryInfo story = Catalog.Find("Learn/Graphics/Shaders")
            ?? Catalog.All.First(s => !s.RealWindowOnly);
        StoryInfo imageStory = Catalog.Find("Controls/Button/Intents")
            ?? Catalog.All.First(s => !s.RealWindowOnly && s.Path != story.Path);
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
            Assert.Contains("safeSet(openKey", script);
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
            Assert.Null(Catalog.Find("Reference/" + path));

        foreach (string ns in TypeApiRegistry.Namespaces)
        {
            StoryInfo story = Catalog.Find("Reference/" + ns)
                ?? throw new InvalidOperationException($"Namespace reference is missing: {ns}");
            ISemanticDocument document = BuildSemanticDocument(story)
                ?? throw new InvalidOperationException($"Namespace reference is not a document: {ns}");
            Assert.Contains($"# {ns}", document.DocumentSource);
            Assert.All(document.DocumentEmbeds, embed => Assert.Equal(DocEmbedKind.TypeApiTable, embed.Kind));
        }

        string[] requiredNamespaces =
        [
            "Luxel.Controls", "Luxel.Framework.DevTools", "Luxel.NodeGraph", "Luxel.Particles",
            "Luxel.Particles.TwoD", "Luxel.Particles.ThreeD", "Luxel.Particles.UI", "Luxel.Physics.Gizmos",
            "Luxel.Player", "Luxel.SceneEdit", "Luxel.Settings", "Luxel.Scripting", "Luxel.Scripting.Framework",
            "Luxel.Strudel", "Luxel.Graphics.TwoD.Skia", "Luxel.UI.App", "Luxel.Workbench",
        ];
        Assert.All(requiredNamespaces, ns => Assert.Contains(ns, TypeApiRegistry.Namespaces));

        Assert.NotNull(Catalog.Find("Examples/UI/Navigation"));
        Assert.NotNull(Catalog.Find("Controls/NavigationView/Basic"));
        Assert.NotNull(Catalog.Find("Controls/NavigationView/Overview"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.Navigation"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.NavigationHost"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.UI.NavigationPath"));

        string[] existingCategories = Catalog.All
            .Where(story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                            && !story.Path.EndsWith("/Overview", StringComparison.Ordinal))
            .Select(story => story.Path.Split('/')[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] overviewCategories = Catalog.All
            .Where(story => story.Path.StartsWith("Controls/", StringComparison.Ordinal)
                            && story.Path.EndsWith("/Overview", StringComparison.Ordinal))
            .Select(story => story.Path.Split('/')[1])
            // Terminal is an integration guide at the requested Controls route, not a Luxel.Controls catalog entry.
            .Where(category => category != "Terminal")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(existingCategories, overviewCategories);

        var mapped = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Button"] = "Button", ["CheckBox"] = "Check", ["KnobsTable"] = "KnobsTable",
            ["RichText"] = "RichTextView", ["ScrollViewer"] = "Scroll", ["WrapPanel"] = "Wrap",
        };
        foreach ((string category, string apiName) in mapped)
        {
            StoryInfo story = Catalog.Find($"Controls/{category}/Overview")
                ?? throw new InvalidOperationException($"Control overview is missing: {category}");
            StoryResult result = story.BuildResult(new StoryContext());
            if (result.Kind == StoryResultKind.Markdown)
            {
                Assert.Contains($"# {apiName}", result.Markdown, StringComparison.Ordinal);
                if (result.Embeds.Count > 0)
                    Assert.Contains(result.Embeds, embed => embed.Kind == nameof(DocEmbedKind.ControlApiTable)
                        && embed.Reference == apiName);
                else
                {
                    Assert.Contains("Events, parameters and API", result.Markdown, StringComparison.Ordinal);
                    Assert.Contains(result.References, reference => reference.Path == $"Controls/{category}/Basic");
                }
            }
            else
            {
                ISemanticDocument document = DocsIndex.FindSemanticDocument(result.Widget)
                    ?? throw new InvalidOperationException($"Control overview is not a document: {category}");
                Assert.Contains(document.DocumentEmbeds,
                    embed => embed.Kind == DocEmbedKind.ControlApiTable && embed.Reference == apiName);
            }
        }
        foreach (string special in new[] { "Layout", "Kit", "CommandPalette" })
            Assert.NotNull(Catalog.Find($"Controls/{special}/Overview"));

    }

    [SkippableFact]
    public void Generated_overview_iframes_and_api_tables_export_as_semantic_html()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo control = Catalog.Find("Controls/Button/Overview")
            ?? throw new InvalidOperationException("Controls/Button/Overview is missing.");
        StoryInfo type = Catalog.Find("Reference/Luxel.UI")
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
            Assert.Contains("Events, parameters and API", controlHtml);
            Assert.Contains("Controls/Button/Basic", controlHtml);
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
        Assert.Null(Catalog.Find("Demos/2D/CameraRig"));
        Assert.NotNull(Catalog.Find("Examples/2D/CameraRig"));
        Assert.Empty(Catalog.AliasesFor("Examples/2D/CameraRig"));
    }

    [Fact]
    public void TwoD_lessons_embed_the_expected_runtime_samples()
    {
        string[] livePaths =
        [
            "Examples/2D/SceneRender", "Examples/2D/Shapes", "Examples/2D/VectorPaths",
            "Examples/2D/CameraRig", "Examples/2D/Sprites",
            "Examples/2D/Rasterizer/InputPathsLive", "Examples/2D/Rasterizer/EncodedSceneLive",
            "Examples/2D/Rasterizer/BoundsLive", "Examples/2D/Rasterizer/TileBinsLive",
            "Examples/2D/Rasterizer/CoverageLive", "Examples/2D/Rasterizer/StrokeLive",
            "Examples/2D/Rasterizer/CompositeLive", "Examples/2D/Rasterizer/DispatchLive",
            "Examples/2D/Rasterizer/RetainedUpdatesLive",
        ];
        StoryCatalog browserCatalog = CoreUiStoryProject.CreateCatalog();
        foreach (string path in livePaths)
        {
            StoryInfo story = browserCatalog.Find(path)
                ?? throw new InvalidOperationException($"Browser 2D story is missing: {path}");
            Assert.Equal(CoreUiStoryProject.RuntimeBundleId, story.RuntimeBundleId);
            using var context = new StoryContext();
            Assert.Equal(StoryResultKind.Widget, story.BuildResult(context).Kind);
        }

        StoryInfo[] lessons = Catalog.All
            .Where(story => story.Path.StartsWith("Learn/Graphics/2D/", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(18, lessons.Length);
        Assert.Null(Catalog.Find("Learn/Graphics/2D/RetainedCanvas"));
        Assert.Null(browserCatalog.Find("Examples/2D/RetainedCanvasLive"));
        Assert.NotNull(Catalog.Find("Examples/2D/Backends"));
        foreach (StoryInfo lesson in lessons)
        {
            using var context = new StoryContext();
            StoryResult result = lesson.BuildResult(context);
            Assert.Equal(StoryResultKind.Markdown, result.Kind);
            Assert.DoesNotContain("## WASM", result.Markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("## 実装を読む", result.Markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("## 実装の正", result.Markdown, StringComparison.Ordinal);
            Assert.True(lesson.Toc);
            Assert.Contains("<!-- luxel-toc -->", StoryMarkdownRenderer.EffectiveMarkdown(lesson, result.Markdown));
            Assert.All(result.References, reference =>
            {
                if (reference.Path == "Examples/2D/Backends")
                {
                    Assert.NotNull(Catalog.Find(reference.Path));
                    Assert.Null(browserCatalog.Find(reference.Path));
                    return;
                }
                Assert.Contains(reference.Path, livePaths);
                Assert.Equal(CoreUiStoryProject.RuntimeBundleId, browserCatalog.Find(reference.Path)?.RuntimeBundleId);
            });
        }
    }

    [Fact]
    public void TwoD_live_samples_use_the_APIs_taught_by_the_lessons()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root,
            "src", "Luxel.Gallery.Stories.CoreUi", "Stories", "TwoDBrowserStories.cs"));

        Assert.Contains("scene.ImageRect(", source, StringComparison.Ordinal);
        Assert.Contains("scene.ImageSubRect(", source, StringComparison.Ordinal);
        Assert.Contains("scene.DrawSprite(", source, StringComparison.Ordinal);
        Assert.Contains("encoded.Render(camera", source, StringComparison.Ordinal);
        Assert.Contains("new RetainedCanvas()", source, StringComparison.Ordinal);
        Assert.Contains("canvas.LastTransformWrites", source, StringComparison.Ordinal);
        Assert.Contains("surface.StridePixels", source, StringComparison.Ordinal);
        Assert.Contains("world (80, 10) → screen center", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector2 Transform(", source, StringComparison.Ordinal);

        string backendSource = File.ReadAllText(Path.Combine(root,
            "src", "Luxel.Gallery.Stories", "Stories", "TwoDBackendStories.cs"));
        Assert.Contains("new GpuDeviceRasterizer2D", backendSource, StringComparison.Ordinal);
        Assert.Contains("new SkiaRasterizer2D", backendSource, StringComparison.Ordinal);
        Assert.Contains("GpuView(", backendSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Audio_learn_chain_is_complete_ordered_and_linked()
    {
        string[] orderedRoutes = Catalog.All
            .Where(story => story.Path.StartsWith("Learn/Audio/", StringComparison.Ordinal))
            .Select(story => story.Path)
            .ToArray();
        Assert.Equal(AudioCourseCatalog.Routes, orderedRoutes);

        StoryInfo[] stories = AudioCourseCatalog.Routes.Select(path => Catalog.Find(path)
            ?? throw new InvalidOperationException($"Audio Learn route is missing: {path}")).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null, Catalog);
        Assert.Empty(DocsIndex.ValidateLinks(pages, Catalog));

        for (int i = 0; i < stories.Length; i++)
        {
            Assert.True(stories[i].Toc);
            string source = pages[stories[i].Path].Text;
            Assert.Contains("**難易度:**", source);
            Assert.Contains("**実行環境:**", source);
            Assert.Contains("**Backend:**", source);
            Assert.Contains("**前提知識:**", source);
            Assert.Contains("## ", source);
            if (i > 0) Assert.Contains("story:" + stories[i - 1].Path, source);
            if (i + 1 < stories.Length) Assert.Contains("story:" + stories[i + 1].Path, source);
        }
    }

    [Fact]
    public void Audio_learn_pages_have_concept_sized_examples()
    {
        StoryInfo[] stories = AudioCourseCatalog.Routes.Select(path => Catalog.Find(path)!).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null, Catalog);

        foreach (string path in AudioCourseCatalog.Routes.Skip(1))
        {
            string source = pages[path].Text;
            Assert.True(source.Contains("```", StringComparison.Ordinal)
                || source.Contains("SampleSource", StringComparison.Ordinal)
                || source.Contains("docs:begin", StringComparison.Ordinal),
                $"Audio lesson lacks a concept-sized code example: {path}");
        }

        string examples = string.Join('\n', new[]
        {
            BuildSemanticDocument(Catalog.Find("Examples/Audio/Buses")!)!.DocumentSource!,
            BuildSemanticDocument(Catalog.Find("Examples/Audio/SpatialAttenuation")!)!.DocumentSource!,
            BuildSemanticDocument(Catalog.Find("Examples/Audio/StreamingQueue")!)!.DocumentSource!,
        });
        foreach (string api in new[] { "AudioBus", "EffectiveVolume", "AudioSource3D", "EffectivePan", "WavStream", "StreamingVoice", "Pump()" })
            Assert.Contains(api, examples, StringComparison.Ordinal);
    }

    [Fact]
    public void Audio_docs_cover_backend_lifecycle_and_testing_contracts()
    {
        StoryInfo[] stories = AudioCourseCatalog.Routes.Select(path => Catalog.Find(path)!).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null, Catalog);
        string course = string.Join('\n', AudioCourseCatalog.Routes.Select(path => pages[path].Text));

        foreach (string contract in new[]
                 {
                     "NullAudioBackend", "XAudio2", "Initialize()", "Tick()", "Pump()",
                     "BuffersQueued", "Dispose()", "HRTF", "Doppler", "occlusion", "reverb",
                 })
            Assert.Contains(contract, course, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("現在このrepositoryにWeb Audio backendは**実装されていません**", course, StringComparison.Ordinal);
        Assert.Contains("AudioContext.resume()", course, StringComparison.Ordinal);
        Assert.Contains("user gesture", course, StringComparison.Ordinal);
        Assert.Contains("SharedArrayBuffer", course, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_learn_chain_is_complete_and_has_inline_examples()
    {
        string[] routes =
        [
            "Learn/Graphics/Overview", "Learn/Graphics/Environment",
            "Learn/Graphics/ClearColor", "Learn/Graphics/FirstTriangle",
            "Learn/Graphics/Buffers", "Learn/Graphics/Textures", "Learn/Graphics/Shaders",
            "Learn/Graphics/PipelineState", "Learn/Graphics/Synchronization",
        ];

        string[] orderedGraphicsRoutes = Catalog.All
            .Where(story => story.Path.StartsWith("Learn/Graphics/", StringComparison.Ordinal))
            .Select(story => story.Path)
            .ToArray();
        Assert.Equal(RenderingCourseCatalog.Routes, orderedGraphicsRoutes);
        Assert.Equal("Learn/Graphics/2D/Overview", orderedGraphicsRoutes[9]);
        Assert.Equal("Learn/Graphics/2D/Internal/Overview", orderedGraphicsRoutes[16]);

        string[] orderedRenderGraphRoutes = Catalog.All
            .Where(story => story.Path.StartsWith("Learn/Graphics/RenderGraph/", StringComparison.Ordinal))
            .Select(story => story.Path)
            .ToArray();
        Assert.Equal(RenderingCourseCatalog.Routes[^6..], orderedRenderGraphRoutes);
        Assert.Equal("Learn/Graphics/2D/Internal/Validation", orderedGraphicsRoutes[^7]);
        Assert.Equal("Learn/Graphics/RenderGraph/Overview", orderedGraphicsRoutes[^6]);
        Assert.Equal("Learn/Graphics/RenderGraph/Debugging", orderedGraphicsRoutes[^1]);

        string validationPage = BuildSemanticDocument(Catalog.Find("Learn/Graphics/2D/Internal/Validation")!)!.DocumentSource!;
        string renderGraphOverviewPage = BuildSemanticDocument(Catalog.Find("Learn/Graphics/RenderGraph/Overview")!)!.DocumentSource!;
        Assert.Contains("story:Learn/Graphics/RenderGraph/Overview", validationPage);
        Assert.Contains("story:Learn/Graphics/2D/Internal/Validation", renderGraphOverviewPage);

        for (int i = 0; i < routes.Length; i++)
        {
            string path = routes[i];
            StoryInfo story = Catalog.Find(path)
                ?? throw new InvalidOperationException($"Rendering Learn route is missing: {path}");
            ISemanticDocument document = BuildSemanticDocument(story)
                ?? throw new InvalidOperationException($"Rendering Learn route is not a document: {path}");
            Assert.Contains("**難易度:**", document.DocumentSource);
            if (i > 0)
                Assert.Contains("```", document.DocumentSource); // The page remains understandable without opening sample files.
        }
    }

    [Fact]
    public void RenderGraph_learn_chain_is_ordered_and_linked()
    {
        string[] routes = RenderingCourseCatalog.Routes[^6..];
        StoryInfo[] stories = routes.Select(path => Catalog.Find(path)
            ?? throw new InvalidOperationException($"RenderGraph Learn route is missing: {path}")).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null, Catalog);

        Assert.Empty(DocsIndex.ValidateLinks(pages, Catalog));
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
    }

    [Fact]
    public void Rendering_docs_links_metadata_search_and_sample_sources_are_verified()
    {
        string[] routes =
        [
            "Learn/Graphics/Overview", "Learn/Graphics/Environment",
            "Learn/Graphics/ClearColor", "Learn/Graphics/FirstTriangle",
            "Learn/Graphics/Buffers", "Learn/Graphics/Textures", "Learn/Graphics/Shaders",
            "Learn/Graphics/PipelineState", "Learn/Graphics/Synchronization",
        ];
        StoryInfo[] stories = routes.Select(name => Catalog.Find(name)
            ?? throw new InvalidOperationException($"Rendering Learn route is missing: {name}")).ToArray();
        Dictionary<string, DocsPage> pages = DocsIndex.Build(stories, resources: null, Catalog);

        Assert.Empty(DocsIndex.ValidateLinks(pages, Catalog));
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
        string environment = pages["Learn/Graphics/Environment"].Text;
        Assert.Contains("# グラフィック環境", environment);
        Assert.Contains("## Backend", environment);
        foreach (string backend in new[] { "## Vulkan", "## Direct3D 12", "## WebGPU (native)", "## WebGPU (browser)" })
            Assert.Contains(backend, environment);
        Assert.Contains("`Luxel.Platform`", environment);
        Assert.Contains("Frameworkアプリ", environment);
        foreach (string surfaceApi in new[] { "CreateWin32Surface", "VulkanPresentationSource", "CreateSurface", "CreateXlibSurface", "CreateCanvasSurface" })
            Assert.Contains(surfaceApi, environment);
        Assert.DoesNotContain("using Luxel.Platform", environment);
        Assert.DoesNotContain("device.CreateSurface", environment);
        Assert.DoesNotContain("device.CreateCanvasSurface", environment);
        Assert.DoesNotContain("IVulkanWindowSurface", environment);
        Assert.DoesNotContain("## ビルド", environment);
        Assert.DoesNotContain("## Shader cache", environment);

        string clearColor = pages["Learn/Graphics/ClearColor"].Text;
        Assert.Contains("# ClearColor", clearColor);
        foreach (string stage in new[]
                 {
                     "## 描画先とframebufferを作成する",
                     "## コマンドバッファを作成する",
                     "## コマンドを作成する",
                     "## Submitする",
                     "## SurfaceへPresentする",
                 })
            Assert.Contains(stage, clearColor);
        foreach (string api in new[]
                 {
                     "CreateRenderTarget", "StartCommandRecording", "BeginRendering",
                     "CopyTextureToBuffer", "Finish", "SubmitAndWait", "surface.Present",
                 })
            Assert.Contains(api, clearColor);
        Assert.Contains("## Framebufferのバッファリング", clearColor);
        Assert.Contains("single framebuffer + `SubmitAndWait`", clearColor);
        Assert.Contains("story:Learn/Graphics/Synchronization", clearColor);
        Assert.Contains("story:Internals/Gpu/Synchronization", clearColor);
        Assert.DoesNotContain("## 実sampleのframe loop", clearColor);
        Assert.DoesNotContain("SampleSource(\"samples/LuxelTriangle/Program.cs\", \"standalone-frame-loop\")", clearColor);

        Assert.Contains("story:Learn/Graphics/2D/Overview", pages[stories[^1].Path].Text);

        string overview = pages[stories[0].Path].Text.ToLowerInvariant();
        foreach (string term in new[] { "triangle", "texture", "shader", "pipeline", "barrier", "submit", "render graph" })
            Assert.Contains(term, overview);

        string trianglePage = pages["Learn/Graphics/FirstTriangle"].Text;
        Assert.Contains("# 三角形表示", trianglePage);
        string[] triangleStages =
        [
            "## 1. 頂点バッファの作成",
            "## 2. 頂点データの作成と転送",
            "## 3. シェーダーの作成",
            "## 4. パイプラインの作成",
            "## 5. コマンドの設定",
        ];
        int previousTriangleStage = -1;
        foreach (string stage in triangleStages)
        {
            int position = trianglePage.IndexOf(stage, previousTriangleStage + 1, StringComparison.Ordinal);
            Assert.True(position > previousTriangleStage, $"Missing or out-of-order FirstTriangle stage: {stage}");
            previousTriangleStage = position;
        }
        foreach (string api in new[]
                 {
                     "device.Malloc", "vertexBuffer.Span<float>", "GpuShaderCode.Load",
                     "CreateGraphicsPipeline", "StartCommandRecording", "SetRootArguments", "Draw(vertexCount)",
                 })
            Assert.Contains(api, trianglePage);
        Assert.DoesNotContain("ResourceHandle", trianglePage);
        Assert.DoesNotContain("resources.Create", trianglePage);
        Assert.DoesNotContain("ScopedResources", trianglePage);

        string buffersPage = pages["Learn/Graphics/Buffers"].Text;
        Assert.Contains("# Buffers", buffersPage);
        Assert.Contains("四角形sample", buffersPage);
        foreach (string buffer in new[] { "vertexBuffer", "indexBuffer", "colorBuffer" })
            Assert.Contains(buffer, buffersPage);
        foreach (string operation in new[]
                 {
                     "indices.CopyTo", "IndexBufferIndex", "ColorBufferIndex",
                     "Load<uint>(vertexId * 4)", "Load2(index * 8)", "Load4(index * 16)", "Draw(6)",
                 })
            Assert.Contains(operation, buffersPage);

        Assert.Null(Catalog.Find("Learn/Graphics/BuffersAndBindings"));
        string texturesPage = pages["Learn/Graphics/Textures"].Text;
        Assert.Contains("# Textures", texturesPage);
        foreach (string api in new[]
                 {
                     "CreateCheckerboard", "device.CreateTexture", "using GpuTexture",
                     "texture.BindlessIndex", "device.CreateSampler", "using GpuSampler", "TextureIndex", "SamplerIndex",
                     "Texture2D g_textures[]", "SamplerState g_samplers[]", ".Sample(",
                     "## Format一覧", "R8Unorm", "Rg8Unorm", "Rgba8UnormSrgb", "Bgra8UnormSrgb",
                     "Rgb8Unorm", "width * bytesPerPixel",
                     "GpuSamplerFilter.Point", "GpuSamplerFilter.Linear",
                     "GpuSamplerAddress.Clamp", "GpuSamplerAddress.Repeat", "WrapPanel",
                     "## Pixel、色空間、alpha", "## Upload rowとbackend差",
                 })
            Assert.Contains(api, texturesPage);
        Assert.DoesNotContain("## Texture付きquadで確認する", texturesPage);
        Assert.DoesNotContain("## UV原点とindexed quad", texturesPage);
        Assert.DoesNotContain("--stage texture", texturesPage);
        foreach (string resourceSystemTerm in new[]
                 {
                     "ResourceSystem", "ResourceScope", "ResourceHandle", "ResourceState",
                     "resources.", "ctx.Observe", ".Value.BindlessIndex",
                 })
            Assert.DoesNotContain(resourceSystemTerm, texturesPage);

        string shadersPage = pages["Learn/Graphics/Shaders"].Text;
        Assert.Contains("# Shaders", shadersPage);
        string[] shaderTopics =
        [
            "## Slangとは",
            "## シェーダーの種類と作り方",
            "## メイン関数の入出力",
            "## bindingとroot argument",
            "## オンラインコンパイル",
            "## オフラインコンパイルとキャッシュ",
            "## Publishする際の注意",
        ];
        int previousShaderTopic = -1;
        foreach (string topic in shaderTopics)
        {
            int position = shadersPage.IndexOf(topic, previousShaderTopic + 1, StringComparison.Ordinal);
            Assert.True(position > previousShaderTopic, $"Missing or out-of-order Shaders topic: {topic}");
            previousShaderTopic = position;
        }
        foreach (string shaderTerm in new[]
                 {
                     "https://shader-slang.org/", "[shader(\"vertex\")]", "[shader(\"pixel\")]",
                     "[shader(\"compute\")]", "SV_VertexID", "SV_Position", "SV_Target",
                     "SV_DispatchThreadID", "vk::binding", "vk::push_constant", "BindlessIndex",
                     "SetRootArguments", "Create<SlangSource, GpuShaderCode>", "CompileLuxelShaderCache",
                     "inputs.sha256", "GpuShaderCode.Load", "Luxel.Shaders.targets",
                     "AppContext.BaseDirectory", "dotnet publish", "luxel-empty-cwd",
                 })
            Assert.Contains(shaderTerm, shadersPage);

        string pipelinePage = pages["Learn/Graphics/PipelineState"].Text;
        Assert.Contains("# Pipelineのその他の設定", pipelinePage);
        string[] pipelineTopics =
        [
            "## Rasterizer State",
            "## Depth-Stencil State",
            "## Blend State",
            "## Viewport / Scissor",
            "## Pipelineを分ける判断",
            "## よくある症状",
        ];
        int previousPipelineTopic = -1;
        foreach (string topic in pipelineTopics)
        {
            int position = pipelinePage.IndexOf(topic, previousPipelineTopic + 1, StringComparison.Ordinal);
            Assert.True(position > previousPipelineTopic, $"Missing or out-of-order Pipeline State topic: {topic}");
            previousPipelineTopic = position;
        }
        foreach (string pipelineTerm in new[]
                 {
                     "GpuRasterDesc.Default", "GpuPrimitiveTopology.TriangleList", "GpuCullMode.Back",
                     "GpuFrontFace.CounterClockwise", "DepthTest", "DepthWrite", "GpuFormat.D32Float",
                     "GpuBlendMode.AlphaBlend", "CreateDepthTarget", "LessOrEqual",
                     "SetViewport", "SetScissor", "render target全体",
                 })
            Assert.Contains(pipelineTerm, pipelinePage);
        ISemanticDocument pipelineDocument = BuildSemanticDocument(Catalog.Find("Learn/Graphics/PipelineState")!)!;
        Assert.Contains(pipelineDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/3D/Depth");
        Assert.Contains(pipelineDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/3D/Blend");

        string synchronizationPage = pages["Learn/Graphics/Synchronization"].Text;
        Assert.Contains("# 同期", synchronizationPage);
        foreach (string synchronizationTopic in new[]
                 {
                     "## Barrierは何を同期するか", "## GpuStage一覧", "## よく使うBarrier",
                     "## Barrierでは解決しないこと", "## FinishとSubmit",
                     "## SubmitAndWait", "## SubmitAsync", "## WaitIdleとWaitIdleAsync",
                     "## RenderGraphとの関係",
                 })
            Assert.Contains(synchronizationTopic, synchronizationPage);
        foreach (string synchronizationTerm in new[]
                 {
                     "GpuStage.None", "GpuStage.DrawIndirect", "GpuStage.VertexShader",
                     "GpuStage.PixelShader", "GpuStage.ComputeShader", "GpuStage.ColorOutput",
                     "GpuStage.DepthStencil", "GpuStage.Copy", "GpuStage.AllGraphics", "GpuStage.All",
                     "GpuHazard.IndirectArguments", "SubmitAndWait", "SubmitAsync", "WaitIdle", "WaitIdleAsync",
                     "story:Internals/Gpu/Synchronization", "story:Learn/Graphics/RenderGraph/Overview",
                 })
            Assert.Contains(synchronizationTerm, synchronizationPage);
        Assert.DoesNotContain("## Frame slotとFence", synchronizationPage);
        Assert.DoesNotContain("fence.CompletedValue", synchronizationPage);

        StoryInfo synchronizationInternals = Catalog.Find("Internals/Gpu/Synchronization")!;
        string synchronizationInternalsPage = BuildSemanticDocument(synchronizationInternals)!.DocumentSource!;
        Assert.Contains("# GPU同期の内部実装", synchronizationInternalsPage);
        foreach (string backendTopic in new[] { "## Vulkan", "## DirectX 12", "## Native WebGPU", "## Browser WebGPU" })
            Assert.Contains(backendTopic, synchronizationInternalsPage);
        foreach (string implementationTerm in new[]
                 {
                     "vkCmdPipelineBarrier2", "vkQueueWaitIdle", "VkFence",
                     "ID3D12Fence", "CompletedValue", "DevicePoll", "submission serial",
                 })
            Assert.Contains(implementationTerm, synchronizationInternalsPage);

        Assert.Null(Catalog.Find("Learn/Graphics/RenderGraph"));
        foreach (string oldRoute in new[]
                 {
                     "Learn/RenderGraph/Overview", "Learn/RenderGraph/Resources", "Learn/RenderGraph/Passes",
                     "Learn/RenderGraph/Compilation", "Learn/RenderGraph/Lifecycle", "Learn/RenderGraph/Debugging",
                 })
            Assert.Null(Catalog.Find(oldRoute));

        foreach (string removedRoute in new[]
                 {
                     "Learn/Graphics/FrameLoopAndSynchronization", "Learn/Graphics/Fence",
                     "Learn/Graphics/ThreeD/FirstRenderGraph",
                     "Learn/Graphics/ThreeD/Textures", "Learn/Graphics/ThreeD/TransformsAndCamera",
                     "Learn/Graphics/ThreeD/DepthCullingLighting", "Learn/Graphics/ThreeD/StaticGltf",
                     "Learn/Graphics/ThreeD/Debugging", "Learn/Graphics/ThreeD/Shipping",
                 })
            Assert.Null(Catalog.Find(removedRoute));

    }

    [Fact]
    public void Story_source_limitations_are_preserved()
    {
        StoryInfo authoring = Catalog.Find("Internals/Authoring")
            ?? throw new InvalidOperationException("Internals/Authoring is missing.");
        Assert.Contains("完全な `[Story]` method宣言", authoring.Source);
        Assert.Contains("下部の **Source** タブ", authoring.Source);
        Assert.Contains("SampleSource(path, region)", authoring.Source);
    }

    [Fact]
    public void Terminal_api_references_are_generated_under_reference()
    {
        string[] namespaces =
        [
            "Luxel.Terminal.Input",
            "Luxel.Terminal.Parsing",
            "Luxel.Terminal.Screen",
            "Luxel.Terminal.Session",
            "Luxel.Terminal.UI",
            "Luxel.Terminal.Windows",
            "Luxel.Terminal.Linux",
        ];
        foreach (string ns in namespaces)
        {
            Assert.Contains(ns, TypeApiRegistry.Namespaces);
            StoryInfo story = Catalog.Find("Reference/" + ns)
                ?? throw new InvalidOperationException($"Terminal API reference is missing: {ns}");
            ISemanticDocument document = BuildSemanticDocument(story)
                ?? throw new InvalidOperationException($"Terminal API reference is not a document: {ns}");
            Assert.Contains($"# {ns}", document.DocumentSource);
            Assert.NotEmpty(document.DocumentEmbeds);
            Assert.All(document.DocumentEmbeds, embed => Assert.Equal(DocEmbedKind.TypeApiTable, embed.Kind));
        }

        Assert.NotNull(TypeApiRegistry.Find("Luxel.Terminal.Session.TerminalSession"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Terminal.UI.TerminalView"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Terminal.Windows.WindowsConPty"));
        Assert.NotNull(TypeApiRegistry.Find("Luxel.Terminal.Linux.LinuxPty"));
    }

    [Fact]
    public void Terminal_docs_cover_platforms_usage_and_rendering_adjustments()
    {
        StoryInfo story = Catalog.Find("Controls/Terminal/Overview")
            ?? throw new InvalidOperationException("Controls/Terminal/Overview is missing.");
        Dictionary<string, DocsPage> pages = DocsIndex.Build([story], resources: null);
        DocsPage page = pages[story.Path];

        Assert.Contains("WindowsConPty", page.Text);
        Assert.Contains("LinuxPty", page.Text);
        Assert.Contains("TerminalSession", page.Text);
        Assert.Contains("TerminalView", page.Text);
        Assert.Contains("GlyphAdvanceScale", page.Text);
        Assert.Contains("delayed wrap", page.Text);
        Assert.Contains("samples/LuxelTerminal", page.Text);
        Assert.Empty(DocsIndex.ValidateLinks(pages));
    }

    [Fact]
    public void Start_courses_and_sample_bundles_are_registered_and_link_clean()
    {
        Assert.Equal("Start", Catalog.All[0].Component);
        Assert.NotNull(Catalog.Find("Start/Welcome"));
        Assert.NotNull(Catalog.Find("Learn/Graphics/Overview"));
        Assert.NotNull(Catalog.Find("Learn/Graphics/2D/Overview"));
        Assert.NotNull(Catalog.Find("Learn/Graphics/2D/Internal/Overview"));
        Assert.Null(Catalog.Find("Learn/Graphics/TwoD/Overview"));
        Assert.Null(Catalog.Find("Learn/Graphics/RasterizerInternals/Overview"));
        Assert.Null(Catalog.Find("Learn/Rendering/Basics/Overview"));
        Assert.Null(Catalog.Find("Learn/Graphics/Basics/Overview"));
        foreach (string route in new[] { "Learn/Input/Overview", "Learn/Input/SourcesAndBus", "Learn/Input/ActionsAndContexts",
                     "Learn/Input/BindingsAndRebinding", "Learn/Input/PlatformsAndTesting", "Examples/Input/SourcesAndBus",
                     "Examples/Input/Actions", "Examples/Input/ContextStack", "Examples/Input/Bindings",
                     "Learn/Audio/Overview", "Learn/Audio/ClipsSourcesAndBuses", "Learn/Audio/SpatialStreamingAndTesting",
                     "Learn/Resources/Overview", "Learn/Resources/PipelinesAndDag", "Learn/Resources/ReloadAndLifetime" })
            Assert.NotNull(Catalog.Find(route));
        string[] diagnostics = ["InputPaths", "EncodedScene", "Bounds", "TileBins", "Coverage", "Stroke", "Composite", "Dispatch", "RetainedUpdates"];
        foreach (string diagnostic in diagnostics)
            Assert.NotNull(Catalog.Find("Examples/2D/Rasterizer/" + diagnostic));
        Assert.DoesNotContain(Catalog.All, story => story.Path.StartsWith("Build/", StringComparison.Ordinal));
        Assert.DoesNotContain(Catalog.All, story => story.Path.StartsWith("Reference/Guides/", StringComparison.Ordinal));
        Assert.Contains(Catalog.All, story => story.Path.StartsWith("Examples/", StringComparison.Ordinal));
        Assert.DoesNotContain(Catalog.All, story => story.Path.StartsWith("Demos/", StringComparison.Ordinal));
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(Catalog.All, resources: null, Catalog), Catalog));
    }

    [Fact]
    public void Input_learn_pages_embed_running_stories_and_concept_sized_source_fragments()
    {
        ISemanticDocument overviewDocument = BuildSemanticDocument(Catalog.Find("Learn/Input/Overview")!)!;
        Assert.DoesNotContain("コピーして動かす", overviewDocument.DocumentSource!);

        ISemanticDocument platformsDocument = BuildSemanticDocument(Catalog.Find("Learn/Input/PlatformsAndTesting")!)!;
        Assert.DoesNotContain("story:Learn/Audio/Overview", platformsDocument.DocumentSource!);

        ISemanticDocument sourcesDocument = BuildSemanticDocument(Catalog.Find("Learn/Input/SourcesAndBus")!)!;
        string sources = sourcesDocument.DocumentSource!;
        Assert.Contains(sourcesDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/Input/SourcesAndBus");
        string[] sourceStages =
        [
            "## IInputSourceの役割",
            "## InputEventの共通形式",
            "## 1 tickの収集順序",
            "## 押下と解放を別tickにする",
        ];
        int previousSourceStage = -1;
        foreach (string stage in sourceStages)
        {
            int position = sources.IndexOf(stage, previousSourceStage + 1, StringComparison.Ordinal);
            Assert.True(position > previousSourceStage, $"Missing or out-of-order Input source stage: {stage}");
            previousSourceStage = position;
        }
        foreach (string token in new[]
                 {
                     "IInputSource[] sources", "new InputBus", "bus.Clear()", "source.Poll(bus)",
                     "bus.Events", "InputEventKind.AxisChanged", "InputEventKind.PointerMoved",
                 })
            Assert.Contains(token, sources);
        Assert.Contains("sourceごとに`bus.Clear()`してはいけません", sources);
        Assert.Contains("`InputStack.Update`はイベントを保持状態へ反映した後、busをクリアします", sources);
        foreach (string uiScaffolding in new[] { "Frame(VStack", "Heading(", "Button(_ =>" })
            Assert.DoesNotContain(uiScaffolding, sources);

        ISemanticDocument actionsDocument = BuildSemanticDocument(Catalog.Find("Learn/Input/ActionsAndContexts")!)!;
        string actions = actionsDocument.DocumentSource!;
        Assert.Contains(actionsDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/Input/Actions");
        Assert.Contains(actionsDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/Input/ContextStack");

        string[] actionStages =
        [
            "## アクションを構成する",
            "## 押下と解放のエッジを処理する",
            "## コンテキストの優先順位と入力の消費",
            "### スタックを構成する",
            "### 上位コンテキストで入力を消費する",
            "### コンテキストを一時停止する",
        ];
        int previousActionStage = -1;
        foreach (string stage in actionStages)
        {
            int position = actions.IndexOf(stage, previousActionStage + 1, StringComparison.Ordinal);
            Assert.True(position > previousActionStage, $"Missing or out-of-order Input action stage: {stage}");
            previousActionStage = position;
        }
        foreach (string token in new[]
                 {
                     "new FakeInputSource", "stack.Push(gameplay)", "jump.Triggered", "jump.Released",
                     "stack.Push(menu)", "source.PressKey(KeyCode.Enter)", "stack.SetSuspended",
                 })
            Assert.Contains(token, actions);
        foreach (string uiScaffolding in new[] { "Frame(VStack", "Heading(", "KeyButton(" })
            Assert.DoesNotContain(uiScaffolding, actions);

        ISemanticDocument bindingsDocument = BuildSemanticDocument(Catalog.Find("Learn/Input/BindingsAndRebinding")!)!;
        string bindings = bindingsDocument.DocumentSource!;
        Assert.Contains(bindingsDocument.DocumentEmbeds,
            embed => embed.Kind == DocEmbedKind.StoryRef && embed.Reference == "Examples/Input/Bindings");

        string[] bindingStages =
        [
            "## アクションとテスト用入力を用意する",
            "## JSONとして保存する",
            "## JSONを読み込み、コンテキストへ反映する",
            "## 反映したバインディングを確認する",
            "## 再設定UIの責務",
        ];
        int previousBindingStage = -1;
        foreach (string stage in bindingStages)
        {
            int position = bindings.IndexOf(stage, previousBindingStage + 1, StringComparison.Ordinal);
            Assert.True(position > previousBindingStage, $"Missing or out-of-order Input binding stage: {stage}");
            previousBindingStage = position;
        }
        foreach (string token in new[]
                 {
                     "new ButtonAction(\"Jump\"", "JsonSerializer.Serialize", "InputBindingsJsonContext.Default.InputBindings",
                     "InputBindingsApplier.Apply", "source.PressKey(key)", "source.ReleaseKey(key)",
                 })
            Assert.Contains(token, bindings);
        foreach (string uiScaffolding in new[] { "Frame(VStack", "Heading(", "Button(_ =>" })
            Assert.DoesNotContain(uiScaffolding, bindings);
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
    }

    [Fact]
    public void Rendering_overview_follows_catalog()
    {
        Dictionary<string, DocsPage> pages = DocsIndex.Build(
            [Catalog.Find("Learn/Graphics/Overview")!], resources: null);
        string overview = pages["Learn/Graphics/Overview"].Text;
        int previous = -1;
        foreach (string route in RenderingCourseCatalog.ApplicationRoute)
        {
            int current = overview.IndexOf("story:" + route, StringComparison.Ordinal);
            Assert.True(current > previous, $"Overview route is missing or out of order: {route}");
            previous = current;
        }
        Assert.True(overview.IndexOf("story:Examples/3D/Triangle", StringComparison.Ordinal) > previous);
        Assert.Contains("その下のInternal", overview);

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
    public void Runtime_sample_bundles_are_connected_to_learn_pages()
    {
        (string Bundle, string Learn)[] cases =
        [
            ("input.actions", "Learn/Input/Overview"),
            ("audio.tone", "Learn/Audio/Overview"),
            ("resources.pipeline", "Learn/Resources/Overview"),
        ];
        foreach (var item in cases)
        {
            SampleBundleInfo bundle = SampleBundleRegistry.Find(item.Bundle)!;
            Assert.Equal(SampleCopyLevel.Block, bundle.CopyLevel);
            Assert.False(string.IsNullOrWhiteSpace(bundle.SmokeCommand));
            Assert.Equal(item.Bundle, Catalog.Find(item.Learn)!.SampleBundle);
        }
    }

    [Fact]
    public void Framework_and_ui_learning_paths_have_clean_consumer_bundles()
    {
        (string Route, string Bundle)[] pages =
        [
            ("Learn/Framework/Overview", "framework.fixed-timestep"),
            ("Learn/Framework/FixedTimestepAndPhases", "framework.fixed-timestep"),
            ("Learn/UI/WidgetTrees", "ui.headless-tree"),
            ("Learn/UI/Signals", "ui.headless-tree"),
        ];
        foreach ((string route, string bundle) in pages)
        {
            StoryInfo story = Catalog.Find(route) ?? throw new InvalidOperationException(route);
            Assert.Equal(bundle, story.SampleBundle);
            Assert.NotNull(SampleBundleRegistry.Find(bundle));
        }
        Assert.NotNull(Catalog.Find("Learn/Framework/ScenesAndServices"));
        Assert.NotNull(Catalog.Find("Learn/UI/BuildAndReconciliation"));
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(Catalog.All, resources: null, Catalog), Catalog));
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
        ];
        StoryInfo[] stories = routes.Select(route => Catalog.Find(route)
            ?? throw new InvalidOperationException(route)).ToArray();
        Assert.Empty(DocsIndex.ValidateLinks(DocsIndex.Build(stories, resources: null)));
    }

    [Fact]
    public void Input_examples_are_canonical_story_based_samples()
    {
        StoryCatalog browserCatalog = CoreUiStoryProject.CreateCatalog();
        foreach (string route in new[] { "Examples/Input/SourcesAndBus", "Examples/Input/Actions", "Examples/Input/ContextStack", "Examples/Input/Bindings" })
        {
            StoryInfo story = Catalog.Find(route) ?? throw new InvalidOperationException(route);
            StoryInfo browserStory = browserCatalog.Find(route) ?? throw new InvalidOperationException($"Browser catalog: {route}");
            Assert.Equal(CoreUiStoryProject.RuntimeBundleId, story.RuntimeBundleId);
            Assert.Equal(CoreUiStoryProject.RuntimeBundleId, browserStory.RuntimeBundleId);
            Assert.Null(story.SampleBundle);
            using var context = new StoryContext();
            Assert.Equal(StoryResultKind.Widget, browserStory.BuildResult(context).Kind);
        }
        Assert.Null(Catalog.Find("Learn/Input/BrowserWasm"));
        Assert.Null(Catalog.Find("Examples/Input/WindowActions"));
    }

    [Fact]
    public void Runtime_examples_are_source_backed_and_bundle_connected()
    {
        (string Route, string Bundle)[] examples =
        [
            ("Examples/Audio/WaveformAndVoice", "audio.tone"),
            ("Examples/Audio/Buses", "audio.tone"), ("Examples/Audio/SpatialAttenuation", "audio.tone"),
            ("Examples/Audio/StreamingQueue", "audio.tone"), ("Examples/Resources/Pipeline", "resources.pipeline"),
            ("Examples/Resources/DependencyDag", "resources.pipeline"), ("Examples/Resources/Reload", "resources.pipeline"),
            ("Examples/Resources/Lifetime", "resources.pipeline"),
        ];
        StoryInfo[] stories = examples.Select(item => Catalog.Find(item.Route)!).ToArray();
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

    private static ISemanticDocument? BuildSemanticDocument(StoryInfo story)
    {
        using var context = new StoryContext();
        StoryResult result = story.BuildResult(context);
        if (result.Kind == StoryResultKind.Widget) return DocsIndex.FindSemanticDocument(result.Widget);
        var embeds = result.Embeds.Select(embed => new DocEmbed(embed.Widget,
                Enum.TryParse(embed.Kind, out DocEmbedKind kind) ? kind : DocEmbedKind.Widget,
                embed.Reference, embed.Inline, embed.IncludeInherited, embed.WidgetFactory))
            .Concat(result.References.Select(reference => new DocEmbed(null, DocEmbedKind.StoryRef, reference.Path)))
            .ToArray();
        return new TestSemanticDocument(StoryMarkdownRenderer.EffectiveMarkdown(story, result.Markdown), embeds);
    }

    private sealed record TestSemanticDocument(string? DocumentSource, IReadOnlyList<DocEmbed> DocumentEmbeds)
        : ISemanticDocument;

    [Fact]
    public void Rendering_samples_are_in_solution_and_pages_ci_builds_solution()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(root, "Luxel.slnx"));
        Assert.Contains("samples/LuxelTriangle/LuxelTriangle.csproj", solution);
        Assert.Contains("samples/LuxelRange/LuxelRange/LuxelRange.csproj", solution);

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy-pages.yml"));
        Assert.Contains("dotnet build Luxel.slnx --no-restore --configuration Release", workflow);
        Assert.Contains("dotnet run --project src/Luxel.Gallery.Site/Luxel.Gallery.Site.csproj", workflow);
        Assert.Contains("--no-restore --no-build --configuration Release -- artifacts/gallery-site", workflow);
        Assert.Contains("--playground-browser-root samples/LuxelPlaygroundBrowser/bin/Release/net10.0/publish/wwwroot", workflow);
        Assert.Contains("--static-capture golden-only", workflow);
        Assert.Contains("JamesIves/github-pages-deploy-action@v4.8.0", workflow);
        Assert.Contains("clean-exclude: pr-preview", workflow);
        Assert.Contains("force: false", workflow);

        string preview = File.ReadAllText(Path.Combine(root, ".github", "workflows", "preview-pages.yml"));
        Assert.Contains("pull_request:", preview);
        Assert.Contains("types: [opened, reopened, synchronize, closed]", preview);
        Assert.Contains("rossjrw/pr-preview-action@v1.8.1", preview);
        Assert.Contains("source-dir: artifacts/gallery-site", preview);
        Assert.Contains("wait-for-pages-deployment: true", preview);
        Assert.Contains("dotnet run --project src/Luxel.Gallery.Site/Luxel.Gallery.Site.csproj", preview);
        Assert.Contains("--no-restore --no-build --configuration Release -- artifacts/gallery-site", preview);
        Assert.Contains("--playground-browser-root samples/LuxelPlaygroundBrowser/bin/Release/net10.0/publish/wwwroot", preview);
        Assert.Contains("--static-capture golden-only", preview);
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
    public void Mermaid_fence_uses_the_official_browser_library_in_html_exports()
    {
        string root = GallerySiteExporter.FindRepositoryRoot();
        StoryInfo story = Catalog.Find("Internals/Architecture")
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
            string index = File.ReadAllText(Path.Combine(output, "index.html"));
            string bootstrap = File.ReadAllText(Path.Combine(output, "mermaid-bootstrap.js"));

            Assert.DoesNotContain("```mermaid", renderedBody);
            Assert.Contains("<pre class=\"mermaid\">", renderedBody);
            Assert.Contains("```mermaid", html); // generated method source remains visible in the collapsed Source section
            Assert.Empty(Directory.GetFiles(Path.Combine(output, "images"), "mermaid-*.png"));
            Assert.Contains("type=\"module\" src=\"mermaid-bootstrap.js\"", index);
            Assert.Contains("cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs", bootstrap);
            Assert.Contains("securityLevel: 'strict'", bootstrap);
            Assert.Contains("mermaid.run({ nodes })", bootstrap);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    private static StoryResult ReferenceMarkdown(string title, string path)
    {
        StoryResult result = $"# {title}\n\n{StoryReference.To(path)}";
        return result;
    }

    private static string CreateBrowserRuntimeRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "luxel-browser-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_framework"));
        File.WriteAllText(Path.Combine(root, "index.html"),
            "<!doctype html><html><body><script type=\"module\" src=\"./main.js\"></script></body></html>");
        File.WriteAllText(Path.Combine(root, "main.js"), "import './_framework/dotnet.js';\n");
        File.Copy(Path.Combine(GallerySiteExporter.FindRepositoryRoot(), "samples", "LuxelWebGpuBrowser", "wwwroot",
            "browser-runtime-manifest.json"), Path.Combine(root, "browser-runtime-manifest.json"));
        File.WriteAllText(Path.Combine(root, "_framework", "dotnet.js"), "export const dotnet = {};\n");
        return root;
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
