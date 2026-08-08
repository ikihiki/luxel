using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Resources Story assembly内の埋め込みsampleと学習メタデータを扱う専用helper。</summary>
internal static class ResourceDocsKit
{
    internal static DocMarkdown SampleSource(string relativePath, string? region = null, string? language = null)
        => global::Luxel.Gallery.DocKit.DocsKit.SampleSource(
            typeof(ResourceDocsKit).Assembly, relativePath, region, language);

    internal static DocMarkdown SampleBundle(string id)
        => global::Luxel.Gallery.DocKit.DocsKit.SampleBundle(typeof(ResourceDocsKit).Assembly, id);

    internal static DocMarkdown RenderingMeta(string difficulty, string environment, string backend, string prerequisites,
        string? previous = null, string? next = null)
        => global::Luxel.Gallery.DocKit.DocsKit.RenderingMeta(
            difficulty, environment, backend, prerequisites, previous, next);
}
