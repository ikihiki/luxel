using System.Text.Json;
using Luxel.Gallery;
using Luxel.ValueDocument;
using Xunit;

namespace Luxel.Tests;

public sealed class StoryArgValueDocumentTests
{
    [Fact]
    public void Descriptor_maps_story_arg_metadata()
    {
        StoryArgDefinition definition = StoryArgDefinition.Create("graph", "NodeGraphDocument",
            Json("""{"nodes":[]}"""), description: "Graph JSON", order: 12, min: 1, max: 9, step: .5,
            options: ["\"small\"", "{\"nodes\":[]}"], editor: StoryArgEditorKind.Json);

        ValueDescriptor descriptor = StoryArgValueDescriptor.Create(definition);

        Assert.Equal(new DescriptorId("story-arg:graph"), descriptor.Id);
        Assert.Equal(ValueShape.Object, descriptor.Shape);
        Assert.Equal(ValueEditorKind.Json, descriptor.EditorKind);
        Assert.Equal("graph", descriptor.DisplayName);
        Assert.Equal("Graph JSON", descriptor.Description);
        Assert.Equal(12, descriptor.Order);
        Assert.Equal(new ValueNumericConstraint(1, 9, .5m), descriptor.Numeric);
        Assert.Equal(["small", "{\"nodes\":[]}"], descriptor.Options.Select(option => option.Label));
        Assert.Equal("NodeGraphDocument", descriptor.Annotations["gallery.type"]);
    }

    [Fact]
    public void Invalid_draft_isolated_then_valid_correction_commits_once()
    {
        StoryArgDefinition definition = JsonDefinition();
        var commits = new List<JsonElement>();
        var document = new StoryJsonArgDocument(definition, Json("""{"nodes":[1]}"""), value => commits.Add(value.Clone()));

        document.SetRawDraft("""{"nodes":[}""");
        Assert.False(document.Apply());

        Assert.True(document.IsDirty);
        Assert.True(document.IsInvalid);
        Assert.Empty(commits);
        Assert.NotNull(document.Diagnostic);
        Assert.True(document.Diagnostic!.Line >= 1);
        Assert.True(document.Diagnostic.Column >= 1);

        document.SetRawDraft("""{"nodes":[1,2]}""");
        Assert.True(document.Apply());
        Assert.True(document.Apply());

        JsonElement committed = Assert.Single(commits);
        Assert.Equal(2, committed.GetProperty("nodes").GetArrayLength());
        Assert.False(document.IsDirty);
        Assert.False(document.IsInvalid);
    }

    [Fact]
    public void Formatting_invalid_text_does_not_commit_and_discard_restores_accepted_text()
    {
        StoryArgDefinition definition = JsonDefinition();
        int commits = 0;
        var document = new StoryJsonArgDocument(definition, Json("""{"nodes":[1]}"""), _ => commits++);

        document.SetRawDraft("""{"nodes":[}""");
        Assert.False(document.Format(indented: true));
        Assert.Equal(0, commits);
        Assert.True(document.IsInvalid);

        document.Discard();

        Assert.Equal("{\"nodes\":[1]}", document.Text);
        Assert.False(document.IsDirty);
        Assert.False(document.IsInvalid);
    }

    [Fact]
    public void External_refresh_replaces_clean_draft_but_preserves_dirty_draft_as_conflict()
    {
        StoryArgDefinition definition = JsonDefinition();
        var document = new StoryJsonArgDocument(definition, Json("""{"nodes":[1]}"""), _ => { });

        Assert.True(document.RefreshAccepted(Json("""{"nodes":[1,2]}"""), "2"));
        Assert.Equal("{\"nodes\":[1,2]}", document.Text);

        document.SetRawDraft("""{"nodes":[3]}""");
        Assert.False(document.RefreshAccepted(Json("""{"nodes":[4]}"""), "3"));
        Assert.Equal("{\"nodes\":[3]}", document.Text);
        Assert.True(document.IsInvalid);
        Assert.Equal("external-conflict", document.Diagnostic!.Source);
    }

    private static StoryArgDefinition JsonDefinition()
        => new("graph", "json", Json("""{"nodes":[1]}"""), Editor: StoryArgEditorKind.Json);

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
