namespace Luxel.Gallery.Presentation;

/// <summary>Gallery chrome or preview appearance selected by a host.</summary>
public enum GalleryAppearance
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Host-neutral appearance preference. Hosts own persistence and supply the resolved system appearance.
/// </summary>
public sealed record GalleryAppearanceSettings(
    GalleryAppearance Shell = GalleryAppearance.System,
    GalleryAppearance Preview = GalleryAppearance.Light,
    bool SynchronizePreview = true)
{
    /// <summary>Resolves the shell preference to a concrete Light or Dark appearance.</summary>
    public GalleryAppearance ResolveShell(GalleryAppearance systemAppearance)
        => Resolve(Shell, systemAppearance);

    /// <summary>Resolves the preview preference, honoring <see cref="SynchronizePreview"/>.</summary>
    public GalleryAppearance ResolvePreview(GalleryAppearance systemAppearance)
        => SynchronizePreview ? ResolveShell(systemAppearance) : Resolve(Preview, systemAppearance);

    private static GalleryAppearance Resolve(GalleryAppearance preference, GalleryAppearance systemAppearance)
    {
        if (systemAppearance is not (GalleryAppearance.Light or GalleryAppearance.Dark))
            throw new ArgumentOutOfRangeException(nameof(systemAppearance), systemAppearance,
                "The resolved system appearance must be Light or Dark.");
        return preference == GalleryAppearance.System ? systemAppearance : preference;
    }
}
