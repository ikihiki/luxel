using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for the ECS course order and previous/next navigation.</summary>
internal static class EcsCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/ECS/Overview",
        "Learn/ECS/WorldAndEntities",
        "Learn/ECS/ComponentsAndTags",
        "Learn/ECS/Queries",
        "Learn/ECS/SystemsAndPhases",
        "Learn/ECS/TransformHierarchy",
        "Learn/ECS/Interpolation",
        "Learn/ECS/Persistence",
        "Learn/ECS/Diagnostics",
        "Learn/ECS/Physics/Overview",
        "Learn/ECS/Physics/BodiesAndShapes",
        "Learn/ECS/Physics/FixedStep",
        "Learn/ECS/Physics/CollisionsAndTriggers",
        "Learn/ECS/Physics/MeshesAndRaycasts",
        "Learn/ECS/Physics/GizmosAndDebugging",
    ];

    internal static string RouteMarkdown()
    {
        return string.Join("\n", Routes.Select((route, index) =>
        {
            string label = route[(route.LastIndexOf('/') + 1)..];
            return $"{index + 1}. [{label}](story:{route})";
        }));
    }

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"ECS course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return DocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}
