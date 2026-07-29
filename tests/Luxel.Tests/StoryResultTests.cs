using System.Text.Json;
using Luxel.UI;

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
