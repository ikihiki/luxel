using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for rendering-course order and previous/next navigation.</summary>
internal static class RenderingCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Graphics/Overview", "Learn/Graphics/Environment",
        "Learn/Graphics/ClearColor", "Learn/Graphics/FirstTriangle",
        "Learn/Graphics/Buffers", "Learn/Graphics/Textures", "Learn/Graphics/Shaders",
        "Learn/Graphics/PipelineState", "Learn/Graphics/Synchronization",
        "Learn/Graphics/2D/Overview", "Learn/Graphics/2D/Paths",
        "Learn/Graphics/2D/Compositing", "Learn/Graphics/2D/Images",
        "Learn/Graphics/2D/Camera", "Learn/Graphics/2D/Backends",
        "Learn/Graphics/2D/IncrementalUpdates",
        "Learn/Graphics/2D/Internal/Overview", "Learn/Graphics/2D/Internal/Flattening",
        "Learn/Graphics/2D/Internal/SceneEncoding", "Learn/Graphics/2D/Internal/Abi",
        "Learn/Graphics/2D/Internal/Bounds", "Learn/Graphics/2D/Internal/TileBinning",
        "Learn/Graphics/2D/Internal/FineRaster", "Learn/Graphics/2D/Internal/ImagesAndComposite",
        "Learn/Graphics/2D/Internal/Dispatch", "Learn/Graphics/2D/Internal/RetainedUploads",
        "Learn/Graphics/2D/Internal/Validation",
    ];

    internal static readonly string[] ApplicationRoute = Routes
        .Skip(1)
        .TakeWhile(route => !route.StartsWith("Learn/Graphics/2D/", StringComparison.Ordinal))
        .ToArray();

    internal static string ApplicationRouteMarkdown()
    {
        var lines = ApplicationRoute.Select((route, index) =>
        {
            string label = route[(route.LastIndexOf('/') + 1)..];
            return $"{index + 1}. [{label}](story:{route})";
        });
        return string.Join("\n", lines.Append($"{ApplicationRoute.Length + 1}. [Gallery Triangle](story:Examples/3D/Triangle)"));
    }

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"Rendering course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return DocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}
