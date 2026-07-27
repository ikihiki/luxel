using System.Text;
using Luxel.Terminal.Input;
using Luxel.Terminal.Parsing;
using Luxel.Terminal.Screen;

namespace Luxel.Terminal.Tests;

public sealed class TerminalCoreTests
{
    [Fact] public void PlainTextAndCursorAreApplied()
    {
        var b = new TerminalBuffer(10, 3); var p = new VtParser(b); p.Parse("hello"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("hello", string.Concat(s.Lines[0].Take(5).Select(c => c.Text))); Assert.Equal(5, s.Cursor.Column);
    }

    [Fact] public void SplitUtf8AndTrueColorAreApplied()
    {
        var b = new TerminalBuffer(10, 3); var p = new VtParser(b); byte[] bytes = Encoding.UTF8.GetBytes("\x1b[38;2;1;2;3m界");
        p.Parse(bytes.AsSpan(0, bytes.Length - 1)); p.Parse(bytes.AsSpan(bytes.Length - 1));
        TerminalCell c = b.Snapshot().Lines[0][0]; Assert.Equal("界", c.Text); Assert.Equal(2, c.Width); Assert.Equal(TerminalColor.Rgb(1, 2, 3), c.Attributes.Foreground);
    }

    [Fact] public void CombiningAndPuaWidthsAreTerminalFriendly()
    {
        Assert.Equal(0, TerminalCellWidth.GetWidth(new Rune(0x0301))); Assert.Equal(2, TerminalCellWidth.GetWidth(new Rune('界'))); Assert.Equal(1, TerminalCellWidth.GetWidth(new Rune(0xe0b0)));
    }

    [Fact] public void AlternateScreenRestoresPrimary()
    {
        var b = new TerminalBuffer(8, 2); var p = new VtParser(b); p.Parse("main\x1b[?1049halt\x1b[?1049l"u8);
        Assert.StartsWith("main", string.Concat(b.Snapshot().Lines[0].Select(c => c.Text)));
    }

    [Fact] public void BracketedPasteIsEncoded()
        => Assert.Equal("\x1b[200~hello\x1b[201~", Encoding.UTF8.GetString(TerminalKeyEncoder.EncodePaste("hello", true)));

    [Fact] public void OscTitleAndHyperlinkAreParsed()
    {
        var b = new TerminalBuffer(10, 2); var p = new VtParser(b); p.Parse("\x1b]2;Luxel\a\x1b]8;;https://example.test\aX\x1b]8;;\a"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("Luxel", s.Title); Assert.Equal("https://example.test", s.Lines[0][0].Attributes.Hyperlink);
    }
}
