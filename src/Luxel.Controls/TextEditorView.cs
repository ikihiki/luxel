using Luxel.Document;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// テキストエディタ新スタック (ADR-0006 / ToDo 22) の**ビュー** — canvas がないとできないことだけを担う薄い皮。
/// 編集意味論・座標写像・装飾は <see cref="EditorState"/> / <see cref="EditorGeometry"/> / <see cref="EditCommands"/>
/// (canvas 非依存の Luxel.Document) が持ち、この widget は「入力を Transaction にし、ジオメトリが返す矩形を塗る」。
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
    /// <summary>太字/斜体/太字斜体/等幅フェイス (Markdown/リッチ文書の <see cref="FontVariant"/> マーク用、null = 変種なし)。</summary>
    public VectorFont? BoldFont { get; set; }
    public VectorFont? ItalicFont { get; set; }
    public VectorFont? BoldItalicFont { get; set; }
    public VectorFont? MonoFont { get; set; }
    /// <summary>折返し (既定 false = コード)。</summary>
    public bool WrapText { get; set; }
    /// <summary>折返し段落内の行送り倍率 (null = ブロック行送りと同じ)。文書は段落内を詰めると読みやすい (例 1.3)。</summary>
    public float? WrapLineHeight { get; set; }
    /// <summary>読み取り専用 (文書レンダラ用)。文書を変える編集は無視し、選択/スクロール/widget 操作は許す。</summary>
    public bool ReadOnly { get; set; }
    /// <summary>Markdown 文書レンダラの場合、索引用の markdown ソース (build 時に取れる = realize 不要)。
    /// null = 通常のエディタ (docs 索引の対象外)。<see cref="MarkdownDoc"/> が設定する。</summary>
    public string? DocSource { get; set; }
    /// <summary>領域いっぱいに広がる (固定 editorWidth/Height でなく制約サイズを使う)。文書ページ向け。</summary>
    public bool Fill { get; set; }
    /// <summary>クリック時にクリック位置のソースオフセットを通知する (リンクナビ等の当たり判定用)。</summary>
    public Action<int>? OnClickOffset { get; set; }

    /// <summary>行番号ガターを左に出す (コードエディタ用)。本文はガター幅ぶん右へ寄る。</summary>
    public bool ShowLineNumbers { get; set; }

    /// <summary>キー横取りフック — true を返すと既定処理をしない (上位のカスタム、例: Strudel の Ctrl+Enter)。</summary>
    public Func<KeyEvent, bool>? OnKeyIntercept { get; set; }

    /// <summary>行内 widget の解決 — 装飾の不透明キー → 実 Widget (null = 無視)。BlockWidgetRegistry と同じ流儀。
    /// view が生成・所有し、スロット矩形に Realize する。状態は外部 (signal) に持たせると再実体化で生き残る。</summary>
    public Func<object, Widget?>? WidgetResolver { get; set; }

    /// <summary>ホストした行内 widget のクリップ方式 (既定 = 枠でクリップ、はみ出しを隠す)。</summary>
    public WidgetClipMode WidgetClip { get; set; } = WidgetClipMode.Box;

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
    private UiNode? _gutter, _gutterText;
    private readonly List<UiNode> _colorNodes = new();
    private FocusTarget? _focus;
    private Rect _caretLocal;

    private const float GutterPadR = 8f;
    private float _gutterW, _charW = 8f;
    private VectorFont? _primaryFont;
    private float ContentX => ShowLineNumbers ? _gutterW : Pad;

    private sealed class Hosted { public required object Key; public required Widget Widget; public required UiNode Container; public TextRect Rect; }
    private readonly Dictionary<object, Hosted> _widgets = new();
    private readonly Dictionary<object, float> _blockMeasured = new();   // 自動高さ block widget の実測高さ (key→px)
    private readonly Dictionary<object, (float W, float H)> _widgetMeasured = new();   // 自動サイズ行内 widget の実測 (key→px)
    private HashSet<object> _autoBlockKeys = new();                       // Height<=0 の block widget キー
    private HashSet<object> _autoWidgetKeys = new();                      // W<=0 or H<=0 の行内 widget キー
    private bool _blockDirty;                                            // 実測が変わった → 次フレームで採用
    private float _fillW, _fillH;                                        // Fill 時の制約サイズ
    private bool _layoutDirty;                                           // Fill サイズが変わった → 次フレームで config 再構築

    private const float Pad = 6f;
    private float _fs = 13;

    private float W => Fill && _fillW > 0 ? _fillW : MathF.Max(120, EditorWidth.Or(360));
    private float H => Fill && _fillH > 0 ? _fillH : MathF.Max(40, EditorHeight.Get());

    public override string DebugType => "TextEditorView";
    public override string? DebugDetail => $"{_state.Doc.LineCount} 行";

    /// <summary>現在のテキスト。</summary>
    public string Text => _state.Doc.Text;
    /// <summary>キャレット (主レンジ head) のフラットオフセット。</summary>
    public int CaretOffset => _state.Selection.Main.Head;
    /// <summary>選択があるか。</summary>
    public bool HasSelection => !_state.Selection.Main.Empty;
    /// <summary>カーソル (選択レンジ) の本数 — マルチカーソルの観測用。</summary>
    public int CursorCount => _state.Selection.Ranges.Count;

    /// <summary>owner の装飾を外部から差し替える (毎フレーム更新も可 — 再生囲みのような
    /// レイアウト非依存装飾は行キャッシュに触れず 60fps で回せる)。プロバイダを使わない push 型。</summary>
    public void SetDecorations(string owner, DecorationSet set)
    {
        _state = _state.WithDecorations(owner, set).State;
        Refresh();
    }

    // ---- 検索 / 置換 ----
    private string _searchQuery = "";
    private readonly List<(int From, int To)> _matches = new();
    private int _matchCur = -1;

    /// <summary>検索マッチ数。</summary>
    public int SearchMatchCount => _matches.Count;
    /// <summary>現在マッチ番号 (0 始まり、なし = -1)。</summary>
    public int SearchCurrent => _matchCur;

    /// <summary>検索語を設定し全マッチを収集・ハイライトする (空でクリア)。</summary>
    public void SetSearch(string query)
    {
        _searchQuery = query ?? "";
        _matches.Clear();
        _matches.AddRange(TextSearch.FindAll(_state.Doc.Text, _searchQuery));
        _matchCur = _matches.Count > 0 ? 0 : -1;
        UpdateSearchDeco();
        if (_matchCur >= 0) SelectMatch();
        Refresh();
    }

    /// <summary>次のマッチへ (ラップ)。</summary>
    public void FindNext() => StepMatch(+1);
    /// <summary>前のマッチへ。</summary>
    public void FindPrev() => StepMatch(-1);

    private void StepMatch(int dir)
    {
        if (_matches.Count == 0) return;
        _matchCur = (_matchCur + dir + _matches.Count) % _matches.Count;
        UpdateSearchDeco();
        SelectMatch();
        Refresh();
    }

    /// <summary>全マッチを置換して再検索する。</summary>
    public void ReplaceAll(string replacement)
    {
        if (_matches.Count == 0) return;
        var specs = _matches.Select(m => new ChangeSpec(m.From, m.To, replacement ?? "")).ToList();
        Apply(_state.Update(new TransactionSpec { Changes = specs }));
        SetSearch(_searchQuery);
    }

    private void UpdateSearchDeco()
    {
        if (_matches.Count == 0)
        {
            _state = _state.Update(new TransactionSpec { Effects = [new RemoveDecorations("search")] }).State;
            return;
        }
        var marks = new List<Decoration>(_matches.Count);
        for (int i = 0; i < _matches.Count; i++)
        {
            (int f, int t) = _matches[i];
            byte alpha = (byte)(i == _matchCur ? 170 : 80);   // 現在マッチは強め
            marks.Add(new MarkDecoration(f, t, Background: Styles.WithAlpha(_theme.Peek().Warning, alpha)));
        }
        _state = _state.WithDecorations("search", new DecorationSet(marks)).State;
    }

    private void SelectMatch()
    {
        (int f, int t) = _matches[_matchCur];
        _state = EditCommands.SetSelection(_state, EditorSelection.Single(f, t)).State;
        _caretOn.Value = true;
        EnsureCaretVisible();
    }

    // ---- 補完 / dwell ホバー (言語サービス連携。浮遊 UI は PopupPlacer で CaretRect にアンカー = 端でフリップ) ----

    /// <summary>言語サービス (補完/診断/ホバー — null で無効)。Controls は実装 (Roslyn 等) を知らない。</summary>
    public ICodeLanguage? LanguageService { get; set; }

    /// <summary>補完ポップアップが開いているか (play の Expect 用)。</summary>
    public bool CompletionOpen => _compOpen;
    /// <summary>補完候補数 (フィルタ後)。</summary>
    public int CompletionCount => _comp.Count;
    /// <summary>dwell ホバーツールチップが表示中か。</summary>
    public bool HoverTipOpen => _tipOpen;
    /// <summary>ホバーツールチップの文字列。</summary>
    public string HoverTipText => _tipText;

    private bool _compOpen;
    private IReadOnlyList<CodeCompletion> _compAll = [];
    private IReadOnlyList<CodeCompletion> _comp = [];
    private int _compSel, _compTop, _compTrigger;
    private UiNode? _popupBg, _popupSel, _popupText;
    private const int MaxCompRows = 8;
    private const float PopupHitW = 320f;

    private float _hoverX = -1, _hoverY = -1;
    private int _dwellFrames;
    private bool _tipOpen;
    private string _tipText = "";
    private UiNode? _tipBg, _tipTxt;
    private const int DwellFrames = 30;

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // キャレット直前の識別子の開始オフセット (補完の絞り込み起点)
    private int IdentStart()
    {
        string t = _state.Doc.Text;
        int i = _state.Selection.Main.Head;
        while (i > 0 && IsIdentChar(t[i - 1])) i--;
        return i;
    }

    private void OpenCompletion()
    {
        if (LanguageService is null) return;
        int caret = _state.Selection.Main.Head;
        _compAll = LanguageService.Complete(_state.Doc.Text, caret);
        _compTrigger = IdentStart();
        _comp = _compAll;
        _compSel = 0;
        _compOpen = _comp.Count > 0;
        Refresh();
    }

    private void CloseCompletion()
    {
        if (!_compOpen) return;
        _compOpen = false;
        Refresh();
    }

    // トリガーからキャレットまでの断片で候補を絞る (prefix 優先 → 大小無視の包含)
    private void RefilterCompletion()
    {
        if (!_compOpen) return;
        string t = _state.Doc.Text;
        int caret = _state.Selection.Main.Head;
        if (caret < _compTrigger) { CloseCompletion(); return; }
        string frag = t[_compTrigger..caret];
        if (frag.Length == 0) _comp = _compAll;
        else
        {
            var pref = _compAll.Where(c => c.Label.StartsWith(frag, StringComparison.OrdinalIgnoreCase)).ToList();
            _comp = pref.Count > 0 ? pref : _compAll.Where(c => c.Label.Contains(frag, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        _compSel = 0;
        if (_comp.Count == 0) { _compOpen = false; }
    }

    private void ConfirmCompletion()
    {
        if (!_compOpen || _compSel >= _comp.Count) { CloseCompletion(); return; }
        _compOpen = false;
        string insert = _comp[_compSel].InsertText;
        int caret = _state.Selection.Main.Head;
        Apply(_state.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(_compTrigger, caret, insert)],
            Selection = EditorSelection.Cursor(_compTrigger + insert.Length),
            ScrollIntoView = true,
        }));
    }

    private void PopupRowClick(int row)
    {
        if (!_compOpen) return;
        int idx = _compTop + row;
        if (idx < 0 || idx >= _comp.Count) return;
        _compSel = idx;
        ConfirmCompletion();
    }

    private void OnHoverMove(float x, float y)
    {
        if (MathF.Abs(x - _hoverX) > 1 || MathF.Abs(y - _hoverY) > 1)
        {
            _hoverX = x; _hoverY = y; _dwellFrames = 0;
            if (_tipOpen) { _tipOpen = false; Refresh(); }
        }
    }

    private Rect CaretLocalRect()
        => new(ContentX + _caretLocal.X, Pad + _caretLocal.Y - _scroll.Clamped, 2, MathF.Max(4, _caretLocal.Height));

    /// <summary>指定ソースオフセットの行を上端近くまでスクロールする (docs の <c>#アンカー</c> / TOC ナビ用)。
    /// realize 済み (geometry あり) が前提。</summary>
    public void ScrollToSource(int offset)
    {
        if (_geo is null) return;
        float y = _geo.CaretRect(Math.Clamp(offset, 0, _state.Doc.Text.Length)).Y;
        _scroll.SetLengths(ContentH, H);
        _scroll.ScrollTo(y);
        Refresh();
    }

    private Rect LocalViewport()
    {
        float vw = _ctx.Host?.Width ?? W, vh = _ctx.Host?.Height ?? H;
        return new Rect(-WorldPos.X, -WorldPos.Y, vw, vh);
    }

    private void DrawCompletion()
    {
        if (_popupBg is null) return;
        if (!_compOpen || _comp.Count == 0 || _primaryFont is null)
        {
            _popupBg.Visible = _popupSel!.Visible = _popupText!.Visible = false;
            _popupBg.Content = null; _popupSel.Content = null; _popupText.Content = null;
            return;
        }
        _popupBg.Visible = _popupSel!.Visible = _popupText!.Visible = true;
        VectorFont font = _primaryFont;
        float rowH = _fs + 8;
        int n = Math.Min(MaxCompRows, _comp.Count);
        float wpx = 0;
        foreach (CodeCompletion it in _comp.Take(MaxCompRows))
            wpx = MathF.Max(wpx, font.Measure($"{it.Label}  {it.Kind}", _fs).width);
        wpx += 16;
        float ph = n * rowH + 4;
        _compTop = Math.Clamp(_compSel - MaxCompRows + 1, 0, Math.Max(0, _comp.Count - MaxCompRows));

        PopupSolve sol = PopupPlacer.Solve(CaretLocalRect(), new Size(wpx, ph), LocalViewport(),
            new AnchoredPlacement { Side = PopupSide.Below, Align = PopupAlign.Start, Gap = 2, Margin = 4 });
        _popupBg.Transform = Affine2D.Translate(sol.Rect.X, sol.Rect.Y);

        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, wpx, ph, 4);
        _popupBg.Content = bg;

        var sel = new Scene2D();
        sel.FillRect(Color2D.White, 2, 2 + (_compSel - _compTop) * rowH, wpx - 4, rowH);
        _popupSel.Content = sel;

        Theme t = _theme.Peek();
        var txt = new Scene2D();
        for (int i = 0; i < n; i++)
        {
            CodeCompletion it = _comp[_compTop + i];
            float y = 2 + i * rowH + (rowH - _fs) / 2 + font.Ascent(_fs);
            font.AppendText(txt, it.Label, 6, y, _fs, t.Text);
            float kx = wpx - 8 - font.Measure(it.Kind, _fs).width;
            font.AppendText(txt, it.Kind, kx, y, _fs, t.TextMuted);
        }
        _popupText.Content = txt;
    }

    private void DrawTip()
    {
        if (_tipBg is null) return;
        if (!_tipOpen || _tipText.Length == 0 || _primaryFont is null)
        {
            _tipBg.Visible = _tipTxt!.Visible = false;
            _tipBg.Content = null; _tipTxt.Content = null;
            return;
        }
        _tipBg.Visible = _tipTxt!.Visible = true;
        VectorFont font = _primaryFont;
        float w = font.Measure(_tipText, _fs).width + 12, h = _fs + 8;
        PopupSolve sol = PopupPlacer.Solve(new Rect(_hoverX, _hoverY + 16, 0, 0), new Size(w, h), LocalViewport(),
            new AnchoredPlacement { Side = PopupSide.Below, Gap = 0, Margin = 4 });
        _tipBg.Transform = Affine2D.Translate(sol.Rect.X, sol.Rect.Y);

        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, w, h, 4);
        _tipBg.Content = bg;
        var txt = new Scene2D();
        _primaryFont.AppendText(txt, _tipText, 6, (h - _fs) / 2 + font.Ascent(_fs), _fs, _theme.Peek().Text);
        _tipTxt.Content = txt;
    }

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
        if (Fill)
        {
            // 制約サイズを採用。変わったら次フレームで config (折返し幅) を作り直す (再入回避)。
            float nw = float.IsFinite(c.MaxW) ? c.MaxW : W;
            float nh = float.IsFinite(c.MaxH) ? c.MaxH : H;
            if (MathF.Abs(nw - _fillW) > 0.5f || MathF.Abs(nh - _fillH) > 0.5f) { _fillW = nw; _fillH = nh; _layoutDirty = true; }
        }
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => Fill ? 100000f : W;

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
            WrapLineHeight = WrapLineHeight,
            DefaultColor = _theme.Peek().Text,
            BoldFont = BoldFont,
            ItalicFont = ItalicFont,
            BoldItalicFont = BoldItalicFont,
            MonoFont = MonoFont,
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
        EditorConfig cfg = BuildConfig();
        _primaryFont = cfg.Fonts.Primary;
        _charW = MathF.Max(1, cfg.Fonts.Primary.Measure("0", _fs).width);
        _gutterW = ShowLineNumbers ? MathF.Max(2, _state.Doc.LineCount.ToString().Length) * _charW + GutterPadR * 2 : 0;
        _geo = new EditorGeometry(cfg, _state);
        _geo.BlockHeight = k => _blockMeasured.GetValueOrDefault(k);   // 自動高さ block widget の実測を供給
        _geo.WidgetSize = k => _widgetMeasured.TryGetValue(k, out (float W, float H) s) ? s : null;   // 自動サイズ行内 widget の実測
        _widgets.Clear();   // 旧ホストは再実体化でスコープごと破棄済み — dict だけ捨てて Refresh で作り直す

        _root = CreateRoot(ctx, parent, worldOrigin);
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = _theme.Value.SurfaceAlt);
        FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        // ガター (行番号) — 固定 x、縦だけスクロール追従、クリップ
        _gutter = _gutterText = null;
        if (ShowLineNumbers)
        {
            _gutter = ctx.Canvas.AddChild(_root);
            _gutter.Z = 1;
            _gutter.Clip = new RectClip(0, 0, _gutterW, H);
            var gbg = new Scene2D();
            gbg.FillRect(Color2D.White, 0, 0, _gutterW, H);
            _gutter.Content = gbg;
            ctx.Effect(() => _gutter.Color = _theme.Value.Surface);
            _gutterText = ctx.Canvas.AddChild(_gutter);
            _gutterText.Z = 1;
            _gutterText.ContentColors = true;
            ctx.Effect(() => _gutterText!.Transform = Affine2D.Translate(0, Pad - _scroll.Clamped));
        }

        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 2;
        clip.Clip = new RectClip(_gutterW, 0, W - _gutterW, H);
        _content = ctx.Canvas.AddChild(clip);
        ctx.Effect(() => _content.Transform = Affine2D.Translate(ContentX, Pad - _scroll.Clamped));

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
        _caretNode.ContentColors = true;   // 主/セカンダリで色を変えるため色を焼き込む
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

        // Fill リサイズ (config=折返し幅 再構築) と 自動高さ block widget の再測定を次フレームで反映 (再入回避で収束)
        ctx.AddAnimation(_ =>
        {
            if (_layoutDirty) { _layoutDirty = false; _geo?.Configure(BuildConfig()); Refresh(); }
            else if (_blockDirty) { _blockDirty = false; Refresh(); }
            return false;
        });

        float t = 0;
        ctx.AddAnimation(dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; });

        _focus ??= new FocusTarget
        {
            OnFocus = on => { Focused.Value = on; if (on) _caretOn.Value = true; },
            OnKey = OnKey,
            OnText = s => { _compText = ""; Apply(EditCommands.InsertText(_state, s)); },
            OnComposeEx = c =>
            {
                if (_state.Selection.Ranges.Count > 1) _state = EditCommands.ClearSecondaryCursors(_state).State;   // IME は主のみ
                _compText = c.Text; _compTargetStart = c.TargetStart; _compTargetLen = c.TargetLen; Refresh(); EnsureCaretVisible();
            },
            OnCommit = final => { _compText = ""; if (final.Length > 0) Apply(EditCommands.InsertText(_state, final)); else Refresh(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        void Place(float lx, float ly, bool extend, bool additive = false)
        {
            if (_geo is null || Composing) return;
            int off = _geo.HitTest(lx - ContentX, ly - Pad + _scroll.Clamped);
            EditorSelection sel;
            if (additive)   // Alt+Click: 既存キャレットを保ったまま新しいキャレットを追加 (ポインタからのマルチカーソル)
                sel = _state.Selection.AddRange(SelectionRange.Cursor(off), asMain: true);
            else
            {
                SelectionRange main = _state.Selection.Main;
                sel = EditorSelection.Single(extend ? main.Anchor : off, off);
            }
            Apply(EditCommands.SetSelection(_state, sel));
            _goalX = null; _caretOn.Value = true;
        }
        ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f, cursor: CursorKind.IBeam,
            onDragStart: e =>
            {
                Place(e.X, e.Y, extend: e.Shift, additive: e.Alt);
                if (OnClickOffset is { } cb && _geo is not null)
                    cb(_geo.HitTest(e.X - ContentX, e.Y - Pad + _scroll.Clamped));
            },
            onDrag: e => Place(e.X, e.Y, extend: true),
            onMovePos: e => OnHoverMove(e.X, e.Y));
        ctx.AddScroll(_root, new Rect(0, 0, W, H), d => _scroll.ScrollBy(-d));
        ScrollBars.AttachVertical(ctx, _root, _scroll, W, H, minThumb: 24);

        // 補完ポップアップ + ホバーツールチップ (最前面、エディタのクリップ外 = _root 直下・高 Z)
        _popupBg = ctx.Canvas.AddChild(_root); _popupBg.Z = 2000;
        ctx.Effect(() => _popupBg.Color = _theme.Value.Surface);
        _popupSel = ctx.Canvas.AddChild(_popupBg); _popupSel.Z = 1;
        ctx.Effect(() => _popupSel!.Color = Styles.WithAlpha(_theme.Value.Primary, 60));
        _popupText = ctx.Canvas.AddChild(_popupBg); _popupText.Z = 2; _popupText.ContentColors = true;
        _tipBg = ctx.Canvas.AddChild(_root); _tipBg.Z = 2001;
        ctx.Effect(() => _tipBg.Color = _theme.Value.Surface);
        _tipTxt = ctx.Canvas.AddChild(_tipBg); _tipTxt.Z = 1; _tipTxt.ContentColors = true;

        float rh0 = _fs + 8;
        for (int i = 0; i < MaxCompRows; i++)
        {
            int row = i;
            ctx.AddHit(_popupBg, new Rect(0, 2 + row * rh0, PopupHitW, rh0),
                onClick: () => PopupRowClick(row))
                .Active = () => _compOpen && row < Math.Min(MaxCompRows, _comp.Count);
        }

        // dwell ホバー: 同一位置に DwellFrames 留まったらツールチップ (フレーム基準 = 決定的)
        ctx.AddAnimation(_ =>
        {
            if (LanguageService is null || _hoverX < 0 || _tipOpen || _compOpen || _geo is null) return false;
            if (++_dwellFrames >= DwellFrames)
            {
                int off = _geo.HitTest(_hoverX - ContentX, _hoverY - Pad + _scroll.Clamped);
                string? h = LanguageService.Hover(_state.Doc.Text, off);
                if (!string.IsNullOrEmpty(h)) { _tipText = h!; _tipOpen = true; Refresh(); }
            }
            return false;
        });

        Refresh();
    }

    private float ContentH => (_geo?.ContentHeight ?? 0) + Pad * 2;

    // ---- 入力 ----

    private bool OnKey(KeyEvent ev)
    {
        if (OnKeyIntercept is { } hook && hook(ev)) return true;   // 上位カスタムが横取り

        // 補完ポップアップが開いている間はナビゲーションを奪う
        if (_compOpen)
        {
            switch (ev.Key)
            {
                case Key.Up: _compSel = (_compSel - 1 + _comp.Count) % _comp.Count; DrawCompletion(); return true;
                case Key.Down: _compSel = (_compSel + 1) % _comp.Count; DrawCompletion(); return true;
                case Key.Enter or Key.Tab: ConfirmCompletion(); return true;
                case Key.Escape: CloseCompletion(); return true;
                case Key.Left or Key.Right or Key.Home or Key.End: CloseCompletion(); break;   // 位置移動で閉じ通常処理
                default: break;
            }
        }
        if (ev.Key == Key.Space && ev.Ctrl && LanguageService is not null) { OpenCompletion(); return true; }

        // マルチカーソル (Ctrl+D=次の同一語、Ctrl+Alt+↑↓=縦列に追加、Esc=解除)。Ctrl+Alt+↓ は Alt+↓ より先に判定
        if (ev.Ctrl && ev.Key == Key.D) { Apply(EditCommands.SelectNextOccurrence(_state)); _goalX = null; return true; }
        if (ev.Ctrl && ev.Alt && ev.Key == Key.Down) { AddColumnCursor(+1); return true; }
        if (ev.Ctrl && ev.Alt && ev.Key == Key.Up) { AddColumnCursor(-1); return true; }
        if (ev.Key == Key.Escape && _state.Selection.Ranges.Count > 1) { Apply(EditCommands.ClearSecondaryCursors(_state)); return true; }

        // 行操作 (VS Code 風。Shift+Alt+↓=複製、Alt+↑↓=移動、Ctrl+/=コメント)
        if (ev.Alt && ev.Shift && ev.Key == Key.Down) { Apply(EditCommands.DuplicateLine(_state)); _goalX = null; return true; }
        if (ev.Alt && ev.Key == Key.Up) { Apply(EditCommands.MoveLineUp(_state)); _goalX = null; return true; }
        if (ev.Alt && ev.Key == Key.Down) { Apply(EditCommands.MoveLineDown(_state)); _goalX = null; return true; }
        if (ev.Ctrl && ev.Key == Key.Slash) { Apply(EditCommands.ToggleLineComment(_state)); _goalX = null; return true; }

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

    // Ctrl+Alt+↑↓ — 主カーソルの列に沿って上/下の行へキャレットを 1 本追加 (縦列選択)。
    // 列 x は主キャレット基準 (同一 x への HitTest なので等幅非依存)。端では追加しない。
    private void AddColumnCursor(int dir)
    {
        if (_geo is null) return;
        IReadOnlyList<SelectionRange> ranges = _state.Selection.Ranges;
        float colX = _geo.CaretRect(_state.Selection.Main.Head).X;
        int extreme = dir > 0 ? ranges.Max(r => r.Head) : ranges.Min(r => r.Head);
        float? g = colX;
        int next = _geo.MoveVertical(extreme, dir, ref g);
        if (next == extreme || ranges.Any(r => r.Empty && r.Head == next)) return;
        var list = ranges.ToList();
        list.Add(SelectionRange.Cursor(next));
        Apply(EditCommands.SetSelection(_state, EditorSelection.Of(list, list.Count - 1)));
    }

    private void Apply(Transaction tr, bool coalesce = false)
    {
        if (ReadOnly && tr.DocChanged) return;   // 読み取り専用: 文書変更は無視 (選択/スクロールは空 ChangeSet で通す)
        if (tr.DocChanged) _history.Record(tr, coalesce);
        _state = tr.State;
        Sync();
    }

    private void Sync()
    {
        _compText = "";
        Value.Get().Value = _state.Doc.Text;
        _caretOn.Value = true;
        if (_compOpen) RefilterCompletion();   // タイプ/削除で候補を絞り直す (0 件で閉じる)
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
        BuildGutter();
        DrawCompletion();
        DrawTip();
    }

    // 行番号ガター (右寄せ、本文と同じベースライン)。スクロール追従は effect が y 平行移動でやる
    private void BuildGutter()
    {
        if (_gutterText is null || _geo is null || _primaryFont is null) return;
        var scene = new Scene2D();
        uint muted = _theme.Peek().TextMuted;
        for (int i = 0; i < _geo.LineCount; i++)
        {
            string num = (i + 1).ToString();
            float x = _gutterW - GutterPadR - _primaryFont.Measure(num, _fs).width;
            float baseline = _geo.LineTop(i) + _geo.Line(i).Layout.LineAscentAt(0);
            _primaryFont.AppendText(scene, num, x, baseline, _fs, muted);
        }
        _gutterText.Content = scene;
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
        _autoBlockKeys = _state.Decorations.All().OfType<BlockWidgetDecoration>()
            .Where(b => b.Height <= 0).Select(b => b.Key).ToHashSet();
        _autoWidgetKeys = _state.Decorations.All().OfType<WidgetDecoration>()
            .Where(w => w.Width <= 0 || w.Height <= 0).Select(w => w.Key).ToHashSet();
        var seen = new HashSet<object>();
        foreach (WidgetSlot slot in _geo!.WidgetSlots())
        {
            seen.Add(slot.Key);
            bool isNew = false;
            if (!_widgets.TryGetValue(slot.Key, out Hosted? h))
            {
                if (WidgetResolver(slot.Key) is not { } w) continue;
                UiNode container = _ctx.Canvas.AddChild(_content);
                container.Z = 5;
                h = new Hosted { Key = slot.Key, Widget = w, Container = container };
                _widgets[slot.Key] = h;
                isNew = true;
            }
            bool auto = _autoBlockKeys.Contains(slot.Key);
            // 既に実体化済みで箱が不変なら再実体化しない — ライブ/GPU/audio を毎フレーム回す埋め込みを
            // 毎 Refresh で Scope.Release()+Realize() すると使用中リソースの use-after-free で device lost する。
            // 旧スタックと同じ「1 回だけ実体化して保持、あとは埋め込み自身の scope が自己更新」。
            if (isNew)
            {
                h.Rect = slot.Rect;
                Size sz = RealizeWidget(h, auto);
                MeasureAutoBlock(slot.Key, sz, auto);
                MeasureAutoWidget(slot.Key, sz);
            }
            else if (h.Rect != slot.Rect)
            {
                // **幅が同じなら再実体化せず位置 (transform) と clip だけ更新する**。widget は初回に高さ
                // 無制約 (∞) で自然高さのまま実体化済みなので、確定時は clip を広げれば全体が見え、位置が
                // 動いた (上のブロック高さ確定で下がった) ときはルートノードの transform を差し替えれば動く。
                // ライブ GPU シーン (GpuView) は再実体化 = SceneGuard の Dispose→Init 往復で描画中リソースが
                // 壊れ device lost するので、この経路 (RealizeWidget) を通さないことが必須。積み重なった GPU
                // デモ (下段は上段の高さ確定で Y が動く) でもこれで安全。折返し幅が変わるときだけ組み直す。
                bool widthChanged = MathF.Abs(h.Rect.Width - slot.Rect.Width) > 0.5f;
                float dx = slot.Rect.X - h.Rect.X, dy = slot.Rect.Y - h.Rect.Y;
                h.Rect = slot.Rect;
                if (widthChanged)
                {
                    Size sz = RealizeWidget(h, auto);
                    MeasureAutoBlock(slot.Key, sz, auto);
                    MeasureAutoWidget(slot.Key, sz);
                }
                else
                {
                    RepositionHosted(h);
                    h.Widget.ShiftWorldPos(new Point(dx, dy));   // WorldPos も同期 (programmatic d.Click 用)
                }
            }
        }
        if (_widgets.Count > seen.Count)
            foreach (object k in _widgets.Keys.Where(k => !seen.Contains(k)).ToList())
            { DisposeHosted(_widgets[k]); _widgets.Remove(k); }
    }

    // 自動高さ block widget の実測を反映 (変化があれば次フレームで採用高さに = _blockDirty)
    private void MeasureAutoBlock(object key, Size sz, bool auto)
    {
        if (!auto || sz.Height <= 0) return;
        if (MathF.Abs(_blockMeasured.GetValueOrDefault(key) - sz.Height) > 0.5f)
        { _blockMeasured[key] = sz.Height; _blockDirty = true; }
    }

    // 自動サイズ行内 widget の実測を反映 (行内なので W/H 両方、変化で次フレームに採用 = _blockDirty)
    private void MeasureAutoWidget(object key, Size sz)
    {
        if (!_autoWidgetKeys.Contains(key) || sz.Width <= 0 || sz.Height <= 0) return;
        (float W, float H) old = _widgetMeasured.GetValueOrDefault(key);
        if (MathF.Abs(old.W - sz.Width) > 0.5f || MathF.Abs(old.H - sz.Height) > 0.5f)
        { _widgetMeasured[key] = (sz.Width, sz.Height); _blockDirty = true; }
    }

    // widget をスロットへ実体化。auto=自動高さ block widget は高さ無制約で組んで自然高さを測る。
    private Size RealizeWidget(Hosted h, bool autoHeight = false)
    {
        Widget w = h.Widget;
        w.Scope?.Release();
        var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
        // 自動サイズ行内 widget は幅・高さとも無制約で自然サイズを測る (箱幅は測定前の見積りなので縛らない)。
        bool autoWidget = _autoWidgetKeys.Contains(h.Key);
        float maxW = autoWidget ? float.PositiveInfinity : h.Rect.Width;
        float maxH = (autoWidget || autoHeight) ? float.PositiveInfinity : h.Rect.Height;
        Size sz = w.Layout(new Constraints(0, maxW, 0, maxH), lc);
        w.Offset = new Point(h.Rect.X, h.Rect.Y);
        // 枠でクリップ (はみ出しを隠す)。RectClip は container (=_content の子) のローカル空間 = h.Rect と同じ座標系。
        h.Container.Clip = WidgetClip == WidgetClipMode.Box
            ? new RectClip(h.Rect.X, h.Rect.Y, h.Rect.Width, h.Rect.Height) : null;
        // worldOrigin は container (=_content) の原点。CreateRoot が WorldPos = worldOrigin + Offset とするので
        // ここに rect を足すと二重計上になる (描画はノード transform で正しいが WorldPos がずれ d.Click が外れる)。
        w.Realize(_ctx, h.Container, new Point(WorldPos.X + ContentX, WorldPos.Y + Pad - _scroll.Clamped));
        w.ParentWidget = this;
        return sz;
    }

    // 実体化済み埋め込みを再実体化せず移動する (位置 + clip のみ)。自動高さ確定や上ブロックの高さ変化で
    // 箱が縦に動いても、GPU/audio シーンを Dispose→Init し直さずに済ませる (device lost 回避)。ルートノードの
    // transform を差し替える (CreateRoot が Translate(Offset) を張るのと同じ) — 実クリックは ComputeWorldNow
    // がこの transform を辿るのでヒットは追従する (programmatic d.Click の WorldPos だけは据え置きだが read-only
    // 文書では不要)。
    private void RepositionHosted(Hosted h)
    {
        h.Widget.Offset = new Point(h.Rect.X, h.Rect.Y);
        if (h.Container.Children.Count > 0)
            h.Container.Children[0].Transform = Affine2D.Translate(h.Rect.X, h.Rect.Y);
        h.Container.Clip = WidgetClip == WidgetClipMode.Box
            ? new RectClip(h.Rect.X, h.Rect.Y, h.Rect.Width, h.Rect.Height) : null;
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
            if (ReferenceEquals(h.Widget, child))
            {
                bool auto = _autoBlockKeys.Contains(h.Key);
                MeasureAutoBlock(h.Key, RealizeWidget(h, auto), auto);   // 自動高さは再実体化でも測り直す
                return true;
            }
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
        Theme t = _theme.Peek();
        uint mainC = t.Primary, secC = Styles.WithAlpha(t.Primary, 150);   // セカンダリは薄め
        IReadOnlyList<SelectionRange> ranges = eff.Selection.Ranges;
        for (int i = 0; i < ranges.Count; i++)
        {
            TextRect cr = _geo!.CaretRect(ranges[i].Head);
            if (i == eff.Selection.MainIndex) _caretLocal = new Rect(cr.X, cr.Y, 2, cr.Height);
            caret.FillRect(i == eff.Selection.MainIndex ? mainC : secC, cr.X, cr.Y + 1, 2, MathF.Max(4, cr.Height - 2));
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
    Rect ITextInput.CaretRect => new(WorldPos.X + ContentX + _caretLocal.X, WorldPos.Y + Pad + _caretLocal.Y - _scroll.Clamped, 2, _caretLocal.Height);
}
