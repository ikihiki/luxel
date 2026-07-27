using System.Runtime.CompilerServices;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Renamed story routes kept for documentation links, bookmarks, and automation compatibility.</summary>
internal static class StoryAliases
{
    [ModuleInitializer]
    internal static void Register()
    {
        Stories.DocsApi.RegisterReferenceProvider();
        StoryRegistry.RegisterAlias("Demos/TwoD/CameraRig", "Demos/2D/CameraRig");
        StoryRegistry.RegisterAlias("Demos/TwoD/Sprites", "Demos/2D/Sprites");
        StoryRegistry.RegisterAlias("Demos/TwoD/Tilemap", "Demos/2D/Tilemap");
        StoryRegistry.RegisterAlias("Demos/TwoD/Particles", "Demos/2D/Particles");
        StoryRegistry.RegisterAlias("Demos/TwoD/ParticleView", "Demos/2D/ParticleView");
        StoryRegistry.RegisterAlias("Demos/TwoD/Gizmos2D", "Demos/2D/Gizmos2D");
    }
}
