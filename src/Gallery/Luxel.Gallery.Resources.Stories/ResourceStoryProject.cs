using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Gallery;

/// <summary>Resources、Assets、glTFのStoryをCoreUi WebAssembly-safe catalog境界の外側で集約する。</summary>
public static class ResourceStoryProject
{
    /// <summary>Resources、Assets、glTFのStoryをGeneric Hostのservice collectionへ追加する。</summary>
    public static IServiceCollection AddResourceStory(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Resources_Stories.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
