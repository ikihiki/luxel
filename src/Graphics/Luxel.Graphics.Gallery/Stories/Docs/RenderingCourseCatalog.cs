using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for rendering-course order and previous/next navigation.</summary>
internal static class RenderingCourseCatalog
{
    internal static readonly string[] GraphicsRoutes =
    [
        "Learn/Graphics/Overview", "Learn/Graphics/Environment",
        "Learn/Graphics/ClearColor", "Learn/Graphics/FirstTriangle",
        "Learn/Graphics/Buffers", "Learn/Graphics/TexturesBasics", "Learn/Graphics/Shaders",
        "Learn/Graphics/PipelineState", "Learn/Graphics/Synchronization",
    ];

    internal static readonly string[] BackendInternalRoutes =
    [
        "Learn/Graphics/Internal/DirectX12", "Learn/Graphics/Internal/Vulkan",
        "Learn/Graphics/Internal/WebGpu",
    ];

    internal static readonly string[] TwoDRoutes =
    [
        "Learn/Graphics/First2DScene", "Learn/Graphics/2D/Paths",
        "Learn/Graphics/2D/Compositing", "Learn/Graphics/2D/Images",
        "Learn/Graphics/2D/Camera", "Learn/Graphics/2D/Backends",
        "Learn/Graphics/2D/IncrementalUpdates",
    ];

    internal static readonly string[] RasterizerInternalRoutes =
    [
        "Learn/Graphics/2D/Internal/Overview", "Learn/Graphics/2D/Internal/Flattening",
        "Learn/Graphics/2D/Internal/SceneEncoding", "Learn/Graphics/2D/Internal/Abi",
        "Learn/Graphics/2D/Internal/Bounds", "Learn/Graphics/2D/Internal/TileBinning",
        "Learn/Graphics/2D/Internal/FineRaster", "Learn/Graphics/2D/Internal/ImagesAndComposite",
        "Learn/Graphics/2D/Internal/Dispatch", "Learn/Graphics/2D/Internal/RetainedUploads",
        "Learn/Graphics/2D/Internal/Validation",
    ];

    internal static readonly string[] RenderGraphRoutes =
    [
        "Learn/Graphics/RenderGraph/Overview", "Learn/Graphics/RenderGraph/Resources",
        "Learn/Graphics/RenderGraph/Passes", "Learn/Graphics/RenderGraph/Compilation",
        "Learn/Graphics/RenderGraph/Lifecycle", "Learn/Graphics/RenderGraph/Debugging",
    ];

    internal static readonly string[] Routes =
    [
        .. GraphicsRoutes,
        .. TwoDRoutes,
        .. RenderGraphRoutes,
        .. BackendInternalRoutes,
        .. RasterizerInternalRoutes,
    ];

    internal static string ApplicationRouteMarkdown()
    {
        var lines = GraphicsRoutes.Skip(1).Select((route, index) =>
        {
            string label = route[(route.LastIndexOf('/') + 1)..];
            return $"{index + 1}. [{label}](story:{route})";
        });
        return string.Join("\n", lines.Append($"{GraphicsRoutes.Length}. [Gallery Triangle](story:Learn/Graphics/TriangleSample)"));
    }

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        string[][] courses = [GraphicsRoutes, TwoDRoutes, RenderGraphRoutes, BackendInternalRoutes, RasterizerInternalRoutes];
        string[]? course = courses.FirstOrDefault(routes => Array.IndexOf(routes, path) >= 0);
        if (course is null) throw new InvalidOperationException($"Rendering course route is not registered: {path}");
        int index = Array.IndexOf(course, path);
        return (index > 0 ? course[index - 1] : null, index + 1 < course.Length ? course[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return global::Luxel.Gallery.DocKit.DocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}
