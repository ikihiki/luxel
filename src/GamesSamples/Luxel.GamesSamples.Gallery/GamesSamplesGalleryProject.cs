using Microsoft.Extensions.DependencyInjection;

namespace Luxel.GamesSamples.Gallery;

public static class GamesSamplesGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("GamesSamples", "GamesSamples.Base");

    public static IServiceCollection AddGamesSamplesGallery(this IServiceCollection services) => services.AddStoryCatalog(Register);
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_GamesSamples_Gallery.Register(builder);
    }
    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
