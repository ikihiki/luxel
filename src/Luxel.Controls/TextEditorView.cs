using Luxel.Editor;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// テキストエディタ新スタック (ADR-0006 / ToDo 22) の**ビュー** — canvas がないとできないことだけを担う薄い皮。
/// 編集意味論・座標写像・装飾は <see cref="EditorState"/> / <see cref="EditorGeometry"/> / <see cref="EditCommands"/>
/// (canvas 非依存の Luxel.Editor) が持ち、この widget は「入力を Transaction にし、ジオメトリが返す矩形を塗る」。
/// マルチカーソル描画・折返し・プロポーショナル/日本語/合字はジオメトリ由来で最初から正しい。装飾プロバイダ
/// (シンタックス色/診断/検索/再生囲み) や行内 widget は後続ステージがこの上に載る。
/// </summary>
[UiComponent]
public sealed partial class TextEditorView : Widget, ITextInput
{
    /// <summary>編集対象テキスト (双方向)。</summary>
    [UiParam] private readonly Bindable<Signal<string>> _value = new();
    /// <summary>表示高さ px (最小 40)。</summary>
    [UiParam] private readonly Bindable<float> _editorHeight = 200f;
    /// <summary>表示幅 px。</summary>
    [UiParam] private readonly Bindable<float> _editorWidth = new();
    /// <summary>基準文字サイズ (未設定 → テーマ FontSm)。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();

    /// <summary>フォント (null = テーマ Font)。</summary>
    public VectorFont? EditorFont { get; set; }
    /// <summary>フォールバック用フォント列 (null = EditorFont/ctx.Font 単体)。</summary>
    public FontCollection? Fonts { get; set; }
    /// <summary>折返し (既定 false = コード)。</summary>
    public bool WrapText { get; set; }

    /// <summary>行内 widget の解決 — 装飾の不透明キー → 実 Widget (null = 無視)。BlockWidgetRegistry と同じ流儀。
    /// view が生成・所有し、スロット矩形に Realize する。状態は外部 (signal) に持たせると再実体化で生き残る。</summary>
    public Func<object, Widget?>? WidgetResolver { get; set; }

    /// <summary>装飾プロバイダ — 状態が変わるたびに走らせ、結果を装飾として反映する
    /// (シンタックス色/診断/検索/再生囲みが後続ステージでここに載る)。</summary>
    public IList<IDecorationProvider> Providers => _providers;
    private readonly List<IDecorationProvider> _providers = new();

    // ---- モデル (canvas 非依存) ----
    private EditorState _state = EditorState.Create();
    private readonly History _history = new();
    private EditorGeometry? _geo;
    private bool _init;
    private float? _goalX;

    // ---- IME 変換中 (view ローカル、確定まで履歴に積まない) ----
    private string _compText = "";
    private int _compTargetStart, _compTargetLen;
    private bool Composing => _compText.Length > 0;

    private readonly ScrollModel _scroll = new();
    private readonly Signal<bool> _caretOn = new(true);

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _content = null!, _selNode = null!, _caretNode = null!;
    private UiNode _textLayer = null!, _overlayBg = null!, _overlayFg = null!;
    private readonly List<UiNode> _colorNodes = new();
    private FocusTarget? _focus;
    private Rect _caretLocal;

    private sealed class Hosted { public required Widget Widget; public required UiNode Container; public TextRect Rect; }
    private readonly Dictionary<object, Hosted> _widgets = new();

    private const float Pad = 6f;
    private float _fs = 13;

    private float W => MathF.Max(120, EditorWidth.Or(360));
    private float H => MathF.Max(40, EditorHeight.Get());

    public override string DebugType => "TextEditorView";
    public override string? DebugDetail => $"{_state.Doc.LineCount} 行";

    /// <summary>現在のテキスト。</summary>
    public string Text => _state.Doc.Text;
    /// <summary>キャレット (主レンジ head) のフラットオフセット。</summary>
    public int CaretOffset => _state.Selection.Main.Head;
    /// <summary>選択があるか。</summary>
    public bool HasSelection => !_state.Selection.Main.Empty;

    private void EnsureInit()
    {
        if (_init) return;
        _init = true;
        _state = EditorState.Create(Value.Get().Peek());
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        EnsureInit();
        _fs = FontSize.Or(ctx.Theme.FontSm);
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    private EditorConfig BuildConfig()
    {
        VectorFont baseFont = EditorFont ?? _ctx.Font;
        FontCollection fonts = Fonts ?? new FontCollection(baseFont);
        return new EditorConfig
        {
            Fonts = fonts,
            FontSize = _fs,
            Wrap = WrapText ? TextWrap.Word : TextWrap.None,
            MaxWidth = WrapText ? W - Pad * 2 : float.PositiveInfinity,
            LineHeight = 1.5f,
            DefaultColor = _theme.Peek().Text,
        };
    }

    // 変換中は composition を挿入した一時状態 + 下線/対象強調の装飾で描く (確定まで _state は不変)
    private EditorState Effective()
    {
        if (!Composing) return _state;
        int caret = _state.Selection.Main.From;
        int end = caret + _compText.Length;
        EditorState st = _state.Replace(caret, caret, _compText,
            EditorSelection.Cursor(caret + _compTargetStart + Math.Max(0, _compTargetLen))).State;
        var deco = new List<Decoration>
        {
            new MarkDecoration(caret, end, Underline: new UnderlineStyle(_theme.Peek().TextMuted)),
        };
        if (_compTargetLen > 0)
            deco.Add(new MarkDecoration(caret + _compTargetStart, caret + _compTargetStart + _compTargetLen,
                Background: Styles.WithAlpha(_theme.Peek().Primary, 55)));
        return st.WithDecorations("ime", new DecorationSet(deco)).State;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        EnsureInit();
        _ctx = ctx;
        _theme = ctx.Theme;
        _fs = FontSize.Or(_theme.Peek().FontSm);
        _geo = new EditorGeometry(BuildConfig(), _state);
        _widgets.Clear();   // 旧ホストは再実体化でスコープごと破棄済み — dict だけ捨てて Refresh で作り直す

        _root = CreateRoot(ctx, parent, worldOrigin);
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = _theme.Value.SurfaceAlt);
        FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 1;
        clip.Clip = new RectClip(0, 0, W, H);
        _content = ctx.Canvas.AddChild(clip);
        ctx.Effect(() => _content.Transform = Affine2D.Translate(Pad, Pad - _scroll.Clamped));

        _overlayBg = ctx.Canvas.AddChild(_content);   // 背景/行背景/ブロック/囲み塗り (テキストの背面)
        _overlayBg.Z = 0;
        _overlayBg.ContentColors = true;
        _selNode = ctx.Canvas.AddChild(_content);
        _selNode.Z = 1;
        ctx.Effect(() => _selNode.Color = Styles.WithAlpha(_theme.Value.Primary, 70));
        _textLayer = ctx.Canvas.AddChild(_content);
        _textLayer.Z = 2;
        _overlayFg = ctx.Canvas.AddChild(_content);    // 下線/波線/囲み枠 (テキストの前面)
        _overlayFg.Z = 3;
        _overlayFg.ContentColors = true;
        _caretNode = ctx.Canvas.AddChild(_content);
        _caretNode.Z = 4;
        ctx.Effect(() => _caretNode.Color = _theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value ? 1f : 0f);

        // テーマ変化で色が変わる → ジオメトリを作り直して再描画 (稀なので全再構築で可)
        ctx.Effect(() =>
        {
            _ = _theme.Value;
            _geo?.Configure(BuildConfig());
            Refresh();
        });

        // 外部からの value 変更を反映
        ctx.Effect(() =>
        {
            string v = Value.Get().Value;
            if (v != _state.Doc.Text) { _state = EditorState.Create(v); _history.Clear(); _scroll.ScrollTo(0); Refresh(); }
        });

        float t = 0;
        ctx.AddAnimation(dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; });

        _focus ??= new FocusTarget
        {
            OnFocus = on => { Focused.Value = on; if (on) _caretOn.Value = true; },
            OnKey = OnKey,
            OnText = s => { _compText = ""; Apply(EditCommands.InsertText(_state, s)); },
            OnComposeEx = c => { _compText = c.Text; _compTargetStart = c.TargetStart; _compTargetLen = c.TargetLen; Refresh(); EnsureCaretVisible(); },
            OnCommit = final => { _compText = ""; if (final.Length > 0) Apply(EditCommands.InsertText(_state, final)); else Refresh(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        void Place(float lx, float ly, bool extend)
        {
            if (_geo is null || Composing) return;
            int off = _geo.HitTest(lx - Pad, ly - Pad + _scroll.Clamped);
            SelectionRange main = _state.Selection.Main;
            var sel = EditorSelection.Single(extend ? main.Anchor : off, off);
            Apply(EditCommands.SetSelection(_state, sel));
            _goalX = null; _caretOn.Value = true;
        }
        ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f, cursor: CursorKind.IBeam,
            onDragStart: e => Place(e.X, e.Y, extend: false),
            onDrag: e => Place(e.X, e.Y, extend: true));
        ctx.AddScroll(_root, new Rect(0, 0, W, H), d => _scroll.ScrollBy(-d));
        ScrollBars.AttachVertical(ctx, _root, _scroll, W, H, minThumb: 24);

        Refresh();
    }

    private float ContentH => (_geo?.ContentHeight ?? 0) + Pad * 2;

    // ---- 入力 ----

    private bool OnKey(KeyEvent ev)
    {
        switch (ev.Key)
        {
            case Key.Left: Apply(EditCommands.MoveLeft(_state, ev.Shift)); _goalX = null; return true;
            case Key.Right: Apply(EditCommands.MoveRight(_state, ev.Shift)); _goalX = null; return true;
            case Key.Home: Apply(EditCommands.MoveLineStart(_state, ev.Shift)); _goalX = null; return true;
            case Key.End: Apply(EditCommands.MoveLineEnd(_state, ev.Shift)); _goalX = null; return true;
            case Key.Up: MoveVertical(-1, ev.Shift); return true;
            case Key.Down: MoveVertical(+1, ev.Shift); return true;
            case Key.Enter: Apply(EditCommands.InsertNewline(_state)); _goalX = null; return true;
            case Key.Tab: Apply(EditCommands.InsertText(_state, "    ")); _goalX = null; return true;
            case Key.Backspace: Apply(EditCommands.DeleteBackward(_state)); _goalX = null; return true;
            case Key.Delete: Apply(EditCommands.DeleteForward(_state)); _goalX = null; return true;
            case Key.A when ev.Ctrl: Apply(EditCommands.SelectAll(_state)); return true;
            case Key.Z when ev.Ctrl: _state = _history.Undo(_state); _goalX = null; Sync(); return true;
            case Key.Y when ev.Ctrl: _state = _history.Redo(_state); _goalX = null; Sync(); return true;
            case Key.C when ev.Ctrl: CopySelection(); return true;
            case Key.X when ev.Ctrl: if (CopySelection()) Apply(EditCommands.DeleteBackward(_state)); return true;
            case Key.V when ev.Ctrl:
                if (UiClipboard.Instance?.GetText() is { Length: > 0 } paste) Apply(EditCommands.InsertText(_state, paste));
                return true;
            default: return false;
        }
    }

    private void MoveVertical(int dir, bool select)
    {
        if (_geo is null) return;
        SelectionRange main = _state.Selection.Main;
        int head = _geo.MoveVertical(main.Head, dir, ref _goalX);
        var sel = EditorSelection.Single(select ? main.Anchor : head, head);
        Apply(EditCommands.SetSelection(_state, sel));
    }

    private void Apply(Transaction tr, bool coalesce = false)
    {
        if (tr.DocChanged) _history.Record(tr, coalesce);
        _state = tr.State;
        Sync();
    }

    private void Sync()
    {
        _compText = "";
        Value.Get().Value = _state.Doc.Text;
        _caretOn.Value = true;
        Refresh();
        EnsureCaretVisible();
    }

    private bool CopySelection()
    {
        SelectionRange m = _state.Selection.Main;
        if (m.Empty || UiClipboard.Instance is not { } clip) return false;
        clip.SetText(_state.Doc.Slice(m.From, m.To));
        return true;
    }

    private void EnsureCaretVisible()
    {
        if (_geo is null) return;
        _scroll.SetLengths(ContentH, H);
        TextRect cr = _geo.CaretRect(_state.Selection.Main.Head);
        _scroll.EnsureVisible(cr.Y, cr.Y + cr.Height, Pad);
    }

    // ---- 描画 ----

    private void Refresh()
    {
        if (_ctx is null || _geo is null) return;
        RunProviders();
        EditorState eff = Effective();
        _geo.SetState(eff);
        _scroll.SetLengths(ContentH, H);

        RebuildText();
        BuildSelection(eff);
        BuildOverlays();
        BuildCaret(eff);
        HostWidgets();
    }

    // 装飾プロバイダを走らせて結果を _state へ (文書は変えないので履歴に積まない・純関数なので冪等)
    private void RunProviders()
    {
        if (_providers.Count == 0) return;
        IReadOnlyList<StateEffect> effects = DecorationProviders.Collect(_state, _providers);
        _state = _state.Update(new TransactionSpec { Effects = effects }).State;
    }

    // ---- 行内 widget のホスト (resolver → Realize → OnChildNeedsRealize) ----

    private void HostWidgets()
    {
        if (WidgetResolver is null) { if (_widgets.Count > 0) ClearWidgets(); return; }
        var seen = new HashSet<object>();
        foreach (WidgetSlot slot in _geo!.WidgetSlots())
        {
            seen.Add(slot.Key);
            if (!_widgets.TryGetValue(slot.Key, out Hosted? h))
            {
                if (WidgetResolver(slot.Key) is not { } w) continue;
                UiNode container = _ctx.Canvas.AddChild(_content);
                container.Z = 5;
                h = new Hosted { Widget = w, Container = container };
                _widgets[slot.Key] = h;
            }
            h.Rect = slot.Rect;
            RealizeWidget(h);
        }
        if (_widgets.Count > seen.Count)
            foreach (object k in _widgets.Keys.Where(k => !seen.Contains(k)).ToList())
            { DisposeHosted(_widgets[k]); _widgets.Remove(k); }
    }

    // widget を宣言サイズのスロットへ実体化 (箱サイズは装飾が宣言 → 行レイアウトは変わらない = 高さ吸収不要)
    private void RealizeWidget(Hosted h)
    {
        Widget w = h.Widget;
        w.Scope?.Release();
        var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
        w.Layout(new Constraints(0, h.Rect.Width, 0, h.Rect.Height), lc);
        w.Offset = new Point(h.Rect.X, h.Rect.Y);
        // worldOrigin は container (=_content) の原点。CreateRoot が WorldPos = worldOrigin + Offset とするので
        // ここに rect を足すと二重計上になる (描画はノード transform で正しいが WorldPos がずれ d.Click が外れる)。
        w.Realize(_ctx, h.Container, new Point(WorldPos.X + Pad, WorldPos.Y + Pad - _scroll.Clamped));
        w.ParentWidget = this;
    }

    private void ClearWidgets()
    {
        foreach (Hosted h in _widgets.Values) DisposeHosted(h);
        _widgets.Clear();
    }

    private void DisposeHosted(Hosted h)
    {
        h.Widget.Scope?.Release();
        (h.Widget as IDisposable)?.Dispose();
        _ctx.Canvas.Remove(h.Container);
    }

    /// <summary>ホストした行内 widget の再実体化要求を吸収する — 箱サイズは固定なので同じスロットへ
    /// 実体化し直すだけ (行レイアウトは変わらない)。</summary>
    protected override bool OnChildNeedsRealize(Widget child)
    {
        foreach (Hosted h in _widgets.Values)
            if (ReferenceEquals(h.Widget, child)) { RealizeWidget(h); return true; }
        return false;
    }

    private void RebuildText()
    {
        foreach (UiNode n in _colorNodes) _ctx.Canvas.Remove(n);
        _colorNodes.Clear();

        var colors = new HashSet<uint>();
        for (int i = 0; i < _geo!.LineCount; i++)
            foreach (uint c in _geo.Line(i).Layout.Colors) colors.Add(c);

        foreach (uint color in colors)
        {
            UiNode node = _ctx.Canvas.AddChild(_textLayer);
            node.Color = color;
            var scene = new Scene2D();
            for (int i = 0; i < _geo.LineCount; i++)
                _geo.Line(i).Layout.DrawColorRuns(scene, _geo.LineIndent(i), _geo.LineTop(i), color);
            node.Content = scene;
            _colorNodes.Add(node);
        }
    }

    private void BuildSelection(EditorState eff)
    {
        var sel = new Scene2D();
        foreach (SelectionRange r in eff.Selection.Ranges)
        {
            if (r.Empty) continue;
            foreach (TextRect rect in _geo!.SelectionRects(r.From, r.To))
                sel.FillRect(Color2D.White, rect.X, rect.Y, MathF.Max(2, rect.Width), rect.Height);
        }
        _selNode.Content = sel;
    }

    private void BuildOverlays()
    {
        var bg = new Scene2D();
        var fg = new Scene2D();
        foreach (OverlayRect o in _geo!.OverlayRects())
        {
            TextRect r = o.Rect;
            switch (o.Kind)
            {
                case OverlayKind.Background:
                case OverlayKind.LineBackground:
                case OverlayKind.BlockBackground:
                    bg.FillRect(o.Color, r.X, r.Y, r.Width, r.Height);
                    break;
                case OverlayKind.BlockBar:
                    bg.FillRect(o.Color, r.X, r.Y, r.Width, r.Height);
                    break;
                case OverlayKind.Box:
                    if (o.Fill is { } fill) bg.FillRect(fill, r.X, r.Y, r.Width, r.Height);
                    StrokeRect(fg, r, o.Color);
                    break;
                case OverlayKind.Underline:
                    fg.FillRect(o.Color, r.X, r.Y + r.Height - 1.5f, r.Width, 1.5f);
                    break;
                case OverlayKind.WavyUnderline:
                    for (float x = r.X; x < r.X + r.Width - 1; x += 4)
                    {
                        fg.FillRect(o.Color, x, r.Y + r.Height - 2, 2, 1);
                        fg.FillRect(o.Color, x + 2, r.Y + r.Height - 1, 2, 1);
                    }
                    break;
            }
        }
        _overlayBg.Content = bg;
        _overlayFg.Content = fg;
    }

    private static void StrokeRect(Scene2D s, TextRect r, uint color)
    {
        s.FillRect(color, r.X, r.Y, r.Width, 1);
        s.FillRect(color, r.X, r.Y + r.Height - 1, r.Width, 1);
        s.FillRect(color, r.X, r.Y, 1, r.Height);
        s.FillRect(color, r.X + r.Width - 1, r.Y, 1, r.Height);
    }

    private void BuildCaret(EditorState eff)
    {
        var caret = new Scene2D();
        foreach (SelectionRange r in eff.Selection.Ranges)
        {
            TextRect cr = _geo!.CaretRect(r.Head);
            if (r.Head == eff.Selection.Main.Head) _caretLocal = new Rect(cr.X, cr.Y, 2, cr.Height);
            caret.FillRect(Color2D.White, cr.X, cr.Y + 1, 2, MathF.Max(4, cr.Height - 2));
        }
        _caretNode.Content = caret;
    }

    // ---- ITextInput (TSF/IME) ----
    string ITextInput.Text => _state.Doc.Text;
    (int start, int length) ITextInput.Selection { get { SelectionRange m = _state.Selection.Main; return (m.From, m.To - m.From); } }
    void ITextInput.Select(int start, int end) { Apply(EditCommands.SetSelection(_state, EditorSelection.Single(start, end))); }
    void ITextInput.Replace(int start, int end, string s)
        => Apply(_state.Update(new TransactionSpec { Changes = [new ChangeSpec(start, end, s)], Selection = EditorSelection.Cursor(start + s.Length) }));
    void ITextInput.SetComposition(ImeComposition comp) { _compText = comp.Text; _compTargetStart = comp.TargetStart; _compTargetLen = comp.TargetLen; Refresh(); EnsureCaretVisible(); }
    void ITextInput.CommitComposition(string final) { _compText = ""; if (final.Length > 0) Apply(EditCommands.InsertText(_state, final)); else Refresh(); }
    Rect ITextInput.CaretRect => new(WorldPos.X + Pad + _caretLocal.X, WorldPos.Y + Pad + _caretLocal.Y - _scroll.Clamped, 2, _caretLocal.Height);
}
