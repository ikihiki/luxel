using Luxel.Document;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// 複数行プレーンテキスト編集 (ED-M2)。<see cref="DocumentEditor"/> が編集意味論を持ち、
/// 本 widget は「ブロック (段落) 毎の TextLayout + ノード」で表示する。
/// **部分更新が規律**: タイプでは編集されたブロックのノード Content だけ差し替え、
/// 高さが変わったら後続ブロックの transform を平行移動する (chrome の SetRoot はしない)。
/// ブロック増減 (Enter/行結合) はブロックノード列のみ作り直し。
/// ↑↓ は goal-x 保存 + TextLayout.HitTest (折返し行も正しい)、キャレット追従スクロール、
/// ブロック跨ぎドラッグ選択、IME はキャレットブロック内 (TSF 文書 = 現在ブロック)。
/// </summary>
[UiComponent]
public sealed partial class TextArea : Widget, ITextInput
{
    private readonly DocumentEditor _ed = new();
    private bool _edInit;          // value → _ed の初期反映は最初の Realize で一度だけ
    private readonly Signal<bool> _caretOn = new(true);
    private FocusTarget? _focus;   // Realize をまたいで同一インスタンス (フォーカス保存)
    private readonly Signal<float> _scroll = new(0);

    private const float DefaultWidth = 320f;
    private const float Pad = 10f;
    private float W = DefaultWidth;
    private float _fs = 16, _lineH = 1.35f;
    private float? _goalX;   // ↑↓ 移動の目標 x (水平移動/編集でリセット)

    /// <summary>値の signal (編集で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<string>> _value = new();
    /// <summary>表示高さ (px)。40 未満は 40 に切り上げ。</summary>
    [UiParam] private readonly Bindable<float> _height = 160f;
    /// <summary>文字サイズ。未設定 → テーマ Font。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();
    /// <summary>背景色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();
    /// <summary>グリフ未収載時のフォールバック (RichTextView と同じ流儀)。null = ctx.Font のみ。</summary>
    public FontCollection? Fonts { get; set; }

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _content = null!, _selNode = null!, _caretNode = null!;
    private UiNode _underlineNode = null!, _targetNode = null!;
    private Rect _caretLocal;   // content ローカルのキャレット矩形 (TSF/追従スクロール用)

    /// <summary>ブロック 1 つ分の表示状態 (部分更新の単位)。</summary>
    private sealed class BlockView
    {
        public required UiNode Node;
        public required TextLayout Layout;
        public required string Disp;   // レイアウト済み表示文字列 (composition 込み) — 差分検出キー
        public float Top;              // content ローカル y
        public float H;
    }

    private readonly List<BlockView> _views = new();
    private int _structSeen = -1;
    private uint _textColor = Color2D.White;

    public override string? DebugDetail => $"{_ed.Doc.Blocks.Count} ブロック";

    /// <summary>解決済みの表示高さ (旧 ctor の 40 クランプは読み出し時に適用)。</summary>
    private float HeightPx => MathF.Max(40, Height.Get());

    private float ContentH => (_views.Count > 0 ? _views[^1].Top + _views[^1].H : 0) + Pad;
    private float MaxScroll => MathF.Max(0, ContentH - HeightPx);
    private float Clamped() => Math.Clamp(_scroll.Value, 0, MaxScroll);

    private void CacheMetrics(Theme t) => _fs = FontSize.Or(t.Font);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        CacheMetrics(ctx.Theme);
        W = ResolveW(c, ctx, DefaultWidth);
        float w = HAlign.Get() == Align.Stretch && !float.IsInfinity(c.MaxW) ? c.MaxW : W;
        Size = c.Constrain(new Size(w, HeightPx));
        W = Size.Width;
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _theme = ctx.Theme;
        CacheMetrics(_theme.Peek());
        Signal<string> value = Value.Get();
        if (!_edInit) { _edInit = true; _ed.SetText(value.Peek()); }   // 旧 ctor の初期反映 (一度きり)
        float height = HeightPx;
        _views.Clear();
        _structSeen = -1;

        _root = ctx.Canvas.AddChild(parent);
        _root.Transform = Affine2D.Translate(Offset.X, Offset.Y);
        SetWorldPos(worldOrigin + Offset);

        // クリップは内側コンテナに掛ける — _root に掛けると FocusRing (Z=-1 の面塗り、
        // 通常は不透明背景の背後で外周だけ見える) がクリップレイヤ内で内側全面に被る。
        FocusRing.Add(ctx, _root, -3, -3, W + 6, height + 6, 9, Focused);

        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, height, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = Background.Or(_theme.Value.SurfaceAlt));

        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 1;
        clip.Clip = new RectClip(0, 0, W, height);

        _content = ctx.Canvas.AddChild(clip);
        ctx.Effect(() => _content.Transform = Affine2D.Translate(0, -Clamped()));

        _selNode = ctx.Canvas.AddChild(_content);
        _selNode.Z = 1;
        ctx.Effect(() => _selNode.Color = Styles.WithAlpha(_theme.Value.Primary, 70));
        _targetNode = ctx.Canvas.AddChild(_content);
        _targetNode.Z = 1;
        ctx.Effect(() => _targetNode.Color = Styles.WithAlpha(_theme.Value.Primary, 55));
        _underlineNode = ctx.Canvas.AddChild(_content);
        _underlineNode.Z = 1;
        ctx.Effect(() => _underlineNode.Color = _theme.Value.TextMuted);

        _caretNode = ctx.Canvas.AddChild(_content);
        _caretNode.Z = 3;
        ctx.Effect(() => _caretNode.Color = _theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value ? 1f : 0f);

        // ブロックテキスト色は単一 Effect で全ノードへ (ListView と同じ流儀 — ブロック毎に Effect を作らない)
        ctx.Effect(() =>
        {
            _textColor = _theme.Value.Text;
            foreach (BlockView v in _views) v.Node.Color = _textColor;
        });

        Refresh();
        ctx.Effect(() =>
        {
            string v = value.Value;
            if (v != _ed.Doc.PlainText) { _ed.SetText(v); _scroll.Value = 0; Refresh(); }
        });

        float t = 0;
        ctx.AddAnimation(dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; });

        // FocusTarget は同一インスタンスを再登録 — 再実体化をまたいでフォーカスが生き残る (TableBlock と同じ)
        _focus ??= new FocusTarget
        {
            OnFocus = on => { Focused.Value = on; if (on) _caretOn.Value = true; },
            OnKey = OnKey,
            OnText = s => { _ed.Insert(s); _goalX = null; Sync(); },
            OnComposeEx = c => { _ed.SetComposition(c.Text, c.TargetStart, c.TargetLen); Refresh(); EnsureCaretVisible(); },
            OnCommit = s => { _ed.CommitComposition(s); _goalX = null; Sync(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        // クリック/ドラッグ (ブロック跨ぎ選択)。IME 変換中は位置操作しない (TextField と同じ)。
        void PlaceFromPoint(float lx, float ly, bool extend)
        {
            if (_ed.Composition.Length > 0) return;
            DocPos p = HitDoc(lx, ly + Clamped());
            _ed.Select(extend ? _ed.Anchor : p, p);
            _goalX = null;
            _caretOn.Value = true;
            Refresh();
        }
        ctx.AddHit(_root, new Rect(0, 0, W, height), focus: f,
            onDragStart: e => { PlaceFromPoint(e.X, e.Y, extend: false); MultiClick(e.X, e.Y); },
            onDrag: e => PlaceFromPoint(e.X, e.Y, extend: true),
            cursor: CursorKind.IBeam,
            onContext: e => ContextMenu.OpenForEditor(ctx, _root, e.X, e.Y, f));
        ctx.AddScroll(_root, new Rect(0, 0, W, height),
            d => _scroll.Value = Math.Clamp(Clamped() - d, 0, MaxScroll));
    }

    // ---- 入力 ----

    private bool OnKey(KeyEvent ev)
    {
        switch (ev.Key)
        {
            case Key.Left: _ed.MoveLeft(ev.Shift); _goalX = null; break;
            case Key.Right: _ed.MoveRight(ev.Shift); _goalX = null; break;
            case Key.Up: MoveVertical(-1, ev.Shift); break;
            case Key.Down: MoveVertical(+1, ev.Shift); break;
            case Key.Home: LineHomeEnd(home: true, ev.Shift); _goalX = null; break;
            case Key.End: LineHomeEnd(home: false, ev.Shift); _goalX = null; break;
            case Key.Enter: _ed.InsertNewline(); _goalX = null; Sync(); return true;
            case Key.Backspace: _ed.Backspace(); _goalX = null; Sync(); return true;
            case Key.Delete: _ed.DeleteForward(); _goalX = null; Sync(); return true;
            case Key.A when ev.Ctrl: _ed.SelectAll(); break;
            case Key.Z when ev.Ctrl: _ed.Undo(); _goalX = null; Sync(); return true;
            case Key.Y when ev.Ctrl: _ed.Redo(); _goalX = null; Sync(); return true;
            case Key.C when ev.Ctrl: CopySelection(); return true;
            case Key.X when ev.Ctrl: if (CopySelection()) { _ed.Backspace(); _goalX = null; Sync(); } return true;
            case Key.V when ev.Ctrl:
                if (UiClipboard.Instance?.GetText() is string paste && paste.Length > 0)
                { _ed.Insert(paste); _goalX = null; Sync(); }
                return true;
            default: return false;
        }
        _caretOn.Value = true;
        Refresh();
        EnsureCaretVisible();
        return true;
    }

    /// <summary>↑↓: goal-x を保存して行間移動 (折返し行 → 同ブロック内、端 → 隣ブロック)。</summary>
    private void MoveVertical(int dir, bool select)
    {
        int bi = _ed.Caret.Line;
        BlockView v = _views[bi];
        TextRect cr = v.Layout.CaretRect(_ed.DisplayCaretOffset);
        float x = _goalX ?? cr.X;
        _goalX = x;
        float adv = v.Layout.LineAdvance;
        int line = LineIndexOf(v.Layout, cr.Y);

        int targetBlock;
        float targetY;
        if (dir < 0)
        {
            if (line > 0) { targetBlock = bi; targetY = (line - 1) * adv + adv / 2; }
            else if (bi > 0)
            {
                targetBlock = bi - 1;
                TextLayout pl = _views[targetBlock].Layout;
                targetY = (pl.LineCount - 1) * pl.LineAdvance + pl.LineAdvance / 2;
            }
            else return;
        }
        else
        {
            if (line < v.Layout.LineCount - 1) { targetBlock = bi; targetY = (line + 1) * adv + adv / 2; }
            else if (bi < _views.Count - 1) { targetBlock = bi + 1; targetY = _views[targetBlock].Layout.LineAdvance / 2; }
            else return;
        }

        int idx = Math.Clamp(_views[targetBlock].Layout.HitTest(x, targetY), 0, _ed.Doc.Blocks[targetBlock].Length);
        var p = new DocPos(targetBlock, idx);
        _ed.Select(select ? _ed.Anchor : p, p);
    }

    /// <summary>Home/End は表示行 (折返し) 単位。</summary>
    private void LineHomeEnd(bool home, bool select)
    {
        int bi = _ed.Caret.Line;
        TextLayout l = _views[bi].Layout;
        int line = LineIndexOf(l, l.CaretRect(_ed.DisplayCaretOffset).Y);
        (int start, int end) = l.LineCharRange(line);
        var p = new DocPos(bi, Math.Clamp(home ? start : end, 0, _ed.Doc.Blocks[bi].Length));
        _ed.Select(select ? _ed.Anchor : p, p);
    }

    private static int LineIndexOf(TextLayout l, float y)
        => Math.Clamp((int)MathF.Round(y / l.LineAdvance), 0, l.LineCount - 1);

    /// <summary>content ローカル座標 → 文書位置 (y でブロック特定 → TextLayout.HitTest)。</summary>
    private DocPos HitDoc(float lx, float ly)
    {
        int bi = _views.Count - 1;
        for (int i = 0; i < _views.Count; i++)
            if (ly < _views[i].Top + _views[i].H) { bi = i; break; }
        if (bi < 0) return new DocPos(0, 0);
        BlockView v = _views[bi];
        int idx = Math.Clamp(v.Layout.HitTest(lx - Pad, ly - v.Top), 0, _ed.Doc.Blocks[bi].Length);
        return new DocPos(bi, idx);
    }

    private void Sync()
    {
        Value.Get().Value = _ed.Doc.PlainText;
        _caretOn.Value = true;
        Refresh();
        EnsureCaretVisible();
    }

    private bool CopySelection()
    {
        if (!_ed.HasSelection || UiClipboard.Instance is not IClipboard clip) return false;
        clip.SetText(_ed.GetText(_ed.SelMin, _ed.SelMax));
        return true;
    }

    /// <summary>ダブル = 単語 (Segmenter.GetWordAt)、トリプル = ブロック選択。onDragStart で呼ぶ。</summary>
    private DateTime _lastDown;
    private float _lastDownX, _lastDownY;
    private int _clickCount;

    private void MultiClick(float lx, float ly)
    {
        DateTime now = DateTime.UtcNow;
        bool near = MathF.Abs(lx - _lastDownX) < 4 && MathF.Abs(ly - _lastDownY) < 4;
        _clickCount = near && (now - _lastDown) < TimeSpan.FromMilliseconds(500) ? _clickCount + 1 : 1;
        _lastDown = now;
        _lastDownX = lx;
        _lastDownY = ly;
        if (_clickCount < 2) return;
        int bi = _ed.Caret.Line;
        string text = _ed.Doc.Blocks[bi].Text;
        if (_clickCount == 2 && text.Length > 0)
        {
            (int ws, int we) = TextSegmenter.Default.GetWordAt(text, Math.Min(_ed.Caret.Offset, text.Length - 1));
            _ed.Select(new DocPos(bi, ws), new DocPos(bi, we));
        }
        else
        {
            _ed.Select(new DocPos(bi, 0), new DocPos(bi, text.Length));
            _clickCount = 0;
        }
        Refresh();
    }

    // ---- 表示 (部分更新) ----

    private TextLayout Build(string disp)
    {
        var opt = new TextLayoutOptions { MaxWidth = W - Pad * 2, Wrap = TextWrap.Word, LineHeight = _lineH };
        return Fonts is null
            ? new TextLayout(_ctx.Font, disp, _fs, opt)
            : new TextLayout(Fonts, [new TextSpan(disp)], _fs, opt);
    }

    private float BlockHeight(TextLayout l) => MathF.Max(l.Height, l.LineAdvance);

    /// <summary>差分更新: 構造変化 → ノード列再構築、テキスト変化 → 該当ブロックの Content 差替え +
    /// 高さ変化分だけ後続を平行移動。キャレット/選択/IME 装飾は毎回更新 (軽量ノード)。</summary>
    private void Refresh()
    {
        if (_structSeen != _ed.StructureVersion) RebuildBlocks();
        else
        {
            bool shifted = false;
            float top = Pad;
            for (int i = 0; i < _views.Count; i++)
            {
                BlockView v = _views[i];
                string disp = _ed.DisplayTextOf(i);
                if (v.Disp != disp)
                {
                    v.Layout = Build(disp);
                    v.Disp = disp;
                    var s = new Scene2D();
                    v.Layout.Draw(s, Pad, 0, Color2D.White);
                    v.Node.Content = s;
                    float h = BlockHeight(v.Layout);
                    if (h != v.H) { v.H = h; shifted = true; }
                }
                if (shifted || v.Top != top) { v.Top = top; v.Node.Transform = Affine2D.Translate(0, top); }
                top += v.H;
            }
        }
        RefreshDecorations();
        _scroll.Value = Clamped();   // 内容が縮んだら範囲内へ
    }

    /// <summary>ブロックノード列を作り直す (Enter/行結合/SetText 時のみ)。</summary>
    private void RebuildBlocks()
    {
        _structSeen = _ed.StructureVersion;
        foreach (BlockView v in _views) _ctx.Canvas.Remove(v.Node);
        _views.Clear();

        float top = Pad;
        for (int i = 0; i < _ed.Doc.Blocks.Count; i++)
        {
            string disp = _ed.DisplayTextOf(i);
            TextLayout l = Build(disp);
            UiNode n = _ctx.Canvas.AddChild(_content);
            n.Z = 2;
            n.Transform = Affine2D.Translate(0, top);
            var s = new Scene2D();
            l.Draw(s, Pad, 0, Color2D.White);
            n.Content = s;
            n.Color = _textColor;
            var v = new BlockView { Node = n, Layout = l, Disp = disp, Top = top, H = BlockHeight(l) };
            _views.Add(v);
            top += v.H;
        }
    }

    /// <summary>キャレット/選択/IME 下線 (専用ノードの Content/transform のみ)。</summary>
    private void RefreshDecorations()
    {
        int cb = _ed.Caret.Line;
        BlockView cv = _views[cb];
        TextRect cr = cv.Layout.CaretRect(_ed.DisplayCaretOffset);
        _caretLocal = new Rect(Pad + cr.X, cv.Top + cr.Y, 2, cr.Height);
        var cs = new Scene2D();
        cs.FillRect(Color2D.White, 0, 0, 2, cr.Height);
        _caretNode.Content = cs;
        _caretNode.Transform = Affine2D.Translate(_caretLocal.X, _caretLocal.Y);

        // 選択 (ブロック跨ぎ): ブロック毎に SelectionRects、中間ブロックは改行分の幅を足す
        var sel = new Scene2D();
        if (_ed.HasSelection && _ed.Composition.Length == 0)
        {
            DocPos a = _ed.SelMin, b = _ed.SelMax;
            for (int i = a.Line; i <= b.Line; i++)
            {
                BlockView v = _views[i];
                int s0 = i == a.Line ? a.Offset : 0;
                int s1 = i == b.Line ? b.Offset : _ed.Doc.Blocks[i].Length;
                foreach (TextRect r in v.Layout.SelectionRects(s0, s1))
                    sel.FillRect(Color2D.White, Pad + r.X, v.Top + r.Y, r.Width, r.Height);
                if (i < b.Line)   // 改行の選択を可視化 (行末に小さな延長)
                {
                    TextRect e = v.Layout.CaretRect(_ed.Doc.Blocks[i].Length);
                    sel.FillRect(Color2D.White, Pad + e.X, v.Top + e.Y, _fs * 0.4f, e.Height);
                }
            }
        }
        _selNode.Content = sel;

        // IME: preedit 下線 + 変換対象節 (キャレットブロック内)。実 IME (TSF) は装飾のみの通知 (_hl*)
        var us = new Scene2D();
        var tg = new Scene2D();
        (int is0, int il) = _ed.CompositionDisplayRange;
        (int t0, int tl) = _ed.TargetDisplayRange;
        if (il == 0 && _hlLen > 0) { is0 = _hlStart; il = _hlLen; t0 = _hlTargetStart; tl = _hlTargetLen; }
        int dispLen = _ed.DisplayTextOf(cb).Length;
        is0 = Math.Clamp(is0, 0, dispLen);
        il = Math.Clamp(il, 0, dispLen - is0);
        if (il > 0)
        {
            foreach (TextRect r in cv.Layout.SelectionRects(is0, is0 + il))
                us.FillRect(Color2D.White, Pad + r.X, cv.Top + r.Y + r.Height - 2, r.Width, 2);
            if (tl > 0)
                foreach (TextRect r in cv.Layout.SelectionRects(t0, t0 + tl))
                    tg.FillRect(Color2D.White, Pad + r.X, cv.Top + r.Y, r.Width, r.Height);
        }
        _underlineNode.Content = us;
        _targetNode.Content = tg;
    }

    private int _hlStart, _hlLen, _hlTargetStart, _hlTargetLen;   // TSF display attribute 由来の装飾

    /// <summary>キャレットが見える位置までスクロールを合わせる (編集/移動で呼ぶ)。</summary>
    private void EnsureCaretVisible()
    {
        float height = HeightPx;
        float top = _caretLocal.Y, bottom = _caretLocal.Y + _caretLocal.Height;
        float s = Clamped();
        if (top - s < Pad) s = MathF.Max(0, top - Pad);
        else if (bottom - s > height - Pad) s = bottom - height + Pad;
        _scroll.Value = Math.Clamp(s, 0, MaxScroll);
    }

    // ---- ITextInput (TSF/IME ブリッジ — 文書 = 現在ブロック) ----
    string ITextInput.Text => _ed.CurrentBlockText;
    (int start, int length) ITextInput.Selection => _ed.SelectionInBlock;
    void ITextInput.Select(int start, int end) { _ed.SelectInBlock(start, end); Refresh(); }
    void ITextInput.Replace(int start, int end, string s) { _ed.ReplaceInBlock(start, end, s); _goalX = null; Sync(); }
    void ITextInput.SetComposition(ImeComposition comp)
    { _ed.SetComposition(comp.Text, comp.TargetStart, comp.TargetLen); Refresh(); EnsureCaretVisible(); }
    void ITextInput.CommitComposition(string final) { _ed.CommitComposition(final); _goalX = null; Sync(); }
    void ITextInput.SetCompositionHighlight(int start, int length, int targetStart, int targetLength)
    {
        _hlStart = start; _hlLen = length; _hlTargetStart = targetStart; _hlTargetLen = targetLength;
        Refresh();
    }
    Rect ITextInput.CaretRect => new(WorldPos.X + _caretLocal.X, WorldPos.Y + _caretLocal.Y - Clamped(), 2, _caretLocal.Height);
}
