using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Resources.Gallery;

/// <summary>Resources、Assets、glTFのStoryをCoreUi WebAssembly-safe catalog境界の外側で集約する。</summary>
public static class ResourceGalleryProject
{
    /// <summary>Resources、Assets、glTFのStoryをGeneric Hostのservice collectionへ追加する。</summary>
    public static IServiceCollection AddResourceGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Resources_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
