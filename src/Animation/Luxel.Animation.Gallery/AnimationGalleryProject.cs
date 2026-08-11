using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Animation.Gallery;

public static class AnimationGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Animation", "Animation.Base");

    public static IServiceCollection AddAnimationGallery(this IServiceCollection services) => services.AddStoryCatalog(Register);
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Animation_Gallery.Register(builder);
    }
    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
