using System.Text.RegularExpressions;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.Tests;

public sealed class InputControlDocsTests
{
    private static readonly string[] ControlNames =
    [
        "Button",
        "CheckBox",
        "ColorPicker",
        "LengthField",
        "RadioGroup",
        "SegmentedControl",
        "Select",
        "Slider",
        "Switch",
    ];

    [Fact]
    public void Input_docs_are_dense_decisive_and_link_every_canonical_story()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        GeneratedComponentStoryDescriptor[] descriptors = global::Luxel.UI.Gallery.UiGalleryProject.ProductionComponents
            .Where(static descriptor => descriptor.Category == "Input")
            .OrderBy(static descriptor => descriptor.ControlName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ControlNames.Order(StringComparer.Ordinal).ToArray(),
            descriptors.Select(static descriptor => descriptor.ControlName).ToArray());

        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors)
        {
            StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find(descriptor.DocsPath));
            using var context = new StoryContext();
            StoryResult result = docs.Build(context);

            Assert.Equal(StoryResultKind.Markdown, result.Kind);
            Assert.True(result.Markdown.Length >= 3_000,
                $"{descriptor.DocsPath} is not dense enough ({result.Markdown.Length} chars). ");
            Assert.Contains("**推奨:**", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("**非推奨:**", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("### 代替・関連コンポーネント", result.Markdown, StringComparison.Ordinal);
            Assert.Contains("フレームワーク水準のセマンティクス", result.Markdown, StringComparison.Ordinal);

            string prefix = descriptor.RoutePrefix + "/";
            string[] expectedRelated = catalog.All
                .Where(story => story.Path.StartsWith(prefix, StringComparison.Ordinal)
                    && story.Path != descriptor.DocsPath
                    && story.Path != descriptor.BasicPath)
                .Select(static story => story.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] linkedRelated = MarkdownDecorations.Links(result.Markdown)
                .Where(static link => link.Url.StartsWith("story:", StringComparison.Ordinal))
                .Select(static link => link.Url[6..])
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedRelated, linkedRelated);
            foreach (string path in linkedRelated)
            {
                Assert.NotNull(catalog.Find(path));
                Assert.Matches(
                    $@"- \[[^\]]+\]\(story:{Regex.Escape(path)}\) — .+",
                    result.Markdown);
            }
        }
    }

    [Fact]
    public void Input_docs_record_the_implemented_keyboard_contracts()
    {
        StoryCatalog catalog = global::Luxel.UI.Gallery.UiGalleryProject.CreateCatalog();
        var expectedKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Button"] = [],
            ["CheckBox"] = [],
            ["ColorPicker"] = ["`Enter` / `Space`", "`Escape`", "`Tab` / `Shift+Tab`", "`Left` / `Right`"],
            ["LengthField"] = ["`Enter` / `Space`", "`Up` / `Down`", "`Escape`", "`Home` / `End`"],
            ["RadioGroup"] = ["`Up`", "`Down`"],
            ["SegmentedControl"] = ["`Left`", "`Right`"],
            ["Select"] = ["`Enter` / `Space`", "`Up`", "`Down`", "`Escape`"],
            ["Slider"] = ["`Left`", "`Right`"],
            ["Switch"] = ["`Space` / `Enter`"],
        };

        foreach ((string control, string[] keys) in expectedKeys)
        {
            StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find($"Controls/Input/{control}/Docs"));
            using var context = new StoryContext();
            string markdown = docs.Build(context).Markdown;

            if (keys.Length == 0)
            {
                Assert.Contains("| — | 専用のキーボード割り当てはありません。 |", markdown,
                    StringComparison.Ordinal);
                continue;
            }

            foreach (string keysLabel in keys)
                Assert.Contains($"| {keysLabel} |", markdown, StringComparison.Ordinal);
        }

        string slider = BuildDocs(catalog, "Slider");
        Assert.Contains("`(Max - Min) / 20`", slider, StringComparison.Ordinal);
        Assert.DoesNotContain("`Step` パラメーター", BuildDocs(catalog, "LengthField"), StringComparison.Ordinal);
    }

    private static string BuildDocs(StoryCatalog catalog, string control)
    {
        StoryInfo docs = Assert.IsType<StoryInfo>(catalog.Find($"Controls/Input/{control}/Docs"));
        using var context = new StoryContext();
        return docs.Build(context).Markdown;
    }
}
