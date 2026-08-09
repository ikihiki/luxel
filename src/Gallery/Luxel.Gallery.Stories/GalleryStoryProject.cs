using Microsoft.Extensions.DependencyInjection;
using Luxel.UI;
using Luxel.Resources.Gallery;

namespace Luxel.Gallery;

/// <summary>現在の Gallery story assembly を明示的に composition root へ登録する entry point。</summary>
public static class GalleryStoryProject
{
    /// <summary>Native Galleryの全StoryをGeneric Hostへ登録する。</summary>
    public static IServiceCollection AddGalleryStory(this IServiceCollection services)
        => services
            .AddStoryCatalog(RegisterGalleryOnly)
            .AddResourceGallery()
            .AddCoreUiStory();

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        RegisterGalleryOnly(builder);
        ResourceGalleryProject.Register(builder);
        CoreUiStoryProject.Register(builder);
    }

    private static void RegisterGalleryOnly(StoryCatalogBuilder builder)
    {
        var fullBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Stories.Register(fullBuilder);
        Stories.DocsApi.RegisterReferenceStories(fullBuilder);
        StoryCatalog fullCatalog = fullBuilder.Build();
        HashSet<string> coreUiPaths = CoreUiStoryProject.CreateCatalog().All
            .Select(story => story.Path)
            .ToHashSet(StringComparer.Ordinal);
        foreach (StoryInfo story in fullCatalog.All)
        {
            // CoreUi owns browser-safe routes. Native-only registration is emitted first for display order,
            // but overlapping routes are deferred to the later AddCoreUiStory registration.
            if (coreUiPaths.Contains(story.Path) || builder.ContainsPath(story.Path)) continue;
            builder.Add(story);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
