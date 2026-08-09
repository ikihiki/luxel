using Luxel.Controls;
using Luxel.Gallery.DocKit;

namespace Luxel.Gallery.Stories;

internal static class InputDocsKit
{
    internal static DocEmbed StoryRef(StoryContext ctx, string path, bool knobs = false)
        => DocsKit.StoryRef(ctx, path, knobs);

    internal static DocMarkdown SampleSource(string relativePath, string? region = null, string? language = null)
        => DocsKit.SampleSource(typeof(InputDocsKit).Assembly, relativePath, region, language, searchLoadedAssemblies: true);

    internal static DocMarkdown RenderingMeta(string difficulty, string environment, string backend, string prerequisites, string? previous = null, string? next = null)
        => DocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
}
