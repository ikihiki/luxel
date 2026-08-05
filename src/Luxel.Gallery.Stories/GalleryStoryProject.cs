using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>現在の Gallery story assembly を明示的に composition root へ登録する entry point。</summary>
public static class GalleryStoryProject
{
    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        CoreUiStoryProject.Register(builder);

        var fullBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Gallery_Stories.Register(fullBuilder);
        Stories.DocsApi.RegisterReferenceStories(fullBuilder);
        StoryCatalog fullCatalog = fullBuilder.Build();
        foreach (StoryInfo story in fullCatalog.All)
        {
            // CoreUi owns every production component's exact canonical Overview/Basic fallback.
            // Other duplicates are composition errors rather than silently disappearing across projects.
            if (CoreUiStoryProject.IsProductionCanonicalPath(story.Path) || builder.ContainsPath(story.Path)) continue;
            builder.Add(story);
            if (story.Path.StartsWith("Learn/Graphics/", StringComparison.Ordinal))
                builder.AddAlias("Learn/Grapics/" + story.Path["Learn/Graphics/".Length..], story.Path);
        }
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
