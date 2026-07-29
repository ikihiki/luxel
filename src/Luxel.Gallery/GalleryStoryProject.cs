using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>現在の Gallery story assembly を明示的に composition root へ登録する entry point。</summary>
public static class GalleryStoryProject
{
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Luxel.UI.Generated.StoryRegistration_Luxel_Gallery.Register(builder);
        Stories.DocsApi.RegisterReferenceStories(builder);
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
