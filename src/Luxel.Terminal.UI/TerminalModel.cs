using System.Text;
using Luxel.Terminal.Screen;

namespace Luxel.Terminal.UI;

public readonly record struct TerminalPoint(int Line, int Column) : IComparable<TerminalPoint>
{
    public int CompareTo(TerminalPoint other)
        => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
}

public readonly record struct TerminalSelection(TerminalPoint Anchor, TerminalPoint Active)
{
    public (TerminalPoint Start, TerminalPoint End) Ordered
        => Anchor.CompareTo(Active) <= 0 ? (Anchor, Active) : (Active, Anchor);
    public bool IsEmpty => Anchor == Active;
}

public static class TerminalViewport
{
    public static IReadOnlyList<IReadOnlyList<TerminalCell>> VisibleLines(
        TerminalSnapshot snapshot, int visibleRows, int scrollOffset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        visibleRows = Math.Max(1, visibleRows);
        int total = snapshot.Scrollback.Count + snapshot.Lines.Count;
        int maxOffset = Math.Max(0, total - visibleRows);
        int offset = Math.Clamp(scrollOffset, 0, maxOffset);
        int start = Math.Max(0, total - visibleRows - offset);
        int count = Math.Min(visibleRows, total - start);
        var result = new IReadOnlyList<TerminalCell>[count];
        for (int i = 0; i < count; i++)
        {
            int line = start + i;
            result[i] = line < snapshot.Scrollback.Count
                ? snapshot.Scrollback[line]
                : snapshot.Lines[line - snapshot.Scrollback.Count];
        }
        return result;
    }

    public static string ExtractSelection(TerminalSnapshot snapshot, TerminalSelection? selection)
    {
        if (selection is not { IsEmpty: false } selected) return string.Empty;
        (TerminalPoint start, TerminalPoint end) = selected.Ordered;
        int total = snapshot.Scrollback.Count + snapshot.Lines.Count;
        if (total == 0) return string.Empty;
        start = new TerminalPoint(Math.Clamp(start.Line, 0, total - 1), Math.Clamp(start.Column, 0, snapshot.Columns));
        end = new TerminalPoint(Math.Clamp(end.Line, 0, total - 1), Math.Clamp(end.Column, 0, snapshot.Columns));
        var result = new StringBuilder();
        for (int lineIndex = start.Line; lineIndex <= end.Line; lineIndex++)
        {
            IReadOnlyList<TerminalCell> line = lineIndex < snapshot.Scrollback.Count
                ? snapshot.Scrollback[lineIndex]
                : snapshot.Lines[lineIndex - snapshot.Scrollback.Count];
            int from = lineIndex == start.Line ? start.Column : 0;
            int to = lineIndex == end.Line ? end.Column : line.Count;
            for (int column = from; column < Math.Min(to, line.Count); column++)
                if (!line[column].Continuation) result.Append(line[column].Text);
            if (lineIndex != end.Line) result.AppendLine();
        }
        return result.ToString().TrimEnd(' ');
    }
}

public interface ITerminalClipboard
{
    string? GetText();
    void SetText(string text);
}

public sealed class PlatformTerminalClipboard : ITerminalClipboard
{
    public string? GetText() => Luxel.Platform.PlatformClipboard.Current?.GetText();
    public void SetText(string text) => Luxel.Platform.PlatformClipboard.Current?.SetText(text);
}
