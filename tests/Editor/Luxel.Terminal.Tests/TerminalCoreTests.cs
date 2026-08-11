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

    [Fact] public void OutputUsesDelayedSoftWrap()
    {
        var b = new TerminalBuffer(4, 3); var p = new VtParser(b); p.Parse("abcde"u8);
        TerminalSnapshot s = b.Snapshot();
        Assert.Equal("abcd", Text(s.Lines[0])); Assert.Equal("e", Text(s.Lines[1]));
        Assert.True(s.LineWraps[0]); Assert.Equal(new TerminalCursor(1, 1), s.Cursor);
    }

    [Fact] public void CarriageReturnCancelsPendingWrap()
    {
        var b = new TerminalBuffer(4, 2); var p = new VtParser(b); p.Parse("abcd\rX"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("Xbcd", Text(s.Lines[0])); Assert.False(s.LineWraps[0]); Assert.Equal(0, s.Cursor.Row);
    }

    [Fact] public void DecawmOffDoesNotWrapAtMargin()
    {
        var b = new TerminalBuffer(4, 2); var p = new VtParser(b); p.Parse("abcd\x1b[?7lEf"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("abcf", Text(s.Lines[0])); Assert.Equal("", Text(s.Lines[1])); Assert.False(s.LineWraps[0]);
    }

    [Fact] public void WideGlyphWrapsAtomicallyAndCombiningUsesRightMarginBase()
    {
        var b = new TerminalBuffer(4, 3); var p = new VtParser(b); p.Parse(Encoding.UTF8.GetBytes("abc界e\u0301"));
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("abc", Text(s.Lines[0])); Assert.True(s.LineWraps[0]);
        Assert.Equal("界e\u0301", Text(s.Lines[1])); Assert.True(s.Lines[1][1].Continuation);
    }

    [Fact] public void NarrowOverwriteClearsOldWideContinuation()
    {
        var b = new TerminalBuffer(4, 2); var p = new VtParser(b); p.Parse(Encoding.UTF8.GetBytes("界\rA"));
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("A", Text(s.Lines[0])); Assert.False(s.Lines[0][1].Continuation);
    }

    [Fact] public void ResizeReflowsSoftWrappedOutputWithoutLosingText()
    {
        var b = new TerminalBuffer(8, 4); var p = new VtParser(b); p.Parse("abcdefghijkl"u8);
        b.Resize(4, 4); TerminalSnapshot narrow = b.Snapshot();
        Assert.Equal(["abcd", "efgh", "ijkl"], AllLines(narrow).Where(text => text.Length > 0).Take(3));
        Assert.Equal([true, true, false], AllWraps(narrow).Take(3));
        b.Resize(8, 4); TerminalSnapshot wide = b.Snapshot();
        Assert.Equal(["abcdefgh", "ijkl"], AllLines(wide).Where(text => text.Length > 0).Take(2));
        Assert.True(AllWraps(wide).First());
    }

    [Fact] public void ResizePreservesHardBreaks()
    {
        var b = new TerminalBuffer(8, 4); var p = new VtParser(b); p.Parse("abcd\r\nefgh"u8); b.Resize(4, 4);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("abcd", Text(s.Lines[0])); Assert.Equal("efgh", Text(s.Lines[1])); Assert.False(s.LineWraps[0]);
    }

    [Fact] public void AlternateResizeAlsoReflowsSavedPrimaryScreen()
    {
        var b = new TerminalBuffer(8, 3); var p = new VtParser(b); p.Parse("abcdefghij\x1b[?1049h"u8); b.Resize(4, 4); p.Parse("\x1b[?1049l"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal(["abcd", "efgh", "ij"], s.Lines.Take(3).Select(Text));
    }

    [Fact] public void BracketedPasteIsEncoded()
        => Assert.Equal("\x1b[200~hello\x1b[201~", Encoding.UTF8.GetString(TerminalKeyEncoder.EncodePaste("hello", true)));

    [Fact] public void OscTitleAndHyperlinkAreParsed()
    {
        var b = new TerminalBuffer(10, 2); var p = new VtParser(b); p.Parse("\x1b]2;Luxel\a\x1b]8;;https://example.test\aX\x1b]8;;\a"u8);
        TerminalSnapshot s = b.Snapshot(); Assert.Equal("Luxel", s.Title); Assert.Equal("https://example.test", s.Lines[0][0].Attributes.Hyperlink);
    }

    private static string Text(IReadOnlyList<TerminalCell> line)
        => string.Concat(line.Where(cell => !cell.Continuation).Select(cell => cell.Text)).TrimEnd();
    private static IEnumerable<string> AllLines(TerminalSnapshot snapshot)
        => snapshot.Scrollback.Concat(snapshot.Lines).Select(Text);
    private static IEnumerable<bool> AllWraps(TerminalSnapshot snapshot)
        => snapshot.ScrollbackWraps.Concat(snapshot.LineWraps);
}
