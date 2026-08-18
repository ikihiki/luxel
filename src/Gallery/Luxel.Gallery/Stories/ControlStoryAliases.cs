namespace Luxel.Gallery;

/// <summary>Registers hidden compatibility routes for pre-taxonomy control story paths.</summary>
public static class ControlStoryAliases
{
    public static void Add(StoryCatalogBuilder builder, StoryCatalog catalog,
        IEnumerable<GeneratedComponentStoryDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(descriptors);

        foreach (GeneratedComponentStoryDescriptor descriptor in descriptors.Where(static value => value.IsUserFacing))
        {
            string[] legacyNames = LegacyNames(descriptor.ControlName);
            foreach (StoryInfo story in catalog.All.Where(story =>
                         story.Path.StartsWith(descriptor.RoutePrefix + "/", StringComparison.Ordinal)))
            {
                string suffix = story.Path[(descriptor.RoutePrefix.Length + 1)..];
                foreach (string legacyName in legacyNames)
                    AddIfHidden(builder, catalog, $"Controls/{legacyName}/{suffix}", story.Path);
                if (descriptor.AssemblyOwner == "Luxel.Editor.UI")
                    AddIfHidden(builder, catalog, $"Editor/Controls/{descriptor.ControlName}/{suffix}", story.Path);
            }
        }
    }

    public static void AddIfHidden(StoryCatalogBuilder builder, StoryCatalog catalog, string alias, string canonicalPath)
    {
        if (string.Equals(alias, canonicalPath, StringComparison.Ordinal) || catalog.Find(alias) is not null) return;
        builder.AddAlias(alias, canonicalPath);
    }

    private static string[] LegacyNames(string controlName) => controlName switch
    {
        "CheckBox" => ["CheckBox", "Check"],
        "SegmentedControl" => ["SegmentedControl", "Segmented"],
        "RadioGroup" => ["RadioGroup", "Radios"],
        "RichTextView" => ["RichTextView", "RichText"],
        "ScrollViewer" => ["ScrollViewer", "Scroll"],
        "WrapPanel" => ["WrapPanel", "Wrap"],
        _ => [controlName],
    };
}
