using System.Text;

namespace Luxel.Terminal.Screen;

public sealed class TerminalBuffer
{
    private TerminalLine[] _lines;
    private TerminalLine[]? _primaryLines;
    private int _primaryRow, _primaryColumn;
    private bool _primaryWrapPending;
    private readonly List<TerminalLine> _scrollback = [];
    private readonly int _scrollbackLimit;
    private int _row, _column, _savedRow, _savedColumn, _scrollTop, _scrollBottom;
    private bool _wrapPending;
    private long _generation;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public TerminalAttributes Attributes { get; set; } = TerminalAttributes.Default;
    public bool CursorVisible { get; set; } = true;
    public bool AutoWrap { get; private set; } = true;
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

    public void SetAutoWrap(bool enabled)
    {
        AutoWrap = enabled;
        if (!enabled) _wrapPending = false;
        Touch();
    }

    public void WriteRune(Rune rune)
    {
        int width = TerminalCellWidth.GetWidth(rune, AmbiguousWidthIsWide);
        if (width == 0) { AppendCombining(rune); return; }

        if (_wrapPending)
        {
            if (AutoWrap)
            {
                _lines[_row].WrappedToNext = true;
                _wrapPending = false; _column = 0; Index(softWrap: true);
            }
            else _wrapPending = false;
        }

        if (width == 2 && _column == Columns - 1)
        {
            if (!AutoWrap) return;
            _lines[_row].WrappedToNext = true;
            _column = 0; Index(softWrap: true);
        }

        BreakWideAt(_row, _column);
        if (width == 2) BreakWideAt(_row, _column + 1);
        TerminalCell cell = _lines[_row].Cells[_column];
        cell.Text = rune.ToString(); cell.Width = width; cell.Continuation = false; cell.Occupied = true;
        cell.Attributes = Attributes with { Hyperlink = Hyperlink };
        if (width == 2)
        {
            TerminalCell next = _lines[_row].Cells[_column + 1];
            next.Text = ""; next.Width = 0; next.Continuation = true; next.Occupied = true; next.Attributes = cell.Attributes;
        }
        _lines[_row].ContentColumns = Math.Max(_lines[_row].ContentColumns, _column + width);
        if (_column + width >= Columns) { _column = Columns - 1; _wrapPending = AutoWrap; }
        else _column += width;
        Touch();
    }

    private void AppendCombining(Rune rune)
    {
        int c = _wrapPending ? _column : _column > 0 ? _column - 1 : 0;
        if (_lines[_row].Cells[c].Continuation && c > 0) c--;
        TerminalCell cell = _lines[_row].Cells[c];
        if (cell.Occupied && !cell.Continuation) cell.Text += rune.ToString();
        Touch();
    }

    public void CarriageReturn() { _column = 0; _wrapPending = false; Touch(); }
    public void Backspace() { _column = Math.Max(0, _column - 1); _wrapPending = false; Touch(); }
    public void Tab() { _column = Math.Min(Columns - 1, ((_column / 8) + 1) * 8); _wrapPending = false; Touch(); }
    public void LineFeed() { _lines[_row].WrappedToNext = false; _wrapPending = false; Index(softWrap: false); Touch(); }

    private void Index(bool softWrap)
    {
        if (!softWrap) _lines[_row].WrappedToNext = false;
        if (_row == _scrollBottom) ScrollUp(1); else _row = Math.Min(Rows - 1, _row + 1);
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
    public void RestoreCursor() { _row = Math.Clamp(_savedRow, 0, Rows - 1); _column = Math.Clamp(_savedColumn, 0, Columns - 1); _wrapPending = false; Touch(); }

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
        _wrapPending = false; Touch();
    }
    public void EraseLine(int mode)
    {
        if (mode == 2) ClearRange(_row, 0, Columns); else if (mode == 1) ClearRange(_row, 0, _column + 1); else ClearRange(_row, _column, Columns);
        _wrapPending = false; Touch();
    }
    public void EraseCharacters(int count) { ClearRange(_row, _column, Math.Min(Columns, _column + Math.Max(1, count))); _wrapPending = false; Touch(); }

    public void InsertCharacters(int count)
    {
        count = Math.Min(Math.Max(1, count), Columns - _column);
        BreakWideAt(_row, _column); BreakWideAt(_row, Columns - count);
        Array.Copy(_lines[_row].Cells, _column, _lines[_row].Cells, _column + count, Columns - _column - count);
        for (int i = 0; i < count; i++) _lines[_row].Cells[_column + i] = NewCell();
        RepairWide(_lines[_row]); RecalculateContent(_lines[_row]); _wrapPending = false; Touch();
    }
    public void DeleteCharacters(int count)
    {
        count = Math.Min(Math.Max(1, count), Columns - _column);
        BreakWideAt(_row, _column); BreakWideAt(_row, _column + count);
        Array.Copy(_lines[_row].Cells, _column + count, _lines[_row].Cells, _column, Columns - _column - count);
        for (int i = Columns - count; i < Columns; i++) _lines[_row].Cells[i] = NewCell();
        RepairWide(_lines[_row]); RecalculateContent(_lines[_row]); _wrapPending = false; Touch();
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
                _scrollback.Add(_lines[0].Clone());
                TrimScrollback();
            }
            for (int r = top; r < _scrollBottom; r++) _lines[r] = _lines[r + 1];
            _lines[_scrollBottom] = new TerminalLine(Columns);
        }
        Touch();
    }
    private void ScrollDown(int count, int? from = null)
    {
        int top = from ?? _scrollTop;
        for (int n = 0; n < count; n++) { for (int r = _scrollBottom; r > top; r--) _lines[r] = _lines[r - 1]; _lines[top] = new TerminalLine(Columns); }
        Touch();
    }

    public void UseAlternateScreen(bool enable, bool clear = true)
    {
        if (enable == AlternateScreen) return;
        if (enable)
        {
            _primaryLines = _lines; _primaryRow = _row; _primaryColumn = _column; _primaryWrapPending = _wrapPending;
            _lines = CreateLines(Rows, Columns); _row = _column = 0; _wrapPending = false;
        }
        else
        {
            _lines = _primaryLines ?? CreateLines(Rows, Columns); _row = _primaryRow; _column = _primaryColumn;
            _wrapPending = _primaryWrapPending; _primaryLines = null;
        }
        AlternateScreen = enable; if (clear && enable) EraseDisplay(2); Touch();
    }

    public void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1 || (columns == Columns && rows == Rows)) return;
        int oldColumns = Columns;
        if (AlternateScreen)
        {
            _lines = ResizeGrid(_lines, columns, rows, ref _row, ref _column);
            _wrapPending = false;
            if (_primaryLines is not null)
                ReflowPrimary(_primaryLines, _primaryRow, _primaryColumn, _primaryWrapPending, oldColumns, columns, rows,
                    out _primaryLines, out _primaryRow, out _primaryColumn, out _primaryWrapPending);
        }
        else
        {
            ReflowPrimary(_lines, _row, _column, _wrapPending, oldColumns, columns, rows,
                out _lines, out _row, out _column, out _wrapPending);
        }
        Columns = columns; Rows = rows;
        _savedRow = Math.Clamp(_savedRow, 0, rows - 1); _savedColumn = Math.Clamp(_savedColumn, 0, columns - 1);
        _scrollTop = 0; _scrollBottom = rows - 1; Touch();
    }

    private void ReflowPrimary(TerminalLine[] screen, int cursorRow, int cursorColumn, bool pending, int oldColumns, int newColumns, int newRows,
        out TerminalLine[] newScreen, out int newCursorRow, out int newCursorColumn, out bool newPending)
    {
        var all = _scrollback.Select(line => line.Clone()).Concat(screen.Select(line => line.Clone())).ToList();
        int cursorPhysical = _scrollback.Count + cursorRow;
        int cursorLogicalIndex = 0, cursorOffset = 0;
        var logical = new List<List<TerminalCell>>();
        int physical = 0;
        while (physical < all.Count)
        {
            var units = new List<TerminalCell>();
            int groupStart = physical;
            do
            {
                TerminalLine line = all[physical];
                int content = line.WrappedToNext ? oldColumns : Math.Max(line.ContentColumns, physical == cursorPhysical ? cursorColumn + (pending ? 1 : 0) : 0);
                content = Math.Clamp(content, 0, oldColumns);
                if (cursorPhysical == physical)
                {
                    cursorLogicalIndex = logical.Count;
                    cursorOffset = units.Sum(c => Math.Max(1, c.Width)) + Math.Clamp(cursorColumn + (pending ? 1 : 0), 0, content);
                }
                for (int c = 0; c < content; c++)
                {
                    TerminalCell cell = line.Cells[c];
                    if (!cell.Continuation) units.Add(cell.Clone());
                }
                bool more = line.WrappedToNext && physical + 1 < all.Count;
                physical++;
                if (!more) break;
            } while (physical < all.Count);
            logical.Add(units);
        }

        var reflowed = new List<TerminalLine>();
        int cursorGlobal = 0, cursorColumnResult = 0; bool pendingResult = false;
        for (int logicalIndex = 0; logicalIndex < logical.Count; logicalIndex++)
        {
            int logicalStart = reflowed.Count;
            TerminalLine current = new(newColumns); int col = 0;
            foreach (TerminalCell source in logical[logicalIndex])
            {
                int width = Math.Clamp(source.Width, 1, 2);
                if (width > newColumns) continue;
                if (col + width > newColumns)
                {
                    current.WrappedToNext = true; current.ContentColumns = Math.Max(current.ContentColumns, col); reflowed.Add(current);
                    current = new TerminalLine(newColumns); col = 0;
                }
                TerminalCell lead = source.Clone(); lead.Continuation = false; lead.Width = width;
                current.Cells[col] = lead;
                if (width == 2)
                {
                    current.Cells[col + 1] = new TerminalCell { Text = "", Width = 0, Continuation = true, Occupied = lead.Occupied, Attributes = lead.Attributes };
                }
                col += width; current.ContentColumns = Math.Max(current.ContentColumns, col);
            }
            reflowed.Add(current);

            if (logicalIndex == cursorLogicalIndex)
            {
                int offset = Math.Max(0, cursorOffset);
                if (pending && offset > 0 && offset % newColumns == 0)
                {
                    cursorGlobal = logicalStart + offset / newColumns - 1; cursorColumnResult = newColumns - 1; pendingResult = true;
                }
                else
                {
                    cursorGlobal = logicalStart + Math.Min(offset / newColumns, reflowed.Count - logicalStart - 1);
                    cursorColumnResult = Math.Min(newColumns - 1, offset % newColumns); pendingResult = false;
                }
            }
        }

        while (reflowed.Count < newRows) reflowed.Add(new TerminalLine(newColumns));
        int screenStart = Math.Max(0, reflowed.Count - newRows);
        _scrollback.Clear();
        foreach (TerminalLine line in reflowed.Take(screenStart)) _scrollback.Add(line);
        TrimScrollback();
        newScreen = reflowed.Skip(screenStart).Take(newRows).ToArray();
        while (newScreen.Length < newRows) newScreen = [.. newScreen, new TerminalLine(newColumns)];
        newCursorRow = Math.Clamp(cursorGlobal - screenStart, 0, newRows - 1);
        newCursorColumn = Math.Clamp(cursorColumnResult, 0, newColumns - 1);
        newPending = pendingResult;
    }

    private static TerminalLine[] ResizeGrid(TerminalLine[] source, int columns, int rows, ref int row, ref int column)
    {
        TerminalLine[] next = CreateLines(rows, columns);
        int copyRows = Math.Min(rows, source.Length), srcStart = Math.Max(0, source.Length - copyRows), dstStart = Math.Max(0, rows - copyRows);
        for (int r = 0; r < copyRows; r++)
        {
            int copyCols = Math.Min(columns, source[srcStart + r].Cells.Length);
            for (int c = 0; c < copyCols; c++) next[dstStart + r].Cells[c] = source[srcStart + r].Cells[c].Clone();
            next[dstStart + r].WrappedToNext = false; RepairWide(next[dstStart + r]); RecalculateContent(next[dstStart + r]);
        }
        row = Math.Clamp(row + dstStart - srcStart, 0, rows - 1); column = Math.Clamp(column, 0, columns - 1);
        return next;
    }

    public TerminalSnapshot Snapshot() => new(Columns, Rows,
        _lines.Select(line => (IReadOnlyList<TerminalCell>)line.Cells.Select(c => c.Clone()).ToArray()).ToArray(),
        _scrollback.Select(line => (IReadOnlyList<TerminalCell>)line.Cells.Select(c => c.Clone()).ToArray()).ToArray(),
        new TerminalCursor(_row, _column, CursorVisible), _generation, AlternateScreen, Title, BracketedPaste)
    {
        LineWraps = _lines.Select(line => line.WrappedToNext).ToArray(),
        ScrollbackWraps = _scrollback.Select(line => line.WrappedToNext).ToArray(),
    };

    private void ClearRange(int row, int from, int to)
    {
        from = Math.Max(0, from); to = Math.Min(Columns, to);
        for (int c = from; c < to; c++) BreakWideAt(row, c);
        for (int c = from; c < to; c++) _lines[row].Cells[c].Clear(Attributes);
        RecalculateContent(_lines[row]);
    }
    private void BreakWideAt(int row, int column)
    {
        if (column < 0 || column >= Columns) return;
        TerminalCell cell = _lines[row].Cells[column];
        if (cell.Continuation && column > 0)
        {
            _lines[row].Cells[column - 1].Clear(Attributes); cell.Clear(Attributes);
        }
        else if (cell.Width == 2)
        {
            cell.Clear(Attributes); if (column + 1 < Columns) _lines[row].Cells[column + 1].Clear(Attributes);
        }
    }
    private static void RepairWide(TerminalLine line)
    {
        for (int c = 0; c < line.Cells.Length; c++)
        {
            TerminalCell cell = line.Cells[c];
            if (cell.Continuation && (c == 0 || line.Cells[c - 1].Width != 2)) cell.Clear(TerminalAttributes.Default);
            if (cell.Width == 2 && (c + 1 >= line.Cells.Length || !line.Cells[c + 1].Continuation)) cell.Clear(TerminalAttributes.Default);
        }
    }
    private static void RecalculateContent(TerminalLine line)
    {
        int end = 0; for (int c = 0; c < line.Cells.Length; c++) if (line.Cells[c].Occupied) end = c + 1; line.ContentColumns = end;
    }
    private void TrimScrollback() { while (_scrollback.Count > _scrollbackLimit) _scrollback.RemoveAt(0); }
    private TerminalCell NewCell() => new() { Attributes = Attributes };
    private static TerminalLine[] CreateLines(int rows, int columns) => Enumerable.Range(0, rows).Select(_ => new TerminalLine(columns)).ToArray();
    private void Touch() => _generation++;
}
