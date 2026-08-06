using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>Single source of truth for Animation course order and previous/next navigation.</summary>
internal static class AnimationCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Animation/Overview",
        "Learn/Animation/CurvesAndTweens",
        "Learn/Animation/PlayerAndTiming",
        "Learn/Animation/SequenceAndParallel",
        "Learn/Animation/ClipsAndTracks",
        "Learn/Animation/TargetsAndBindings",
        "Learn/Animation/GraphsAndBlending",
        "Learn/Animation/StateMachines",
        "Learn/Animation/ImportAndDebugging",
        "Learn/Animation/Particles/Overview",
        "Learn/Animation/Particles/ValuesAndConfiguration",
        "Learn/Animation/Particles/EmissionAndSimulation",
        "Learn/Animation/Particles/ForcesAndDeterminism",
        "Learn/Animation/Particles/Rendering2DAndUI",
        "Learn/Animation/Particles/Rendering3D",
        "Learn/Animation/Particles/ResourcesAndDebugging",
    ];

    internal static readonly string[] ParticleRoutes = Routes[9..];

    internal static string LearningRouteMarkdown()
        => string.Join("\n", Routes.Skip(1).Select((route, index) =>
            $"{index + 1}. [{route[(route.LastIndexOf('/') + 1)..]}](story:{route})"));

    internal static string ParticleRouteMarkdown()
        => string.Join("\n", ParticleRoutes.Skip(1).Select((route, index) =>
            $"{index + 1}. [{route[(route.LastIndexOf('/') + 1)..]}](story:{route})"));

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"Animation course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        return DocsKit.RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}
