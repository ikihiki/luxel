using System.Text.Json;
using Luxel.UI;
using Luxel.Gallery;

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
        first.Add(new StoryInfo("Test/One", 10, 10, null, _ => Luxel.Controls.Kit.Text("one")));

        StoryCatalog firstCatalog = first.Build();
        StoryCatalog secondCatalog = second.Build();

        Assert.NotNull(firstCatalog.Find("Test/One"));
        Assert.Null(secondCatalog.Find("Test/One"));
    }
}
