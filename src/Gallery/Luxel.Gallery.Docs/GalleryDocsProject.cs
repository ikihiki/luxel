using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Gallery.Docs;

public static class GalleryDocsProject
{
    public static IServiceCollection AddGalleryDocs(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var docsBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Docs.Register(docsBuilder);
        foreach (StoryInfo story in docsBuilder.Build().All)
        {
            // Cross-cutting docs may demonstrate canonical component routes. The owning category
            // remains authoritative when that route is already composed.
            if (!builder.ContainsPath(story.Path)) builder.Add(story);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
