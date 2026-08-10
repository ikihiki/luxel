using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Editor.Gallery;

/// <summary>Owns the browser-safe authored editor, workbench, and Studio stories.</summary>
public static class EditorGalleryProject
{
    public static IServiceCollection AddEditorGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var categoryBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Editor_Gallery.Register(categoryBuilder);
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
