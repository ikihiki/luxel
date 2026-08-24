using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Gallery.UI;
using Luxel.UI.Gallery;

namespace Luxel.Tests;

public sealed class OverlayControlDocsTests
{
    [Theory]
    [InlineData("Dialog", "focusable content がない場合", "`Margin = 16`", "`Z = 1000`", "dialog role")]
    [InlineData("Drawer", "`OverlayPlacement.RightEdge`", "viewport 全高", "focusable content がなければ", "右端")]
    [InlineData("Dropdown", "`Opened.Value`", "現行 `Button` と `MenuRow` は focus target を登録しない", "open 時に評価", "Action が例外")]
    [InlineData("MenuRow", "`UiEvent<MenuRow>`", "`Enabled` を false", "80 ms", "wrap/ellipsis")]
    [InlineData("Popover", "open の瞬間に有効", "`Flip = true`", "global focusables", "light-dismiss に使ったクリックは背面へ通しません")]
    [InlineData("Toast", "`DismissOnOutside = false`", "複数 Toast", "live-region", "top modal 外")]
    [InlineData("Tooltip", "`child` と `text`", "最も深い hit target", "delay や fade animation はありません", "`SemanticNode.Description`")]
    public void Overlay_docs_capture_verified_control_and_host_contracts(
        string control,
        string first,
        string second,
        string third,
        string fourth)
    {
        StoryResult result = Render(control);

        Assert.True(result.Markdown.Length >= 3_200,
            $"Controls/Overlay/{control}/Docs is too sparse ({result.Markdown.Length} characters).");
        Assert.Contains(first, result.Markdown, StringComparison.Ordinal);
        Assert.Contains(second, result.Markdown, StringComparison.Ordinal);
        Assert.Contains(third, result.Markdown, StringComparison.Ordinal);
        Assert.Contains(fourth, result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_docs_use_canonical_basic_playground_and_resolvable_related_story_paths()
    {
        StoryCatalog catalog = UiGalleryProject.CreateCatalog();
        GeneratedComponentStoryDescriptor[] descriptors = UiGalleryProject.ProductionComponents
            .Where(static descriptor => descriptor.Category == "Overlay")
            .OrderBy(static descriptor => descriptor.ControlName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Dialog", "Drawer", "Dropdown", "MenuRow", "Popover", "Toast", "Tooltip"],
            descriptors.Select(static descriptor => descriptor.ControlName));

        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors)
        {
            StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find(descriptor.DocsPath));
            using var context = new StoryContext();
            StoryResult result = docs.Build(context);

            Assert.Equal(descriptor.BasicPath, Assert.Single(result.References).Path);
            string[] related = MarkdownDecorations.Links(result.Markdown)
                .Where(static link => link.Url.StartsWith("story:", StringComparison.Ordinal))
                .Select(static link => link.Url[6..])
                .ToArray();
            Assert.Single(related, path => path == descriptor.PlaygroundPath);
            Assert.Equal(related.Length, related.Distinct(StringComparer.Ordinal).Count());
            Assert.All(related, path => Assert.Equal(path, Assert.IsType<StoryInfo>(catalog.Find(path)).Path));
        }

        Assert.Contains("Tutorials/UIApp/DialogSample",
            MarkdownDecorations.Links(Render("Dialog").Markdown)
                .Select(static link => link.Url)
                .Where(static url => url.StartsWith("story:", StringComparison.Ordinal))
                .Select(static url => url[6..]),
            StringComparer.Ordinal);
    }

    private static StoryResult Render(string control)
    {
        StoryCatalog catalog = UiGalleryProject.CreateCatalog();
        StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find($"Controls/Overlay/{control}/Docs"));
        using var context = new StoryContext();
        return docs.Build(context);
    }
}
