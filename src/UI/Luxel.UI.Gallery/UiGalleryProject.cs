using Microsoft.Extensions.DependencyInjection;

namespace Luxel.UI.Gallery;

/// <summary>Owns the browser-safe authored control and UI pattern stories.</summary>
public static class UiGalleryProject
{
    public static IServiceCollection AddUiGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var categoryBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_UI_Gallery.Register(categoryBuilder);
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_UI_Gallery.Register(categoryBuilder);
        foreach (StoryInfo story in categoryBuilder.Build().All)
            builder.Add(story, replaceGenerated: true);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
