using Microsoft.Extensions.DependencyInjection;
using Luxel.Resources.Gallery;
using Luxel.Audio.Gallery;
using Luxel.UI.Gallery;
using Luxel.Graphics.Gallery;
using Luxel.Input.Gallery;
using Luxel.Framework.Gallery;
using Luxel.Animation.Gallery;
using Luxel.Particles.Gallery;
using Luxel.Scripting.Gallery.Native;
using Luxel.Editor.Gallery.Native;
using Luxel.DevTools.Gallery;
using Luxel.GamesSamples.Gallery;
using Luxel.Gallery.Docs;
using Luxel.Platform.Gallery.Native;

namespace Luxel.Gallery;

/// <summary>Compatibility aggregate for the explicit category-owned Gallery catalogs.</summary>
public static class GalleryStoryProject
{
    public static IServiceCollection AddGalleryStory(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Merge(ResourceGalleryProject.Register);
        Merge(AudioGalleryProject.Register);
        Merge(UiGalleryProject.Register);
        Merge(GraphicsGalleryProject.Register);
        Merge(InputGalleryProject.Register);
        Merge(FrameworkGalleryProject.Register);
        Merge(AnimationGalleryProject.Register);
        Merge(ParticlesGalleryProject.Register);
        // Native extension catalogs include their same-category browser base.
        Merge(ScriptingNativeGalleryProject.Register);
        Merge(EditorNativeGalleryProject.Register);
        Merge(DevToolsGalleryProject.Register);
        Merge(GamesSamplesGalleryProject.Register);
        Merge(GalleryDocsProject.Register);
        // Native is an extension catalog and already includes PlatformGalleryProject.
        Merge(PlatformNativeGalleryProject.Register);

        void Merge(Action<StoryCatalogBuilder> register)
        {
            var category = new StoryCatalogBuilder();
            register(category);
            foreach (StoryInfo story in category.Build().All)
                builder.Add(story, replaceGenerated: true);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
