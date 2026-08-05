using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for rendering-course order and previous/next navigation.</summary>
internal static class RenderingCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Grapics/Overview", "Learn/Grapics/Environment",
        "Learn/Grapics/ClearColor", "Learn/Grapics/FirstTriangle",
        "Learn/Grapics/Buffers", "Learn/Grapics/Textures", "Learn/Grapics/Shaders",
        "Learn/Grapics/PipelineState", "Learn/Grapics/Synchronization", "Learn/Grapics/RenderGraph",
        "Learn/Grapics/2D/Overview", "Learn/Grapics/2D/Paths",
        "Learn/Grapics/2D/Compositing", "Learn/Grapics/2D/Images",
        "Learn/Grapics/2D/Camera", "Learn/Grapics/2D/Backends",
        "Learn/Grapics/2D/RetainedCanvas", "Learn/Grapics/2D/IncrementalUpdates",
        "Learn/Grapics/2D/Internal/Overview", "Learn/Grapics/2D/Internal/Flattening",
        "Learn/Grapics/2D/Internal/SceneEncoding", "Learn/Grapics/2D/Internal/Abi",
        "Learn/Grapics/2D/Internal/Bounds", "Learn/Grapics/2D/Internal/TileBinning",
        "Learn/Grapics/2D/Internal/FineRaster", "Learn/Grapics/2D/Internal/ImagesAndComposite",
        "Learn/Grapics/2D/Internal/Dispatch", "Learn/Grapics/2D/Internal/RetainedUploads",
        "Learn/Grapics/2D/Internal/Validation",
    ];

    internal static readonly string[] ApplicationRoute = Routes
        .Skip(1)
        .TakeWhile(route => !route.StartsWith("Learn/Grapics/2D/", StringComparison.Ordinal))
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
