using System.Text.Json;
using Luxel.UI;
using Luxel.Gallery;
using static Luxel.Gallery.Story;

namespace Luxel.Tests;

public sealed class StoryResultTests
{
    [Fact]
    public void Interpolated_story_result_preserves_markdown_and_references()
    {
        StoryResult result = $$"""
            # Button

            {{StoryReference.To("Controls/Button/Playground", new { label = "Save", disabled = false })}}
            """;

        Assert.Equal(StoryResultKind.Markdown, result.Kind);
        StoryReference reference = Assert.Single(result.References);
        Assert.Equal("Controls/Button/Playground", reference.Path);
        Assert.Equal("Save", reference.Args.Values["label"].GetString());
        Assert.Contains("```luxel-story", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_story_ref_uses_the_canonical_story_reference_fence()
    {
        StoryResult result = $$"""
            # Embedded story

            {{StoryRef("Examples/Input/Actions", knobs: true)}}
            """;

        StoryReference reference = Assert.Single(result.References);
        Assert.Equal("Examples/Input/Actions", reference.Path);
        Assert.True(reference.ShowControls);
        Assert.Empty(result.Embeds);
        Assert.Contains("```luxel-story", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("```luxel-ui", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Toc_uses_a_canonical_markdown_placeholder_at_the_interpolation_position()
    {
        StoryResult result = $$"""
            # Guide

            before

            {{Toc()}}

            after
            """;

        int before = result.Markdown.IndexOf("before", StringComparison.Ordinal);
        int placeholder = result.Markdown.IndexOf("<!-- luxel-toc-placeholder -->", StringComparison.Ordinal);
        int after = result.Markdown.IndexOf("after", StringComparison.Ordinal);
        Assert.True(before < placeholder && placeholder < after);
        Assert.Empty(result.References);
        Assert.Empty(result.Embeds);
    }

    [Fact]
    public void Interpolated_story_result_preserves_markdown_fragments_and_widgets()
    {
        Widget widget = Luxel.Controls.Kit.Text("live");
        Luxel.Controls.DocMarkdown source = new("```csharp\nint x = 1;\n```");
        StoryResult result = $$"""
            # Direct document

            {{source}}

            {{widget}}
            """;

        Assert.Contains("int x = 1", result.Markdown, StringComparison.Ordinal);
        StoryMarkdownEmbed embed = Assert.Single(result.Embeds);
        Assert.Same(widget, embed.Widget);
        Assert.Contains("```luxel-ui", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Story_args_are_canonical_and_arg_seeds_first_build()
    {
        StoryArgs args = StoryArgs.Parse("{\"label\":\"Save\",\"count\":3}");
        var context = new StoryContext(args: args);

        Signal<string> label = context.Arg("label", "Button", new StoryArgOptions<string> { Description = "Text", Order = 10 });
        Signal<int> count = context.Arg("count", 0);

        Assert.Equal("Save", label.Value);
        Assert.Equal(3, count.Value);
        Assert.Equal(["count", "label"], args.Values.Keys);
        Assert.Equal(2, context.ArgDefinitions.Count);
        Assert.Equal("Text", context.ArgDefinitions.Single(definition => definition.Name == "label").Description);
        Assert.Equal(JsonValueKind.String, context.ArgDefinitions.Single(definition => definition.Name == "label").DefaultValue.ValueKind);
    }

    [Fact]
    public void Story_log_notifies_output_subscribers_with_the_recorded_entry()
    {
        var context = new StoryContext();
        StoryLogEntry observed = default;
        context.Logged += entry => observed = entry;

        context.Log("Button.Clicked");

        Assert.Equal(1, observed.Seq);
        Assert.Equal("Button.Clicked", observed.Message);
        Assert.Equal(observed, Assert.Single(context.LogSnapshot()));
    }

    [Fact]
    public void Catalogs_are_explicit_and_isolated()
    {
        var first = new StoryCatalogBuilder();
        var second = new StoryCatalogBuilder();
        first.Add(new StoryInfo("Test/One", _ => Luxel.Controls.Kit.Text("one")));

        StoryCatalog firstCatalog = first.Build();
        StoryCatalog secondCatalog = second.Build();

        Assert.NotNull(firstCatalog.Find("Test/One"));
        Assert.Null(secondCatalog.Find("Test/One"));
    }

    [Fact]
    public void Page_navigation_uses_catalog_order_within_the_same_group_and_skips_samples()
    {
        var builder = new StoryCatalogBuilder();
        var overview = new StoryInfo("Tutorials/3DApp/Overview", _ => StoryResult.FromMarkdown("# Overview"));
        var sample = new StoryInfo("Tutorials/3DApp/TriangleSample", _ => Luxel.Controls.Kit.Text("sample"),
            IncludeInPageNavigation: false);
        var firstFrame = new StoryInfo("Tutorials/3DApp/FirstFrame", _ => StoryResult.FromMarkdown("# First frame"));
        var finish = new StoryInfo("Tutorials/3DApp/Finish", _ => StoryResult.FromMarkdown("# Finish"));
        builder.Add(overview);
        builder.Add(sample);
        builder.Add(firstFrame);
        builder.Add(finish);
        builder.Add(new StoryInfo("Tutorials/UIApp/Overview", _ => StoryResult.FromMarkdown("# UI")));
        StoryCatalog catalog = builder.Build();

        StoryPageNavigation navigation = StoryPageNavigation.Resolve(catalog, firstFrame);

        Assert.Equal(overview.Path, navigation.Previous?.Path);
        Assert.Equal(finish.Path, navigation.Next?.Path);
    }

    [Fact]
    public void Page_navigation_hides_missing_sides_and_is_empty_for_excluded_stories()
    {
        var builder = new StoryCatalogBuilder();
        var first = new StoryInfo("Learn/Guide/First", _ => StoryResult.FromMarkdown("# First"));
        var last = new StoryInfo("Learn/Guide/Last", _ => StoryResult.FromMarkdown("# Last"));
        var sample = new StoryInfo("Learn/Guide/Sample", _ => Luxel.Controls.Kit.Text("sample"),
            IncludeInPageNavigation: false);
        builder.Add(first);
        builder.Add(last);
        builder.Add(sample);
        StoryCatalog catalog = builder.Build();

        StoryPageNavigation firstNavigation = StoryPageNavigation.Resolve(catalog, first);
        StoryPageNavigation lastNavigation = StoryPageNavigation.Resolve(catalog, last);

        Assert.Null(firstNavigation.Previous);
        Assert.Equal(last.Path, firstNavigation.Next?.Path);
        Assert.Equal(first.Path, lastNavigation.Previous?.Path);
        Assert.Null(lastNavigation.Next);
        Assert.True(StoryPageNavigation.Resolve(catalog, sample).IsEmpty);
    }
}
