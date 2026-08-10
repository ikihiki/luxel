using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Resources.Gallery;

/// <summary>Resources、Assets、glTFのbrowser-safe Storyをカテゴリ所有のcatalogへ集約する。</summary>
public static class ResourceGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Resources", "Resources.Base");

    /// <summary>Resources、Assets、glTFのStoryをGeneric Hostのservice collectionへ追加する。</summary>
    public static IServiceCollection AddResourceGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Resources_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
