using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// 1 行テキスト入力。<see cref="TextEditor"/> で caret/選択/IME を扱い、編集で値 signal を更新。
/// キャレット/選択/クリック位置は <see cref="TextLayout"/> のクラスタ対応 API を使う —
/// カーニング/合字があっても**描画とキャレットが必ず一致**する。クリックでキャレット配置。
/// IME 変換中は preedit に下線、変換対象節を強調下線。caret 点滅。<see cref="ITextInput"/> 実装 (TSF 連携)。
/// </summary>
[UiComponent]
public sealed partial class TextField : Widget, ITextInput
{
    /// <summary>値の signal (編集で書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<string>> _value = new();
    /// <summary>プレースホルダ (空のとき薄色表示)。</summary>
    [UiParam] private readonly Bindable<string> _placeholder = "";

    private readonly TextEditor _ed = new();
    private bool _edInit;          // value → _ed の初期反映は最初の Realize で一度だけ
    private readonly Signal<bool> _caretOn = new(true);
    private FocusTarget? _focus;   // Realize をまたいで同一インスタンス (フォーカス保存)
    /// <summary>入力を全文一致で規制する正規表現 (例: 数値 <c>^-?[0-9]*\.?[0-9]*$</c>)。
    /// 不一致になる挿入/置換は拒否する (削除は常に許可)。null = 無制限。</summary>
    public string? Pattern { get; set; }
    private System.Text.RegularExpressions.Regex? _rx;
    private string? _rxSrc;

    /// <summary>背景の丸める角 (入力グループで接合面を直角にする用。LengthField 等が設定)。</summary>
    internal RectCorners BgCorners = RectCorners.All;

    private const float DefaultWidth = 240f;
    private float _padX = 10, _h = 38, _fs = 16;   // Realize/Layout 時にテーマから確定 (キャッシュ)
    private float EffectiveWidth = DefaultWidth;   // PerformLayout で解決 (% / em / vw 対応)

    /// <summary>文字サイズ。未設定 → テーマ Font。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();

    private void CacheMetrics(Theme t)
    {
        _padX = t.PadIn;
        _h = t.ControlH;
        _fs = FontSize.Or(t.Font);
    }
    /// <summary>背景色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();

    /// <summary>未処理キーの拡張フック (コマンドパレットの ↑↓/Enter/Esc 等)。true = 消費。</summary>
    public Func<KeyEvent, bool>? ExtraKeys { get; set; }

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _textNode = null!, _caretNode = null!, _selNode = null!, _underlineNode = null!, _targetNode = null!;
    private float _ascent, _fontH, _topY, _caretX;
    private TextLayout? _disp;   // 表示文字列のレイアウト (Refresh 毎に構築 — 描画/キャレット/ヒットが共有)

    public override string? DebugDetail => _ed.Display.Length > 0 ? _ed.Display : $"(placeholder: {Placeholder.Get()})";
    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        CacheMetrics(ctx.Theme);
        EffectiveWidth = ResolveW(c, ctx, DefaultWidth);
        float w = HAlign.Get() == Align.Stretch && !float.IsInfinity(c.MaxW) ? c.MaxW : EffectiveWidth;
        Size = c.Constrain(new Size(w, _h));
    }
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _theme = ctx.Theme;
        CacheMetrics(_theme.Peek());
        Signal<string> value = Value.Get();
        if (!_edInit) { _edInit = true; _ed.SetText(value.Peek()); }   // 旧 ctor の初期反映 (一度きり)
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        FocusRing.Add(ctx, node, -3, -3, Size.Width + 6, _h + 6, 9, Focused);

        var bg = new Scene2D(); bg.FillRoundedRect(Color2D.White, 0, 0, Size.Width, _h, _theme.Peek().Radius + 1, BgCorners);
        node.Content = bg;
        ctx.Effect(() => node.Color = Background.Or(_theme.Value.SurfaceAlt));

        _fontH = ctx.Font.Measure("Mg", _fs).height;
        _ascent = ctx.Font.Ascent(_fs);
        _topY = (_h - _fontH) / 2;

        _selNode = ctx.Canvas.AddChild(node); _selNode.Z = 1;
        ctx.Effect(() => _selNode.Color = Styles.WithAlpha(_theme.Value.Primary, 70));
        _targetNode = ctx.Canvas.AddChild(node); _targetNode.Z = 1;     // 変換対象節の地色
        ctx.Effect(() => _targetNode.Color = Styles.WithAlpha(_theme.Value.Primary, 55));
        _underlineNode = ctx.Canvas.AddChild(node); _underlineNode.Z = 1; // preedit 下線
        ctx.Effect(() => _underlineNode.Color = _theme.Value.TextMuted);

        _textNode = ctx.Canvas.AddChild(node); _textNode.Z = 2;
        _caretNode = ctx.Canvas.AddChild(node); _caretNode.Z = 3;
        var caret = new Scene2D(); caret.FillRect(Color2D.White, 0, _topY, 2, _fontH); _caretNode.Content = caret;
        ctx.Effect(() => _caretNode.Color = _theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value ? 1f : 0f);

        Refresh();
        ctx.Effect(() => { string v = value.Value; if (v != _ed.Text) { _ed.SetText(v); Refresh(); } });

        float t = 0;
        ctx.AddAnimation(
            dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; },
            () => Focused.Peek());

        // FocusTarget は同一インスタンスを再登録 — 再実体化 (CompositeControl.Rebuild/部分 Realize) を
        // またいで参照保持フォーカスが生き残る (TableBlock と同じパターン)
        _focus ??= new FocusTarget
        {
            OnFocus = on => { Focused.Value = on; if (on) { _caretOn.Value = true; _ed.End(false); Refresh(); } },
            OnKey = OnKey,
            OnText = s => { if (Accepts(Prospective(_ed.SelMin, _ed.SelMax, s))) { _ed.Insert(s); Sync(); } },
            OnCompose = s => { _ed.SetComposition(s); Refresh(); },
            OnComposeEx = c => { _ed.SetComposition(c.Text, c.TargetStart, c.TargetLen); Refresh(); },
            OnCommit = s => { _ed.CommitComposition(s); Sync(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        // クリックでキャレット配置、ドラッグで選択 (HitTest はクラスタ/グラフェム吸着)。変換中は位置操作しない。
        // ダブルクリック = 単語 (Segmenter.GetWordAt)、トリプル = 全選択。
        DateTime lastDown = default;
        float lastX = 0;
        int clicks = 0;
        void PlaceFromX(float lx, bool extend)
        {
            if (_disp is null || _ed.CompositionDisplayRange.len > 0) return;
            int i = Math.Clamp(_disp.HitTest(lx - _padX, 0), 0, _ed.Text.Length);
            _ed.Select(extend ? _ed.Anchor : i, i);
            _caretOn.Value = true;
            Refresh();
        }
        void MultiClick(float lx)
        {
            DateTime now = DateTime.UtcNow;
            clicks = MathF.Abs(lx - lastX) < 4 && (now - lastDown) < TimeSpan.FromMilliseconds(500) ? clicks + 1 : 1;
            lastDown = now;
            lastX = lx;
            string t = _ed.Text;
            if (clicks == 2 && t.Length > 0)
            {
                (int ws, int we) = Luxel.Typography.TextSegmenter.Default.GetWordAt(t, Math.Min(_ed.Caret, t.Length - 1));
                _ed.Select(ws, we);
                Refresh();
            }
            else if (clicks >= 3)
            {
                _ed.Select(0, t.Length);
                clicks = 0;
                Refresh();
            }
        }
        ctx.AddHit(node, new Rect(0, 0, Size.Width, _h), focus: f,
            onDragStart: e => { PlaceFromX(e.X, extend: false); MultiClick(e.X); },
            onDrag: e => PlaceFromX(e.X, extend: true),
            cursor: CursorKind.IBeam,
            onContext: e => ContextMenu.OpenForEditor(ctx, node, e.X, e.Y, f));
    }

    private bool OnKey(KeyEvent ev)
    {
        switch (ev.Key)
        {
            case Key.Left: _ed.MoveLeft(ev.Shift); break;
            case Key.Right: _ed.MoveRight(ev.Shift); break;
            case Key.Home: _ed.Home(ev.Shift); break;
            case Key.End: _ed.End(ev.Shift); break;
            case Key.Backspace: _ed.Backspace(); break;
            case Key.Delete: _ed.DeleteForward(); break;
            case Key.A when ev.Ctrl: _ed.Select(0, _ed.Text.Length); Refresh(); return true;
            case Key.C when ev.Ctrl: CopySelection(); return true;
            case Key.X when ev.Ctrl: if (CopySelection()) { _ed.Backspace(); Sync(); } return true;
            case Key.V when ev.Ctrl:
                // 1 行入力: 改行は空白へ潰す。Pattern 規制も通常入力と同じに適用
                if (UiClipboard.Instance?.GetText() is string p && p.Length > 0)
                {
                    string s = p.Replace("\r", "").Replace('\n', ' ');
                    if (Accepts(Prospective(_ed.SelMin, _ed.SelMax, s))) { _ed.Insert(s); Sync(); }
                }
                return true;
            default: return ExtraKeys?.Invoke(ev) ?? false;
        }
        Sync();
        return true;
    }

    private bool CopySelection()
    {
        if (_ed.SelMax <= _ed.SelMin || UiClipboard.Instance is not IClipboard clip) return false;
        clip.SetText(_ed.Text[_ed.SelMin.._ed.SelMax]);
        return true;
    }

    private void Sync() { Value.Get().Value = _ed.Text; Refresh(); _caretOn.Value = true; }

    /// <summary>[start,end) を s で置き換えた場合の全文 (Pattern の事前検査用)。</summary>
    private string Prospective(int start, int end, string s)
    {
        string t = _ed.Text;
        start = Math.Clamp(start, 0, t.Length); end = Math.Clamp(end, start, t.Length);
        return t[..start] + s + t[end..];
    }

    private bool Accepts(string text)
    {
        if (Pattern is null) return true;
        if (_rxSrc != Pattern) { _rx = new System.Text.RegularExpressions.Regex(Pattern); _rxSrc = Pattern; }
        return _rx!.IsMatch(text);
    }

    /// <summary>表示 index → キャレット x。レイアウトのクラスタ対応 API (描画と同一結果) を使う。</summary>
    private float X(string disp, int idx) => _padX + (_disp?.CaretRect(idx).X ?? 0);

    private void Refresh()
    {
        string disp = _ed.Display;
        bool empty = disp.Length == 0;
        _disp = new TextLayout(_ctx.Font, disp, _fs, new TextLayoutOptions { Wrap = TextWrap.None });
        var ts = new Scene2D();
        if (empty) _ctx.Font.AppendText(ts, Placeholder.Get(), _padX, _topY + _ascent, _fs, Color2D.White);
        else _disp.Draw(ts, _padX, _topY, Color2D.White);
        _textNode.Content = ts;
        _textNode.Color = empty ? _theme.Value.TextMuted : _theme.Value.Text;

        _caretX = X(disp, _ed.DisplayCaret);
        _caretNode.Transform = Affine2D.Translate(_caretX, 0);

        // 選択
        if (_ed.HasSelection)
        {
            var ss = new Scene2D(); ss.FillRect(Color2D.White, X(disp, _ed.SelMin), _topY, X(disp, _ed.SelMax) - X(disp, _ed.SelMin), _fontH);
            _selNode.Content = ss;
        }
        else _selNode.Content = new Scene2D();

        // IME: preedit 下線 + 対象節。実 IME (TSF) は preedit が文書内で装飾のみ通知 (_hl*)
        (int cs, int cl) = _ed.CompositionDisplayRange;
        (int ht0, int htl) = _ed.TargetDisplayRange;
        if (cl == 0 && _hlLen > 0) { cs = _hlStart; cl = _hlLen; ht0 = _hlTargetStart; htl = _hlTargetLen; }
        cs = Math.Clamp(cs, 0, disp.Length);
        cl = Math.Clamp(cl, 0, disp.Length - cs);
        if (cl > 0)
        {
            float ux0 = X(disp, cs), ux1 = X(disp, cs + cl);
            var us = new Scene2D(); us.FillRect(Color2D.White, ux0, _topY + _fontH, ux1 - ux0, 2);
            _underlineNode.Content = us;

            if (htl > 0)
            {
                float tx0 = X(disp, ht0), tx1 = X(disp, ht0 + htl);
                var tg = new Scene2D(); tg.FillRect(Color2D.White, tx0, _topY, tx1 - tx0, _fontH);
                _targetNode.Content = tg;
            }
            else _targetNode.Content = new Scene2D();
        }
        else { _underlineNode.Content = new Scene2D(); _targetNode.Content = new Scene2D(); }
    }

    // ---- ITextInput (TSF/IME ブリッジ) ----
    string ITextInput.Text => _ed.Text;
    (int start, int length) ITextInput.Selection => (_ed.SelMin, _ed.SelMax - _ed.SelMin);
    void ITextInput.Select(int start, int end) { _ed.Select(start, end); Refresh(); }
    void ITextInput.Replace(int start, int end, string s)
    { if (Accepts(Prospective(start, end, s))) { _ed.Replace(start, end, s); Sync(); } }
    void ITextInput.SetComposition(ImeComposition comp) { _ed.SetComposition(comp.Text, comp.TargetStart, comp.TargetLen); Refresh(); }
    void ITextInput.CommitComposition(string final) { _ed.CommitComposition(final); Sync(); }
    void ITextInput.SetCompositionHighlight(int start, int length, int targetStart, int targetLength)
    {
        _hlStart = start; _hlLen = length; _hlTargetStart = targetStart; _hlTargetLen = targetLength;
        Refresh();
    }
    Rect ITextInput.CaretRect => new(WorldPos.X + _caretX, WorldPos.Y + _topY, 2, _fontH);

    private int _hlStart, _hlLen, _hlTargetStart, _hlTargetLen;   // TSF display attribute 由来の装飾
}
