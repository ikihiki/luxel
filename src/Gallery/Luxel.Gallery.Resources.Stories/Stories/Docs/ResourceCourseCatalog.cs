using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for the Resources Learn order and previous/next navigation.</summary>
internal static class ResourceCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Resources/Overview",
        "Learn/Resources/LoadingAndHandles",
        "Learn/Resources/SourcesAndUris",
        "Learn/Resources/Steps",
        "Learn/Resources/RegistrationAndComposition",
        "Learn/Resources/PipelinesAndDag",
        "Learn/Resources/ScopesAndOwnership",
        "Learn/Resources/ReloadAndLifetime",
        "Learn/Resources/Assets/Overview",
        "Learn/Resources/Assets/DocumentAndSceneGraph",
        "Learn/Resources/Assets/MeshesAndPrimitives",
        "Learn/Resources/Assets/MaterialsTexturesAndSamplers",
        "Learn/Resources/Assets/AnimationSkinCameraAndLight",
        "Learn/Resources/Assets/LoadingAndGpu",
        "Learn/Resources/Assets/ShaderAbi",
        "Learn/Resources/Gltf/Overview",
        "Learn/Resources/Gltf/RegistrationAndLoading",
        "Learn/Resources/Gltf/ExternalBuffersImagesAndUris",
        "Learn/Resources/Gltf/ValidationAndDiagnostics",
        "Learn/Resources/Gltf/SceneRuntime",
        "Learn/Resources/Gltf/AnimationSkinningAndMorph",
        "Learn/Resources/Gltf/ReloadAndLifetime",
    ];

    internal static string LearningRouteMarkdown()
    {
        var lines = Routes.Skip(1).Select((route, index) =>
        {
            string label = route[(route.LastIndexOf('/') + 1)..];
            return $"{index + 1}. [{label}](story:{route})";
        });
        return string.Join("\n", lines);
    }

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"Resources course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return ResourceDocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}
