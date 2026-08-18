namespace Luxel.Editor.Gallery.Stories.Docs;

/// <summary>Registers Editor-owned non-production control guides.</summary>
internal static class EditorControlDocsApi
{
    internal static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(new StoryInfo("Controls/Editor/CommandPalette/Docs", static _ => $$"""
            # CommandPalette

            `CommandPalette` は Editor command registry を検索して実行するための非 `[UiComponent]` guide です。
            実例は [Basic](story:Controls/Editor/CommandPalette/Basic) を参照してください。
            """, Source: "Authored Editor command guide."));
    }
}
