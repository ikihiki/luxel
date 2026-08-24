using Luxel.Gallery;
using Luxel.Gallery.Presentation;

namespace Luxel.Tests;

public sealed class GalleryPresentationContractTests
{
    private static StoryInfo S(string path, StoryKind kind = StoryKind.Unspecified)
        => new(path, _ => null!, Kind: kind);

    [Fact]
    public void Chrome_tokens_are_complete_and_meet_contrast_relevant_invariants()
    {
        System.Reflection.PropertyInfo[] colorProperties = typeof(GalleryChromeTokens).GetProperties()
            .Where(property => property.PropertyType == typeof(GalleryColor))
            .ToArray();
        Assert.NotEmpty(colorProperties);

        foreach (GalleryChromeTokens tokens in new[] { GalleryChromeTokens.Light, GalleryChromeTokens.Dark })
        {
            foreach (System.Reflection.PropertyInfo property in colorProperties)
            {
                GalleryColor color = Assert.IsType<GalleryColor>(property.GetValue(tokens));
                Assert.True(color.IsOpaque, $"{tokens.Appearance}.{property.Name} must be opaque.");
            }

            AssertContrast(tokens.Text, tokens.Background, 4.5d, $"{tokens.Appearance}.Text/Background");
            AssertContrast(tokens.Text, tokens.Surface, 4.5d, $"{tokens.Appearance}.Text/Surface");
            AssertContrast(tokens.MutedText, tokens.Background, 4.5d, $"{tokens.Appearance}.MutedText/Background");
            AssertContrast(tokens.MutedText, tokens.Surface, 4.5d, $"{tokens.Appearance}.MutedText/Surface");
            AssertContrast(tokens.InverseText, tokens.Primary, 4.5d, $"{tokens.Appearance}.InverseText/Primary");
            AssertContrast(tokens.Border, tokens.Background, 3d, $"{tokens.Appearance}.Border/Background");
            AssertContrast(tokens.Border, tokens.Surface, 3d, $"{tokens.Appearance}.Border/Surface");
            AssertContrast(tokens.Focus, tokens.Background, 3d, $"{tokens.Appearance}.Focus/Background");
            AssertContrast(tokens.Focus, tokens.Surface, 3d, $"{tokens.Appearance}.Focus/Surface");
            AssertContrast(tokens.Error, tokens.Background, 3d, $"{tokens.Appearance}.Error/Background");
            AssertContrast(tokens.Warning, tokens.Background, 3d, $"{tokens.Appearance}.Warning/Background");
            AssertContrast(tokens.Success, tokens.Background, 3d, $"{tokens.Appearance}.Success/Background");

            Assert.NotEqual(tokens.Surface, tokens.Selected);
            Assert.NotEqual(tokens.Surface, tokens.Hover);
            Assert.NotEqual(tokens.Hover, tokens.Pressed);
            Assert.True(tokens.BodyFontSize >= 16f);
            Assert.True(tokens.SupportingFontSize >= 13f);
            Assert.True(tokens.CodeFontSize >= 13f);
            Assert.InRange(tokens.BodyLineHeight, 1.55f, 1.75f);
            Assert.True(tokens.ToolbarHeight > 0f);
            Assert.True(tokens.SidebarWidth > tokens.PanelMinimumSize);
            Assert.True(tokens.DocsMaximumWidth >= 960f);
            Assert.InRange(tokens.DocsTextMaximumCharacters, 65, 80);
        }

        Assert.Same(GalleryChromeTokens.Dark,
            GalleryChromeTokens.Resolve(GalleryAppearance.System, GalleryAppearance.Dark));
        Assert.Same(GalleryChromeTokens.Light,
            GalleryChromeTokens.Resolve(GalleryAppearance.Light, GalleryAppearance.Dark));
    }

    [Fact]
    public void Appearance_settings_resolve_shell_and_preview_without_host_dependencies()
    {
        var independent = new GalleryAppearanceSettings(
            GalleryAppearance.System,
            GalleryAppearance.Dark,
            SynchronizePreview: false);
        Assert.Equal(GalleryAppearance.Light, independent.ResolveShell(GalleryAppearance.Light));
        Assert.Equal(GalleryAppearance.Dark, independent.ResolvePreview(GalleryAppearance.Light));

        var synchronized = independent with
        {
            Shell = GalleryAppearance.Dark,
            Preview = GalleryAppearance.Light,
            SynchronizePreview = true,
        };
        Assert.Equal(GalleryAppearance.Dark, synchronized.ResolveShell(GalleryAppearance.Light));
        Assert.Equal(GalleryAppearance.Dark, synchronized.ResolvePreview(GalleryAppearance.Light));
        Assert.Throws<ArgumentOutOfRangeException>(() => synchronized.ResolveShell(GalleryAppearance.System));
    }

    [Fact]
    public void Labels_use_canonical_japanese_terms_and_preserve_api_identifiers()
    {
        Assert.Equal("ストーリー", GalleryLabels.Stories);
        Assert.Equal("ドキュメント", GalleryLabels.Documentation);
        Assert.Equal("プレビュー", GalleryLabels.Preview);
        Assert.Equal("引数", GalleryLabels.Arguments);
        Assert.Equal("出力", GalleryLabels.Output);
        Assert.Equal("ソース", GalleryLabels.Source);
        Assert.Equal("操作", GalleryLabels.Actions);
        Assert.Equal("テーマ", GalleryLabels.Theme);
        Assert.Equal("例", GalleryLabels.RouteGroupLabel("Examples"));
        Assert.Equal("状態", GalleryLabels.RouteGroupLabel("States"));
        Assert.Equal("アクセシビリティ", GalleryLabels.RouteGroupLabel("Accessibility"));
        Assert.Equal("Button", GalleryLabels.RouteGroupLabel("Button"));
        Assert.Equal("Native のみ", GalleryLabels.CompatibilityLabel(GalleryCompatibility.NativeOnly));
    }

    [Fact]
    public void Navigation_groups_components_orders_sections_defaults_to_docs_and_preserves_paths()
    {
        StoryOwnership ownership = StoryOwnership.NativeOnly("UI", "UI.Base");
        StoryInfo docs = S("Controls/Input/Button/Docs", StoryKind.Docs) with
        {
            Ownership = ownership,
            CapabilityNote = "Native fixture",
            ShortDescription = "Button の概要です。",
            LongDescription = "Button の用途と操作方法を説明します。",
        };
        StoryCatalog catalog = new StoryCatalogBuilder()
            .Add(S("Controls/Input/Button/Examples/Interactive", StoryKind.Example))
            .Add(S("Controls/Input/Button/Examples/Text", StoryKind.Example))
            .Add(S("Controls/Input/Button/Test/Stress", StoryKind.TestFixture))
            .Add(S("Controls/Input/Button/Basic", StoryKind.Basic))
            .Add(S("Start/Welcome"))
            .Add(S("Controls/Input/Button/States/Disabled", StoryKind.State))
            .Add(docs)
            .Add(S("Controls/Input/Button/Accessibility/Keyboard", StoryKind.AccessibilityFixture))
            .Add(S("Controls/Input/Button/Playground", StoryKind.Playground))
            .AddAlias("Controls/Button/Docs", docs.Path)
            .Build();

        GalleryNavigationModel model = GalleryNavigationBuilder.Build(catalog);

        Assert.Equal(["Start", "Controls"], model.Categories.Select(node => node.Segment));
        Assert.Equal(["はじめに", "コントロール"], model.Categories.Select(node => node.DisplayLabel));
        GalleryNavigationNode input = Assert.IsType<GalleryNavigationNode>(model.FindNode("Controls/Input"));
        Assert.Equal("入力", input.DisplayLabel);
        GalleryNavigationNode component = Assert.IsType<GalleryNavigationNode>(model.FindNode("Controls/Input/Button"));
        Assert.Equal(GalleryNavigationNodeKind.Component, component.Kind);
        Assert.Equal("Button", component.DisplayLabel);
        Assert.Equal(docs.Path, component.DefaultStoryPath);
        Assert.Equal(docs.Path, component.TargetPath);
        Assert.Equal(
            ["Docs", "Playground", "Basic", "Examples", "States", "Accessibility", "Test"],
            component.Children.Select(node => node.Segment));
        Assert.Equal(
            ["ドキュメント", "プレイグラウンド", "基本", "例", "状態", "アクセシビリティ", "テスト"],
            component.Children.Select(node => node.DisplayLabel));

        GalleryNavigationNode examples = Assert.IsType<GalleryNavigationNode>(model.FindNode("Controls/Input/Button/Examples"));
        Assert.Equal(GalleryNavigationNodeKind.Group, examples.Kind);
        Assert.Equal(["Interactive", "Text"], examples.Children.Select(node => node.DisplayLabel));

        GalleryNavigationStory navigationDocs = Assert.IsType<GalleryNavigationStory>(model.FindStory(docs.Path));
        Assert.Same(docs.Build, navigationDocs.Info.Build);
        Assert.Equal(StoryKind.Docs, navigationDocs.Kind);
        Assert.Equal(ownership, navigationDocs.Ownership);
        Assert.Equal(GalleryCompatibility.NativeOnly, navigationDocs.Compatibility);
        Assert.Equal("Native fixture", navigationDocs.CapabilityNote);
        Assert.Equal(["Controls/Button/Docs"], navigationDocs.Aliases);
        Assert.Equal("Button の概要です。", navigationDocs.ShortDescription);
        Assert.Equal("Button の用途と操作方法を説明します。", navigationDocs.LongDescription);

        Assert.Equal(
            catalog.All.Select(story => story.Path).Order(StringComparer.Ordinal),
            model.Stories.Select(story => story.CanonicalPath).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(model.Stories, story => story.CanonicalPath == "Controls/Button/Docs");
    }

    [Fact]
    public void Production_controls_navigation_preserves_all_paths_and_uses_generated_docs_targets()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        GalleryNavigationModel model = GalleryNavigationBuilder.Build(catalog);

        Assert.Equal(
            catalog.All.Select(story => story.Path).Order(StringComparer.Ordinal),
            model.Stories.Select(story => story.CanonicalPath).Order(StringComparer.Ordinal));
        foreach (GeneratedComponentStoryDescriptor descriptor in global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents)
        {
            GalleryNavigationNode component = Assert.IsType<GalleryNavigationNode>(model.FindNode(descriptor.RoutePrefix));
            Assert.Equal(GalleryNavigationNodeKind.Component, component.Kind);
            Assert.Equal(descriptor.ControlName, component.DisplayLabel);
            Assert.Equal(descriptor.DocsPath, component.DefaultStoryPath);
            Assert.Equal(descriptor.DocsPath, component.TargetPath);
            Assert.False(string.IsNullOrWhiteSpace(model.FindStory(descriptor.DocsPath)?.ShortDescription));
            Assert.False(string.IsNullOrWhiteSpace(model.FindStory(descriptor.BasicPath)?.ShortDescription));
            if (descriptor.IsUserFacing)
                Assert.False(string.IsNullOrWhiteSpace(model.FindStory(descriptor.PlaygroundPath)?.ShortDescription));
        }
    }

    [Fact]
    public void Authored_component_replacement_inherits_generated_descriptions_when_unspecified()
    {
        var descriptor = new GeneratedComponentStoryDescriptor(
            "global::Luxel.Controls.Button", "Luxel.Controls", "Input", "Button");
        StoryOwnership ownership = StoryOwnership.BrowserSafe("UI", "UI.Base");
        StoryInfo generated = S(descriptor.BasicPath, StoryKind.Basic) with
        {
            RegistrationKind = StoryRegistrationKind.GeneratedComponentFallback,
            ProductionComponent = descriptor,
            Ownership = ownership,
            ShortDescription = "生成された短い説明",
            LongDescription = "生成された詳しい説明",
        };
        StoryInfo authored = S(descriptor.BasicPath, StoryKind.Basic) with
        {
            LongDescription = "authored の詳しい説明",
        };

        StoryInfo resolved = Assert.Single(new StoryCatalogBuilder()
            .Add(generated)
            .Add(authored, replaceGenerated: true)
            .Build().All);

        Assert.Same(authored.Build, resolved.Build);
        Assert.Equal("生成された短い説明", resolved.ShortDescription);
        Assert.Equal("authored の詳しい説明", resolved.LongDescription);
        Assert.Equal(descriptor, resolved.ProductionComponent);
        Assert.Equal(ownership, resolved.Ownership);
    }

    private static void AssertContrast(
        GalleryColor foreground,
        GalleryColor background,
        double minimum,
        string label)
        => Assert.True(foreground.ContrastRatio(background) >= minimum,
            $"{label} contrast was {foreground.ContrastRatio(background):0.00}, expected at least {minimum:0.0}.");
}
