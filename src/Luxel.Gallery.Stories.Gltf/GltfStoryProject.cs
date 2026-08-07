namespace Luxel.Gallery;

/// <summary>Native/heavy GLTF stories kept outside the CoreUi WebAssembly-safe catalog boundary.</summary>
public static class GltfStoryProject
{
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Stories_Gltf.Register(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
