using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Graphics.Gallery;

/// <summary>Owns the browser-safe Graphics, RenderGraph, 2D, shader, backend, and typography gallery catalog.</summary>
public static class GraphicsGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Graphics", "Graphics.Base");

    public static IServiceCollection AddGraphicsGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Graphics_Gallery.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
