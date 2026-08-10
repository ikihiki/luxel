using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Platform.Gallery.Native;

public static class PlatformNativeGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.NativeOnly("Platform", "Platform.Native");

    public static IServiceCollection AddPlatformNativeGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        global::Luxel.Platform.Gallery.PlatformGalleryProject.Register(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Platform_Gallery_Native.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
