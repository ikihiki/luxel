using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for rendering-course order and previous/next navigation.</summary>
internal static class RenderingCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Rendering/Basics/Overview", "Learn/Rendering/Basics/Environment",
        "Learn/Rendering/Basics/ClearColor", "Learn/Rendering/Basics/FirstTriangle",
        "Learn/Rendering/Basics/BuffersAndBindings", "Learn/Rendering/Basics/Shaders",
        "Learn/Rendering/Basics/FrameLoopAndSynchronization",
        "Learn/Rendering/ThreeD/Textures", "Learn/Rendering/ThreeD/TransformsAndCamera",
        "Learn/Rendering/ThreeD/DepthCullingLighting", "Learn/Rendering/ThreeD/FirstRenderGraph",
        "Learn/Rendering/ThreeD/StaticGltf", "Learn/Rendering/ThreeD/Debugging",
        "Learn/Rendering/ThreeD/Shipping",
        "Learn/Rendering/TwoD/Overview", "Learn/Rendering/TwoD/Paths",
        "Learn/Rendering/TwoD/Compositing", "Learn/Rendering/TwoD/Images",
        "Learn/Rendering/TwoD/Camera", "Learn/Rendering/TwoD/Backends",
        "Learn/Rendering/TwoD/RetainedCanvas", "Learn/Rendering/TwoD/IncrementalUpdates",
        "Learn/Rendering/RasterizerInternals/Overview", "Learn/Rendering/RasterizerInternals/Flattening",
        "Learn/Rendering/RasterizerInternals/SceneEncoding", "Learn/Rendering/RasterizerInternals/Abi",
        "Learn/Rendering/RasterizerInternals/Bounds", "Learn/Rendering/RasterizerInternals/TileBinning",
        "Learn/Rendering/RasterizerInternals/FineRaster", "Learn/Rendering/RasterizerInternals/ImagesAndComposite",
        "Learn/Rendering/RasterizerInternals/Dispatch", "Learn/Rendering/RasterizerInternals/RetainedUploads",
        "Learn/Rendering/RasterizerInternals/Validation",
    ];

    internal static readonly string[] ApplicationRoute = Routes
        .Skip(1)
        .TakeWhile(route => !route.StartsWith("Learn/Rendering/TwoD/", StringComparison.Ordinal))
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
