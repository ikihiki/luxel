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
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Editor_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
