using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for the RenderGraph course order and navigation.</summary>
internal static class RenderGraphCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/RenderGraph/Overview",
        "Learn/RenderGraph/Resources",
        "Learn/RenderGraph/Passes",
        "Learn/RenderGraph/Compilation",
        "Learn/RenderGraph/Lifecycle",
        "Learn/RenderGraph/Debugging",
    ];

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"RenderGraph course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return DocsKit.RenderingMeta(
            difficulty,
            "Standalone + DevTools",
            "Vulkan / DirectX 12",
            prerequisites,
            previous,
            next);
    }
}
