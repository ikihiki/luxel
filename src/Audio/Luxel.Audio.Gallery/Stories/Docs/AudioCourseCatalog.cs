using Luxel.Controls;

namespace Luxel.Audio.Gallery;

/// <summary>Single source of truth for the ordered Audio course and strict previous/next navigation.</summary>
internal static class AudioCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Audio/Overview",
        "Learn/Audio/Environment",
        "Learn/Audio/Formats",
        "Learn/Audio/Voices",
        "Learn/Audio/Sources",
        "Learn/Audio/Spatial",
        "Learn/Audio/Streaming",
        "Learn/Audio/Testing",
    ];

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"Audio course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return global::Luxel.Gallery.DocKit.DocsKit.RenderingMeta(
            difficulty, environment, backend, prerequisites, previous, next);
    }
}
