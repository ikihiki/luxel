using System.Text.Json;
using Luxel.Gallery;
using Luxel.UI;

namespace Luxel.Tests;

public sealed class ProductionDocsCompletenessTests
{
    private static readonly HashSet<string> Categories =
        ["Layout", "Input", "Text", "Collections", "Overlay", "Rendering", "Editor"];

    [Fact]
    public void Production_inventory_and_canonical_paths_are_unique_across_owners()
    {
        GeneratedComponentStoryDescriptor[] ui = [.. global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] editor = [.. global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] particles = [.. global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents];
        GeneratedComponentStoryDescriptor[] all = [.. ui, .. editor, .. particles];

        Assert.Equal(57, ui.Length);
        Assert.Equal(7, editor.Length);
        Assert.Single(particles);
        Assert.Equal(65, all.Length);
        Assert.Equal(62, all.Count(static descriptor => descriptor.IsUserFacing));
        Assert.Equal(all.Length, all.Select(static descriptor => descriptor.ComponentType).Distinct(StringComparer.Ordinal).Count());
        int expectedPaths = all.Sum(static descriptor => descriptor.IsUserFacing ? 3 : 2);
        Assert.Equal(expectedPaths, all.SelectMany(static descriptor => descriptor.IsUserFacing
                ? new[] { descriptor.DocsPath, descriptor.BasicPath, descriptor.PlaygroundPath }
                : new[] { descriptor.DocsPath, descriptor.BasicPath })
            .Distinct(StringComparer.Ordinal).Count());

        foreach (GeneratedComponentStoryDescriptor descriptor in all.Where(static descriptor => descriptor.IsUserFacing))
        {
            Assert.Contains(descriptor.Category, Categories);
            Assert.StartsWith($"Controls/{descriptor.Category}/{descriptor.ControlName}/", descriptor.DocsPath, StringComparison.Ordinal);
            Assert.EndsWith("/Docs", descriptor.DocsPath, StringComparison.Ordinal);
            Assert.EndsWith("/Basic", descriptor.BasicPath, StringComparison.Ordinal);
            Assert.EndsWith("/Playground", descriptor.PlaygroundPath, StringComparison.Ordinal);
        }
        Assert.All(all.Where(static descriptor => !descriptor.IsUserFacing), descriptor =>
            Assert.StartsWith("Gallery/Infrastructure/", descriptor.DocsPath, StringComparison.Ordinal));
    }

    [Fact]
    public void All_control_stories_use_the_fixed_category_taxonomy()
    {
        StoryInfo[] stories =
        [
            .. global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog().All,
            .. global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog().All,
            .. global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog().All,
        ];

        string[] invalid = stories.Where(static story => story.Path.StartsWith("Controls/", StringComparison.Ordinal))
            .Select(static story => story.Path)
            .Where(path =>
            {
                string[] segments = path.Split('/');
                return segments.Length < 4 || !Categories.Contains(segments[1]);
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(invalid.Length == 0, $"Noncanonical control stories: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void Each_owner_catalog_has_one_docs_basic_and_user_facing_playground_story_with_matching_ownership()
    {
        VerifyOwner(global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog(),
            global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents,
            global::Luxel.UI.Gallery.UiGalleryProject.Ownership);
        VerifyOwner(global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog(),
            global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents,
            global::Luxel.Editor.Gallery.EditorGalleryProject.Ownership);
        VerifyOwner(global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog(),
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents,
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.Ownership);
    }

    [Fact]
    public void Button_basic_explicitly_disables_args_while_playground_keeps_them()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();

        StoryInfo basic = Assert.IsType<StoryInfo>(catalog.Find("Controls/Input/Button/Basic"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StoryArgDefinition>>(basic.ArgDefinitions));

        StoryInfo playground = Assert.IsType<StoryInfo>(catalog.Find("Controls/Input/Button/Playground"));
        Assert.NotEmpty(Assert.IsAssignableFrom<IReadOnlyList<StoryArgDefinition>>(playground.ArgDefinitions));
    }

    [Fact]
    public void NodeGraph_playground_exposes_only_the_authored_graph_json_arg()
    {
        StoryCatalog catalog = global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog();
        StoryInfo playground = Assert.IsType<StoryInfo>(catalog.Find("Controls/Editor/NodeGraphView/Playground"));

        StoryArgDefinition definition = Assert.Single(playground.ArgDefinitions ?? []);
        Assert.Equal("graph", definition.Name);
        Assert.Equal("json", definition.Type);
        Assert.Equal(StoryArgEditorKind.Json, definition.EditorKind);
        Assert.Equal(JsonValueKind.Object, definition.DefaultValue.ValueKind);

        using var context = new StoryContext();
        _ = playground.Build(context);
        Assert.Equal(["graph"], context.ArgDefinitions.Select(static arg => arg.Name));
    }

    [Fact]
    public void Legacy_control_paths_are_aliases_and_not_sidebar_entries()
    {
        StoryCatalog ui = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        Assert.DoesNotContain(ui.All, story => story.Path == "Controls/Button/Basic");
        Assert.Equal("Controls/Input/Button/Basic", ui.Find("Controls/Button/Basic")?.Path);
        Assert.Equal("Controls/Input/CheckBox/Basic", ui.Find("Controls/Check/Basic")?.Path);
        Assert.Equal("Controls/Collections/ScrollViewer/Basic", ui.Find("Controls/Scroll/Basic")?.Path);

        StoryCatalog editor = global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog();
        Assert.DoesNotContain(editor.All, story => story.Path == "Editor/Controls/AssetBrowser/Basic");
        Assert.Equal("Controls/Collections/AssetBrowser/Basic", editor.Find("Controls/AssetBrowserBasic")?.Path);
        Assert.Equal("Controls/Collections/AssetBrowser/Basic", editor.Find("Editor/Controls/AssetBrowser/Basic")?.Path);
        Assert.Equal("Controls/Editor/CommandPalette/Docs", editor.Find("Controls/CommandPalette/Docs")?.Path);
    }

    [Fact]
    public void Every_user_facing_component_has_exact_canonical_docs_basic_and_playground_contracts()
    {
        foreach ((StoryCatalog catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors) in OwnerCatalogs())
        {
            foreach (GeneratedComponentStoryDescriptor descriptor in descriptors.Where(static item => item.IsUserFacing))
            {
                StoryInfo docs = Assert.Single(catalog.All, story => story.Path == descriptor.DocsPath);
                StoryInfo basic = Assert.Single(catalog.All, story => story.Path == descriptor.BasicPath);
                StoryInfo playground = Assert.Single(catalog.All, story => story.Path == descriptor.PlaygroundPath);

                Assert.Equal(StoryKind.Docs, docs.Kind);
                Assert.Equal(StoryKind.Basic, basic.Kind);
                Assert.Equal(StoryKind.Playground, playground.Kind);
                Assert.Empty(basic.ArgDefinitions ?? []);

                using var context = new StoryContext();
                StoryResult docsResult = docs.Build(context);
                Assert.Equal(descriptor.BasicPath, Assert.IsType<StoryReference>(docsResult.References.FirstOrDefault()).Path);
            }
        }
    }

    [Fact]
    public void Every_canonical_playground_contains_every_ui_param_and_runtime_matches_its_static_schema()
    {
        int playgroundCount = 0;
        foreach ((StoryCatalog catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors) in OwnerCatalogs())
        {
            foreach (GeneratedComponentStoryDescriptor descriptor in descriptors.Where(static item => item.IsUserFacing))
            {
                playgroundCount++;
                StoryInfo playground = Assert.IsType<StoryInfo>(catalog.Find(descriptor.PlaygroundPath));
                string[] staticArgs = (playground.ArgDefinitions ?? []).Select(static definition => definition.Name).ToArray();
                string componentType = descriptor.ComponentType.StartsWith("global::", StringComparison.Ordinal)
                    ? descriptor.ComponentType[8..]
                    : descriptor.ComponentType;
                int namespaceEnd = componentType.LastIndexOf('.');
                string apiIdentity = namespaceEnd < 0
                    ? descriptor.ControlName
                    : componentType[..(namespaceEnd + 1)] + descriptor.ControlName;
                ControlApi? api = ControlApiRegistry.Find(apiIdentity);
                Assert.True(api is not null, $"No ControlApi registered for {apiIdentity} ({descriptor.PlaygroundPath}).");
                string[] missingParams = api.Members
                    .Where(static member => member.Kind == "param")
                    .Select(static member => LowerFirst(member.Name))
                    .Distinct(StringComparer.Ordinal)
                    .Where(name => !staticArgs.Contains(name, StringComparer.Ordinal))
                    .ToArray();
                Assert.True(missingParams.Length == 0,
                    $"{descriptor.PlaygroundPath} is missing UiParam args: {string.Join(", ", missingParams)}");

                using var context = new StoryContext();
                _ = playground.Build(context);
                Assert.Equal(staticArgs, context.ArgDefinitions.Select(static definition => definition.Name));
            }
        }

        Assert.Equal(62, playgroundCount);
    }

    [Fact]
    public void High_value_generated_playgrounds_have_visible_fixtures()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        string[] visibleFixtures =
        [
            "Controls/Layout/Box/Playground",
            "Controls/Layout/Border/Playground",
            "Controls/Layout/Center/Playground",
            "Controls/Layout/Stack/Playground",
            "Controls/Layout/Spacer/Playground",
            "Controls/Collections/ListView/Playground",
            "Controls/Collections/Tabs/Playground",
            "Controls/Input/ColorPicker/Playground",
            "Controls/Input/Slider/Playground",
        ];
        foreach (string path in visibleFixtures)
        {
            StoryInfo story = Assert.IsType<StoryInfo>(catalog.Find(path));
            using var context = new StoryContext();
            StoryResult result = story.Build(context);
            Assert.NotNull(result.Widget);
            Assert.IsNotType<StoryCapabilityFallback>(result.Widget);
        }
    }

    [Fact]
    public void Authored_interactive_examples_remain_registered_outside_canonical_playgrounds()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        string[] paths =
        [
            "Controls/Input/Button/Examples/Interactive",
            "Controls/Input/ColorPicker/Examples/Interactive",
            "Controls/Input/Slider/Examples/Interactive",
            "Controls/Text/TextField/Examples/Interactive",
            "Controls/Collections/TreeView/Examples/Interactive",
            "Controls/Layout/Box/Examples/Interactive",
            "Controls/Layout/Border/Examples/Interactive",
            "Controls/Layout/Center/Examples/Interactive",
            "Controls/Layout/Stack/Examples/Interactive",
            "Controls/Layout/Spacer/Examples/Interactive",
            "Controls/Collections/ListView/Examples/Interactive",
            "Controls/Collections/Tabs/Examples/Interactive",
        ];

        foreach (string path in paths)
        {
            StoryInfo story = Assert.Single(catalog.All, candidate => candidate.Path == path);
            Assert.Equal(StoryKind.Example, story.Kind);
            using var context = new StoryContext();
            Assert.NotNull(story.Build(context).Widget);
        }
    }

    [Fact]
    public void Infrastructure_components_do_not_register_playgrounds()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        foreach (GeneratedComponentStoryDescriptor descriptor in global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents
                     .Where(static item => !item.IsUserFacing))
            Assert.Null(catalog.Find(descriptor.PlaygroundPath));
    }

    [Fact]
    public void Auto_frame_wraps_basic_and_playground_widgets_but_not_docs()
    {
        using var context = new StoryContext();
        var basic = new StoryInfo("Controls/Input/Test/Basic", _ => Luxel.Controls.Kit.Button(text: "Basic"), Kind: StoryKind.Basic);
        var playground = new StoryInfo("Controls/Input/Test/Playground", _ => Luxel.Controls.Kit.Button(text: "Playground"), Kind: StoryKind.Playground);
        var docs = new StoryInfo("Controls/Input/Test/Docs", _ => StoryResult.FromMarkdown("# Test"), Kind: StoryKind.Docs);

        StoryResult basicResult = global::Luxel.Gallery.UI.StoryPresentation.Build(basic, context);
        StoryResult playgroundResult = global::Luxel.Gallery.UI.StoryPresentation.Build(playground, context);
        StoryResult docsResult = global::Luxel.Gallery.UI.StoryPresentation.Build(docs, context);

        Assert.IsType<Luxel.Controls.Border>(basicResult.Widget);
        Assert.IsType<Luxel.Controls.Border>(playgroundResult.Widget);
        Assert.Equal(StoryResultKind.Markdown, docsResult.Kind);
    }

    [Fact]
    public void Authored_basic_and_playground_sources_do_not_call_frame()
    {
        string root = FindRepositoryRoot();
        string[] sourceRoots =
        [
            Path.Combine(root, "src", "UI", "Luxel.UI.Gallery"),
            Path.Combine(root, "src", "Editor", "Luxel.Editor.Gallery"),
            Path.Combine(root, "src", "Particles", "Luxel.Particles.Gallery"),
        ];
        var violations = new List<string>();
        foreach (string file in sourceRoots.SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                     .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            string source = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(source,
                         @"\[Story\([^\]]*/(?:Basic|Playground)[^\]]*\)\](?<body>.*?)(?=\n\s*\[Story|\z)",
                         System.Text.RegularExpressions.RegexOptions.Singleline))
                if (match.Groups["body"].Value.Contains("Frame(", StringComparison.Ordinal))
                    violations.Add(Path.GetRelativePath(root, file));
        }
        Assert.True(violations.Count == 0, $"Basic/Playground stories must rely on auto-frame: {string.Join(", ", violations.Distinct())}");
    }

    [Fact]
    public void Xml_doc_resolver_is_case_sensitive_and_preserves_fallbacks()
    {
        Assert.Equal("fallback", GalleryXmlDocText.Resolve("xml:T:Missing", "fallback"));
        Assert.Equal(string.Empty, GalleryXmlDocText.Resolve("xml:T:Missing", string.Empty));
        Assert.Equal("fallback", GalleryXmlDocText.Resolve("XML:T:Missing", "fallback"));
    }

    [Fact]
    public void Control_api_registry_uses_fully_qualified_identity_and_rejects_ambiguous_short_names()
    {
        var left = new ControlApi("Probe.Left", "Collision", "left", []);
        var right = new ControlApi("Probe.Right", "Collision", "right", []);
        ControlApiRegistry.Register(left);
        ControlApiRegistry.Register(right);

        Assert.Same(left, ControlApiRegistry.Find("Probe.Left.Collision"));
        Assert.Same(right, ControlApiRegistry.Find("Probe.Right.Collision"));
        Assert.Null(ControlApiRegistry.Find("Collision"));
    }

    [Fact]
    public void Localized_control_api_registration_wins_regardless_of_initializer_order()
    {
        var localized = new ControlApi("Probe.Priority", "Localized", "日本語", []);
        var raw = new ControlApi("Probe.Priority", "Localized", "English", []);

        ControlApiRegistry.RegisterLocalized(localized);
        ControlApiRegistry.Register(raw);

        Assert.Same(localized, ControlApiRegistry.Find("Probe.Priority.Localized"));
    }

    private static string LowerFirst(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static IEnumerable<(StoryCatalog Catalog, IReadOnlyList<GeneratedComponentStoryDescriptor> Descriptors)> OwnerCatalogs()
    {
        yield return (global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog(),
            global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents);
        yield return (global::Luxel.Editor.Gallery.EditorGalleryProject.CreateCatalog(),
            global::Luxel.Editor.Gallery.EditorGalleryProject.ProductionComponents);
        yield return (global::Luxel.Particles.Gallery.ParticlesGalleryProject.CreateCatalog(),
            global::Luxel.Particles.Gallery.ParticlesGalleryProject.ProductionComponents);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Luxel.slnx.");
    }

    private static void VerifyOwner(StoryCatalog catalog,
        IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors, StoryOwnership ownership)
    {
        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors)
        {
            StoryInfo docs = Assert.Single(catalog.All, story => story.Path == descriptor.DocsPath);
            StoryInfo basic = Assert.Single(catalog.All, story => story.Path == descriptor.BasicPath);
            Assert.Equal(descriptor, docs.ProductionComponent);
            Assert.Equal(descriptor, basic.ProductionComponent);
            Assert.Equal(ownership, docs.Ownership);
            Assert.Equal(ownership, basic.Ownership);
            if (descriptor.IsUserFacing)
            {
                StoryInfo playground = Assert.Single(catalog.All, story => story.Path == descriptor.PlaygroundPath);
                Assert.Equal(descriptor, playground.ProductionComponent);
                Assert.Equal(ownership, playground.Ownership);
            }
            else
            {
                Assert.Null(catalog.Find(descriptor.PlaygroundPath));
            }
        }
    }
}
