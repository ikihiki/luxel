using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

internal enum GalleryThemeMode
{
    Light,
    Dark,
}

/// <summary>Native Gallery chrome で使う、shell theme ごとの非 semantic token。</summary>
internal readonly record struct GalleryChromeTokens(
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
    uint Success);

/// <summary>Native Gallery chrome の Light / Dark palette。</summary>
internal static class GalleryChromeTheme
{
    private static readonly GalleryChromeTokens LightTokens = new(
        Main: C(0xfa, 0xfb, 0xfd),
        Preview: C(0xee, 0xf1, 0xf5),
        Search: C(0xff, 0xff, 0xff),
        AccentSoft: C(0xdb, 0xe8, 0xfc),
        Border: C(0xcf, 0xd6, 0xe1),
        TreeHover: C(0xe8, 0xed, 0xf4),
        TreeFolder: C(0x4f, 0x5d, 0x72),
        TreeLeaf: C(0x25, 0x2d, 0x3a),
        TreeHoverText: C(0x12, 0x18, 0x21),
        TreeSelectedText: C(0x16, 0x4a, 0x92),
        TreeChevron: C(0x67, 0x72, 0x84),
        Panel: C(0xff, 0xff, 0xff),
        PanelCode: C(0xf6, 0xf7, 0xf9),
        OutputRow: C(0xf8, 0xfa, 0xfc),
        OutputTime: C(0x68, 0x74, 0x87),
        OutputKind: C(0x2f, 0x65, 0xa8),
        OutputText: C(0x26, 0x2f, 0x3c),
        Success: C(0x2f, 0x8f, 0x5b));

    private static readonly GalleryChromeTokens DarkTokens = new(
        Main: C(0x0d, 0x14, 0x1d),
        Preview: C(0x10, 0x15, 0x1d),
        Search: C(0x0b, 0x12, 0x1b),
        AccentSoft: C(0x18, 0x34, 0x5c),
        Border: C(0x26, 0x32, 0x42),
        TreeHover: C(0x18, 0x23, 0x31),
        TreeFolder: C(0x87, 0x99, 0xaf),
        TreeLeaf: C(0xbd, 0xca, 0xda),
        TreeHoverText: C(0xf5, 0xf8, 0xfc),
        TreeSelectedText: C(0xcf, 0xe2, 0xff),
        TreeChevron: C(0x5f, 0x72, 0x8a),
        Panel: C(0x10, 0x19, 0x23),
        PanelCode: C(0x09, 0x11, 0x1a),
        OutputRow: C(0x0b, 0x12, 0x1b),
        OutputTime: C(0x67, 0x7b, 0x93),
        OutputKind: C(0x8f, 0xac, 0xce),
        OutputText: C(0xcb, 0xd7, 0xe5),
        Success: C(0x54, 0xbd, 0x83));

    public static GalleryChromeTokens Tokens(GalleryThemeMode mode)
        => mode == GalleryThemeMode.Dark ? DarkTokens : LightTokens;

    public static Theme Create(GalleryThemeMode mode)
        => mode == GalleryThemeMode.Dark ? CreateDark() : CreateLight();

    public static Theme CreatePreview(GalleryThemeMode mode)
        => (mode == GalleryThemeMode.Dark ? Theme.Dark : Theme.Light).Compact();

    private static Theme CreateLight() => Theme.Light.Compact() with
    {
        Background = C(0xf4, 0xf6, 0xf9),
        Surface = C(0xff, 0xff, 0xff),
        SurfaceAlt = C(0xf8, 0xf9, 0xfb),
        BorderColor = C(0xcf, 0xd6, 0xe1),
        Text = C(0x1e, 0x25, 0x30),
        TextMuted = C(0x5e, 0x69, 0x7b),
        Primary = C(0x2f, 0x68, 0xc8),
        PrimaryHover = C(0x3d, 0x79, 0xda),
        PrimaryActive = C(0x25, 0x56, 0xaa),
        Radius = 6,
        RadiusLg = 9,
    };

    private static Theme CreateDark() => Theme.Dark.Compact() with
    {
        Background = C(0x0b, 0x10, 0x17),
        Surface = C(0x11, 0x1a, 0x25),
        SurfaceAlt = C(0x10, 0x17, 0x21),
        BorderColor = C(0x26, 0x32, 0x42),
        Text = C(0xe5, 0xed, 0xf7),
        TextMuted = C(0x8f, 0xa0, 0xb5),
        Primary = C(0x76, 0xa9, 0xff),
        PrimaryHover = C(0x8b, 0xb8, 0xff),
        PrimaryActive = C(0x5f, 0x91, 0xe8),
        Radius = 6,
        RadiusLg = 9,
    };

    private static uint C(byte r, byte g, byte b) => Color2D.Rgba(r, g, b);
}
