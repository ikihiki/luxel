using Luxel.Graphics.TwoD;
using Luxel.Terminal.Screen;

namespace Luxel.Terminal.UI;

public sealed class TerminalPalette
{
    private static readonly (byte R, byte G, byte B)[] Ansi =
    [
        (0,0,0), (205,49,49), (13,188,121), (229,229,16), (36,114,200), (188,63,188), (17,168,205), (229,229,229),
        (102,102,102), (241,76,76), (35,209,139), (245,245,67), (59,142,234), (214,112,214), (41,184,219), (255,255,255),
    ];

    public uint Foreground { get; init; } = Color2D.Rgba(204, 204, 204);
    public uint Background { get; init; } = Color2D.Rgba(12, 12, 12);
    public uint Selection { get; init; } = Color2D.Rgba(38, 79, 120);
    public uint Cursor { get; init; } = Color2D.Rgba(220, 220, 220);
    public uint ImeBackground { get; init; } = Color2D.Rgba(45, 45, 48);
    public uint ImeUnderline { get; init; } = Color2D.Rgba(86, 156, 214);

    public uint Resolve(TerminalColor color, bool foreground)
    {
        if (color.Kind == TerminalColorKind.Default) return foreground ? Foreground : Background;
        if (color.Kind == TerminalColorKind.Rgb) return Color2D.Rgba(color.R, color.G, color.B);
        int i = color.Index;
        if (i < 16) { var c = Ansi[i]; return Color2D.Rgba(c.R, c.G, c.B); }
        if (i < 232)
        {
            int n = i - 16, r = n / 36, g = n / 6 % 6, b = n % 6;
            static byte C(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
            return Color2D.Rgba(C(r), C(g), C(b));
        }
        byte gray = (byte)(8 + (i - 232) * 10);
        return Color2D.Rgba(gray, gray, gray);
    }
}
