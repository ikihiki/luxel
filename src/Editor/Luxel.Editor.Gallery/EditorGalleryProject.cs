using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Editor.Gallery;

/// <summary>Owns the browser-safe authored editor and workbench stories.</summary>
public static class EditorGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Editor", "Editor.Base");

    public static IServiceCollection AddEditorGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        var categoryBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Editor_UI.Register(categoryBuilder);

        var authoredBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Editor_Gallery.Register(authoredBuilder);
        foreach (StoryInfo story in authoredBuilder.Build().All)
            categoryBuilder.Add(story, replaceGenerated: true);

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
