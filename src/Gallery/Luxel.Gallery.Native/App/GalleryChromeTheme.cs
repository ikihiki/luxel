using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Blazor Gallery の CSS custom properties に対応する Native chrome テーマ。</summary>
internal static class GalleryChromeTheme
{
    public static Theme Create()
    {
        Theme theme = Theme.Dark.Compact();
        theme.Background = C(0x0b, 0x10, 0x17);
        theme.Surface = C(0x11, 0x1a, 0x25);
        theme.SurfaceAlt = C(0x10, 0x17, 0x21);
        theme.BorderColor = C(0x26, 0x32, 0x42);
        theme.Text = C(0xe5, 0xed, 0xf7);
        theme.TextMuted = C(0x8f, 0xa0, 0xb5);
        theme.Primary = C(0x76, 0xa9, 0xff);
        theme.PrimaryHover = C(0x8b, 0xb8, 0xff);
        theme.PrimaryActive = C(0x5f, 0x91, 0xe8);
        theme.Radius = 6;
        theme.RadiusLg = 9;
        return theme;
    }

    private static uint C(byte r, byte g, byte b) => Color2D.Rgba(r, g, b);
}
