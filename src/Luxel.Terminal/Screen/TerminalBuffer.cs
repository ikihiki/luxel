using System.Text;

namespace Luxel.Terminal.Screen;

public sealed class TerminalBuffer
{
    private TerminalCell[][] _lines;
    private TerminalCell[][]? _primaryLines;
    private int _primaryRow, _primaryColumn;
    private readonly List<IReadOnlyList<TerminalCell>> _scrollback = [];
    private readonly int _scrollbackLimit;
    private int _row, _column, _savedRow, _savedColumn, _scrollTop, _scrollBottom;
    private bool _wrapPending;
    private long _generation;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public TerminalAttributes Attributes { get; set; } = TerminalAttributes.Default;
    public bool CursorVisible { get; set; } = true;
    public bool AutoWrap { get; set; } = true;
    public bool OriginMode { get; set; }
    public bool AlternateScreen { get; private set; }
    public bool BracketedPaste { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public string? Title { get; set; }
    public string? Hyperlink { get; set; }
    public bool AmbiguousWidthIsWide { get; set; }
    public long Generation => _generation;

    public TerminalBuffer(int columns, int rows, int scrollbackLimit = 10_000)
    {
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        Columns = columns; Rows = rows; _scrollbackLimit = Math.Max(0, scrollbackLimit);
        _lines = CreateLines(rows, columns); _scrollBottom = rows - 1;
    }

    public void WriteRune(Rune rune)
    {
        int width = TerminalCellWidth.GetWidth(rune, AmbiguousWidthIsWide);
        if (width == 0)
        {
            int c = _column > 0 ? _column - 1 : 0;
            if (_lines[_row][c].Continuation && c > 0) c--;
            _lines[_row][c].Text += rune.ToString(); Touch(); return;
        }
        if (_wrapPending) { _wrapPending = false; _column = 0; LineFeed(); }
        if (width == 2 && _column == Columns - 1) { _column = 0; LineFeed(); }
        TerminalCell cell = _lines[_row][_column];
        cell.Text = rune.ToString(); cell.Width = width; cell.Continuation = false;
        cell.Attributes = Attributes with { Hyperlink = Hyperlink };
        if (width == 2 && _column + 1 < Columns)
        {
            TerminalCell next = _lines[_row][_column + 1]; next.Text = ""; next.Width = 0; next.Continuation = true; next.Attributes = cell.Attributes;
        }
        if (_column + width >= Columns) { _column = Columns - 1; _wrapPending = AutoWrap; }
        else _column += width;
        Touch();
    }

    public void CarriageReturn() { _column = 0; _wrapPending = false; Touch(); }
    public void Backspace() { _column = Math.Max(0, _column - 1); _wrapPending = false; Touch(); }
    public void Tab() { _column = Math.Min(Columns - 1, ((_column / 8) + 1) * 8); _wrapPending = false; Touch(); }
    public void LineFeed()
    {
        _wrapPending = false;
        if (_row == _scrollBottom) ScrollUp(1); else _row = Math.Min(Rows - 1, _row + 1);
        Touch();
    }

    public void MoveCursor(int row, int column)
    {
        int top = OriginMode ? _scrollTop : 0;
        int bottom = OriginMode ? _scrollBottom : Rows - 1;
        _row = Math.Clamp(row + top, top, bottom); _column = Math.Clamp(column, 0, Columns - 1); _wrapPending = false; Touch();
    }
    public void MoveRelative(int dr, int dc) { _row = Math.Clamp(_row + dr, 0, Rows - 1); _column = Math.Clamp(_column + dc, 0, Columns - 1); _wrapPending = false; Touch(); }
    public void SetColumn(int column) { _column = Math.Clamp(column, 0, Columns - 1); _wrapPending = false; Touch(); }
    public void SaveCursor() { _savedRow = _row; _savedColumn = _column; }
    public void RestoreCursor() { _row = Math.Clamp(_savedRow, 0, Rows - 1); _column = Math.Clamp(_savedColumn, 0, Columns - 1); Touch(); }

    public void SetScrollRegion(int top, int bottom)
    {
        if (top < 0 || bottom >= Rows || top >= bottom) { _scrollTop = 0; _scrollBottom = Rows - 1; }
        else { _scrollTop = top; _scrollBottom = bottom; }
        MoveCursor(0, 0);
    }

    public void EraseDisplay(int mode)
    {
        if (mode == 2 || mode == 3) { for (int r = 0; r < Rows; r++) ClearRange(r, 0, Columns); if (mode == 3) _scrollback.Clear(); }
        else if (mode == 0) { ClearRange(_row, _column, Columns); for (int r = _row + 1; r < Rows; r++) ClearRange(r, 0, Columns); }
        else if (mode == 1) { for (int r = 0; r < _row; r++) ClearRange(r, 0, Columns); ClearRange(_row, 0, _column + 1); }
        Touch();
    }
    public void EraseLine(int mode)
    {
        if (mode == 2) ClearRange(_row, 0, Columns); else if (mode == 1) ClearRange(_row, 0, _column + 1); else ClearRange(_row, _column, Columns); Touch();
    }
    public void EraseCharacters(int count) { ClearRange(_row, _column, Math.Min(Columns, _column + Math.Max(1, count))); Touch(); }

    public void InsertCharacters(int count)
    {
        count = Math.Min(Math.Max(1, count), Columns - _column);
        Array.Copy(_lines[_row], _column, _lines[_row], _column + count, Columns - _column - count);
        for (int i = 0; i < count; i++) _lines[_row][_column + i] = NewCell(); Touch();
    }
    public void DeleteCharacters(int count)
    {
        count = Math.Min(Math.Max(1, count), Columns - _column);
        Array.Copy(_lines[_row], _column + count, _lines[_row], _column, Columns - _column - count);
        for (int i = Columns - count; i < Columns; i++) _lines[_row][i] = NewCell(); Touch();
    }
    public void InsertLines(int count) { if (_row > _scrollBottom) return; ScrollDown(Math.Min(Math.Max(1, count), _scrollBottom - _row + 1), _row); }
    public void DeleteLines(int count) { if (_row > _scrollBottom) return; ScrollUp(Math.Min(Math.Max(1, count), _scrollBottom - _row + 1), _row); }

    public void ScrollUp(int count, int? from = null)
    {
        int top = from ?? _scrollTop; count = Math.Min(Math.Max(1, count), _scrollBottom - top + 1);
        for (int n = 0; n < count; n++)
        {
            if (top == 0 && !AlternateScreen && _scrollbackLimit > 0)
            {
                _scrollback.Add(_lines[0].Select(c => c.Clone()).ToArray());
                if (_scrollback.Count > _scrollbackLimit) _scrollback.RemoveAt(0);
            }
            for (int r = top; r < _scrollBottom; r++) _lines[r] = _lines[r + 1];
            _lines[_scrollBottom] = CreateLine(Columns);
        }
        Touch();
    }
    private void ScrollDown(int count, int? from = null)
    {
        int top = from ?? _scrollTop;
        for (int n = 0; n < count; n++) { for (int r = _scrollBottom; r > top; r--) _lines[r] = _lines[r - 1]; _lines[top] = CreateLine(Columns); }
        Touch();
    }

    public void UseAlternateScreen(bool enable, bool clear = true)
    {
        if (enable == AlternateScreen) return;
        if (enable) { _primaryLines = _lines; _primaryRow = _row; _primaryColumn = _column; _lines = CreateLines(Rows, Columns); _row = _column = 0; }
        else { _lines = _primaryLines ?? CreateLines(Rows, Columns); _row = _primaryRow; _column = _primaryColumn; _primaryLines = null; }
        AlternateScreen = enable; if (clear && enable) EraseDisplay(2); Touch();
    }

    public void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1 || (columns == Columns && rows == Rows)) return;
        var next = CreateLines(rows, columns);
        int copyRows = Math.Min(rows, Rows), copyCols = Math.Min(columns, Columns);
        int srcStart = Math.Max(0, Rows - copyRows), dstStart = Math.Max(0, rows - copyRows);
        for (int r = 0; r < copyRows; r++) for (int c = 0; c < copyCols; c++) next[dstStart + r][c] = _lines[srcStart + r][c].Clone();
        _lines = next; Columns = columns; Rows = rows; _row = Math.Clamp(_row + dstStart - srcStart, 0, rows - 1); _column = Math.Clamp(_column, 0, columns - 1);
        _scrollTop = 0; _scrollBottom = rows - 1; Touch();
    }

    public TerminalSnapshot Snapshot() => new(Columns, Rows,
        _lines.Select(line => (IReadOnlyList<TerminalCell>)line.Select(c => c.Clone()).ToArray()).ToArray(),
        _scrollback.Select(line => (IReadOnlyList<TerminalCell>)line.Select(c => c.Clone()).ToArray()).ToArray(),
        new TerminalCursor(_row, _column, CursorVisible), _generation, AlternateScreen, Title, BracketedPaste);

    private void ClearRange(int row, int from, int to) { for (int c = Math.Max(0, from); c < Math.Min(Columns, to); c++) _lines[row][c].Clear(Attributes); }
    private TerminalCell NewCell() => new() { Attributes = Attributes };
    private static TerminalCell[] CreateLine(int columns) => Enumerable.Range(0, columns).Select(_ => new TerminalCell()).ToArray();
    private static TerminalCell[][] CreateLines(int rows, int columns) => Enumerable.Range(0, rows).Select(_ => CreateLine(columns)).ToArray();
    private void Touch() => _generation++;
}
