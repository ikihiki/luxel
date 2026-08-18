using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Editor.Gallery;

/// <summary>Owns the browser-safe authored editor and workbench stories.</summary>
public static class EditorGalleryProject
{
    public static StoryOwnership Ownership { get; } = StoryOwnership.BrowserSafe("Editor", "Editor.Base");

    public static int ProductionComponentCount => ProductionComponents.Count;
    public static IReadOnlyList<GeneratedComponentStoryDescriptor> ProductionComponents
        => Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Editor_UI.Descriptors;

    public static IServiceCollection AddEditorGallery(this IServiceCollection services)
        => services.AddStoryCatalog(Register);

    public static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        using IDisposable ownership = builder.BeginOwnership(Ownership);
        var categoryBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Editor_UI.Register(categoryBuilder);
        global::Luxel.Editor.Gallery.EditorControlDocs.Register(categoryBuilder, ProductionComponents);
        global::Luxel.Editor.Gallery.Stories.Docs.EditorControlDocsApi.Register(categoryBuilder);

        var authoredBuilder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.StoryRegistration_Luxel_Editor_Gallery.Register(authoredBuilder);
        foreach (StoryInfo story in authoredBuilder.Build().All)
            categoryBuilder.Add(story, replaceGenerated: true);

        StoryCatalog catalog = categoryBuilder.Build();
        foreach (StoryInfo story in catalog.All)
            builder.Add(story, replaceGenerated: true);
        ControlStoryAliases.Add(builder, catalog, ProductionComponents);
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/DocumentTabsBasic", "Controls/Collections/DocumentTabs/Basic");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/DockHostBasic", "Controls/Editor/DockHost/Basic");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/DockHostFloating", "Controls/Editor/DockHost/Examples/Floating");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/PropertyGridBasic", "Controls/Editor/PropertyGrid/Basic");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/AssetBrowserBasic", "Controls/Collections/AssetBrowser/Basic");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/StatusBarBasic", "Controls/Editor/StatusBar/Basic");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/CommandPalette/Docs", "Controls/Editor/CommandPalette/Docs");
        ControlStoryAliases.AddIfHidden(builder, catalog, "Controls/CommandPalette/Basic", "Controls/Editor/CommandPalette/Basic");
    }

    public static StoryCatalog CreateProductionCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Luxel.Gallery.Generated.ComponentStoryRegistration_Luxel_Editor_UI.Register(builder);
        return builder.Build();
    }

    public static StoryCatalog CreateCatalog()
    {
        var builder = new StoryCatalogBuilder();
        Register(builder);
        return builder.Build();
    }
}
