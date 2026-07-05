using Luxel.Document;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// コードエディタ (VS Code 風) — **等幅・折り返しなし**のプレーンテキスト編集に、行番号ガター /
/// 現在行ハイライト / シンタックスハイライト (トークン色) を載せる。編集の中核は
/// <see cref="DocumentEditor"/> をそのまま使い (TextArea と共有の資産)、この widget は
/// 「コード向けのビュー + 入力」を持つ。文書 (RichTextEditor) と違い Block/Line 装飾はない。
/// <list type="bullet">
/// <item>ハイライトは <see cref="Highlighter"/> (SH — null で無色)。**同期**トークン化 (コードは短い)</item>
/// <item>キー拡張は <see cref="OnKeyIntercept"/> — 補完 (Ctrl+Space) 等を上位が横取りできる (E2 で使う)</item>
/// <item>等幅前提: キャレット x = offset × 文字幅、ヒット = 逆算 (折り返しなしなので厳密)</item>
/// </list>
/// </summary>
[UiComponent]
public sealed partial class CodeEditor : Widget, ITextInput
{
    /// <summary>編集対象のコード (双方向)。</summary>
    [UiParam] private readonly Bindable<Signal<string>> _value = new();
    /// <summary>表示高さ (px、最小 40)。</summary>
    [UiParam] private readonly Bindable<float> _editorHeight = 200f;
    /// <summary>表示幅 (px)。</summary>
    [UiParam] private readonly Bindable<float> _editorWidth = new();
    /// <summary>基準文字サイズ。未設定 → テーマ FontSm。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();

    /// <summary>等幅フォント (null = テーマ Font で代用 — 桁がずれるので実質必須)。</summary>
    public VectorFont? MonoFont { get; set; }
    /// <summary>ハイライタ (SH。null = 無色プレーン)。同期トークン化。</summary>
    public ISyntaxHighlighter? Highlighter { get; set; }
    /// <summary>ハイライト言語 (Highlighter.Tokenize へ渡す — "csharp" 等)。</summary>
    public string Language { get; set; } = "csharp";

    /// <summary>言語サービス (補完/診断/ホバー — null で無効)。Controls は実装 (Roslyn 等) を知らない。</summary>
    public ICodeLanguage? LanguageService { get; set; }

    /// <summary>キー横取りフック — true を返すとエディタは既定処理をしない (上位のカスタム)。</summary>
    public Func<KeyEvent, bool>? OnKeyIntercept { get; set; }

    // ---- E2 言語サービス状態 ----
    private IReadOnlyList<CodeCompletion> _completions = [];
    private int _compSel;
    private bool _compOpen;
    private IReadOnlyList<CodeDiagnostic> _diags = [];
    private string _hover = "";

    /// <summary>補完ポップアップが開いているか (play の Expect 用)。</summary>
    public bool CompletionOpen => _compOpen;
    /// <summary>補完候補数。</summary>
    public int CompletionCount => _completions.Count;
    /// <summary>直近の診断数 (波線)。</summary>
    public int DiagnosticCount => _diags.Count;
    /// <summary>キャレット位置シンボルのホバー文字列 (無ければ "")。</summary>
    public string HoverText => _hover;

    // ---- E3: 検索/置換 (バー UI は上位が駆動、ここは primitive + ハイライト) ----
    private string _search = "";
    private readonly List<(int Line, int Col)> _matches = new();
    private int _matchCur = -1;

    /// <summary>検索マッチ数。</summary>
    public int SearchMatchCount => _matches.Count;
    /// <summary>現在マッチ番号 (0 始まり、なし = -1)。</summary>
    public int SearchCurrent => _matchCur;

    /// <summary>検索語を設定して全マッチを収集する (大小区別)。空でクリア。</summary>
    public void SetSearch(string query)
    {
        _search = query ?? "";
        _matches.Clear();
        _matchCur = -1;
        if (_search.Length > 0)
        {
            for (int i = 0; i < LineCount; i++)
            {
                string s = _ed.Doc.LineAt(i).Text;
                int idx = 0;
                while ((idx = s.IndexOf(_search, idx, StringComparison.Ordinal)) >= 0)
                {
                    _matches.Add((i, idx));
                    idx += _search.Length;
                }
            }
            if (_matches.Count > 0) { _matchCur = 0; MoveToMatch(); }
        }
        Refresh();
    }

    /// <summary>次のマッチへ (ラップ)。</summary>
    public void FindNext() => Step(+1);
    /// <summary>前のマッチへ。</summary>
    public void FindPrev() => Step(-1);

    private void Step(int dir)
    {
        if (_matches.Count == 0) return;
        _matchCur = (_matchCur + dir + _matches.Count) % _matches.Count;
        MoveToMatch();
        Refresh();
    }

    private void MoveToMatch()
    {
        (int line, int col) = _matches[_matchCur];
        var a = new DocPos(line, col);
        var b = new DocPos(line, col + _search.Length);
        _ed.Select(a, b);   // マッチを選択
        EnsureCaretVisible();
    }

    /// <summary>現在マッチを置換して次へ。</summary>
    public void ReplaceCurrent(string replacement)
    {
        if (_matchCur < 0 || _matchCur >= _matches.Count) return;
        MoveToMatch();
        _ed.Insert(replacement);   // 選択を置換
        Sync();
        SetSearch(_search);        // マッチを取り直す (位置がずれる)
    }

    /// <summary>全マッチを置換。</summary>
    public void ReplaceAll(string replacement)
    {
        if (_search.Length == 0) return;
        string replaced = _ed.Doc.PlainText.Replace(_search, replacement);
        _ed.SetText(replaced);
        Sync();
        SetSearch("");
    }

    private readonly DocumentEditor _ed = new();
    private bool _edInit;
    private readonly Signal<bool> _caretOn = new(true);
    private readonly ScrollModel _scroll = new();   // スクロール位置 + サム計算 (再実体化を跨いで生存)

    private const float Pad = 6f;         // 上下パディング
    private const float GutterPadR = 8f;  // ガターと本文の間
    private float _fs = 13, _adv, _charW, _lineH, _gutterW, _ascent;
    private float? _goalX;

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _content = null!, _gutter = null!;
    private UiNode _curLine = null!, _selNode = null!, _textNode = null!, _caretNode = null!;
    private FocusTarget? _focus;
    private Rect _caretLocal;

    private float W => MathF.Max(120, EditorWidth.Or(360));
    private float H => MathF.Max(40, EditorHeight.Get());
    private int LineCount => _ed.Doc.LineCount;

    public override string DebugType => "CodeEditor";
    public override string? DebugDetail => $"{LineCount} 行";

    /// <summary>現在のコード (テスト/上位からの読み取り)。</summary>
    public string Text => _ed.Doc.PlainText;
    /// <summary>キャレットの全文フラットオフセット (言語サービス連携用)。</summary>
    public int CaretOffset
    {
        get
        {
            int flat = 0;
            for (int i = 0; i < _ed.Caret.Line; i++) flat += _ed.Doc.LineAt(i).Text.Length + 1;
            return flat + _ed.Caret.Offset;
        }
    }

    /// <summary>キャレット直下のワールド座標 (E2 のポップアップ配置用)。</summary>
    public Point CaretWorld => new(WorldPos.X + _caretLocal.X, WorldPos.Y + _caretLocal.Y - _scroll.Clamped + _lineH);

    /// <summary>キャレット位置に挿入して同期する (補完確定に使う)。</summary>
    public void InsertAtCaret(string s)
    {
        EnsureInit();
        _ed.Insert(s);
        Sync();
        Refresh();
    }

    private void EnsureInit()
    {
        if (_edInit) return;
        _edInit = true;
        _ed.SetText(Value.Get().Peek());
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        EnsureInit();
        _fs = FontSize.Or(ctx.Theme.FontSm);
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        EnsureInit();
        _ctx = ctx;
        _theme = ctx.Theme;
        _fs = FontSize.Or(_theme.Peek().FontSm);
        VectorFont mono = MonoFont ?? ctx.Font;
        _charW = mono.Measure("M", _fs).width;
        _adv = mono.Measure("Mg", _fs).height;
        _lineH = _fs * 1.5f;
        _ascent = mono.Ascent(_fs);
        _gutterW = MathF.Max(2, LineCount.ToString().Length) * _charW + GutterPadR * 2;

        _root = CreateRoot(ctx, parent, worldOrigin);
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = _theme.Value.SurfaceAlt);

        if (!ReadOnlyFocusless) FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        // ガター地 (固定、スクロールしない)
        _gutter = ctx.Canvas.AddChild(_root);
        _gutter.Z = 1;
        _gutter.Clip = new RectClip(0, 0, _gutterW, H);   // スクロールで消えた行番号がはみ出さない
        var gbg = new Scene2D();
        gbg.FillRect(Color2D.White, 0, 0, _gutterW, H);
        _gutter.Content = gbg;
        ctx.Effect(() => _gutter.Color = Styles.WithAlpha(_theme.Value.Surface, 255));

        // 本文クリップ (ガター右)
        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 2;
        clip.Clip = new RectClip(_gutterW, 0, W - _gutterW, H);
        _content = ctx.Canvas.AddChild(clip);
        ctx.Effect(() => _content.Transform = Affine2D.Translate(_gutterW, Pad - _scroll.Clamped));

        _curLine = ctx.Canvas.AddChild(_content);   // 現在行ハイライト (背面)
        ctx.Effect(() => _curLine.Color = Styles.WithAlpha(_theme.Value.Primary, 22));
        _searchNode = ctx.Canvas.AddChild(_content);   // 検索マッチ (選択の背面)
        _searchNode.Z = 0;
        ctx.Effect(() => _searchNode.Color = Styles.WithAlpha(_theme.Value.Warning, 90));
        _selNode = ctx.Canvas.AddChild(_content);
        _selNode.Z = 1;
        ctx.Effect(() => _selNode.Color = Styles.WithAlpha(_theme.Value.Primary, 70));
        _textNode = ctx.Canvas.AddChild(_content);
        _textNode.Z = 2;
        _textNode.ContentColors = true;   // トークン色を保持
        _caretNode = ctx.Canvas.AddChild(_content);
        _caretNode.Z = 3;
        ctx.Effect(() => _caretNode.Color = _theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value ? 1f : 0f);

        // 診断波線 (本文と同じ座標系、テキストの上)
        _diagNode = ctx.Canvas.AddChild(_content);
        _diagNode.Z = 3;
        ctx.Effect(() => _diagNode.Color = _theme.Value.Danger);

        // ガター行番号 (スクロールに追従するので content と同じ y、ただし x は固定 → gutter の子)。
        // スクロール追従は effect でリアクティブに — ホイール (signal 更新) でも Refresh を待たず動く
        _gutterText = ctx.Canvas.AddChild(_gutter);
        _gutterText.Z = 1;
        ctx.Effect(() => _gutterText.Color = _theme.Value.TextMuted);
        ctx.Effect(() => _gutterText.Transform = Affine2D.Translate(0, Pad - _scroll.Clamped));

        // 補完ポップアップ (最前面、root 直下 = クリップ外 OK)
        _popupBg = ctx.Canvas.AddChild(_root);
        _popupBg.Z = 10;
        ctx.Effect(() => _popupBg.Color = _theme.Value.Surface);
        _popupSel = ctx.Canvas.AddChild(_popupBg);
        _popupSel.Z = 11;
        ctx.Effect(() => _popupSel.Color = Styles.WithAlpha(_theme.Value.Primary, 60));
        _popupText = ctx.Canvas.AddChild(_popupBg);
        _popupText.Z = 12;
        _popupText.ContentColors = true;

        RefreshDiagnostics();
        Refresh();
        ctx.Effect(() =>
        {
            string v = Value.Get().Value;
            if (v != _ed.Doc.PlainText) { _ed.SetText(v); _scroll.ScrollTo(0); Refresh(); }
        });

        float t = 0;
        ctx.AddAnimation(dt => { t += dt; if (t >= 0.53f) { t = 0; _caretOn.Value = !_caretOn.Value; } return false; });

        _focus ??= new FocusTarget
        {
            OnFocus = on => { Focused.Value = on; if (on) _caretOn.Value = true; },
            OnKey = OnKey,
            OnText = s => { _ed.Insert(s); _goalX = null; Sync(); },
            OnComposeEx = c => { _ed.SetComposition(c.Text, c.TargetStart, c.TargetLen); Refresh(); },
            OnCommit = s => { _ed.CommitComposition(s); _goalX = null; Sync(); },
            TextInput = this,
        };
        FocusTarget f = ctx.AddFocusable(_focus);

        void Place(float lx, float ly, bool extend)
        {
            if (_ed.Composition.Length > 0) return;
            DocPos p = HitDoc(lx, ly);
            _ed.Select(extend ? _ed.Anchor : p, p);
            _goalX = null; _caretOn.Value = true; Refresh();
        }
        ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f, cursor: CursorKind.IBeam,
            onDragStart: e => Place(e.X, e.Y, extend: false),
            onDrag: e => Place(e.X, e.Y, extend: true));
        ctx.AddScroll(_root, new Rect(0, 0, W, H),
            d => _scroll.ScrollBy(-d));
        ScrollBars.AttachVertical(ctx, _root, _scroll, W, H, minThumb: 24);
    }

    private UiNode _gutterText = null!, _diagNode = null!, _popupBg = null!, _popupSel = null!, _popupText = null!, _searchNode = null!;
    private bool ReadOnlyFocusless => false;
    private float ContentH => LineCount * _lineH + Pad * 2;
    private float MaxScroll => MathF.Max(0, ContentH - H);

    // ---- 入力 ----

    private bool OnKey(KeyEvent ev)
    {
        if (OnKeyIntercept is { } hook && hook(ev)) return true;   // 上位カスタムが横取り

        // 補完ポップアップが開いている間はナビゲーションを奪う
        if (_compOpen)
        {
            switch (ev.Key)
            {
                case Key.Up: _compSel = (_compSel - 1 + _completions.Count) % _completions.Count; Refresh(); return true;
                case Key.Down: _compSel = (_compSel + 1) % _completions.Count; Refresh(); return true;
                case Key.Enter or Key.Tab: ConfirmCompletion(); return true;
                case Key.Escape: CloseCompletion(); return true;
                case Key.Left or Key.Right or Key.Home or Key.End: CloseCompletion(); break;   // 位置移動で閉じて通常処理
                default: break;
            }
        }
        if (ev.Key == Key.Space && ev.Ctrl && LanguageService is not null) { OpenCompletion(); return true; }

        // 行操作 (E3) — VS Code 風キーバインド
        if (ev.Ctrl && ev.Key == Key.D) { DuplicateLine(); return true; }
        if (ev.Ctrl && ev.Key == Key.Slash) { ToggleComment(); return true; }
        if (ev.Alt && ev.Key == Key.Up) { MoveLine(-1); return true; }
        if (ev.Alt && ev.Key == Key.Down) { MoveLine(+1); return true; }

        switch (ev.Key)
        {
            case Key.Left: _ed.MoveLeft(ev.Shift); _goalX = null; break;
            case Key.Right: _ed.MoveRight(ev.Shift); _goalX = null; break;
            case Key.Up: MoveVertical(-1, ev.Shift); break;
            case Key.Down: MoveVertical(+1, ev.Shift); break;
            case Key.Home: _ed.Home(ev.Shift); _goalX = null; break;
            case Key.End: _ed.End(ev.Shift); _goalX = null; break;
            case Key.Enter: _ed.InsertNewline(); _goalX = null; Sync(); return true;
            case Key.Tab: _ed.Insert("    "); _goalX = null; Sync(); return true;   // 4 スペース
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
        if (_compOpen && ev.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) CloseCompletion();
        UpdateHover();
        _caretOn.Value = true; Refresh(); EnsureCaretVisible();
        return true;
    }

    // ---- E2: 補完 / 診断 / ホバー ----

    private void OpenCompletion()
    {
        if (LanguageService is null) return;
        _completions = LanguageService.Complete(Text, CaretOffset);
        _compSel = 0;
        _compOpen = _completions.Count > 0;
        Refresh();
    }

    private void CloseCompletion()
    {
        if (!_compOpen) return;
        _compOpen = false;
        Refresh();
    }

    private void ConfirmCompletion()
    {
        if (!_compOpen || _compSel >= _completions.Count) { CloseCompletion(); return; }
        // 直前に打った識別子の断片を消してから挿入 (VS Code 風の置換)
        string insert = _completions[_compSel].InsertText;
        string line = _ed.Doc.LineAt(_ed.Caret.Line).Text;
        int start = _ed.Caret.Offset;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;
        int fragLen = _ed.Caret.Offset - start;
        for (int i = 0; i < fragLen; i++) _ed.Backspace();
        _ed.Insert(insert);
        _compOpen = false;
        _goalX = null;
        Sync();
    }

    // ---- E3: 行操作 ----

    /// <summary>現在行を直下に複製 (Ctrl+D)。キャレットは複製行の同じ桁へ。</summary>
    private void DuplicateLine()
    {
        int col = _ed.Caret.Offset;
        string s = _ed.Doc.LineAt(_ed.Caret.Line).Text;
        _ed.End(false);
        _ed.InsertNewline();
        _ed.Insert(s);
        var p = new DocPos(_ed.Caret.Line, Math.Min(col, s.Length));
        _ed.Select(p, p);
        _goalX = null; Sync();
    }

    /// <summary>現在行の行コメント "// " をトグル (Ctrl+/)。</summary>
    private void ToggleComment()
    {
        int line = _ed.Caret.Line;
        string s = _ed.Doc.LineAt(line).Text;
        int indent = 0;
        while (indent < s.Length && s[indent] == ' ') indent++;
        bool commented = s.Length >= indent + 2 && s[indent] == '/' && s[indent + 1] == '/';

        var start = new DocPos(line, indent);
        _ed.Select(start, start);   // インデント直後へ
        if (commented)
        {
            _ed.DeleteForward(); _ed.DeleteForward();                    // "//"
            if (indent + 2 < s.Length && s[indent + 2] == ' ') _ed.DeleteForward();   // 続く空白 1
        }
        else _ed.Insert("// ");
        _goalX = null; Sync();
    }

    /// <summary>現在行を上/下の行と入れ替える (Alt+↑/↓)。全文文字列でスワップして SetText
    /// (行入れ替えは 1 undo 単位で扱う — VS Code と同じ挙動)。</summary>
    private void MoveLine(int dir)
    {
        int line = _ed.Caret.Line;
        int to = line + dir;
        if (to < 0 || to >= LineCount) return;
        var lines = new List<string>(_ed.Doc.PlainText.Split('\n'));
        (lines[line], lines[to]) = (lines[to], lines[line]);
        int col = _ed.Caret.Offset;
        _ed.SetText(string.Join('\n', lines));
        var p = new DocPos(to, Math.Min(col, lines[to].Length));
        _ed.Select(p, p);
        _goalX = null; Sync();
    }

    private void RefreshDiagnostics()
    {
        _diags = LanguageService?.Diagnose(Text) ?? [];
    }

    private void UpdateHover()
    {
        _hover = LanguageService?.Hover(Text, CaretOffset) ?? "";
    }

    private void MoveVertical(int dir, bool select)
    {
        int line = Math.Clamp(_ed.Caret.Line + dir, 0, LineCount - 1);
        _goalX ??= _ed.Caret.Offset;
        int off = Math.Clamp((int)_goalX.Value, 0, _ed.Doc.LineAt(line).Text.Length);
        var p = new DocPos(line, off);
        _ed.Select(select ? _ed.Anchor : p, p);
    }

    private DocPos HitDoc(float lx, float ly)
    {
        float y = ly + _scroll.Clamped - Pad;
        int line = Math.Clamp((int)(y / _lineH), 0, LineCount - 1);
        float x = lx - _gutterW;
        int off = Math.Clamp((int)MathF.Round(x / MathF.Max(1, _charW)), 0, _ed.Doc.LineAt(line).Text.Length);
        return new DocPos(line, off);
    }

    private void Sync()
    {
        Value.Get().Value = _ed.Doc.PlainText;
        RefreshDiagnostics();
        UpdateHover();
        Refresh();
        EnsureCaretVisible();
    }

    private bool CopySelection()
    {
        if (!_ed.HasSelection || UiClipboard.Instance is not IClipboard clip) return false;
        clip.SetText(_ed.GetText(_ed.SelMin, _ed.SelMax));
        return true;
    }

    private void EnsureCaretVisible()
    {
        _scroll.SetLengths(ContentH, H);   // 行数変化を反映
        float top = _ed.Caret.Line * _lineH;
        _scroll.EnsureVisible(top, top + _lineH, Pad);
    }

    // ---- 描画 ----

    private void Refresh()
    {
        if (_ctx is null) return;
        _scroll.SetLengths(ContentH, H);   // スクロールバーへ内容/表示サイズを伝える
        VectorFont mono = MonoFont ?? _ctx.Font;
        Theme t = _theme.Peek();

        // 本文 (トークン色) — 折り返しなしなので 1 行 = 1 Y
        var text = new Scene2D();
        var gutter = new Scene2D();
        for (int i = 0; i < LineCount; i++)
        {
            string s = _ed.Doc.LineAt(i).Text;
            float y = i * _lineH + (_lineH - _adv) / 2 + _ascent;
            AppendColored(text, mono, s, i, y, t);
            // 行番号 (右寄せ)
            string num = (i + 1).ToString();
            float nx = _gutterW - GutterPadR - mono.Measure(num, _fs).width;
            mono.AppendText(gutter, num, nx, y, _fs, Color2D.White);
        }
        _textNode.Content = text;
        _gutterText.Content = gutter;   // 位置は effect がリアクティブに追従 (スクロール)

        // 現在行ハイライト (全幅)
        var cl = new Scene2D();
        cl.FillRect(Color2D.White, 0, _ed.Caret.Line * _lineH, MathF.Max(W - _gutterW, ContentH), _lineH);
        _curLine.Content = cl;

        // 検索マッチ (全マッチを黄色帯で)
        var search = new Scene2D();
        for (int mi = 0; mi < _matches.Count; mi++)
        {
            (int line, int col) = _matches[mi];
            string s = _ed.Doc.LineAt(line).Text;
            float x0 = mono.Measure(s[..Math.Min(col, s.Length)], _fs).width;
            float x1 = mono.Measure(s[..Math.Min(col + _search.Length, s.Length)], _fs).width;
            search.FillRect(Color2D.White, x0, line * _lineH, MathF.Max(2, x1 - x0), _lineH);
        }
        _searchNode.Content = search;

        // 選択 (行ごとの矩形)
        var sel = new Scene2D();
        if (_ed.HasSelection) BuildSelection(sel, mono);
        _selNode.Content = sel;

        // キャレット
        int co = _ed.Caret.Offset;
        string caretLine = _ed.Doc.LineAt(_ed.Caret.Line).Text;
        float cx = mono.Measure(caretLine[..Math.Min(co, caretLine.Length)], _fs).width;
        float cy = _ed.Caret.Line * _lineH;
        _caretLocal = new Rect(_gutterW + cx, cy, 2, _lineH);
        var caret = new Scene2D();
        caret.FillRect(Color2D.White, cx, cy + 2, 2, _lineH - 4);
        _caretNode.Content = caret;

        // 診断波線 (行/桁/長さから x0..x1、ベースライン下にジグザグ)
        var diag = new Scene2D();
        foreach (CodeDiagnostic dg in _diags)
        {
            int li = dg.Line - 1;
            if (li < 0 || li >= LineCount) continue;
            string s = _ed.Doc.LineAt(li).Text;
            int c0 = Math.Clamp(dg.Column - 1, 0, s.Length);
            int c1 = Math.Clamp(dg.Column - 1 + dg.Length, c0, s.Length);
            float x0 = mono.Measure(s[..c0], _fs).width;
            float x1 = c1 > c0 ? mono.Measure(s[..c1], _fs).width : x0 + _charW;
            float wy = li * _lineH + (_lineH + _adv) / 2 + 1;   // ベースライン少し下
            for (float x = x0; x < x1 - 1; x += 4)              // 2px 山のジグザグ
            {
                diag.FillRect(Color2D.White, x, wy, 2, 1);
                diag.FillRect(Color2D.White, x + 2, wy + 1, 2, 1);
            }
        }
        _diagNode.Content = diag;

        DrawPopup(mono);
        // ガター幅は桁数で変わりうる — 再実体化はしない (次の Realize で反映)。
    }

    /// <summary>補完ポップアップ (キャレット直下、キーボード駆動 — ↑↓/Enter/Escape)。</summary>
    private void DrawPopup(VectorFont mono)
    {
        if (!_compOpen || _completions.Count == 0)
        {
            _popupBg.Content = null; _popupSel.Content = null; _popupText.Content = null;
            _popupBg.Visible = _popupSel.Visible = _popupText.Visible = false;
            return;
        }
        _popupBg.Visible = _popupSel.Visible = _popupText.Visible = true;

        const int maxRows = 8;
        float rowH = _fs + 8;
        int n = Math.Min(maxRows, _completions.Count);
        float wpx = 0;
        foreach (CodeCompletion it in _completions.Take(maxRows))
            wpx = MathF.Max(wpx, mono.Measure($"{it.Label}  {it.Kind}", _fs).width);
        wpx += 16;
        float px = _gutterW + _caretLocal.X - _gutterW;   // キャレット x (root ローカル)
        float py = (_ed.Caret.Line + 1) * _lineH + Pad - _scroll.Clamped;
        _popupBg.Transform = Affine2D.Translate(_caretLocal.X, py);

        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, wpx, n * rowH + 4, 4);
        _popupBg.Content = bg;

        var selBg = new Scene2D();
        int top = Math.Clamp(_compSel - maxRows + 1, 0, Math.Max(0, _completions.Count - maxRows));
        selBg.FillRect(Color2D.White, 2, 2 + (_compSel - top) * rowH, wpx - 4, rowH);
        _popupSel.Content = selBg;

        var txt = new Scene2D();
        for (int i = 0; i < n; i++)
        {
            CodeCompletion it = _completions[top + i];
            float y = 2 + i * rowH + (rowH - _adv) / 2 + _ascent;
            mono.AppendText(txt, it.Label, 6, y, _fs, _theme.Peek().Text);
            float kx = wpx - 8 - mono.Measure(it.Kind, _fs).width;
            mono.AppendText(txt, it.Kind, kx, y, _fs, _theme.Peek().TextMuted);
        }
        _popupText.Content = txt;
        _ = px;
    }

    private void AppendColored(Scene2D scene, VectorFont mono, string line, int lineIndex, float y, Theme t)
    {
        if (Highlighter is null || line.Length == 0)
        {
            if (line.Length > 0) mono.AppendText(scene, line, 0, y, _fs, t.Text);
            return;
        }
        // 行単位トークン化 (コードは短い — 同期で十分)
        SyntaxToken[] toks;
        try { toks = Highlighter.Tokenize(Language, line); }
        catch { mono.AppendText(scene, line, 0, y, _fs, t.Text); return; }
        int pos = 0; float x = 0;
        foreach (SyntaxToken tk in toks)
        {
            int s = Math.Clamp(tk.Start, 0, line.Length);
            int e = Math.Clamp(tk.Start + tk.Length, s, line.Length);
            if (s > pos) x = Emit(scene, mono, line[pos..s], x, y, t.Text);
            if (e > s) x = Emit(scene, mono, line[s..e], x, y, TokenColor(t, tk.Kind));
            pos = Math.Max(pos, e);
        }
        if (pos < line.Length) Emit(scene, mono, line[pos..], x, y, t.Text);
    }

    private float Emit(Scene2D scene, VectorFont mono, string s, float x, float y, uint color)
    {
        if (s.Length == 0) return x;
        mono.AppendText(scene, s, x, y, _fs, color);
        return x + mono.Measure(s, _fs).width;
    }

    private void BuildSelection(Scene2D sel, VectorFont mono)
    {
        DocPos a = _ed.SelMin, b = _ed.SelMax;
        for (int i = a.Line; i <= b.Line; i++)
        {
            string s = _ed.Doc.LineAt(i).Text;
            int from = i == a.Line ? a.Offset : 0;
            int to = i == b.Line ? b.Offset : s.Length;
            float x0 = mono.Measure(s[..Math.Min(from, s.Length)], _fs).width;
            float x1 = i == b.Line
                ? mono.Measure(s[..Math.Min(to, s.Length)], _fs).width
                : mono.Measure(s, _fs).width + _charW;   // 行末まで + 改行 1 文字ぶん
            sel.FillRect(Color2D.White, x0, i * _lineH, MathF.Max(2, x1 - x0), _lineH);
        }
    }

    private static uint TokenColor(Theme t, TokenKind k) => k switch
    {
        TokenKind.Comment => t.TokComment,
        TokenKind.String => t.TokString,
        TokenKind.Escape => t.TokEscape,
        TokenKind.Regexp => t.TokRegexp,
        TokenKind.Number => t.TokNumber,
        TokenKind.Constant => t.TokConstant,
        TokenKind.Keyword => t.TokKeyword,
        TokenKind.KeywordControl => t.TokKeywordControl,
        TokenKind.Operator => t.TokOperator,
        TokenKind.Function => t.TokFunction,
        TokenKind.Type => t.TokType,
        TokenKind.Variable => t.TokVariable,
        TokenKind.Tag => t.TokTag,
        TokenKind.Attribute => t.TokAttribute,
        _ => t.Text,
    };

    // ---- ITextInput (IME) ----
    string ITextInput.Text => _ed.Doc.PlainText;
    (int start, int length) ITextInput.Selection
    {
        get { int a = CaretOffset; return (a, 0); }
    }
    void ITextInput.Select(int start, int end) { }
    void ITextInput.Replace(int start, int end, string s) => InsertAtCaret(s);
    void ITextInput.SetComposition(ImeComposition comp) { }
    void ITextInput.CommitComposition(string final) => InsertAtCaret(final);
    void ITextInput.SetCompositionHighlight(int start, int length, int targetStart, int targetLength) { }
    Rect ITextInput.CaretRect => new(WorldPos.X + _caretLocal.X, WorldPos.Y + _caretLocal.Y - _scroll.Clamped, 2, _lineH);
}
