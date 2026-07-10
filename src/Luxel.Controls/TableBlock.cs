using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// テーブル埋め込みブロックの標準 widget (GFM pipe table)。グリッド描画 (罫線 + ヘッダ地色 + 列整列) と
/// **セルの in-place 編集**: クリックでセル選択 + 編集開始、Tab/Shift+Tab/Enter/↑↓ でセル移動、
/// 最下段で Enter → 行追加、空行の先頭で Backspace → 行削除、Esc で取消。
/// 編集はローカルコピー上で行い、**フォーカスを失ったとき 1 op として Commit** (ReplacePayload = undo 可) —
/// セル間移動のたびに undo が刻まれない。<see cref="ITextInput"/> 実装 (編集中セル = TSF 文書) で日本語入力可。
/// 登録: <c>widgets.Register("table", TableBlocks.Factory())</c>。
/// </summary>
[UiComponent]
public sealed partial class TableBlock : Widget, ITextInput
{
    /// <summary>テーブル embed の payload (Rows/Aligns/Columns)。</summary>
    [UiParam] private readonly Bindable<TablePayload> _payload = new();
    /// <summary>使える最大幅 (最小 80)。収まらない列は比例縮小。</summary>
    [UiParam] private readonly Bindable<float> _maxWidth = new();
    /// <summary>フォーカス喪失時に編集結果を 1 op として通知する commit。</summary>
    [UiParam] private readonly Bindable<Action<TablePayload>> _commit = new();

    private List<string[]>? _rowsData;   // ローカル編集コピー (初回参照で payload から複製)
    private TableAlign[]? _alignsData;

    private TablePayload Pl => Payload.Get();
    private float MaxW => MathF.Max(80, MaxWidth.Get());
    private List<string[]> _rows => _rowsData ??= BuildRows();
    private TableAlign[] _aligns => _alignsData ??= (TableAlign[])Pl.Aligns.Clone();

    private List<string[]> BuildRows()
    {
        TablePayload payload = Pl;
        var rows = payload.Rows.Select(r => Pad(r, payload.Columns)).ToList();
        if (rows.Count == 0) rows.Add(new string[Math.Max(1, payload.Columns)]);
        return rows;

        static string[] Pad(string[] r, int cols)
        {
            var a = new string[Math.Max(cols, r.Length)];
            for (int i = 0; i < a.Length; i++) a[i] = i < r.Length ? r[i] : "";
            return a;
        }
    }

    private int _selR = -1, _selC = -1;
    private readonly TextEditor _ed = new();
    private float[] _colW = [];
    private float _rowH = 26, _fs = 14, _ascent;
    private const float CellPadX = 8;

    private UiBuildContext _ctx = null!;
    private UiNode _root = null!, _gridNode = null!, _textNode = null!, _selNode = null!, _caretNode = null!;
    private UiNode _imeTarget = null!, _imeUnderline = null!;
    private readonly Signal<bool> _caretOn = new(true);
    private FocusTarget? _focus;   // Realize をまたいで同一インスタンス — 再実体化 (行増減) でフォーカスが生き残る

    private int Cols => _aligns.Length > 0 ? _aligns.Length : _rows.Max(r => r.Length);
    public override string? DebugDetail => $"{_rows.Count}x{Cols}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _fs = ctx.Theme.FontSm + 1;
        _rowH = _fs * 1.9f;
        _colW = new float[Cols];
        for (int col = 0; col < Cols; col++)
        {
            float w = 40;
            foreach (string[] r in _rows)
                if (col < r.Length)
                    w = MathF.Max(w, ctx.Font.Measure(r[col], _fs).width + CellPadX * 2);
            _colW[col] = w;
        }
        float total = _colW.Sum() + 1;
        if (total > MaxW)   // 収まらないときは比例縮小
        {
            float k = (MaxW - 1) / (total - 1);
            for (int i = 0; i < _colW.Length; i++) _colW[i] *= k;
            total = MaxW;
        }
        Size = c.Constrain(new Size(total, _rows.Count * _rowH + 1));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _ascent = ctx.Font.Ascent(_fs);

        _root = ctx.Canvas.AddChild(parent);
        _root.Transform = Affine2D.Translate(Offset.X, Offset.Y);
        SetWorldPos(worldOrigin + Offset);

        // ヘッダ帯 (行 0)
        var hb = new Scene2D();
        hb.FillRect(Color2D.White, 0, 0, Size.Width, _rowH);
        _root.Content = hb;
        ctx.Effect(() => _root.Color = ctx.Theme.Value.SurfaceAlt);

        _selNode = ctx.Canvas.AddChild(_root);
        _selNode.Z = 1;
        ctx.Effect(() => _selNode.Color = Styles.WithAlpha(ctx.Theme.Value.Primary, 40));
        _imeTarget = ctx.Canvas.AddChild(_root);
        _imeTarget.Z = 1;
        ctx.Effect(() => _imeTarget.Color = Styles.WithAlpha(ctx.Theme.Value.Primary, 55));

        _gridNode = ctx.Canvas.AddChild(_root);
        _gridNode.Z = 2;
        ctx.Effect(() => _gridNode.Color = ctx.Theme.Value.BorderColor);

        _textNode = ctx.Canvas.AddChild(_root);
        _textNode.Z = 3;
        ctx.Effect(() => _textNode.Color = ctx.Theme.Value.Text);
        _imeUnderline = ctx.Canvas.AddChild(_root);
        _imeUnderline.Z = 3;
        ctx.Effect(() => _imeUnderline.Color = ctx.Theme.Value.TextMuted);

        _caretNode = ctx.Canvas.AddChild(_root);
        _caretNode.Z = 4;
        ctx.Effect(() => _caretNode.Color = ctx.Theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value && _selR >= 0 ? 1f : 0f);

        float t = 0;
        ctx.AddAnimation(dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; });

        _focus ??= new FocusTarget
        {
            OnFocus = on =>
            {
                Focused.Value = on;
                if (!on) { CommitCell(); CommitIfDirty(); }
                else if (_selR < 0) SelectCell(0, 0);
            },
            OnKey = OnKey,
            OnText = s => { _ed.Insert(s); Refresh(); },
            OnComposeEx = c => { _ed.SetComposition(c.Text, c.TargetStart, c.TargetLen); Refresh(); },
            OnCommit = s => { _ed.CommitComposition(s); Refresh(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        ctx.AddHit(_root, new Rect(0, 0, Size.Width, Size.Height), focus: f,
            cursor: CursorKind.IBeam,
            onDragStart: e =>
            {
                (int r, int c2) = HitCell(e.X, e.Y);
                if (r == _selR && c2 == _selC) { PlaceCaret(e.X); return; }
                CommitCell();
                SelectCell(r, c2);
                PlaceCaret(e.X);
            });

        Refresh();
    }

    // ---- セル選択/編集 ----

    private (int r, int c) HitCell(float lx, float ly)
    {
        int r = Math.Clamp((int)(ly / _rowH), 0, _rows.Count - 1);
        float x = 0;
        for (int c = 0; c < Cols; c++)
        {
            x += _colW[c];
            if (lx < x) return (r, c);
        }
        return (r, Cols - 1);
    }

    private void SelectCell(int r, int c)
    {
        _selR = Math.Clamp(r, 0, _rows.Count - 1);
        _selC = Math.Clamp(c, 0, Cols - 1);
        _ed.SetText(Cell(_selR, _selC));
        _ed.End(false);
        _caretOn.Value = true;
        Refresh();
    }

    private string Cell(int r, int c) => c < _rows[r].Length ? _rows[r][c] : "";

    private void PlaceCaret(float lx)
    {
        if (_selR < 0) return;
        float cellX = _colW.Take(_selC).Sum();
        float local = lx - cellX - CellPadX - AlignOffset(_selR, _selC);
        // 簡易ヒット: 文字境界の中点で吸着 (セルは 1 行プレーン)
        string text = _ed.Text;
        int idx = text.Length;
        float prev = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            float wi = _ctx.Font.Measure(text[..i], _fs).width;
            if (local < (prev + wi) / 2) { idx = i - 1; break; }
            prev = wi;
        }
        if (local <= 0) idx = 0;
        _ed.Select(idx, idx);
        _caretOn.Value = true;
        Refresh();
    }

    private void CommitCell()
    {
        if (_selR < 0) return;
        if (_selC < _rows[_selR].Length) _rows[_selR][_selC] = _ed.Text;
    }

    private void CommitIfDirty()
    {
        bool dirty = _rows.Count != Pl.Rows.Count;
        if (!dirty)
            for (int r = 0; r < _rows.Count && !dirty; r++)
                for (int c = 0; c < Cols && !dirty; c++)
                    dirty = Cell(r, c) != Pl.Cell(r, c);
        if (!dirty) return;
        Commit.Get()?.Invoke(new TablePayload(_rows.Select(r => (string[])r.Clone()).ToList(), (TableAlign[])_aligns.Clone()));
    }

    private bool OnKey(KeyEvent ev)
    {
        if (_selR < 0) return false;
        switch (ev.Key)
        {
            case Key.Left: _ed.MoveLeft(ev.Shift); break;
            case Key.Right: _ed.MoveRight(ev.Shift); break;
            case Key.Home: _ed.Home(ev.Shift); break;
            case Key.End: _ed.End(ev.Shift); break;
            case Key.Delete: _ed.DeleteForward(); break;
            case Key.Backspace:
                // 空行の先頭で Backspace → 行削除 (ヘッダは残す)
                if (_ed.Caret == 0 && _ed.Text.Length == 0 && _rows.Count > 1 && _selR > 0
                    && _rows[_selR].All(string.IsNullOrEmpty))
                {
                    _rows.RemoveAt(_selR);
                    SelectCell(_selR - 1, _selC);
                    MarkNeedsRealize();   // 高さ変化 — 同一インスタンス再ホスト (選択/フォーカスは保持)
                    return true;
                }
                _ed.Backspace();
                break;
            case Key.Tab:
                CommitCell();
                if (!ev.Shift && _selC == Cols - 1 && _selR == _rows.Count - 1) { AddRow(); MarkNeedsRealize(); }
                Move(ev.Shift ? -1 : +1, 0);
                return true;
            case Key.Enter:
                CommitCell();
                if (_selR == _rows.Count - 1) { AddRow(); MarkNeedsRealize(); }
                SelectCell(_selR + 1, _selC);
                return true;
            case Key.Up: CommitCell(); SelectCell(_selR - 1, _selC); return true;
            case Key.Down: CommitCell(); SelectCell(_selR + 1, _selC); return true;
            case Key.Escape: _ed.SetText(Cell(_selR, _selC)); _ed.End(false); Refresh(); return true;
            default: return false;
        }
        _caretOn.Value = true;
        Refresh();
        return true;
    }

    /// <summary>セルを水平/垂直に移動 (行末→次行頭へ折返し)。</summary>
    private void Move(int dc, int dr)
    {
        int r = _selR, c = _selC + dc;
        if (c >= Cols) { c = 0; r++; }
        else if (c < 0) { c = Cols - 1; r--; }
        r += dr;
        if (r < 0 || r >= _rows.Count) { SelectCell(_selR, _selC); return; }
        SelectCell(r, c);
    }

    private void AddRow()
    {
        var row = new string[Cols];
        Array.Fill(row, "");
        _rows.Add(row);
    }

    // ---- 描画 ----

    private float AlignOffset(int r, int c)
    {
        TableAlign a = c < _aligns.Length ? _aligns[c] : TableAlign.None;
        if (a is TableAlign.None or TableAlign.Left) return 0;
        float text = _ctx.Font.Measure(DisplayCell(r, c), _fs).width;
        float space = _colW[c] - CellPadX * 2 - text;
        return a == TableAlign.Center ? MathF.Max(0, space / 2) : MathF.Max(0, space);
    }

    private string DisplayCell(int r, int c)
        => r == _selR && c == _selC ? _ed.Display : Cell(r, c);

    private void Refresh()
    {
        // 罫線 (格子)
        var gs = new Scene2D();
        float w = Size.Width, h = _rows.Count * _rowH + 1;
        for (int r = 0; r <= _rows.Count; r++) gs.FillRect(Color2D.White, 0, r * _rowH, w, 1);
        float x = 0;
        for (int c = 0; c <= Cols; c++)
        {
            gs.FillRect(Color2D.White, MathF.Min(x, w - 1), 0, 1, h);
            if (c < Cols) x += _colW[c];
        }
        _gridNode.Content = gs;

        // テキスト (整列込み。編集中セルは preedit 込み表示)
        var ts = new Scene2D();
        for (int r = 0; r < _rows.Count; r++)
        {
            float cx = 0;
            for (int c = 0; c < Cols; c++)
            {
                string text = DisplayCell(r, c);
                if (text.Length > 0)
                    _ctx.Font.AppendText(ts, text, cx + CellPadX + AlignOffset(r, c),
                        r * _rowH + (_rowH - _fs) / 2 + _ascent, _fs, Color2D.White);
                cx += _colW[c];
            }
        }
        _textNode.Content = ts;

        // 選択セル + キャレット + IME
        var ss = new Scene2D();
        var caret = new Scene2D();
        var tg = new Scene2D();
        var us = new Scene2D();
        if (_selR >= 0)
        {
            float cellX = _colW.Take(_selC).Sum();
            float cellY = _selR * _rowH;
            ss.FillRect(Color2D.White, cellX + 1, cellY + 1, _colW[_selC] - 1, _rowH - 1);

            float textX = cellX + CellPadX + AlignOffset(_selR, _selC);
            float caretX = textX + _ctx.Font.Measure(_ed.Display[.._ed.DisplayCaret], _fs).width;
            caret.FillRect(Color2D.White, caretX, cellY + (_rowH - _fs) / 2 - 1, 1.5f, _fs + 2);
            _caretWorld = new Rect(WorldPos.X + caretX, WorldPos.Y + cellY + (_rowH - _fs) / 2, 2, _fs);

            (int cs, int cl) = _ed.CompositionDisplayRange;
            (int t0, int tl) = _ed.TargetDisplayRange;
            if (cl == 0 && _hlLen > 0) { cs = _hlStart; cl = _hlLen; t0 = _hlTargetStart; tl = _hlTargetLen; }
            string disp = _ed.Display;
            cs = Math.Clamp(cs, 0, disp.Length);
            cl = Math.Clamp(cl, 0, disp.Length - cs);
            if (cl > 0)
            {
                float ux0 = textX + _ctx.Font.Measure(disp[..cs], _fs).width;
                float ux1 = textX + _ctx.Font.Measure(disp[..(cs + cl)], _fs).width;
                us.FillRect(Color2D.White, ux0, cellY + _rowH - 5, ux1 - ux0, 2);
                if (tl > 0)
                {
                    float tx0 = textX + _ctx.Font.Measure(disp[..t0], _fs).width;
                    float tx1 = textX + _ctx.Font.Measure(disp[..(t0 + tl)], _fs).width;
                    tg.FillRect(Color2D.White, tx0, cellY + 2, tx1 - tx0, _rowH - 4);
                }
            }
        }
        _selNode.Content = ss;
        _caretNode.Content = caret;
        _imeTarget.Content = tg;
        _imeUnderline.Content = us;
    }

    private Rect _caretWorld;
    private int _hlStart, _hlLen, _hlTargetStart, _hlTargetLen;

    // ---- ITextInput (TSF: 文書 = 編集中セル) ----
    string ITextInput.Text => _ed.Text;
    (int start, int length) ITextInput.Selection => (_ed.SelMin, _ed.SelMax - _ed.SelMin);
    void ITextInput.Select(int start, int end) { _ed.Select(start, end); Refresh(); }
    void ITextInput.Replace(int start, int end, string s) { _ed.Replace(start, end, s); Refresh(); }
    void ITextInput.SetComposition(ImeComposition comp) { _ed.SetComposition(comp.Text, comp.TargetStart, comp.TargetLen); Refresh(); }
    void ITextInput.CommitComposition(string final) { _ed.CommitComposition(final); Refresh(); }
    void ITextInput.SetCompositionHighlight(int start, int length, int targetStart, int targetLength)
    { _hlStart = start; _hlLen = length; _hlTargetStart = targetStart; _hlTargetLen = targetLength; Refresh(); }
    Rect ITextInput.CaretRect => _caretWorld;
}

