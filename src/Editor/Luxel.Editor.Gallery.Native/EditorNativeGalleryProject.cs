using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Editor.Gallery.Native;

public static class EditorNativeGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.NativeOnly("Editor", "Editor.Native");

    public static IServiceCollection AddEditorNativeGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        global::Luxel.Editor.Gallery.EditorGalleryProject.Register(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Editor_Gallery_Native.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
