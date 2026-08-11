using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Scripting.Gallery.Native;

public static class ScriptingNativeGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.NativeOnly("Scripting", "Scripting.Native");

    public static IServiceCollection AddScriptingNativeGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        global::Luxel.Scripting.Gallery.ScriptingGalleryProject.Register(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Scripting_Gallery_Native.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
