using Microsoft.Extensions.DependencyInjection;

namespace Luxel.DevTools.Gallery;

public static class DevToolsGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("DevTools", "DevTools.Base");

    public static IServiceCollection AddDevToolsGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        // No DevTools-specific authored stories remain in the central projects.
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
