using Luxel.Gallery;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.Settings;

namespace Luxel.Gallery.Native.Tests;

public sealed class GalleryChromeThemeTests
{
    [Fact]
    public void LightAndDarkThemesExposeDistinctExpectedTokens()
    {
        Theme light = GalleryChromeTheme.Create(GalleryThemeMode.Light);
        Theme dark = GalleryChromeTheme.Create(GalleryThemeMode.Dark);
        GalleryChromeTokens lightChrome = GalleryChromeTheme.Tokens(GalleryThemeMode.Light);
        GalleryChromeTokens darkChrome = GalleryChromeTheme.Tokens(GalleryThemeMode.Dark);

        Assert.Equal(Color2D.Rgba(0xf4, 0xf6, 0xf9), light.Background);
        Assert.Equal(Color2D.Rgba(0x1e, 0x25, 0x30), light.Text);
        Assert.Equal(Color2D.Rgba(0x0b, 0x10, 0x17), dark.Background);
        Assert.Equal(Color2D.Rgba(0xe5, 0xed, 0xf7), dark.Text);
        Assert.Equal(Color2D.Rgba(0xdb, 0xe8, 0xfc), lightChrome.AccentSoft);
        Assert.Equal(Color2D.Rgba(0x18, 0x34, 0x5c), darkChrome.AccentSoft);
        Assert.NotEqual(lightChrome.Search, darkChrome.Search);
        Assert.NotEqual(lightChrome.PanelCode, darkChrome.PanelCode);
    }

    [Fact]
    public void AppearanceSettingsPersistAndSynchronizationFollowsShell()
    {
        var files = new InMemoryFileStore();
        var state = new GalleryAppearanceState(files);

        Assert.Equal(GalleryThemeMode.Dark, state.ShellTheme.Peek());
        Assert.Equal(GalleryThemeMode.Light, state.PreviewTheme.Peek());
        Assert.False(state.SynchronizePreview.Peek());

        state.ToggleSynchronization();
        Assert.True(state.SynchronizePreview.Peek());
        Assert.Equal(GalleryThemeMode.Dark, state.PreviewTheme.Peek());
        state.ToggleShellTheme();
        Assert.Equal(GalleryThemeMode.Light, state.ShellTheme.Peek());
        Assert.Equal(GalleryThemeMode.Light, state.PreviewTheme.Peek());

        var reloaded = new GalleryAppearanceState(files);
        Assert.Equal(GalleryThemeMode.Light, reloaded.ShellTheme.Peek());
        Assert.Equal(GalleryThemeMode.Light, reloaded.PreviewTheme.Peek());
        Assert.True(reloaded.SynchronizePreview.Peek());
    }
}

public sealed class NativeGalleryLabelTests
{
    [Fact]
    public void ShellAndStateSummariesAreJapaneseFirst()
    {
        Assert.Equal("ストーリー", NativeGalleryLabels.Stories);
        Assert.Equal("プレビュー", NativeGalleryLabels.Preview);
        Assert.Equal("引数", NativeGalleryLabels.Arguments);
        Assert.Equal("出力", NativeGalleryLabels.Output);
        Assert.Equal("ソース", NativeGalleryLabels.Source);
        Assert.Contains("読み込", NativeGalleryLabels.LoadingSummary);
        Assert.Contains("注意", NativeGalleryLabels.WarningSummary);
        Assert.Contains("エラー", NativeGalleryLabels.ErrorSummary);
        Assert.Equal("引数", NativeRenderingLabels.Arguments);
        Assert.Equal("ソースを表示できません。", NativeRenderingLabels.SourceUnavailable);
    }

    [Fact]
    public void NavigationLabelsTranslateShellTermsButPreserveApiIdentifiers()
    {
        Assert.Equal("ドキュメント", NativeGalleryLabels.NavigationSegment("Docs"));
        Assert.Equal("プレイグラウンド", NativeGalleryLabels.NavigationSegment("Playground"));
        Assert.Equal("アクセシビリティ", NativeGalleryLabels.NavigationSegment("Accessibility"));
        Assert.Equal("Button", NativeGalleryLabels.NavigationSegment("Button"));
        Assert.Contains("Controls/Button/Basic", NativeRenderingLabels.StoryNotFound("Controls/Button/Basic"));
    }

    [Fact]
    public void ThemeButtonsDescribeCurrentModesWithoutEnglishShellLabels()
    {
        string shell = NativeGalleryLabels.ShellThemeButton(GalleryThemeMode.Dark);
        string preview = NativeGalleryLabels.PreviewThemeButton(GalleryThemeMode.Light);

        Assert.Equal("画面: ダーク", shell);
        Assert.Equal("プレビュー: ライト", preview);
        Assert.DoesNotContain("Dark", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Light", preview, StringComparison.Ordinal);
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
