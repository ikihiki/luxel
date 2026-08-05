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
        "Learn/Grapics/PipelineState", "Learn/Grapics/Fence", "Learn/Grapics/RenderGraph",
        "Learn/Grapics/ThreeD/Textures", "Learn/Grapics/ThreeD/TransformsAndCamera",
        "Learn/Grapics/ThreeD/DepthCullingLighting",
        "Learn/Grapics/ThreeD/StaticGltf", "Learn/Grapics/ThreeD/Debugging",
        "Learn/Grapics/ThreeD/Shipping",
        "Learn/Grapics/TwoD/Overview", "Learn/Grapics/TwoD/Paths",
        "Learn/Grapics/TwoD/Compositing", "Learn/Grapics/TwoD/Images",
        "Learn/Grapics/TwoD/Camera", "Learn/Grapics/TwoD/Backends",
        "Learn/Grapics/TwoD/RetainedCanvas", "Learn/Grapics/TwoD/IncrementalUpdates",
        "Learn/Grapics/RasterizerInternals/Overview", "Learn/Grapics/RasterizerInternals/Flattening",
        "Learn/Grapics/RasterizerInternals/SceneEncoding", "Learn/Grapics/RasterizerInternals/Abi",
        "Learn/Grapics/RasterizerInternals/Bounds", "Learn/Grapics/RasterizerInternals/TileBinning",
        "Learn/Grapics/RasterizerInternals/FineRaster", "Learn/Grapics/RasterizerInternals/ImagesAndComposite",
        "Learn/Grapics/RasterizerInternals/Dispatch", "Learn/Grapics/RasterizerInternals/RetainedUploads",
        "Learn/Grapics/RasterizerInternals/Validation",
    ];

    internal static readonly string[] ApplicationRoute = Routes
        .Skip(1)
        .TakeWhile(route => !route.StartsWith("Learn/Grapics/TwoD/", StringComparison.Ordinal))
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
