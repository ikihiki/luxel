using Luxel.UI;

namespace Luxel.Gallery.Presentation;

/// <summary>Renderer-neutral RGBA color used by shared Gallery presentation models.</summary>
public readonly record struct GalleryColor(byte Red, byte Green, byte Blue, byte Alpha = byte.MaxValue)
{
    public uint Rgba => (uint)Red | ((uint)Green << 8) | ((uint)Blue << 16) | ((uint)Alpha << 24);
    public bool IsOpaque => Alpha == byte.MaxValue;

    public static GalleryColor FromRgba(uint rgba) => new(
        (byte)(rgba & 0xff),
        (byte)((rgba >> 8) & 0xff),
        (byte)((rgba >> 16) & 0xff),
        (byte)((rgba >> 24) & 0xff));

    /// <summary>Returns a CSS-compatible hexadecimal color without coupling tokens to a browser host.</summary>
    public string ToCssHex() => IsOpaque
        ? $"#{Red:X2}{Green:X2}{Blue:X2}"
        : $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}";

    /// <summary>WCAG relative luminance for this color. Shared Gallery tokens are opaque.</summary>
    public double RelativeLuminance
    {
        get
        {
            static double Linear(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045d
                    ? value / 12.92d
                    : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
            }

            return 0.2126d * Linear(Red) + 0.7152d * Linear(Green) + 0.0722d * Linear(Blue);
        }
    }

    public double ContrastRatio(GalleryColor other)
    {
        double lighter = Math.Max(RelativeLuminance, other.RelativeLuminance);
        double darker = Math.Min(RelativeLuminance, other.RelativeLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    public static GalleryColor Mix(GalleryColor from, GalleryColor to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        static byte Channel(byte from, byte to, float amount)
            => (byte)MathF.Round(from + (to - from) * amount);
        return new GalleryColor(
            Channel(from.Red, to.Red, amount),
            Channel(from.Green, to.Green, amount),
            Channel(from.Blue, to.Blue, amount),
            Channel(from.Alpha, to.Alpha, amount));
    }

    public override string ToString() => ToCssHex();
}

/// <summary>
/// Shared semantic chrome tokens. Browser and Native hosts translate these values to their renderer-specific forms.
/// </summary>
public sealed record GalleryChromeTokens
{
    public required GalleryAppearance Appearance { get; init; }

    public required GalleryColor Background { get; init; }
    public required GalleryColor Surface { get; init; }
    public required GalleryColor ElevatedSurface { get; init; }
    public required GalleryColor Border { get; init; }
    public required GalleryColor Divider { get; init; }
    public required GalleryColor Primary { get; init; }
    public required GalleryColor Selected { get; init; }
    public required GalleryColor Hover { get; init; }
    public required GalleryColor Pressed { get; init; }
    public required GalleryColor Focus { get; init; }
    public required GalleryColor Text { get; init; }
    public required GalleryColor MutedText { get; init; }
    public required GalleryColor SubtleText { get; init; }
    public required GalleryColor InverseText { get; init; }
    public required GalleryColor Error { get; init; }
    public required GalleryColor Warning { get; init; }
    public required GalleryColor Success { get; init; }
    public required GalleryColor CodeSurface { get; init; }
    public required GalleryColor ArgsEditorSurface { get; init; }

    public required float BodyFontSize { get; init; }
    public required float SupportingFontSize { get; init; }
    public required float CodeFontSize { get; init; }
    public required float NavigationFontSize { get; init; }
    public required float HeadingFontSize { get; init; }
    public required float BodyLineHeight { get; init; }
    public required float SpacingUnit { get; init; }
    public required float Radius { get; init; }
    public required float LargeRadius { get; init; }
    public required float ToolbarHeight { get; init; }
    public required float SidebarWidth { get; init; }
    public required float PanelMinimumSize { get; init; }
    public required float DocsMaximumWidth { get; init; }
    public required int DocsTextMaximumCharacters { get; init; }

    public static GalleryChromeTokens Light { get; } = Create(GalleryAppearance.Light, Theme.Light);
    public static GalleryChromeTokens Dark { get; } = Create(GalleryAppearance.Dark, Theme.Dark);

    /// <summary>Returns concrete tokens, resolving System with the host-provided appearance.</summary>
    public static GalleryChromeTokens Resolve(
        GalleryAppearance appearance,
        GalleryAppearance systemAppearance = GalleryAppearance.Light)
    {
        GalleryAppearance resolved = appearance == GalleryAppearance.System ? systemAppearance : appearance;
        return resolved switch
        {
            GalleryAppearance.Light => Light,
            GalleryAppearance.Dark => Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(systemAppearance), systemAppearance,
                "The resolved system appearance must be Light or Dark."),
        };
    }

    private static GalleryChromeTokens Create(GalleryAppearance appearance, Theme theme)
    {
        GalleryColor background = GalleryColor.FromRgba(theme.Background);
        GalleryColor surface = GalleryColor.FromRgba(theme.Surface);
        GalleryColor surfaceAlt = GalleryColor.FromRgba(theme.SurfaceAlt);
        GalleryColor text = GalleryColor.FromRgba(theme.Text);
        GalleryColor onAccent = GalleryColor.FromRgba(theme.OnAccent);
        GalleryColor primaryActive = GalleryColor.FromRgba(theme.PrimaryActive);

        GalleryColor muted = EnsureContrast(
            GalleryColor.FromRgba(theme.TextMuted), background, surface, text, 4.5d);
        GalleryColor subtle = EnsureContrast(
            GalleryColor.Mix(muted, background, 0.2f), background, surface, text, 3d);
        GalleryColor border = EnsureContrast(
            GalleryColor.FromRgba(theme.BorderColor), background, surface, text, 3d);
        GalleryColor primary = GalleryColor.FromRgba(theme.Primary);
        GalleryColor inverse = MostContrasting(primary, onAccent, background, text);
        primary = EnsureContrast(primary, inverse, primaryActive, 4.5d);
        inverse = MostContrasting(primary, onAccent, background, text);
        GalleryColor focus = EnsureContrast(primaryActive, background, surface, text, 3d);

        GalleryColor error = EnsureContrast(
            GalleryColor.FromRgba(theme.Danger), background, surface, text, 3d);
        GalleryColor warning = EnsureContrast(
            GalleryColor.FromRgba(theme.Warning), background, surface, text, 3d);
        GalleryColor success = EnsureContrast(
            GalleryColor.FromRgba(theme.Success), background, surface, text, 3d);

        return new GalleryChromeTokens
        {
            Appearance = appearance,
            Background = background,
            Surface = surface,
            ElevatedSurface = appearance == GalleryAppearance.Light
                ? GalleryColor.Mix(surface, primary, 0.02f)
                : surfaceAlt,
            Border = border,
            Divider = border,
            Primary = primary,
            Selected = GalleryColor.Mix(surface, primary, 0.18f),
            Hover = GalleryColor.Mix(surface, primary, 0.08f),
            Pressed = GalleryColor.Mix(surface, primary, 0.28f),
            Focus = focus,
            Text = text,
            MutedText = muted,
            SubtleText = subtle,
            InverseText = inverse,
            Error = error,
            Warning = warning,
            Success = success,
            CodeSurface = surfaceAlt,
            ArgsEditorSurface = GalleryColor.Mix(surfaceAlt, surface, 0.35f),
            BodyFontSize = theme.Font,
            SupportingFontSize = theme.FontSm,
            CodeFontSize = theme.FontSm,
            NavigationFontSize = theme.FontSm,
            HeadingFontSize = theme.FontLg,
            BodyLineHeight = 1.65f,
            SpacingUnit = theme.Space,
            Radius = theme.Radius,
            LargeRadius = theme.RadiusLg,
            ToolbarHeight = 48f,
            SidebarWidth = 288f,
            PanelMinimumSize = 180f,
            DocsMaximumWidth = 960f,
            DocsTextMaximumCharacters = 72,
        };
    }

    private static GalleryColor EnsureContrast(
        GalleryColor color,
        GalleryColor background,
        GalleryColor alternateBackground,
        GalleryColor toward,
        double minimum)
    {
        for (int attempt = 0;
             attempt < 32 && Math.Min(color.ContrastRatio(background), color.ContrastRatio(alternateBackground)) < minimum;
             attempt++)
            color = GalleryColor.Mix(color, toward, 0.08f);
        return color;
    }

    private static GalleryColor EnsureContrast(
        GalleryColor color,
        GalleryColor background,
        GalleryColor toward,
        double minimum)
    {
        for (int attempt = 0; attempt < 32 && color.ContrastRatio(background) < minimum; attempt++)
            color = GalleryColor.Mix(color, toward, 0.08f);
        return color;
    }

    private static GalleryColor MostContrasting(
        GalleryColor background,
        GalleryColor first,
        GalleryColor second,
        GalleryColor third)
    {
        GalleryColor result = first;
        double contrast = first.ContrastRatio(background);
        if (second.ContrastRatio(background) > contrast)
        {
            result = second;
            contrast = second.ContrastRatio(background);
        }
        if (third.ContrastRatio(background) > contrast) result = third;
        return result;
    }
}
