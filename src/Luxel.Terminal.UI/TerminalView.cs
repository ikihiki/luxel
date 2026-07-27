using System.Text;
using Luxel.Graphics.TwoD;
using Luxel.Platform;
using Luxel.Terminal.Input;
using Luxel.Terminal.Screen;
using Luxel.Terminal.Session;
using Luxel.Typography;
using Luxel.Typography.TwoD;
using Luxel.UI;

namespace Luxel.Terminal.UI;

/// <summary>Controls-independent fixed-cell terminal widget backed by <see cref="TerminalSession"/>.</summary>
public sealed class TerminalView : Widget, IDisposable, IAsyncDisposable
{
    private readonly TerminalSession _session;
    private readonly TerminalFontSet _fonts;
    private readonly ITerminalClipboard _clipboard;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _ownsSession;
    private readonly bool _ownsFonts;
    private FocusTarget? _focus;
    private TerminalTextInput? _textInput;
    private ImeComposition _composition;
    private int _columns, _rows, _scrollOffset;
    private int _sessionDirty;
    private bool _disposed;

    public TerminalView(TerminalSession session, TerminalFontSet fonts,
        ITerminalClipboard? clipboard = null, bool ownsSession = false, bool ownsFonts = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _clipboard = clipboard ?? new PlatformTerminalClipboard();
        _ownsSession = ownsSession;
        _ownsFonts = ownsFonts;
        _session.Updated += OnSessionUpdated;
    }

    public float CellWidth { get; set; } = 9;
    public float CellHeight { get; set; } = 18;
    public float FontSize { get; set; } = 16;
    /// <summary>Warp cell-joining Powerline separators to the full cell bounds.</summary>
    public bool WarpPowerlineGlyphs { get; set; } = true;
    /// <summary>Horizontal overlap in logical pixels used to hide antialiasing seams.</summary>
    public float PowerlineHorizontalBleed { get; set; } = 0.75f;
    /// <summary>Vertical overlap in logical pixels used to align separator tops and bottoms.</summary>
    public float PowerlineVerticalBleed { get; set; } = 0.5f;
    /// <summary>Optional application-specific warp classifier. The built-in Powerline block is always recognized.</summary>
    public Func<Rune, bool>? AdditionalGlyphWarpPredicate { get; set; }
    public TerminalPalette Palette { get; set; } = new();
    public TerminalSelection? Selection { get; private set; }
    public int ScrollOffset => _scrollOffset;
    public ImeComposition Composition => _composition;
    public event Action<Exception>? InputError;

    public override string? DebugDetail => $"{_columns}x{_rows}, scroll={_scrollOffset}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        TerminalSnapshot snapshot = _session.Snapshot();
        float cellW = MathF.Max(1, CellWidth), cellH = MathF.Max(1, CellHeight);
        Size desired = new(snapshot.Columns * cellW, snapshot.Rows * cellH);
        Size = c.Constrain(desired);
        int columns = Math.Max(1, (int)MathF.Floor(Size.Width / cellW));
        int rows = Math.Max(1, (int)MathF.Floor(Size.Height / cellH));
        if (columns != _columns || rows != _rows)
        {
            _columns = columns; _rows = rows;
            if (_session.State == TerminalSessionState.Running)
                Observe(_session.ResizeAsync(columns, rows, _lifetime.Token));
        }
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => _session.Snapshot().Columns * MathF.Max(1, CellWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        SetWorldPos(worldOrigin + Offset);
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, Size.Width, Size.Height);
        node.ContentColors = true;
        TerminalSnapshot snapshot = _session.Snapshot();
        node.Content = Render(snapshot);
        ctx.AddAnimation(_ =>
        {
            if (Interlocked.Exchange(ref _sessionDirty, 0) == 0) return false;
            _session.ConsumeUpdate();
            TerminalSnapshot latest = _session.Snapshot();
            if (_scrollOffset > 0)
                _scrollOffset = Math.Min(_scrollOffset, Math.Max(0, latest.Scrollback.Count + latest.Lines.Count - 1));
            node.Content = Render(latest);
            return false;
        });

        _textInput ??= new TerminalTextInput(this);
        _focus ??= new FocusTarget
        {
            OnFocus = focused => Focused.Value = focused,
            OnKey = HandleKey,
            OnText = HandleText,
            OnCompose = text => SetComposition(new ImeComposition(text, text.Length)),
            OnComposeEx = SetComposition,
            OnCommit = CommitComposition,
            TextInput = _textInput,
        };
        ctx.AddFocusable(_focus);
        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height), focus: _focus,
            onDragStart: e => BeginSelection(snapshot, e.X, e.Y),
            onDrag: e => ExtendSelection(snapshot, e.X, e.Y),
            onDragEnd: e => ExtendSelection(snapshot, e.X, e.Y),
            cursor: CursorKind.IBeam);
        ctx.AddScroll(node, new Rect(0, 0, Size.Width, Size.Height), OnScroll);
    }

    public Scene2D Render(TerminalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        float cellW = MathF.Max(1, CellWidth), cellH = MathF.Max(1, CellHeight);
        int visibleRows = Math.Max(1, _rows == 0 ? snapshot.Rows : _rows);
        IReadOnlyList<IReadOnlyList<TerminalCell>> lines = TerminalViewport.VisibleLines(snapshot, visibleRows, _scrollOffset);
        int total = snapshot.Scrollback.Count + snapshot.Lines.Count;
        int visibleStart = Math.Max(0, total - lines.Count - Math.Clamp(_scrollOffset, 0, Math.Max(0, total - lines.Count)));
        var scene = new Scene2D();
        scene.FillRect(Palette.Background, 0, 0, Size.Width, Size.Height);

        // Pass 1: paint every cell background before glyphs. Warped separators may overlap a neighboring
        // cell by a subpixel; painting later backgrounds would otherwise cover that bleed and recreate seams.
        for (int row = 0; row < lines.Count; row++)
        {
            IReadOnlyList<TerminalCell> line = lines[row];
            int absoluteLine = visibleStart + row;
            for (int column = 0; column < Math.Min(line.Count, _columns == 0 ? line.Count : _columns); column++)
            {
                TerminalCell cell = line[column];
                if (cell.Continuation) continue;
                (uint _, uint background) = ResolveColors(cell);
                int width = Math.Clamp(cell.Width, 1, 2);
                float x = column * cellW, y = row * cellH, w = width * cellW;
                if (background != Palette.Background) scene.FillRect(background, x, y, w, cellH);
                if (IsSelected(absoluteLine, column, width)) scene.FillRect(Palette.Selection, x, y, w, cellH);
            }
        }

        // Pass 2: paint glyphs and decorations above all backgrounds so Powerline bleed stays continuous.
        for (int row = 0; row < lines.Count; row++)
        {
            IReadOnlyList<TerminalCell> line = lines[row];
            for (int column = 0; column < Math.Min(line.Count, _columns == 0 ? line.Count : _columns); column++)
            {
                TerminalCell cell = line[column];
                if (cell.Continuation) continue;
                (uint foreground, uint _) = ResolveColors(cell);
                int width = Math.Clamp(cell.Width, 1, 2);
                float x = column * cellW, y = row * cellH, w = width * cellW;
                if ((cell.Attributes.Style & TerminalStyle.Hidden) == 0 && !string.IsNullOrWhiteSpace(cell.Text))
                    AppendCellGlyph(scene, cell.Text, x, y, w, cellH, foreground);
                if ((cell.Attributes.Style & TerminalStyle.Underline) != 0)
                {
                    uint underline = cell.Attributes.UnderlineColor.Kind == TerminalColorKind.Default
                        ? foreground : Palette.Resolve(cell.Attributes.UnderlineColor, true);
                    scene.FillRect(underline, x, y + cellH - 2, w, 1);
                }
                if ((cell.Attributes.Style & TerminalStyle.Strikethrough) != 0)
                    scene.FillRect(foreground, x, y + cellH * 0.55f, w, 1);
            }
        }

        if (_scrollOffset == 0 && snapshot.Cursor.Visible)
        {
            float x = snapshot.Cursor.Column * cellW, y = snapshot.Cursor.Row * cellH;
            scene.FillRect(Palette.Cursor, x, y + cellH - 2, cellW, 2);
        }
        DrawComposition(scene, snapshot, cellW, cellH);
        return scene;
    }

    private (uint Foreground, uint Background) ResolveColors(TerminalCell cell)
    {
        uint foreground = Palette.Resolve(cell.Attributes.Foreground, true);
        uint background = Palette.Resolve(cell.Attributes.Background, false);
        if ((cell.Attributes.Style & TerminalStyle.Inverse) != 0) (foreground, background) = (background, foreground);
        return (foreground, background);
    }

    private void AppendCellGlyph(Scene2D scene, string text, float x, float y, float width, float height, uint color)
    {
        VectorFont font = _fonts.Resolver.Resolve(text);
        if (WarpPowerlineGlyphs && ShouldWarpGlyph(text))
        {
            float horizontal = Math.Clamp(PowerlineHorizontalBleed, 0, 1);
            float vertical = Math.Clamp(PowerlineVerticalBleed, 0, 1);
            if (font.TryAppendSingleGlyphWarped(scene, text,
                x - horizontal, y - vertical, width + horizontal * 2, height + vertical * 2,
                FontSize, color)) return;
        }

        float measured = font.Measure(text, FontSize).width;
        float glyphX = x + MathF.Max(0, (width - measured) * 0.5f);
        float baseline = y + (height - FontSize) * 0.5f + font.Ascent(FontSize);
        font.AppendText(scene, text, glyphX, baseline, FontSize, color);
    }

    private bool ShouldWarpGlyph(string text)
    {
        var runes = text.EnumerateRunes();
        if (!runes.MoveNext()) return false;
        Rune rune = runes.Current;
        return !runes.MoveNext() &&
            (TerminalGlyphWarpPolicy.IsPowerlineSeparator(rune) || AdditionalGlyphWarpPredicate?.Invoke(rune) == true);
    }

    public bool HandleKey(KeyEvent e)
    {
        if (_disposed) return false;
        if (e.Ctrl && e.Key == Key.C && Selection is { IsEmpty: false }) { CopySelection(); return true; }
        if (e.Ctrl && e.Key == Key.V) { Paste(); return true; }
        if (e.Ctrl && TryControlByte(e.Key, out byte control)) { Send(new byte[] { control }); return true; }
        if (!TryMapKey(e.Key, out TerminalKey key)) return false;
        Send(TerminalKeyEncoder.Encode(new TerminalKeyEvent(key, e.Shift, e.Alt, e.Ctrl), _session.Buffer.ApplicationCursorKeys));
        return true;
    }

    public void HandleText(string text)
    {
        if (!string.IsNullOrEmpty(text)) Send(TerminalKeyEncoder.EncodeText(text));
    }

    public void SetComposition(ImeComposition composition)
    {
        _composition = composition;
        MarkNeedsRealize();
    }

    public void CommitComposition(string text)
    {
        _composition = default;
        if (!string.IsNullOrEmpty(text)) HandleText(text);
        MarkNeedsRealize();
    }

    public void CopySelection()
    {
        string text = TerminalViewport.ExtractSelection(_session.Snapshot(), Selection);
        if (!string.IsNullOrEmpty(text)) _clipboard.SetText(text);
    }

    public void Paste()
    {
        string? text = _clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;
        TerminalSnapshot snapshot = _session.Snapshot();
        Send(TerminalKeyEncoder.EncodePaste(text.Replace("\r\n", "\n"), snapshot.BracketedPaste));
    }

    public void ClearSelection() { Selection = null; MarkNeedsRealize(); }

    public void ScrollBy(int lines)
    {
        TerminalSnapshot snapshot = _session.Snapshot();
        int max = Math.Max(0, snapshot.Scrollback.Count + snapshot.Lines.Count - Math.Max(1, _rows));
        _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, max);
        MarkNeedsRealize();
    }

    public ValueTask ResizeViewportAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        _columns = Math.Max(1, columns); _rows = Math.Max(1, rows);
        return _session.ResizeAsync(_columns, _rows, cancellationToken);
    }

    private void OnSessionUpdated() => Interlocked.Exchange(ref _sessionDirty, 1);

    private void OnScroll(float delta) => ScrollBy(delta > 0 ? 3 : delta < 0 ? -3 : 0);

    private void BeginSelection(TerminalSnapshot snapshot, float x, float y)
    {
        TerminalPoint point = PointFromLocal(snapshot, x, y);
        Selection = new TerminalSelection(point, point);
        MarkNeedsRealize();
    }

    private void ExtendSelection(TerminalSnapshot snapshot, float x, float y)
    {
        TerminalPoint point = PointFromLocal(snapshot, x, y);
        Selection = Selection is { } s ? s with { Active = point } : new TerminalSelection(point, point);
        MarkNeedsRealize();
    }

    private TerminalPoint PointFromLocal(TerminalSnapshot snapshot, float x, float y)
    {
        int total = snapshot.Scrollback.Count + snapshot.Lines.Count;
        int rows = Math.Max(1, _rows);
        int start = Math.Max(0, total - rows - Math.Clamp(_scrollOffset, 0, Math.Max(0, total - rows)));
        int line = Math.Clamp(start + (int)(y / MathF.Max(1, CellHeight)), 0, Math.Max(0, total - 1));
        int column = Math.Clamp((int)(x / MathF.Max(1, CellWidth)), 0, snapshot.Columns);
        return new TerminalPoint(line, column);
    }

    private bool IsSelected(int line, int column, int width)
    {
        if (Selection is not { IsEmpty: false } selection) return false;
        (TerminalPoint start, TerminalPoint end) = selection.Ordered;
        if (line < start.Line || line > end.Line) return false;
        int from = line == start.Line ? start.Column : 0;
        int to = line == end.Line ? end.Column : int.MaxValue;
        return column + width > from && column < to;
    }

    private void DrawComposition(Scene2D scene, TerminalSnapshot snapshot, float cellW, float cellH)
    {
        if (_composition.IsEmpty || _scrollOffset != 0) return;
        int cells = Math.Max(1, _composition.Text.EnumerateRunes().Sum(r => Math.Max(0, TerminalCellWidth.GetWidth(r))));
        float x = snapshot.Cursor.Column * cellW, y = snapshot.Cursor.Row * cellH, width = cells * cellW;
        scene.FillRect(Palette.ImeBackground, x, y, width, cellH);
        VectorFont font = _fonts.Resolver.Resolve(_composition.Text);
        float baseline = y + (cellH - FontSize) * 0.5f + font.Ascent(FontSize);
        font.AppendText(scene, _composition.Text, x, baseline, FontSize, Palette.Foreground);
        scene.FillRect(Palette.ImeUnderline, x, y + cellH - 2, width, 1);
    }

    private void Send(ReadOnlyMemory<byte> bytes) => Observe(_session.SendAsync(bytes, _lifetime.Token));

    private async void Observe(ValueTask operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { InputError?.Invoke(ex); }
    }

    private static bool TryMapKey(Key key, out TerminalKey terminalKey)
    {
        terminalKey = key switch
        {
            Key.Enter => TerminalKey.Enter, Key.Escape => TerminalKey.Escape, Key.Backspace => TerminalKey.Backspace,
            Key.Tab => TerminalKey.Tab, Key.Up => TerminalKey.Up, Key.Down => TerminalKey.Down,
            Key.Left => TerminalKey.Left, Key.Right => TerminalKey.Right, Key.Home => TerminalKey.Home, Key.End => TerminalKey.End,
            Key.Delete => TerminalKey.Delete, Key.PageUp => TerminalKey.PageUp, Key.PageDown => TerminalKey.PageDown,
            Key.F1 => TerminalKey.F1, Key.F2 => TerminalKey.F2, Key.F3 => TerminalKey.F3, Key.F4 => TerminalKey.F4,
            Key.F5 => TerminalKey.F5, Key.F6 => TerminalKey.F6, Key.F7 => TerminalKey.F7, Key.F8 => TerminalKey.F8,
            Key.F9 => TerminalKey.F9, Key.F10 => TerminalKey.F10, Key.F11 => TerminalKey.F11, Key.F12 => TerminalKey.F12,
            _ => default,
        };
        return key is Key.Enter or Key.Escape or Key.Backspace or Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Home or Key.End or Key.Delete or Key.PageUp or Key.PageDown
            or >= Key.F1 and <= Key.F12;
    }

    private static bool TryControlByte(Key key, out byte value)
    {
        value = key switch
        {
            >= Key.A and <= Key.I => (byte)(key - Key.A + 1),
            Key.J => 10, Key.K => 11, Key.L => 12, Key.M => 13, Key.N => 14, Key.O => 15, Key.P => 16,
            Key.Q => 17, Key.R => 18, Key.S => 19, Key.T => 20, Key.U => 21, Key.V => 22, Key.W => 23,
            Key.X => 24, Key.Y => 25, Key.Z => 26, Key.Slash => 31,
            _ => 0,
        };
        return value != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Updated -= OnSessionUpdated;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_ownsFonts) _fonts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_ownsSession) await _session.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class TerminalTextInput(TerminalView owner) : ITextInput
    {
        public string Text => owner._composition.Text ?? string.Empty;
        public (int start, int length) Selection => (owner._composition.Caret, 0);
        public void Select(int start, int end) => owner.SetComposition(owner._composition with { Caret = Math.Clamp(end, 0, Text.Length) });
        public void Replace(int start, int end, string s) => owner.SetComposition(new ImeComposition(s, s.Length));
        public void SetComposition(ImeComposition comp) => owner.SetComposition(comp);
        public void CommitComposition(string final) => owner.CommitComposition(final);
        public Rect CaretRect => new(owner.WorldPos.X + owner._session.Snapshot().Cursor.Column * owner.CellWidth,
            owner.WorldPos.Y + owner._session.Snapshot().Cursor.Row * owner.CellHeight, owner.CellWidth, owner.CellHeight);
    }
}
