using Luxel.Gallery.Presentation;
using Luxel.Settings;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>shell / preview theme と同期設定を既存 SettingsStore pattern で保持する。</summary>
internal sealed class GalleryAppearanceState
{
    internal const string SettingsFileName = "gallery.settings.json";
    internal const string ShellThemeKey = "appearance.shellTheme";
    internal const string PreviewThemeKey = "appearance.previewTheme";
    internal const string SynchronizePreviewKey = "appearance.synchronizePreview";

    private readonly SettingsStore _settings;

    public GalleryAppearanceState(IFileStore files)
    {
        _settings = SettingsStore.LoadFrom(files, SettingsFileName);
        ShellTheme = _settings.Get(ShellThemeKey, GalleryAppearance.Dark);
        PreviewTheme = _settings.Get(PreviewThemeKey, GalleryAppearance.Light);
        SynchronizePreview = _settings.Get(SynchronizePreviewKey, false);
        if (SynchronizePreview.Peek()) PreviewTheme.Value = ShellTheme.Peek();
        _settings.AutoSave = true;
    }

    public Signal<GalleryAppearance> ShellTheme { get; }
    public Signal<GalleryAppearance> PreviewTheme { get; }
    public Signal<bool> SynchronizePreview { get; }

    public void ToggleShellTheme()
    {
        ShellTheme.Value = Opposite(ShellTheme.Peek());
        if (SynchronizePreview.Peek()) PreviewTheme.Value = ShellTheme.Peek();
    }

    public void TogglePreviewTheme()
    {
        if (SynchronizePreview.Peek()) SynchronizePreview.Value = false;
        PreviewTheme.Value = Opposite(PreviewTheme.Peek());
    }

    public void ToggleSynchronization()
    {
        bool synchronize = !SynchronizePreview.Peek();
        SynchronizePreview.Value = synchronize;
        if (synchronize) PreviewTheme.Value = ShellTheme.Peek();
    }

    private static GalleryAppearance Opposite(GalleryAppearance mode)
        => mode == GalleryAppearance.Dark ? GalleryAppearance.Light : GalleryAppearance.Dark;
}
