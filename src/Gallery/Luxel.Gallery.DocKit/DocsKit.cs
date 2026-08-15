using Luxel.Controls;
using Luxel.Gallery;

namespace Luxel.Gallery.DocKit;

/// <summary>Gallery documentation Markdown generated from story metadata.</summary>
public static class DocsKit
{
    /// <summary>Formats the generated source of a registered story as a Markdown code fence.</summary>
    public static DocMarkdown StorySource(string path)
        => StoryRegistry.Find(path) is { Source.Length: > 0 } story
            ? new DocMarkdown($"```csharp\n{story.Source}\n```")
            : new DocMarkdown($"```\n(ソースなし: {path})\n```");

    /// <summary>Formats Learn-page metadata and previous/next navigation as Markdown.</summary>
    public static DocMarkdown RenderingMeta(
        string difficulty,
        string environment,
        string backend,
        string prerequisites,
        string? previous = null,
        string? next = null)
    {
        string navigation = previous is null && next is null ? "" : "\n\n"
            + (previous is null ? "" : $"**前へ:** [{previous.Split('/')[^1]}](story:{previous})")
            + (previous is not null && next is not null ? "　 " : "")
            + (next is null ? "" : $"**次:** [{next.Split('/')[^1]}](story:{next})");
        return new DocMarkdown($"**難易度:** {difficulty}　 **実行環境:** {environment}　 **Backend:** {backend}　 **前提知識:** {prerequisites}{navigation}");
    }
}
