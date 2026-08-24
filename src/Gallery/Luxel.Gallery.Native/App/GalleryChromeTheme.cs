using Luxel.Gallery.Presentation;
using Luxel.UI;
using SharedGalleryChromeTokens = Luxel.Gallery.Presentation.GalleryChromeTokens;

namespace Luxel.Gallery;

/// <summary>Native renderer conveniences derived only from shared semantic Gallery tokens.</summary>
internal readonly record struct NativeGalleryChrome(
    uint Main,
    uint Preview,
    uint Search,
    uint AccentSoft,
    uint Border,
    uint TreeHover,
    uint TreeFolder,
    uint TreeLeaf,
    uint TreeHoverText,
    uint TreeSelectedText,
    uint TreeChevron,
    uint Panel,
    uint PanelCode,
    uint OutputRow,
    uint OutputTime,
    uint OutputKind,
    uint OutputText,
    uint Success,
    uint Warning);

/// <summary>Maps the shared Gallery presentation palette into Luxel Native themes.</summary>
internal static class GalleryChromeTheme
{
    public static SharedGalleryChromeTokens SharedTokens(GalleryAppearance appearance)
        => SharedGalleryChromeTokens.Resolve(appearance);

    public static NativeGalleryChrome Tokens(GalleryAppearance appearance)
    {
        SharedGalleryChromeTokens tokens = SharedTokens(appearance);
        return new NativeGalleryChrome(
            Main: tokens.Background.Rgba,
            Preview: tokens.CodeSurface.Rgba,
            Search: tokens.ArgsEditorSurface.Rgba,
            AccentSoft: tokens.Selected.Rgba,
            Border: tokens.Border.Rgba,
            TreeHover: tokens.Hover.Rgba,
            TreeFolder: tokens.MutedText.Rgba,
            TreeLeaf: tokens.Text.Rgba,
            TreeHoverText: tokens.Text.Rgba,
            TreeSelectedText: tokens.Primary.Rgba,
            TreeChevron: tokens.SubtleText.Rgba,
            Panel: tokens.Surface.Rgba,
            PanelCode: tokens.CodeSurface.Rgba,
            OutputRow: tokens.ElevatedSurface.Rgba,
            OutputTime: tokens.SubtleText.Rgba,
            OutputKind: tokens.Primary.Rgba,
            OutputText: tokens.Text.Rgba,
            Success: tokens.Success.Rgba,
            Warning: tokens.Warning.Rgba);
    }

    public static Theme Create(GalleryAppearance appearance)
    {
        SharedGalleryChromeTokens tokens = SharedTokens(appearance);
        Theme basis = (appearance == GalleryAppearance.Dark ? Theme.Dark : Theme.Light).Compact();
        return basis with
        {
            Background = tokens.Background.Rgba,
            Surface = tokens.Surface.Rgba,
            SurfaceAlt = tokens.ElevatedSurface.Rgba,
            BorderColor = tokens.Border.Rgba,
            Text = tokens.Text.Rgba,
            TextMuted = tokens.MutedText.Rgba,
            OnAccent = tokens.InverseText.Rgba,
            Primary = tokens.Primary.Rgba,
            PrimaryHover = GalleryColor.Mix(tokens.Primary, tokens.Focus, 0.35f).Rgba,
            PrimaryActive = GalleryColor.Mix(tokens.Primary, tokens.Text, 0.14f).Rgba,
            Success = tokens.Success.Rgba,
            Warning = tokens.Warning.Rgba,
            Danger = tokens.Error.Rgba,
            Info = tokens.Primary.Rgba,
            Font = tokens.BodyFontSize,
            FontSm = tokens.SupportingFontSize,
            FontLg = tokens.HeadingFontSize,
            Space = tokens.SpacingUnit,
            Radius = tokens.Radius,
            RadiusLg = tokens.LargeRadius,
        };
    }

    public static Theme CreatePreview(GalleryAppearance appearance)
        => Create(appearance);
}
