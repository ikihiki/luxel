using System.Text;

namespace Luxel.Terminal.Screen;

[Flags]
public enum TerminalStyle : ushort
{
    None = 0, Bold = 1, Dim = 2, Italic = 4, Underline = 8,
    Blink = 16, Inverse = 32, Hidden = 64, Strikethrough = 128,
}

public enum TerminalColorKind : byte { Default, Indexed, Rgb }

public readonly record struct TerminalColor(TerminalColorKind Kind, byte R, byte G, byte B, byte Index)
{
    public static TerminalColor Default => new(TerminalColorKind.Default, 0, 0, 0, 0);
    public static TerminalColor Indexed(byte index) => new(TerminalColorKind.Indexed, 0, 0, 0, index);
    public static TerminalColor Rgb(byte r, byte g, byte b) => new(TerminalColorKind.Rgb, r, g, b, 0);
}

public readonly record struct TerminalAttributes(
    TerminalColor Foreground, TerminalColor Background, TerminalColor UnderlineColor,
    TerminalStyle Style, string? Hyperlink = null)
{
    public static TerminalAttributes Default => new(TerminalColor.Default, TerminalColor.Default, TerminalColor.Default, TerminalStyle.None);
}

public sealed class TerminalCell
{
    public string Text { get; set; } = " ";
    public int Width { get; set; } = 1;
    public bool Continuation { get; set; }
    public TerminalAttributes Attributes { get; set; } = TerminalAttributes.Default;

    public TerminalCell Clone() => new() { Text = Text, Width = Width, Continuation = Continuation, Attributes = Attributes };
    public void Clear(TerminalAttributes attributes) { Text = " "; Width = 1; Continuation = false; Attributes = attributes; }
}

public readonly record struct TerminalCursor(int Row, int Column, bool Visible = true);

public sealed record TerminalSnapshot(
    int Columns, int Rows, IReadOnlyList<IReadOnlyList<TerminalCell>> Lines,
    IReadOnlyList<IReadOnlyList<TerminalCell>> Scrollback, TerminalCursor Cursor,
    long Generation, bool AlternateScreen, string? Title, bool BracketedPaste);

public static class TerminalCellWidth
{
    public static int GetWidth(Rune rune, bool ambiguousWide = false)
    {
        int v = rune.Value;
        if (v == 0 || v < 32 || v is >= 0x7f and < 0xa0 || IsZeroWidth(v)) return 0;
        if (IsWide(v)) return 2;
        if (ambiguousWide && IsAmbiguous(v)) return 2;
        return 1;
    }

    private static bool IsZeroWidth(int v) =>
        v is >= 0x0300 and <= 0x036f or >= 0x0483 and <= 0x0489 or >= 0x0591 and <= 0x05bd or 0x05bf or >= 0x05c1 and <= 0x05c2 or >= 0x05c4 and <= 0x05c5 or 0x05c7 or
        >= 0x0610 and <= 0x061a or >= 0x064b and <= 0x065f or 0x0670 or >= 0x06d6 and <= 0x06ed or >= 0x0711 and <= 0x0711 or >= 0x0730 and <= 0x074a or
        >= 0x07a6 and <= 0x07b0 or >= 0x07eb and <= 0x07f3 or >= 0x0816 and <= 0x082d or >= 0x0859 and <= 0x085b or
        >= 0x1ab0 and <= 0x1aff or >= 0x1dc0 and <= 0x1dff or >= 0x20d0 and <= 0x20ff or >= 0xfe00 and <= 0xfe0f or >= 0xfe20 and <= 0xfe2f or
        0x200b or 0x200c or 0x200d or 0x2060 or >= 0xe0100 and <= 0xe01ef;

    private static bool IsWide(int v) =>
        v is >= 0x1100 and <= 0x115f or 0x2329 or 0x232a or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or
        >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe19 or >= 0xfe30 and <= 0xfe6f or >= 0xff00 and <= 0xff60 or >= 0xffe0 and <= 0xffe6 or
        >= 0x1f300 and <= 0x1faff or >= 0x20000 and <= 0x3fffd;

    private static bool IsAmbiguous(int v) =>
        v is >= 0xe000 and <= 0xf8ff or >= 0xf0000 and <= 0xffffd or >= 0x100000 and <= 0x10fffd;
}
