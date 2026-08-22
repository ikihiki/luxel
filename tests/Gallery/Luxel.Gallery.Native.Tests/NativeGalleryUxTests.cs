using Luxel.Gallery;
using Luxel.Gallery.Presentation;
using Luxel.UI;
using Luxel.Settings;

namespace Luxel.Gallery.Native.Tests;

public sealed class GalleryChromeThemeTests
{
    [Fact]
    public void LightAndDarkThemesMapSharedSemanticTokens()
    {
        Theme light = GalleryChromeTheme.Create(GalleryAppearance.Light);
        Theme dark = GalleryChromeTheme.Create(GalleryAppearance.Dark);
        NativeGalleryChrome lightChrome = GalleryChromeTheme.Tokens(GalleryAppearance.Light);
        NativeGalleryChrome darkChrome = GalleryChromeTheme.Tokens(GalleryAppearance.Dark);

        Assert.Equal(GalleryChromeTokens.Light.Background.Rgba, light.Background);
        Assert.Equal(GalleryChromeTokens.Light.Text.Rgba, light.Text);
        Assert.Equal(GalleryChromeTokens.Dark.Background.Rgba, dark.Background);
        Assert.Equal(GalleryChromeTokens.Dark.Text.Rgba, dark.Text);
        Assert.Equal(GalleryChromeTokens.Light.Selected.Rgba, lightChrome.AccentSoft);
        Assert.Equal(GalleryChromeTokens.Dark.Selected.Rgba, darkChrome.AccentSoft);
        Assert.Equal(GalleryChromeTokens.Light.CodeSurface.Rgba, lightChrome.PanelCode);
        Assert.Equal(GalleryChromeTokens.Dark.Warning.Rgba, darkChrome.Warning);
    }

    [Fact]
    public void AppearanceSettingsPersistAndSynchronizationFollowsShell()
    {
        var files = new InMemoryFileStore();
        var state = new GalleryAppearanceState(files);

        Assert.Equal(GalleryAppearance.Dark, state.ShellTheme.Peek());
        Assert.Equal(GalleryAppearance.Light, state.PreviewTheme.Peek());
        Assert.False(state.SynchronizePreview.Peek());

        state.ToggleSynchronization();
        Assert.True(state.SynchronizePreview.Peek());
        Assert.Equal(GalleryAppearance.Dark, state.PreviewTheme.Peek());
        state.ToggleShellTheme();
        Assert.Equal(GalleryAppearance.Light, state.ShellTheme.Peek());
        Assert.Equal(GalleryAppearance.Light, state.PreviewTheme.Peek());

        var reloaded = new GalleryAppearanceState(files);
        Assert.Equal(GalleryAppearance.Light, reloaded.ShellTheme.Peek());
        Assert.Equal(GalleryAppearance.Light, reloaded.PreviewTheme.Peek());
        Assert.True(reloaded.SynchronizePreview.Peek());
    }
}

public sealed class NativeGalleryLabelTests
{
    [Fact]
    public void ShellAndStateSummariesUseSharedJapaneseLabels()
    {
        Assert.Equal("ストーリー", GalleryLabels.Stories);
        Assert.Equal("プレビュー", GalleryLabels.Preview);
        Assert.Equal("引数", GalleryLabels.Arguments);
        Assert.Equal("出力", GalleryLabels.Output);
        Assert.Equal("ソース", GalleryLabels.Source);
        Assert.Contains("読み込", NativeGalleryLabels.LoadingSummary);
        Assert.Contains("エラー", NativeGalleryLabels.ErrorSummary);
        Assert.Equal(GalleryLabels.Arguments, NativeRenderingLabels.Arguments);
        Assert.Equal("ソースを表示できません。", NativeRenderingLabels.SourceUnavailable);
    }

    [Fact]
    public void SharedNavigationLabelsTranslateShellTermsButPreserveApiIdentifiers()
    {
        Assert.Equal("ドキュメント", GalleryLabels.RouteGroupLabel("Docs"));
        Assert.Equal("プレイグラウンド", GalleryLabels.RouteGroupLabel("Playground"));
        Assert.Equal("アクセシビリティ", GalleryLabels.RouteGroupLabel("Accessibility"));
        Assert.Equal("Button", GalleryLabels.RouteGroupLabel("Button"));
        Assert.Contains("Controls/Button/Basic", NativeRenderingLabels.StoryNotFound("Controls/Button/Basic"));
    }

    [Fact]
    public void ThemeButtonsDescribeCurrentModesWithoutEnglishShellLabels()
    {
        string shell = NativeGalleryLabels.ShellThemeButton(GalleryAppearance.Dark);
        string preview = NativeGalleryLabels.PreviewThemeButton(GalleryAppearance.Light);

        Assert.Equal("画面: ダーク", shell);
        Assert.Equal("プレビュー: ライト", preview);
        Assert.DoesNotContain("Dark", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Light", preview, StringComparison.Ordinal);
    }
}

public sealed class NativeGalleryNavigationAdoptionTests
{
    [Fact]
    public void Component_nodes_use_shared_docs_targets_without_changing_story_paths()
    {
        StoryInfo docs = new("Controls/Input/Button/Docs", _ => null!, Kind: StoryKind.Docs);
        StoryInfo basic = new("Controls/Input/Button/Basic", _ => null!, Kind: StoryKind.Basic);
        StoryCatalog catalog = new StoryCatalogBuilder().Add(basic).Add(docs).Build();
        using var app = new GalleryApp(catalog, new InMemoryFileStore());

        GalleryNavigationNode component = Assert.IsType<GalleryNavigationNode>(
            app.NavigationModel.FindNode("Controls/Input/Button"));
        Assert.Equal(docs.Path, component.TargetPath);
        Assert.Equal("ドキュメント", app.NavigationModel.FindNode(docs.Path)?.DisplayLabel);
        Assert.Equal([basic.Path, docs.Path],
            app.NavigationModel.Stories.Select(story => story.CanonicalPath).Order(StringComparer.Ordinal));
        Assert.Same(docs.Build, app.NavigationModel.FindStory(docs.Path)?.Info.Build);
    }
}

public sealed class GalleryLayoutPolicyTests
{
    [Theory]
    [InlineData(960, 640, true)]
    [InlineData(1040, 700, true)]
    [InlineData(1280, 840, false)]
    public void SelectsDeliberateLayoutMode(float width, float height, bool compact)
        => Assert.Equal(compact ? GalleryLayoutMode.Compact : GalleryLayoutMode.Wide,
            GalleryLayoutPolicy.Select(width, height));

    [Fact]
    public void CompactBaselineProtectsPreviewAndKeepsToolsSecondary()
    {
        GalleryPreviewExtent extent = GalleryLayoutPolicy.PreviewExtent(
            GalleryLayoutMode.Compact,
            GalleryLayoutPolicy.BaselineWidth,
            GalleryLayoutPolicy.BaselineHeight,
            sidebarWidth: 290,
            toolsHeight: 260,
            zen: false,
            maximumSurfaceWidth: 2560,
            maximumSurfaceHeight: 1440);

        Assert.Equal(960, extent.Width);
        Assert.True(extent.Height >= GalleryLayoutPolicy.MinimumPreviewHeight);
        Assert.Equal(GalleryLayoutPolicy.CompactToolsHeight,
            GalleryLayoutPolicy.ToolsHeight(GalleryLayoutMode.Compact, 640, 260));
    }

    [Fact]
    public void TinyInputsNeverProduceNegativePreviewExtents()
    {
        GalleryPreviewExtent extent = GalleryLayoutPolicy.PreviewExtent(
            GalleryLayoutMode.Compact, 120, 90, 290, 260, false, 2560, 1440);

        Assert.True(extent.Width > 0);
        Assert.True(extent.Height > 0);
        Assert.True(GalleryLayoutPolicy.ToolsHeight(GalleryLayoutMode.Compact, 90, 260) >= 0);
    }
}

public sealed class StoryMarkdownLayoutPolicyTests
{
    [Fact]
    public void UsesAvailableWidthUntilReadableMaximum()
    {
        StoryMarkdownLayout medium = StoryMarkdownLayoutPolicy.Calculate(640, 480);
        StoryMarkdownLayout wide = StoryMarkdownLayoutPolicy.Calculate(1280, 720);

        Assert.Equal(592, medium.ContentWidth);
        Assert.True(medium.ContentWidth < medium.ViewportWidth);
        Assert.Equal(StoryMarkdownLayoutPolicy.MaximumReadingWidth, wide.ContentWidth);
        Assert.True(wide.ContentWidth < wide.ViewportWidth);
    }

    [Fact]
    public void NarrowDocumentsStackNavigationWithoutOverflow()
    {
        StoryMarkdownLayout layout = StoryMarkdownLayoutPolicy.Calculate(420, 360);

        Assert.True(layout.StackNavigation);
        Assert.True(layout.ContentWidth <= layout.ViewportWidth);
        Assert.True(layout.EmbedWidth <= layout.ContentWidth);
        Assert.True(layout.NavigationButtonWidth <= layout.EmbedWidth);
    }

    [Fact]
    public void InvalidInputsUseDeterministicDefaults()
    {
        StoryMarkdownLayout layout = StoryMarkdownLayoutPolicy.Calculate(float.NaN, float.PositiveInfinity);

        Assert.Equal(StoryMarkdownLayoutPolicy.DefaultViewportWidth, layout.ViewportWidth);
        Assert.Equal(StoryMarkdownLayoutPolicy.DefaultViewportHeight, layout.ViewportHeight);
    }
}
