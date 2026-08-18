using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Blazor Gallery の CSS custom properties に対応する Native chrome テーマ。</summary>
internal static class GalleryChromeTheme
{
    public static uint Main => C(0x0d, 0x14, 0x1d);
    public static uint Preview => C(0x10, 0x15, 0x1d);
    public static uint Search => C(0x0b, 0x12, 0x1b);
    public static uint AccentSoft => C(0x18, 0x34, 0x5c);
    public static uint Border => C(0x26, 0x32, 0x42);
    public static uint TreeHover => C(0x18, 0x23, 0x31);
    public static uint TreeFolder => C(0x87, 0x99, 0xaf);
    public static uint TreeLeaf => C(0xbd, 0xca, 0xda);
    public static uint TreeHoverText => C(0xf5, 0xf8, 0xfc);
    public static uint TreeSelectedText => C(0xcf, 0xe2, 0xff);
    public static uint TreeChevron => C(0x5f, 0x72, 0x8a);
    public static uint Panel => C(0x10, 0x19, 0x23);
    public static uint PanelCode => C(0x09, 0x11, 0x1a);
    public static uint OutputRow => C(0x0b, 0x12, 0x1b);
    public static uint OutputTime => C(0x67, 0x7b, 0x93);
    public static uint OutputKind => C(0x8f, 0xac, 0xce);
    public static uint OutputText => C(0xcb, 0xd7, 0xe5);
    public static uint Success => C(0x54, 0xbd, 0x83);

    public static Theme Create() => Theme.Dark.Compact() with
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
