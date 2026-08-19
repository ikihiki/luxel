using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

public sealed record GridViewItem(string Key, string Label, object? Tag = null, bool Disabled = false);

[UiComponent]
public sealed partial class GridView : Widget, ISemanticProvider
{
    [UiParam] private readonly Bindable<Signal<IReadOnlyList<GridViewItem>>> _items = new();
    [UiParam] private readonly Bindable<float> _height = 180f;
    [UiParam] private readonly Bindable<float> _itemWidth = 120f;
    [UiParam] private readonly Bindable<float> _itemHeight = 72f;
    [UiEvent] public UiEvent<GridView, GridViewItem> OnSelect;
    [UiEvent] public UiEvent<GridView, int, int> OnReorder;

    public bool AllowReorder { get; set; }
    public sealed record ReorderDrag(GridView Source, int Index, string Key);

    private IReadOnlyList<GridViewItem> _rows = [];
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private readonly ScrollModel _scroll = new();
    private readonly Signal<int> _version = new(0);
    private readonly List<UiNode> _cells = [];
    private readonly List<UiNode> _selection = [];
    private FocusTarget? _focusTarget;
    private string? _anchor;
    private float _width;
    private int _columns = 1;

    public string? FocusedKey { get; private set; }
    public IReadOnlySet<string> SelectedKeys => _selectedKeys;
    public int RealizedCellCount => _cells.Count;
    public float ScrollOffset => _scroll.ClampedPeek;
    public bool IsKeyboardFocused { get; private set; }

    private float CellW => MathF.Max(48, ItemWidth.Get());
    private float CellH => MathF.Max(24, ItemHeight.Get());
    private float ViewH => MathF.Max(1, Height.Get());

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _width = ResolveW(c, ctx, float.IsFinite(c.MaxW) ? c.MaxW : CellW * 3);
        _columns = Math.Max(1, (int)(_width / CellW));
        Size = c.Constrain(new Size(_width, ViewH));
    }

    public string? MoveFocus(Key key, bool extend = false)
    {
        IReadOnlyList<GridViewItem> enabled = _rows.Where(x => !x.Disabled).ToArray();
        if (enabled.Count == 0) return FocusedKey = null;
        int current = enabled.FindIndex(x => x.Key == FocusedKey);
        int step = key switch { Key.Left => -1, Key.Right => 1, Key.Up => -_columns, Key.Down => _columns, _ => 0 };
        int next = key switch
        {
            Key.Home => 0,
            Key.End => enabled.Count - 1,
            _ => Math.Clamp(current < 0 ? 0 : current + step, 0, enabled.Count - 1),
        };
        Select(enabled[next], extend ? SelectionMode.Range : SelectionMode.Replace);
        return FocusedKey;
    }

    private enum SelectionMode { Replace, Toggle, Range }

    private void Select(GridViewItem item, SelectionMode mode)
    {
        if (item.Disabled) return;
        if (mode == SelectionMode.Replace)
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(item.Key);
            _anchor = item.Key;
        }
        else if (mode == SelectionMode.Toggle)
        {
            if (!_selectedKeys.Remove(item.Key)) _selectedKeys.Add(item.Key);
            _anchor = item.Key;
        }
        else
        {
            int a = IndexOf(_anchor ?? FocusedKey ?? item.Key), b = IndexOf(item.Key);
            _selectedKeys.Clear();
            if (a < 0 || b < 0) _selectedKeys.Add(item.Key);
            else for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) if (!_rows[i].Disabled) _selectedKeys.Add(_rows[i].Key);
        }
        FocusedKey = item.Key;
        int index = IndexOf(item.Key);
        int row = index / _columns;
        _scroll.EnsureVisible(row * CellH, (row + 1) * CellH, 2);
        _version.Value++;
        OnSelect.Invoke(this, item);
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < _rows.Count; i++) if (_rows[i].Key == key) return i;
        return -1;
    }

    private bool OnKey(KeyEvent e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
        {
            MoveFocus(e.Key, e.Shift);
            return true;
        }
        if (e.Key == Key.Space && FocusedKey is { } key)
        {
            int index = IndexOf(key);
            if (index >= 0) Select(_rows[index], e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace);
            return index >= 0;
        }
        return false;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, _width, ViewH);
        _focusTarget ??= new FocusTarget { OnFocus = value => IsKeyboardFocused = value, OnKey = OnKey };
        ctx.AddFocusable(_focusTarget);
        _cells.Clear();
        _selection.Clear();

        int poolRows = (int)MathF.Ceiling(ViewH / CellH) + 1;
        int pool = poolRows * _columns;
        for (int i = 0; i < pool; i++)
        {
            UiNode selected = ctx.Canvas.AddChild(node);
            var ss = new Scene2D(); ss.FillRoundedRect(Color2D.White, 3, 3, CellW - 6, CellH - 6, 5);
            selected.Content = ss;
            _selection.Add(selected);
            UiNode cell = ctx.Canvas.AddChild(node); cell.Z = 1;
            _cells.Add(cell);
        }
        ctx.Effect(() =>
        {
            uint color = ctx.Theme.Value.SurfaceAlt;
            foreach (UiNode selected in _selection) selected.Color = color;
        });

        ctx.Effect(() =>
        {
            IReadOnlyList<GridViewItem> incoming = Items.Get()?.Value ?? [];
            if (!ReferenceEquals(incoming, _rows))
            {
                _rows = incoming;
                _selectedKeys.RemoveWhere(key => IndexOf(key) < 0);
                if (FocusedKey is not null && IndexOf(FocusedKey) < 0) FocusedKey = null;
                _version.Value++;
            }
            int totalRows = (int)MathF.Ceiling((float)_rows.Count / _columns);
            _scroll.SetLengths(totalRows * CellH, ViewH);
        });

        float fs = ctx.Theme.Peek().FontSm;
        float baseline = 10 + ctx.Font.Ascent(fs);
        ctx.Effect(() =>
        {
            _ = _version.Value;
            int firstRow = (int)(_scroll.Clamped / CellH);
            int first = firstRow * _columns;
            for (int p = 0; p < _cells.Count; p++)
            {
                int index = first + p;
                int row = index / _columns, column = index % _columns;
                float x = column * CellW, y = row * CellH - _scroll.Clamped;
                UiNode cell = _cells[p];
                UiNode selection = _selection[p];
                cell.Transform = Affine2D.Translate(x + 10, y);
                selection.Transform = Affine2D.Translate(x, y);
                if (index >= _rows.Count)
                {
                    cell.Content = new Scene2D();
                    selection.Opacity = 0;
                    continue;
                }
                GridViewItem item = _rows[index];
                var scene = new Scene2D();
                ctx.Font.AppendText(scene, item.Label, 0, baseline, fs, Color2D.White);
                cell.Content = scene;
                cell.Color = item.Disabled ? ctx.Theme.Value.TextMuted & 0x80ffffffu : ctx.Theme.Value.Text;
                selection.Opacity = _selectedKeys.Contains(item.Key) ? 1 : 0;
            }
        });

        int IndexAt(PointerEvent e)
        {
            int column = Math.Clamp((int)(e.X / CellW), 0, _columns - 1);
            int row = (int)((e.Y + _scroll.ClampedPeek) / CellH);
            return row * _columns + column;
        }

        if (!AllowReorder)
        {
            ctx.AddHit(node, new Rect(0, 0, _width - ScrollBars.GrabW, ViewH), focus: _focusTarget,
                onClickPos: e =>
                {
                    int index = IndexAt(e);
                    if ((uint)index < (uint)_rows.Count)
                        Select(_rows[index], e.Shift ? SelectionMode.Range : e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace);
                });
        }
        else
        {
            int pressed = -1; bool started = false;
            ctx.AddHit(node, new Rect(0, 0, _width - ScrollBars.GrabW, ViewH), focus: _focusTarget,
                onDragStart: e => { pressed = IndexAt(e); started = false; },
                onDrag: e =>
                {
                    if (started || (uint)pressed >= (uint)_rows.Count || ctx.Host is null || MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4) return;
                    started = true;
                    var ghost = new Scene2D(); ghost.FillRoundedRect(ctx.Theme.Peek().SurfaceAlt, 0, 0, CellW, CellH, 5);
                    ctx.Host.BeginDrag(new ReorderDrag(this, pressed, _rows[pressed].Key), ghost);
                },
                onDragEnd: e =>
                {
                    if (!started && (uint)pressed < (uint)_rows.Count)
                        Select(_rows[pressed], e.Shift ? SelectionMode.Range : e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace);
                },
                acceptsDrop: payload => payload is ReorderDrag drag && ReferenceEquals(drag.Source, this),
                onDrop: (payload, e) =>
                {
                    if (payload is ReorderDrag drag) OnReorder.Invoke(this, drag.Index, Math.Clamp(IndexAt(e), 0, _rows.Count));
                });
        }
        ScrollBars.AttachVertical(ctx, node, _scroll, _width, ViewH);
        ctx.AddScroll(node, new Rect(0, 0, _width, ViewH), delta => _scroll.ScrollBy(-delta));
    }

    public SemanticNode GetSemantics()
        => new(SemanticRole.Grid, Children: _rows.Select(item => new SemanticNode(SemanticRole.GridCell,
            item.Label, item.Key, _selectedKeys.Contains(item.Key), item.Disabled)).ToArray());
}

public sealed record DataGridColumn(string Key, string Header, float Width = 120f);
public sealed record DataGridRow(string Key, IReadOnlyList<string> Cells, object? Tag = null, bool Disabled = false);

[UiComponent]
public sealed partial class DataGrid : Widget, ISemanticProvider
{
    [UiParam] private readonly Bindable<Signal<IReadOnlyList<DataGridRow>>> _items = new();
    [UiParam] private readonly Bindable<IReadOnlyList<DataGridColumn>> _columns = new([]);
    [UiParam] private readonly Bindable<float> _height = 220f;
    [UiParam] private readonly Bindable<float> _rowHeight = 26f;
    [UiEvent] public UiEvent<DataGrid, DataGridRow> OnSelect;
    [UiEvent] public UiEvent<DataGrid, int, int> OnReorder;

    public bool AllowReorder { get; set; }
    public sealed record ReorderDrag(DataGrid Source, int Index, string Key);

    private IReadOnlyList<DataGridRow> _rows = [];
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private readonly ScrollModel _scroll = new();
    private readonly Signal<int> _version = new(0);
    private readonly List<UiNode> _rowNodes = [];
    private readonly List<UiNode> _selectionNodes = [];
    private FocusTarget? _focusTarget;
    private string? _anchor;
    private float _width;

    public string? FocusedKey { get; private set; }
    public IReadOnlySet<string> SelectedKeys => _selectedKeys;
    public int RealizedRowCount => _rowNodes.Count;
    public float ScrollOffset => _scroll.ClampedPeek;
    public bool IsKeyboardFocused { get; private set; }
    private float HeaderH => MathF.Max(22, RowHeight.Get());
    private float RowH => MathF.Max(18, RowHeight.Get());
    private float ViewH => MathF.Max(1, Height.Get());

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float natural = Columns.Get().Sum(column => MathF.Max(24, column.Width));
        _width = ResolveW(c, ctx, float.IsFinite(c.MaxW) ? c.MaxW : MathF.Max(160, natural));
        Size = c.Constrain(new Size(_width, ViewH));
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < _rows.Count; i++) if (_rows[i].Key == key) return i;
        return -1;
    }

    private enum SelectionMode { Replace, Toggle, Range }

    private void Select(int index, SelectionMode mode)
    {
        if ((uint)index >= (uint)_rows.Count || _rows[index].Disabled) return;
        DataGridRow item = _rows[index];
        if (mode == SelectionMode.Replace)
        {
            _selectedKeys.Clear(); _selectedKeys.Add(item.Key); _anchor = item.Key;
        }
        else if (mode == SelectionMode.Toggle)
        {
            if (!_selectedKeys.Remove(item.Key)) _selectedKeys.Add(item.Key); _anchor = item.Key;
        }
        else
        {
            int a = IndexOf(_anchor ?? FocusedKey ?? item.Key);
            _selectedKeys.Clear();
            for (int i = Math.Min(a < 0 ? index : a, index); i <= Math.Max(a < 0 ? index : a, index); i++)
                if (!_rows[i].Disabled) _selectedKeys.Add(_rows[i].Key);
        }
        FocusedKey = item.Key;
        _scroll.EnsureVisible(index * RowH, (index + 1) * RowH, 2);
        _version.Value++;
        OnSelect.Invoke(this, item);
    }

    public string? MoveFocus(Key key, bool extend = false)
    {
        if (_rows.Count == 0) return FocusedKey = null;
        int current = IndexOf(FocusedKey ?? "");
        int next = key switch
        {
            Key.Home => 0,
            Key.End => _rows.Count - 1,
            Key.Up => Math.Max(0, current <= 0 ? 0 : current - 1),
            Key.Down => Math.Min(_rows.Count - 1, current < 0 ? 0 : current + 1),
            Key.PageUp => Math.Max(0, current - Math.Max(1, (int)((ViewH - HeaderH) / RowH) - 1)),
            Key.PageDown => Math.Min(_rows.Count - 1, current + Math.Max(1, (int)((ViewH - HeaderH) / RowH) - 1)),
            _ => Math.Max(0, current),
        };
        while (next >= 0 && next < _rows.Count && _rows[next].Disabled)
        {
            int direction = next >= current ? 1 : -1;
            next += direction;
        }
        if ((uint)next >= (uint)_rows.Count) return FocusedKey;
        Select(next, extend ? SelectionMode.Range : SelectionMode.Replace);
        return FocusedKey;
    }

    private bool OnKey(KeyEvent e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            MoveFocus(e.Key, e.Shift); return true;
        }
        if (e.Key == Key.Space && FocusedKey is { } key)
        {
            int index = IndexOf(key);
            if (index >= 0) Select(index, e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace);
            return index >= 0;
        }
        return false;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, _width, ViewH);
        _focusTarget ??= new FocusTarget { OnFocus = value => IsKeyboardFocused = value, OnKey = OnKey };
        ctx.AddFocusable(_focusTarget);
        _rowNodes.Clear(); _selectionNodes.Clear();

        IReadOnlyList<DataGridColumn> columns = Columns.Get();
        float fs = ctx.Theme.Peek().FontSm;
        float baseline = (RowH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);
        UiNode header = ctx.Canvas.AddChild(node); header.Z = 2;
        var headerScene = new Scene2D();
        float x = 8;
        foreach (DataGridColumn column in columns)
        {
            ctx.Font.AppendText(headerScene, column.Header, x, baseline, fs, Color2D.White);
            x += MathF.Max(24, column.Width);
            headerScene.FillRect(Color2D.White, x - 1, 0, 1, HeaderH);
        }
        header.Content = headerScene;
        ctx.Effect(() => header.Color = ctx.Theme.Value.TextMuted);

        int pool = (int)MathF.Ceiling((ViewH - HeaderH) / RowH) + 1;
        for (int i = 0; i < pool; i++)
        {
            UiNode selected = ctx.Canvas.AddChild(node);
            var ss = new Scene2D(); ss.FillRect(Color2D.White, 0, 0, _width - ScrollBars.GrabW, RowH);
            selected.Content = ss;
            _selectionNodes.Add(selected);
            UiNode row = ctx.Canvas.AddChild(node); row.Z = 1;
            _rowNodes.Add(row);
        }
        ctx.Effect(() =>
        {
            foreach (UiNode selected in _selectionNodes) selected.Color = ctx.Theme.Value.SurfaceAlt;
        });
        ctx.Effect(() =>
        {
            IReadOnlyList<DataGridRow> incoming = Items.Get()?.Value ?? [];
            if (!ReferenceEquals(incoming, _rows))
            {
                _rows = incoming;
                _selectedKeys.RemoveWhere(key => IndexOf(key) < 0);
                if (FocusedKey is not null && IndexOf(FocusedKey) < 0) FocusedKey = null;
                _version.Value++;
            }
            _scroll.SetLengths(_rows.Count * RowH, MathF.Max(1, ViewH - HeaderH));
        });
        ctx.Effect(() =>
        {
            _ = _version.Value;
            int first = (int)(_scroll.Clamped / RowH);
            for (int p = 0; p < _rowNodes.Count; p++)
            {
                int index = first + p;
                float y = HeaderH + index * RowH - _scroll.Clamped;
                UiNode row = _rowNodes[p]; UiNode selected = _selectionNodes[p];
                row.Transform = Affine2D.Translate(0, y); selected.Transform = Affine2D.Translate(0, y);
                if (index >= _rows.Count)
                {
                    row.Content = new Scene2D(); selected.Opacity = 0; continue;
                }
                DataGridRow item = _rows[index];
                var scene = new Scene2D(); float cellX = 8;
                for (int cell = 0; cell < columns.Count; cell++)
                {
                    string text = cell < item.Cells.Count ? item.Cells[cell] : "";
                    ctx.Font.AppendText(scene, text, cellX, baseline, fs, Color2D.White);
                    cellX += MathF.Max(24, columns[cell].Width);
                    scene.FillRect(Color2D.White, cellX - 1, 0, 1, RowH);
                }
                row.Content = scene;
                row.Color = item.Disabled ? ctx.Theme.Value.TextMuted & 0x80ffffffu : ctx.Theme.Value.Text;
                selected.Opacity = _selectedKeys.Contains(item.Key) ? 1 : 0;
            }
        });

        int IndexAt(float y) => (int)((y - HeaderH + _scroll.ClampedPeek) / RowH);
        if (!AllowReorder)
        {
            ctx.AddHit(node, new Rect(0, HeaderH, _width - ScrollBars.GrabW, ViewH - HeaderH), focus: _focusTarget,
                onClickPos: e =>
                {
                    int index = IndexAt(e.Y);
                    Select(index, e.Shift ? SelectionMode.Range : e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace);
                });
        }
        else
        {
            int pressed = -1; bool started = false;
            ctx.AddHit(node, new Rect(0, HeaderH, _width - ScrollBars.GrabW, ViewH - HeaderH), focus: _focusTarget,
                onDragStart: e => { pressed = IndexAt(e.Y); started = false; },
                onDrag: e =>
                {
                    if (started || (uint)pressed >= (uint)_rows.Count || ctx.Host is null || MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4) return;
                    started = true;
                    var ghost = new Scene2D(); ghost.FillRect(ctx.Theme.Peek().SurfaceAlt, 0, 0, _width, RowH);
                    ctx.Host.BeginDrag(new ReorderDrag(this, pressed, _rows[pressed].Key), ghost);
                },
                onDragEnd: e => { if (!started) Select(pressed, e.Shift ? SelectionMode.Range : e.Ctrl ? SelectionMode.Toggle : SelectionMode.Replace); },
                acceptsDrop: payload => payload is ReorderDrag drag && ReferenceEquals(drag.Source, this),
                onDrop: (payload, e) =>
                {
                    if (payload is ReorderDrag drag) OnReorder.Invoke(this, drag.Index, Math.Clamp(IndexAt(e.Y), 0, _rows.Count));
                });
        }
        ScrollBars.AttachVertical(ctx, node, _scroll, _width, MathF.Max(1, ViewH - HeaderH));
        ctx.AddScroll(node, new Rect(0, HeaderH, _width, ViewH - HeaderH), delta => _scroll.ScrollBy(-delta));
    }

    public SemanticNode GetSemantics()
        => new(SemanticRole.Grid, Children: _rows.Select(row => new SemanticNode(SemanticRole.Row, row.Key, row.Key,
            _selectedKeys.Contains(row.Key), row.Disabled, Children: row.Cells.Select((cell, i) =>
                new SemanticNode(SemanticRole.GridCell, cell, i < Columns.Get().Count ? Columns.Get()[i].Key : i.ToString())).ToArray())).ToArray());
}

internal static class CollectionListExtensions
{
    public static int FindIndex<T>(this IReadOnlyList<T> items, Predicate<T> predicate)
    {
        for (int i = 0; i < items.Count; i++) if (predicate(items[i])) return i;
        return -1;
    }
}
