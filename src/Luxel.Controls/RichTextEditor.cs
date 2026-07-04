using Luxel.Document;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// リッチテキストエディタ (ED-M3)。<see cref="DocumentEditor"/> の文書を「ブロック毎の TextLayout + ノード」で
/// 整形表示しながら編集する。値は markdown 文字列 (signal) と双方向 — 意味 (見出し/強調) のみを持ち、
/// 見た目はテーマとブロック型から導出する。
/// - ブロック型の見た目: 見出し=サイズ+太字、引用=左バー+ミュート色、コード=等幅+地色、リスト=マーカー+インデント、水平線
/// - インライン: bold/italic/code は差し替えフォント (<see cref="BoldFont"/> 等、未設定なら通常フォント)、link=Primary 色
/// - 部分更新: 編集ブロックの子ノードだけ作り直し (Block.Version + 番号 + 合成状態がキー)、
///   高さ変化は後続の transform 平行移動、ブロック増減のみノード列再構築 (TextArea と同じ規律)
/// - Ctrl+B/I/E=スタイルトグル、Ctrl+Z/Y=undo/redo。ツールバーは <see cref="Apply"/> 経由で操作する
/// </summary>
[UiComponent]
public sealed partial class RichTextEditor : Widget, ITextInput
{
    private readonly Signal<string> _value;   // markdown
    private readonly DocumentEditor _ed = new();
    private readonly Signal<bool> _caretOn = new(true);
    private readonly Signal<float> _scroll = new(0);
    private UiNode? _barNode;    // スクロールバー thumb (内容が収まるときは透明)
    private float _thumbH = -1;

    private const float DefaultWidth = 480f;
    private const float Pad = 12f;
    private const float ThumbW = 6f, ThumbPad = 2f;   // スクロールバー (ScrollViewer と同寸)
    private float W = DefaultWidth;
    private readonly float _height;
    private float H;   // 実効高 (VAlign=Stretch なら領域いっぱい、それ以外は ctor の height)
    private float _fs = 16;
    private const float LineH = 1.4f;
    private float? _goalX;   // ↑↓ 移動の目標 x (キャンバス座標。水平移動/編集でリセット)

    /// <summary>基準文字サイズ。未設定 → テーマ Font。見出し等はここから導出。</summary>
    [UiParam] public readonly Bindable<float> FontSize = new();
    /// <summary>背景色。未設定 → テーマ Surface。</summary>
    [UiParam] public readonly Bindable<uint> Background = new();
    /// <summary>グリフ未収載時のフォールバック列 (先頭が通常フォント)。null = ctx.Font のみ。</summary>
    public FontCollection? Fonts { get; set; }
    /// <summary>太字/斜体/等幅の書体 (null = 通常フォントで代用)。見出しは <see cref="BoldFont"/> を使う。</summary>
    public VectorFont? BoldFont { get; set; }
    public VectorFont? ItalicFont { get; set; }
    public VectorFont? BoldItalicFont { get; set; }
    public VectorFont? MonoFont { get; set; }

    /// <summary>hybrid ソース表示 (Typora 風 MarkdownEditor モード): キャレットのあるブロックだけ
    /// markdown ソースを表示して編集し、離れると再パース → 整形表示に戻す。コードブロックは対象外
    /// (ソースが複数行になり行指向が崩れるため、常に整形のまま編集)。</summary>
    public bool HybridSource { get; set; }
    /// <summary>入力オートフォーマット: 行頭 "# "/"- "/"1. "/"> " + 空白で型変換、"```lang" + Enter でコードブロック化。</summary>
    public bool AutoFormat { get; set; } = true;

    /// <summary>読み取り専用表示 (MDX docs ページ用)。フォーカス/キャレット/選択/編集/コンテキストメニューを
    /// 無効化し、スクロール・埋め込み widget・**リンククリック**だけ残す — 描画資産 (見出し/リスト/コード/
    /// テーブル/embed) をそのまま表示専用で使う。実体化前に設定すること。</summary>
    public bool ReadOnly { get; set; }

    /// <summary>リンククリック (ReadOnly のみ。第一引数 = 発火元)。<c>#アンカー</c> は発火せず
    /// 同一文書内の見出しへスクロールする — それ以外 (story:/https: 等) がここへ届く。</summary>
    [UiEvent] public UiEvent<RichTextEditor, string> OnLink;

    /// <summary>コードブロックのトークナイザ (SH — null = ハイライトなし)。解析は共有ワーカー
    /// (<see cref="HighlightQueue"/>) で行い、結果到着時に該当ブロックだけ色付けし直す。
    /// 実体化前に設定すること。</summary>
    public ISyntaxHighlighter? Highlighter { get; set; }

    /// <summary>インライン widget の解決 (IN): Link URL → widget。非 null を返した run は
    /// リンクではなく**行内の widget** として扱われ、レイアウトに占位ボックスを取り、
    /// その矩形へ実体化される (Kit.Docs が <c>{widget:inline}</c> hole 用に配線する)。
    /// 同じ URL には**同じインスタンス**を返すこと (再レンダーで再実体化される — 状態は生きる)。</summary>
    public Func<string, Widget?>? InlineWidgetResolver { get; set; }

    // ハイライト状態: cache は UI スレッド専有、arrived はワーカー → UI の受け渡し
    private readonly Dictionary<(string Lang, string Text), SyntaxToken[]> _hlCache = new();
    private readonly HashSet<(string Lang, string Text)> _hlRequested = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<((string, string) Key, SyntaxToken[] Tokens)> _hlArrived = new();

    private int _srcBlock = -1;   // hybrid でソース展開中のブロック (-1 = なし)
    private string _synced;       // 最後に取り込んだ/書き出した markdown (value-sync の外部変更判定)

    /// <summary>編集エンジン (ツールバー等の読み取り用)。変更操作は <see cref="Apply"/> を使うこと。</summary>
    public DocumentEditor Editor => _ed;

    /// <summary>外部 (ツールバー等) からの編集操作 — 実行後に表示/値を同期する。</summary>
    public void Apply(Action<DocumentEditor> action)
    {
        action(_ed);
        Sync();
    }

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _content = null!, _selNode = null!, _caretNode = null!;
    private UiNode _underlineNode = null!, _targetNode = null!;
    private UiNode _searchNode = null!, _searchCurNode = null!;   // 検索ハイライト (SR): 全マッチ/現在マッチ
    private Rect _caretLocal;

    // 検索ハイライト状態 (SR)。矩形は選択と同じ SelectionRects 方式 — Refresh でレイアウトが
    // 変わるたびに引き直す (クエリは実体化前に設定されてもよい: 初回 Refresh で反映)
    private string _searchQuery = "";
    private readonly List<(int Block, int Start, int Len)> _searchMatches = new();
    private int _searchCur = -1;
    private bool _searchScrollPending;

    // 色キー (scene は白描き、ノード色はテーマ由来 — 単一 Effect が全ノードへ流す)。
    // 6 以降はシンタックスハイライトのトークン色 (SH — VS Code 相当の 14 分類)
    private const int CText = 1, CMuted = 2, CPrimary = 3, CBorder = 4, CSurfaceAlt = 5;
    private const int CTokBase = 6;   // CTokBase + (TokenKind - 1)。TokenKind.Text は CText
    private const int CTokMax = CTokBase + 13;
    // コールアウト (> [!NOTE] 等) のバー/ラベル色 — テーマの意味色
    private const int CInfo = CTokMax + 1, CSuccess = CTokMax + 2, CWarn = CTokMax + 3, CDanger = CTokMax + 4;
    private const int CKeyMax = CDanger;
    private static readonly uint[] SpanKeys = BuildSpanKeys();   // SpanStyle.Color ← キー

    private static uint[] BuildSpanKeys()
    {
        var keys = new uint[CKeyMax + 1];
        for (int i = 1; i <= CKeyMax; i++) keys[i] = 0xFF000000u | (uint)i;
        keys[4] = keys[5] = 0;   // CBorder/CSurfaceAlt は装飾ノード用 (スパンには使わない)
        return keys;
    }

    /// <summary>コールアウト種別 → 色キー (GitHub alert の配色に準拠)。</summary>
    private static int CalloutKey(string kind) => kind switch
    {
        "TIP" => CSuccess,
        "IMPORTANT" => CPrimary,
        "WARNING" => CWarn,
        "CAUTION" => CDanger,
        _ => CInfo,   // NOTE / 未知
    };

    private sealed class BlockView
    {
        public required UiNode Container;                       // transform = (0, Top)。子 = 色ごとのノード
        public readonly List<(UiNode Node, int ColorKey)> Colored = new();
        public required TextLayout Layout;
        public required string Key;                             // Version|ordinal|合成 — 差分検出
        public float Top, H, Indent, PadTop;
        public int Ordinal;                                     // 番号付きリストの表示番号 (1 起点、それ以外 0)
        public Widget? Embed;                                   // 実体化済み埋め込み widget (Kind == Embed)
        public readonly List<Widget> Inline = new();            // 行内 widget (IN — 再レンダーで再実体化)
    }

    private readonly List<BlockView> _views = new();
    private int _structSeen = -1;

    /// <summary>文書フォーマット (テキスト⇄ブロック列の往復・記法・オートフォーマットの管理者)。
    /// 既定 = markdown。差し替えは ctor 引数で (実体化前に確定)。</summary>
    public IDocumentFormat Format { get; }

    /// <summary>埋め込み widget の解釈 (TypeId → ファクトリ)。**フォーマットと対で決まる** —
    /// 専用フォーマットは構成済みレジストリを ctor へ渡して解釈を固定し、
    /// markdown 等の汎用ではアプリがここへ登録する。エディタ毎に独立。</summary>
    public BlockWidgetRegistry Widgets { get; }

    [UiCtor]
    internal RichTextEditor(Signal<string> markdown, float height = 240f,
        IDocumentFormat? format = null, BlockWidgetRegistry? widgets = null)
    {
        _value = markdown;
        _height = MathF.Max(60, height);
        H = _height;
        Format = format ?? MarkdownFormat.Default;
        Widgets = widgets ?? new BlockWidgetRegistry();
        _synced = markdown.Value;
        _ed.SetBlocks(Format.Parse(_synced).Blocks);
    }

    public override string? DebugDetail => $"{_ed.Doc.Blocks.Count} ブロック";

    private float ContentH => (_views.Count > 0 ? _views[^1].Top + _views[^1].H : 0) + Pad;
    private float MaxScroll => MathF.Max(0, ContentH - H);
    private float Clamped() => Math.Clamp(_scroll.Value, 0, MaxScroll);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _fs = FontSize.Or(ctx.Theme.Font);
        W = ResolveW(c, ctx, DefaultWidth);
        float w = HAlign.Get() == Align.Stretch && !float.IsInfinity(c.MaxW) ? c.MaxW : W;
        float h = VAlign.Get() == Align.Stretch && !float.IsInfinity(c.MaxH) ? c.MaxH : _height;
        Size = c.Constrain(new Size(w, h));
        W = Size.Width;
        H = Size.Height;
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _theme = ctx.Theme;
        _fs = FontSize.Or(_theme.Peek().Font);
        _views.Clear();
        _structSeen = -1;

        _root = ctx.Canvas.AddChild(parent);
        _root.Transform = Affine2D.Translate(Offset.X, Offset.Y);
        SetWorldPos(worldOrigin + Offset);

        if (!ReadOnly) FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = Background.Or(_theme.Value.Surface));

        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 1;
        clip.Clip = new RectClip(0, 0, W, H);

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

        // 検索ハイライト (SR): 蛍光マーカー風 (Warning 半透明)。現在マッチは濃く。
        // ブロックの**前面** (Z=3) に置く — コードブロックは自前の地色矩形 (ブロック内 Z=2) を
        // 持つため、背面に敷くと隠れる。半透明なので文字は透けて読める
        _searchNode = ctx.Canvas.AddChild(_content);
        _searchNode.Z = 3;
        ctx.Effect(() => _searchNode.Color = Styles.WithAlpha(_theme.Value.Warning, 70));
        _searchCurNode = ctx.Canvas.AddChild(_content);
        _searchCurNode.Z = 3;
        ctx.Effect(() => _searchCurNode.Color = Styles.WithAlpha(_theme.Value.Warning, 130));

        _caretNode = ctx.Canvas.AddChild(_content);
        _caretNode.Z = 3;
        ctx.Effect(() => _caretNode.Color = _theme.Value.Primary);
        ctx.Effect(() => _caretNode.Opacity = Focused.Value && _caretOn.Value ? 1f : 0f);

        // スクロールバー thumb: クリップの外 (_root 直下)。形は Refresh (内容高の変化)、
        // 位置はスクロール effect が更新する。内容が収まるときは透明
        _thumbH = -1;
        _barNode = ctx.Canvas.AddChild(_root);
        _barNode.Z = 2;
        _barNode.Opacity = 0f;
        ctx.Effect(() => _barNode.Color = _theme.Value.BorderColor);
        ctx.Effect(() => { _ = _scroll.Value; UpdateScrollbar(); });

        // 色はキー経由の単一 Effect (ブロック毎に Effect を作らない)
        ctx.Effect(() =>
        {
            Theme t = _theme.Value;
            foreach (BlockView v in _views)
                foreach ((UiNode n, int key) in v.Colored)
                    n.Color = ColorFor(t, key);
        });

        // 入力はブロック描画 (Refresh) より**前に**登録する — 埋め込み widget のヒットは
        // Refresh 中に登録され、後勝ち (前面) でエディタのドラッグ選択より優先される。
        // ReadOnly (MDX docs) はフォーカス/キャレット/選択/編集を丸ごと登録しない — スクロールと
        // 埋め込み widget の操作だけ残る。
        if (!ReadOnly)
        {
            float t2 = 0;
            ctx.AddAnimation(dt => { t2 += dt; if (t2 >= 0.53f) { t2 = 0; _caretOn.Value = !_caretOn.Value; } return false; });

            FocusTarget f = ctx.AddFocusable(
                onFocus: on => { Focused.Value = on; if (on) _caretOn.Value = true; },
                onKey: OnKey,
                onText: s => { _ed.Insert(s); MaybeAutoFormat(s); _goalX = null; Sync(); },
                onComposeEx: c => { _ed.SetComposition(c.Text, c.TargetStart, c.TargetLen); Refresh(); EnsureCaretVisible(); },
                onCommit: s => { _ed.CommitComposition(s); _goalX = null; Sync(); },
                textInput: this);

            void PlaceFromPoint(float lx, float ly, bool extend)
            {
                if (_ed.Composition.Length > 0) return;
                DocPos p = HitDoc(lx, ly + Clamped());
                _ed.Select(extend ? _ed.Anchor : p, p);
                _goalX = null;
                _caretOn.Value = true;
                Refresh();
                // hybrid: 進入でソース展開されると同じ座標が別 offset になる — 展開後にもう一度ヒット (正確な配置)
                if (HybridSource)
                {
                    DocPos p2 = HitDoc(lx, ly + Clamped());
                    if (p2 != _ed.Caret) { _ed.Select(extend ? _ed.Anchor : p2, p2); Refresh(); }
                }
            }
            ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f,
                onDragStart: (lx, ly) => { PlaceFromPoint(lx, ly, extend: false); MultiClick(lx, ly); },
                onDrag: (lx, ly) => PlaceFromPoint(lx, ly, extend: true),
                cursor: CursorKind.IBeam,
                onContext: (lx, ly) => ContextMenu.OpenForEditor(ctx, _root, lx, ly, f));
        }
        ctx.AddScroll(_root, new Rect(0, 0, W, H),
            d => _scroll.Value = Math.Clamp(Clamped() - d, 0, MaxScroll));

        // ハイライト結果の到着ドレイン (SH): ワーカーの結果をキャッシュへ移し、該当ブロックだけ
        // 作り直す (KeyOf の |H が変わる)。ReadOnly でも動くよう caret 点滅とは別に登録する
        if (Highlighter is not null)
            ctx.AddAnimation(_ =>
            {
                bool any = false;
                while (_hlArrived.TryDequeue(out ((string, string) Key, SyntaxToken[] Tokens) r))
                {
                    _hlCache[r.Key] = r.Tokens;
                    any = true;
                }
                if (any) Refresh();
                return false;
            });

        Refresh();
        if (ReadOnly) RegisterLinkHits(ctx);   // 静的文書前提 (Refresh 1 回) — リンク run の矩形をヒット化

        // スクロールバーのドラッグ (ScrollViewer と同じ流儀 — サム掴み / トラック押下はジャンプして
        // そのまま掴む)。Refresh (埋め込み/リンクのヒット登録) より後に登録 = 右端の帯は前面勝ち
        {
            const float grabW = ThumbW + ThumbPad * 2 + 6;
            float grabOffset = 0;
            void SetFromThumbTop(float top)
                => _scroll.Value = Math.Clamp(top / MathF.Max(1, H - _thumbH), 0, 1) * MaxScroll;
            ctx.AddHit(_root, new Rect(W - grabW, 0, grabW, H),
                onDragStart: (_, ly) =>
                {
                    if (MaxScroll <= 0 || _thumbH <= 0) return;
                    float thumbTop = Clamped() / MaxScroll * (H - _thumbH);
                    bool onThumb = ly >= thumbTop && ly <= thumbTop + _thumbH;
                    grabOffset = onThumb ? ly - thumbTop : _thumbH / 2;
                    SetFromThumbTop(ly - grabOffset);
                },
                onDrag: (_, ly) => { if (MaxScroll > 0 && _thumbH > 0) SetFromThumbTop(ly - grabOffset); });
        }
        ctx.Effect(() =>
        {
            string v = _value.Value;
            // 「最後に取り込んだ/書き出した値」との比較で外部変更だけに反応する。
            // SerializeForValue() 比較だと正規化差 (絵文字ショートコード/SmartyPants 等) で恒常不一致になり、
            // effect が Refresh 中に読んだ signal (_scroll 等) へ購読して、スクロールのたびに
            // 巻き戻す再発火ループになる (MDX2 のアンカースクロールで顕在化)。
            if (v != _synced)
            {
                _synced = v;
                _ed.SetBlocks(Format.Parse(v).Blocks);
                _srcBlock = -1;   // 旧展開状態は無効 (新文書は整形状態) — Refresh の SyncHybrid が展開し直す
                _scroll.Value = 0;
                Refresh();
            }
        });
    }

    // ---- リンク (MDX2-M2: ReadOnly docs) ----

    /// <summary>各ブロックの Link スタイル run から矩形を求めてヒット登録する。矩形はブロック
    /// Container のローカル座標 — スクロール (content transform) に自動追従する。</summary>
    private void RegisterLinkHits(UiBuildContext ctx)
    {
        for (int bi = 0; bi < _views.Count && bi < _ed.Doc.Blocks.Count; bi++)
        {
            Luxel.Document.Block b = _ed.Doc.Blocks[bi];
            if (b.Kind is Luxel.Document.BlockKind.Embed or Luxel.Document.BlockKind.CodeBlock) continue;
            BlockView v = _views[bi];
            int pos = 0;
            foreach (Luxel.Document.InlineRun r in b.Runs)
            {
                int s = pos, e = pos + r.Text.Length;
                pos = e;
                if (r.Style.Link is not string url || url.Length == 0) continue;
                if (InlineWidgetResolver?.Invoke(url) is not null) continue;   // インライン widget はリンクではない
                foreach (TextRect tr in v.Layout.SelectionRects(s, e))
                    ctx.AddHit(v.Container,
                        new Rect(Pad + v.Indent + tr.X, v.PadTop + tr.Y, tr.Width, tr.Height),
                        onClick: () => HandleLink(url), cursor: CursorKind.Hand);
            }
        }
    }

    private void HandleLink(string url)
    {
        if (url.StartsWith('#')) { ScrollToAnchor(url[1..]); return; }
        OnLink.Invoke(this, url);
    }

    /// <summary>見出しアンカーのスラグ (小文字 + 空白→ハイフン。GitHub 流の簡易版)。</summary>
    public static string Slug(string heading)
        => heading.Trim().ToLowerInvariant().Replace(' ', '-').Replace('　', '-');

    /// <summary>スラグ一致する最初の見出しへスクロールする。true = 見つかった。</summary>
    public bool ScrollToAnchor(string anchor)
    {
        string want = Slug(anchor);
        for (int i = 0; i < _ed.Doc.Blocks.Count; i++)
            if (_ed.Doc.Blocks[i].Kind == Luxel.Document.BlockKind.Heading
                && Slug(_ed.Doc.Blocks[i].Text) == want)
            {
                ScrollTo(i);
                return true;
            }
        return false;
    }

    /// <summary>指定ブロックが上端に来るようスクロールする (TOC/アンカー用)。</summary>
    public void ScrollTo(int block)
    {
        if (block < 0 || block >= _views.Count) return;
        _scroll.Value = Math.Clamp(_views[block].Top, 0, MaxScroll);
    }

    // ---- 検索ハイライト (SR) ----

    /// <summary>実体化済み (ブロックビューあり) か。実体化前はスクロール系が no-op。</summary>
    public bool Realized => _views.Count > 0;

    /// <summary>検索マッチ数 (SetSearchHighlight 後)。</summary>
    public int SearchMatchCount => _searchMatches.Count;

    /// <summary>現在マッチの番号 (0 起点、-1 = なし)。</summary>
    public int SearchCurrent => _searchCur;

    /// <summary>検索語を設定して全マッチを蛍光ハイライトする (大小無視の部分一致、null/空で解除)。
    /// 現在マッチは先頭に戻り、そこへスクロールする。実体化前でもマッチ数は確定し、
    /// 矩形とスクロールは初回 Refresh で反映される。</summary>
    public void SetSearchHighlight(string? query)
    {
        string q = query?.Trim() ?? "";
        if (q == _searchQuery) return;
        _searchQuery = q;
        FindMatches(_ed.Doc.Blocks, q, _searchMatches);
        _searchCur = _searchMatches.Count > 0 ? 0 : -1;
        if (Realized) { RefreshSearch(); ScrollToCurrentMatch(); }
        else _searchScrollPending = true;
    }

    /// <summary>次のマッチへ (末尾で先頭へ折り返し)。</summary>
    public void SearchNext() => MoveMatch(+1);

    /// <summary>前のマッチへ (先頭で末尾へ折り返し)。</summary>
    public void SearchPrev() => MoveMatch(-1);

    private void MoveMatch(int dir)
    {
        int n = _searchMatches.Count;
        if (n == 0) return;
        _searchCur = ((_searchCur + dir) % n + n) % n;
        RefreshSearch();
        ScrollToCurrentMatch();
    }

    /// <summary>全ブロックの表示テキストから大小無視の部分一致を列挙する (テスト用に分離)。</summary>
    internal static void FindMatches(IReadOnlyList<Block> blocks, string query,
                                     List<(int Block, int Start, int Len)> into)
    {
        into.Clear();
        if (string.IsNullOrWhiteSpace(query)) return;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind == BlockKind.Embed) continue;
            string t = blocks[i].Text;
            int from = 0;
            while (from + query.Length <= t.Length)
            {
                int idx = t.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                into.Add((i, idx, query.Length));
                from = idx + query.Length;
            }
        }
    }

    private void RefreshSearch()
    {
        if (_searchNode is null) return;
        var all = new Scene2D();
        var cur = new Scene2D();
        for (int mi = 0; mi < _searchMatches.Count; mi++)
        {
            (int bi, int s, int len) = _searchMatches[mi];
            if (bi >= _views.Count) continue;
            BlockView v = _views[bi];
            Scene2D target = mi == _searchCur ? cur : all;
            foreach (TextRect r in v.Layout.SelectionRects(s, s + len))
                target.FillRect(Color2D.White, Pad + v.Indent + r.X - 1, v.Top + v.PadTop + r.Y,
                                r.Width + 2, r.Height);
        }
        _searchNode.Content = all;
        _searchCurNode.Content = cur;
    }

    private void ScrollToCurrentMatch()
    {
        if (_searchCur < 0 || _searchCur >= _searchMatches.Count) return;
        (int bi, int s, _) = _searchMatches[_searchCur];
        if (bi >= _views.Count) return;
        BlockView v = _views[bi];
        TextRect r = v.Layout.CaretRect(s);
        float y = v.Top + v.PadTop + r.Y;
        // 既に見えているなら動かさない (上下端に少し余白を見て判定)
        float sc = Clamped();
        if (y - sc >= Pad && y + r.Height - sc <= H - Pad) return;
        _scroll.Value = Math.Clamp(y - H / 3, 0, MaxScroll);
    }

    private static uint ColorFor(Theme t, int key) => key switch
    {
        CText => t.Text,
        CMuted => t.TextMuted,
        CPrimary => t.Primary,
        CBorder => t.BorderColor,
        CSurfaceAlt => t.SurfaceAlt,
        CInfo => t.Info,
        CSuccess => t.Success,
        CWarn => t.Warning,
        CDanger => t.Danger,
        >= CTokBase and <= CTokMax => TokenColor(t, (TokenKind)(key - CTokBase + 1)),
        _ => t.Text,
    };

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

    // ---- シンタックスハイライト (SH: 解析は別スレッド、結果到着で色付け) ----

    private static int TokenKey(TokenKind k)
        => k == TokenKind.Text ? CText : CTokBase + (int)k - 1;

    /// <summary>コードブロックのトークン列 (キャッシュ命中のみ)。未命中はワーカーへ依頼して false —
    /// 到着まで単色で描かれ、到着ドレインが該当ブロックを作り直す。</summary>
    private bool TryGetHighlight(Block b, out SyntaxToken[] tokens)
    {
        tokens = [];
        if (Highlighter is not ISyntaxHighlighter hl) return false;
        string lang = b.CodeLang ?? "";
        if (lang.Length == 0 || !hl.Supports(lang)) return false;
        (string, string) key = (lang, b.Text);
        if (_hlCache.TryGetValue(key, out SyntaxToken[]? hit)) { tokens = hit; return true; }
        if (_hlRequested.Add(key))
        {
            string text = b.Text;
            HighlightQueue.Enqueue(() => _hlArrived.Enqueue(((lang, text), hl.Tokenize(lang, text))));
        }
        return false;
    }

    /// <summary>トークン列 → 色キー付きスパン列 (隙間は Text 色)。</summary>
    private TextLayout CodeLayout(Block b, SyntaxToken[] tokens, FontCollection fonts, float px, TextLayoutOptions opt)
    {
        string text = b.Text;
        var spans = new List<TextSpan>(tokens.Length * 2 + 1);
        int pos = 0;
        foreach (SyntaxToken tk in tokens)
        {
            int s = Math.Clamp(tk.Start, 0, text.Length);
            int e = Math.Clamp(tk.Start + tk.Length, s, text.Length);
            if (s > pos) spans.Add(Span(text[pos..s], CText));
            if (e > s) spans.Add(Span(text[s..e], TokenKey(tk.Kind)));
            pos = Math.Max(pos, e);
        }
        if (pos < text.Length) spans.Add(Span(text[pos..], CText));
        if (spans.Count == 0) spans.Add(Span("", CText));
        return new TextLayout(fonts, spans, px, opt);

        TextSpan Span(string t, int key) => new(t, new SpanStyle { Font = MonoFont, Color = SpanKeys[key] });
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
            case Key.Enter:
                if (!TryCodeFence()) _ed.InsertNewline();
                _goalX = null; Sync(); return true;
            case Key.Backspace: _ed.Backspace(); _goalX = null; Sync(); return true;
            case Key.Delete: _ed.DeleteForward(); _goalX = null; Sync(); return true;
            case Key.A when ev.Ctrl: _ed.SelectAll(); break;
            case Key.B when ev.Ctrl: _ed.ToggleBold(); Sync(); return true;
            case Key.I when ev.Ctrl: _ed.ToggleItalic(); Sync(); return true;
            case Key.E when ev.Ctrl: _ed.ToggleCode(); Sync(); return true;
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

    private void MoveVertical(int dir, bool select)
    {
        int bi = _ed.Caret.Block;
        BlockView v = _views[bi];
        TextRect cr = v.Layout.CaretRect(_ed.DisplayCaretOffset);
        float x = _goalX ?? (Pad + v.Indent + cr.X);   // goal-x はキャンバス座標 (ブロック毎の indent 差を吸収)
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

        BlockView tv = _views[targetBlock];
        int idx = Math.Clamp(tv.Layout.HitTest(x - Pad - tv.Indent, targetY), 0, _ed.Doc.Blocks[targetBlock].Length);
        var p = new DocPos(targetBlock, idx);
        _ed.Select(select ? _ed.Anchor : p, p);
    }

    private void LineHomeEnd(bool home, bool select)
    {
        int bi = _ed.Caret.Block;
        TextLayout l = _views[bi].Layout;
        int line = LineIndexOf(l, l.CaretRect(_ed.DisplayCaretOffset).Y);
        (int start, int end) = l.LineCharRange(line);
        var p = new DocPos(bi, Math.Clamp(home ? start : end, 0, _ed.Doc.Blocks[bi].Length));
        _ed.Select(select ? _ed.Anchor : p, p);
    }

    private static int LineIndexOf(TextLayout l, float y)
        => Math.Clamp((int)MathF.Round(y / l.LineAdvance), 0, l.LineCount - 1);

    private DocPos HitDoc(float lx, float ly)
    {
        int bi = _views.Count - 1;
        for (int i = 0; i < _views.Count; i++)
            if (ly < _views[i].Top + _views[i].H) { bi = i; break; }
        if (bi < 0) return new DocPos(0, 0);
        BlockView v = _views[bi];
        int idx = Math.Clamp(v.Layout.HitTest(lx - Pad - v.Indent, ly - v.Top - v.PadTop), 0, _ed.Doc.Blocks[bi].Length);
        return new DocPos(bi, idx);
    }

    private void Sync()
    {
        _synced = SerializeForValue();   // 自分の書き出しは外部変更ではない (effect の再パースを抑止)
        _value.Value = _synced;
        _caretOn.Value = true;
        Refresh();
        EnsureCaretVisible();
    }

    /// <summary>signal へ流すソーステキスト。hybrid でソース展開中のブロックは、リテラル段落のまま
    /// 直列化すると記法がエスケープされて値が汚れる (\*\*bold\*\*) — 畳んだ姿 (ParseLine) で直列化する。</summary>
    private string SerializeForValue()
    {
        if (!HybridSource || _srcBlock < 0 || _srcBlock >= _ed.Doc.Blocks.Count
            || _ed.Doc.Blocks[_srcBlock].Kind != BlockKind.Paragraph)
            return Format.Serialize(_ed.Doc);
        var blocks = new List<Block>(_ed.Doc.Blocks);
        blocks[_srcBlock] = Format.ParseLine(blocks[_srcBlock].Text);
        return Format.Serialize(RichDocument.FromBlocks(blocks));
    }

    /// <summary>選択範囲をフォーマットのソース (記法込み) としてコピー — 貼り付け先でも意味が保たれる。</summary>
    private bool CopySelection()
    {
        if (!_ed.HasSelection || UiClipboard.Instance is not IClipboard clip) return false;
        clip.SetText(Format.SerializeRange(_ed.Doc, _ed.SelMin, _ed.SelMax));
        return true;
    }

    /// <summary>ダブル = 単語 (Segmenter.GetWordAt)、トリプル = ブロック選択。onDragStart で呼ぶ。</summary>
    private DateTime _lastDown;
    private float _lastDownX, _lastDownY;
    private int _clickCount;

    private bool MultiClick(float lx, float ly)
    {
        DateTime now = DateTime.UtcNow;
        bool near = MathF.Abs(lx - _lastDownX) < 4 && MathF.Abs(ly - _lastDownY) < 4;
        _clickCount = near && (now - _lastDown) < TimeSpan.FromMilliseconds(500) ? _clickCount + 1 : 1;
        _lastDown = now;
        _lastDownX = lx;
        _lastDownY = ly;
        if (_clickCount < 2) return false;
        int bi = _ed.Caret.Block;
        string text = _ed.Doc.Blocks[bi].Text;
        if (_clickCount == 2 && text.Length > 0)
        {
            (int ws, int we) = TextSegmenter.Default.GetWordAt(text, Math.Min(_ed.Caret.Offset, text.Length - 1));
            _ed.Select(new DocPos(bi, ws), new DocPos(bi, we));
        }
        else
        {
            _ed.Select(new DocPos(bi, 0), new DocPos(bi, text.Length));
            _clickCount = 0;   // 4 連打はリセット
        }
        Refresh();
        return true;
    }

    // ---- 入力オートフォーマット ----

    /// <summary>入力オートフォーマット (行頭記法の確定) — 判断はフォーマットの責務。
    /// hybrid 中はソース畳み込み時の再パースが同じ役割を担うため無効。</summary>
    private void MaybeAutoFormat(string inserted)
    {
        if (!AutoFormat || HybridSource) return;
        Format.TryAutoFormat(_ed, inserted);
    }

    /// <summary>Enter 時のブロック確定 (フェンス開始等) — 判断はフォーマットの責務。</summary>
    private bool TryCodeFence()
        => AutoFormat && Format.TryBlockCommit(_ed);

    // ---- hybrid ソース表示 (キャレットブロックだけ markdown ソースで編集) ----

    /// <summary>ブロック型由来の行頭記法の長さ (キー移動でソース展開した際の offset 近似写像)。</summary>
    private static int PrefixLen(Block b) => b.Kind switch
    {
        BlockKind.Heading => b.HeadingLevel + 1,
        BlockKind.Quote => 2,
        BlockKind.ListItem => b.Depth * 2 + (b.Ordered ? 3 : 2),
        _ => 0,
    };

    /// <summary>キャレットブロックの移動に合わせてソース展開/畳み込みを行う (Refresh 冒頭で呼ぶ)。
    /// 畳み込みは ParseLine — プレーンに打った記法もここで確定する (離脱 = 確定)。</summary>
    private void SyncHybrid()
    {
        if (!HybridSource || !Format.SupportsHybrid || _ed.Composition.Length > 0) return;
        int cb = _ed.Caret.Block;
        if (_srcBlock == cb) return;

        // 旧アクティブブロックを畳む (ソース文字列 → 再パース = 記法の確定)
        if (_srcBlock >= 0 && _srcBlock < _ed.Doc.Blocks.Count)
        {
            Block old = _ed.Doc.Blocks[_srcBlock];
            if (old.Kind == BlockKind.Paragraph)
                _ed.SwapBlock(_srcBlock, Format.ParseLine(old.Text));
        }
        _srcBlock = cb;

        // 新アクティブブロックをソース展開 (プレーン段落は表示が変わらないので置換不要。コード/Embed は対象外)
        Block b = _ed.Doc.Blocks[cb];
        if (b.Kind is BlockKind.CodeBlock or BlockKind.Embed) return;
        string src = Format.SerializeBlock(b);
        if (src == b.Text && b.Kind == BlockKind.Paragraph) return;
        _ed.SwapBlock(cb, new Block(BlockKind.Paragraph, src), _ed.Caret.Offset + PrefixLen(b));
    }

    // ---- 表示 (部分更新) ----

    private float PxOf(Block b) => b.Kind switch
    {
        BlockKind.Heading => _fs * (b.HeadingLevel == 1 ? 1.7f : b.HeadingLevel == 2 ? 1.4f : 1.2f),
        BlockKind.CodeBlock => _fs * 0.92f,
        _ => _fs,
    };

    private static float IndentOf(Block b) => b.Kind switch
    {
        BlockKind.ListItem => 20 + b.Depth * 18,
        BlockKind.Quote => 14,
        BlockKind.CodeBlock => 8,
        _ => 0,
    };

    private VectorFont? FontFor(InlineStyle s, Block b)
    {
        if (s.Code || b.Kind == BlockKind.CodeBlock) return MonoFont;
        bool bold = s.Bold || b.Kind == BlockKind.Heading;
        return bold && s.Italic ? (BoldItalicFont ?? BoldFont) : bold ? BoldFont : s.Italic ? ItalicFont : null;
    }

    private static int ColorKeyOf(InlineStyle s, Block b)
        => s.Link is not null ? CPrimary
         : b is { CalloutMarker: true, Callout: not null } ? CalloutKey(b.Callout)   // ラベル行は意味色
         : b.Kind == BlockKind.Quote ? CMuted : CText;

    private TextLayout BuildLayout(int i, Block b, string disp, float indent,
                                   List<(int Offset, Widget W)>? inline = null)
    {
        var opt = new TextLayoutOptions { MaxWidth = W - Pad * 2 - indent, Wrap = TextWrap.Word, LineHeight = LineH };
        float px = PxOf(b);
        var fonts = Fonts ?? new FontCollection(_ctx.Font);
        // 合成中 (preedit 挿入済み表示) はスタイル対応が崩れるため単一プレーンスパン
        if (i == _ed.Caret.Block && _ed.Composition.Length > 0)
            return new TextLayout(fonts, [new TextSpan(disp, new SpanStyle { Color = SpanKeys[CText] })], px, opt);
        // Embed: widget 実体化時は空、未登録はプレースホルダ文字列 (呼び出し側が渡す)
        if (b.Kind == BlockKind.Embed)
            return new TextLayout(fonts, [new TextSpan(disp,
                new SpanStyle { Font = MonoFont, Color = SpanKeys[CMuted] })], px, opt);

        // シンタックスハイライト (SH): トークン列がキャッシュ済みなら色キー付きスパンで組む
        if (b.Kind == BlockKind.CodeBlock && TryGetHighlight(b, out SyntaxToken[] tokens))
            return CodeLayout(b, tokens, fonts, px, opt);

        bool srcMode = HybridSource && i == _srcBlock;   // ソース編集中は等幅で「生テキスト」を示す
        var spans = new List<TextSpan>(b.Runs.Count);
        int offset = 0;
        foreach (InlineRun r in b.Runs)
        {
            // インライン widget (IN): resolver が widget を返す Link run は占位ボックスに
            if (!srcMode && r.Style.Link is string url
                && InlineWidgetResolver?.Invoke(url) is Widget iw)
            {
                var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
                iw.Layout(new Constraints(0, W - Pad * 2 - indent, 0, float.PositiveInfinity), lc);
                spans.Add(new TextSpan(r.Text, new SpanStyle
                {
                    BoxW = MathF.Max(iw.Size.Width, 1),
                    BoxH = MathF.Max(iw.Size.Height, 1),
                    Color = 0,
                }));
                inline?.Add((offset, iw));
                offset += r.Text.Length;
                continue;
            }
            spans.Add(new TextSpan(r.Text, new SpanStyle
            {
                Font = srcMode ? MonoFont : FontFor(r.Style, b),
                Color = SpanKeys[srcMode ? CText : ColorKeyOf(r.Style, b)],
            }));
            offset += r.Text.Length;
        }
        if (spans.Count == 0) spans.Add(new TextSpan("", new SpanStyle { Color = SpanKeys[CText] }));
        return new TextLayout(fonts, spans, px, opt);
    }

    private float BlockHeight(BlockView v, Block b)
        => b.Kind switch
        {
            BlockKind.Divider => 24,
            BlockKind.Embed when v.Embed is Widget w => w.Size.Height + 8,     // widget 由来の高さ
            BlockKind.CodeBlock => MathF.Max(v.Layout.Height, v.Layout.LineAdvance) + 12,   // 上下 pad 6
            _ => MathF.Max(v.Layout.Height, v.Layout.LineAdvance) + 4,          // ブロック間隙間
        };

    private static float PadTopOf(Block b) => b.Kind == BlockKind.CodeBlock ? 6 : 2;

    /// <summary>ブロックの表示キー (これが変わったら作り直す)。コードブロックはハイライト到着でも変わる。</summary>
    private string KeyOf(int i, Block b, int ordinal)
        => $"{b.Version}|{ordinal}|{(HybridSource && i == _srcBlock ? "S" : "")}|{(i == _ed.Caret.Block && _ed.Composition.Length > 0 ? _ed.DisplayTextOf(i) : "")}"
         + (b.Kind == BlockKind.CodeBlock && _hlCache.ContainsKey((b.CodeLang ?? "", b.Text)) ? "|H" : "");

    private void Refresh()
    {
        SyncHybrid();
        if (_structSeen != _ed.StructureVersion)
        {
            RebuildBlocks();
        }
        else
        {
            var ordinals = new Dictionary<int, int>();
            float top = Pad;
            bool shifted = false;
            for (int i = 0; i < _views.Count; i++)
            {
                Block b = _ed.Doc.Blocks[i];
                int ord = NextOrdinal(ordinals, b);
                BlockView v = _views[i];
                string key = KeyOf(i, b, ord);
                if (v.Key != key)
                {
                    RenderBlock(v, i, b, ord);
                    if (v.H != BlockHeight(v, b)) shifted = true;
                    v.H = BlockHeight(v, b);
                    v.Key = key;
                }
                if (shifted || v.Top != top) { v.Top = top; v.Container.Transform = Affine2D.Translate(0, top); }
                top += v.H;
            }
        }
        RefreshDecorations();
        if (_searchQuery.Length > 0)
        {
            // レイアウト/内容が変わった可能性 — マッチを取り直して矩形を引き直す (現在位置は維持)
            int cur = _searchCur;
            FindMatches(_ed.Doc.Blocks, _searchQuery, _searchMatches);
            _searchCur = _searchMatches.Count == 0 ? -1 : Math.Clamp(cur, 0, _searchMatches.Count - 1);
            RefreshSearch();
            if (_searchScrollPending) { _searchScrollPending = false; ScrollToCurrentMatch(); }
        }
        _scroll.Value = Clamped();
        UpdateScrollbar();   // 内容高が変わった可能性 — thumb の形/位置を合わせる
    }

    /// <summary>スクロールバー thumb の形と位置を現在の内容高に合わせる (Refresh とスクロールから)。
    /// 内容がビューに収まるときは透明にする。</summary>
    private void UpdateScrollbar()
    {
        if (_barNode is null) return;
        float max = MaxScroll;
        if (max <= 0)
        {
            _barNode.Opacity = 0f;
            return;
        }
        _barNode.Opacity = 1f;
        float th = MathF.Max(28, H * H / ContentH);
        if (th != _thumbH)
        {
            _thumbH = th;
            var s = new Scene2D();
            s.FillRoundedRect(Color2D.White, W - ThumbW - ThumbPad, 0, ThumbW, th, ThumbW / 2);
            _barNode.Content = s;
        }
        _barNode.Transform = Affine2D.Translate(0, Clamped() / max * (H - _thumbH));
    }

    private static int NextOrdinal(Dictionary<int, int> ordinals, Block b)
    {
        if (b.Kind != BlockKind.ListItem || !b.Ordered) { ordinals.Clear(); return 0; }
        ordinals[b.Depth] = ordinals.TryGetValue(b.Depth, out int n) ? n + 1 : 1;
        return ordinals[b.Depth];
    }

    private void RebuildBlocks()
    {
        _structSeen = _ed.StructureVersion;
        foreach (BlockView v in _views) { DestroyEmbed(v); ReleaseInline(v); _ctx.Canvas.Remove(v.Container); }
        _views.Clear();

        var ordinals = new Dictionary<int, int>();
        float top = Pad;
        for (int i = 0; i < _ed.Doc.Blocks.Count; i++)
        {
            Block b = _ed.Doc.Blocks[i];
            int ord = NextOrdinal(ordinals, b);
            UiNode container = _ctx.Canvas.AddChild(_content);
            container.Z = 2;
            container.Transform = Affine2D.Translate(0, top);
            var v = new BlockView { Container = container, Layout = null!, Key = "" };
            RenderBlock(v, i, b, ord);
            v.Key = KeyOf(i, b, ord);
            v.Top = top;
            v.H = BlockHeight(v, b);
            _views.Add(v);
            top += v.H;
        }
    }

    /// <summary>embed widget の再実体化要求 (dirty 伝播) を吸収する — **同一インスタンスのまま**
    /// 同じブロック位置へ再ホストし、高さ変化は後続ブロックの平行移動で吸収する (widget の内部状態:
    /// セル選択等は生き残る)。エディタ自身のサイズは固定 (内側スクロール) なので親へは伝播させない。</summary>
    protected override bool OnChildNeedsRealize(Widget child)
    {
        for (int i = 0; i < _views.Count; i++)
        {
            BlockView v = _views[i];
            if (!ReferenceEquals(v.Embed, child)) continue;

            child.Scope?.Release();
            var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
            child.Layout(new Constraints(0, W - Pad * 2, 0, float.PositiveInfinity), lc);
            child.Realize(_ctx, v.Container, new Point(WorldPos.X + Pad, WorldPos.Y + v.Top + 4));
            child.ParentWidget = this;

            v.H = BlockHeight(v, _ed.Doc.Blocks[i]);
            float top = v.Top;
            for (int j = i; j < _views.Count; j++)   // 高さ変化ぶん後続を平行移動
            {
                BlockView vj = _views[j];
                if (vj.Top != top) { vj.Top = top; vj.Container.Transform = Affine2D.Translate(0, top); }
                top += vj.H;
            }
            RefreshDecorations();
            _scroll.Value = Clamped();
            return true;
        }
        return false;   // 自分の embed ではない → さらに親へ
    }

    /// <summary>埋め込み widget を破棄する (スコープ解放 + IDisposable)。</summary>
    private static void DestroyEmbed(BlockView v)
    {
        if (v.Embed is not Widget w) return;
        w.Scope?.Release();
        (w as IDisposable)?.Dispose();
        v.Embed = null;
    }

    /// <summary>インライン widget のスコープを解放する (IN)。インスタンスは hole 所有者のもの —
    /// Dispose せず、再レンダーで同じインスタンスが実体化し直される (状態は生きる)。</summary>
    private static void ReleaseInline(BlockView v)
    {
        foreach (Widget w in v.Inline) w.Scope?.Release();
        v.Inline.Clear();
    }

    /// <summary>ブロックの子ノード (色ごとのテキスト + 装飾 / 埋め込み widget) を描き直す。
    /// **ノードはカーソル式に再利用** — ノード数が変わらないキーストロークは Content 差し替えだけになり、
    /// canvas の in-place 部分更新 (IC-M1/M2) が効く (毎回 Remove/AddChild すると構造 dirty =
    /// フル再構築に落ちる)。色は現在テーマ値を直接設定。</summary>
    private void RenderBlock(BlockView v, int i, Block b, int ordinal)
    {
        DestroyEmbed(v);
        ReleaseInline(v);

        string disp = _ed.DisplayTextOf(i);
        v.Indent = IndentOf(b);
        v.PadTop = PadTopOf(b);
        v.Ordinal = ordinal;

        // 埋め込み widget (登録済み TypeId): ファクトリが返した任意の Widget をブロック位置へ実体化する。
        // 未登録 TypeId はプレースホルダ表示へフォールバック (下の通常経路)。
        if (b.Kind == BlockKind.Embed && b.Payload is IBlockPayload pl
            && Widgets.Find(pl.TypeId) is BlockWidgetFactory factory)
        {
            foreach ((UiNode n, _) in v.Colored) _ctx.Canvas.Remove(n);   // embed は widget — 色ノードは持たない
            v.Colored.Clear();
            Block blockRef = b;   // 編集で index は動く — Commit 時に参照から解決する
            Widget w = factory(new BlockWidgetContext
            {
                Payload = pl,
                MaxWidth = W - Pad * 2,
                Theme = _theme,
                Commit = p => Apply(e =>
                {
                    int idx = e.Doc.Blocks.IndexOf(blockRef);
                    if (idx >= 0) e.ReplacePayload(idx, p);
                }),
                Invalidate = () => { blockRef.Bump(); Refresh(); },   // 表示だけの再構築 (undo 外)
            });
            v.Embed = w;
            var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
            w.Layout(new Constraints(0, W - Pad * 2, 0, float.PositiveInfinity), lc);
            w.Offset = new Point(Pad, 4);
            w.Realize(_ctx, v.Container, new Point(WorldPos.X + Pad, WorldPos.Y + v.Top + 4));
            w.ParentWidget = this;   // イベント経由の実体化では自動記録されない — dirty 伝播の経路を明示
            if (w is IDisposable d) _ctx.Own(d);   // SetRoot 破棄でも Dispose (差し替え時は DestroyEmbed が先行)
            v.Layout = BuildLayout(i, b, "", 0);   // 空レイアウト (キャレット矩形用のみ)
            return;
        }

        if (b.Kind == BlockKind.Embed) disp = $"[{b.Payload?.TypeId ?? "embed"}]";   // 未登録プレースホルダ
        var inlineWidgets = new List<(int Offset, Widget W)>();
        v.Layout = BuildLayout(i, b, disp, v.Indent, inlineWidgets);
        Theme t = _theme.Value;
        float h = BlockHeight(v, b);
        int used = 0;   // 再利用カーソル

        // 装飾 (テキストの背面)
        if (b.Kind == BlockKind.CodeBlock)
        {
            UiNode n = Use(CSurfaceAlt, z: 0);
            var s = new Scene2D();
            s.FillRoundedRect(Color2D.White, Pad - 4, 0, W - Pad * 2 + 8, h - 2, 4);
            n.Content = s;
        }
        else if (b.Kind == BlockKind.Quote)
        {
            // コールアウト (> [!NOTE] 等) は意味色の少し太いバー、通常引用は Border 色
            UiNode n = Use(b.Callout is not null ? CalloutKey(b.Callout) : CBorder, z: 0);
            var s = new Scene2D();
            s.FillRoundedRect(Color2D.White, Pad, 1, b.Callout is not null ? 4 : 3, h - 4, 1.5f);
            n.Content = s;
        }
        else if (b.Kind == BlockKind.Divider)
        {
            UiNode n = Use(CBorder, z: 0);
            var s = new Scene2D();
            s.FillRect(Color2D.White, Pad, h / 2 - 1, W - Pad * 2, 2);
            n.Content = s;
        }

        // リストマーカー ("•" / "n.") — 先頭行のベースラインに合わせる
        if (b.Kind == BlockKind.ListItem)
        {
            UiNode n = Use(CMuted, z: 1);
            var s = new Scene2D();
            float mpx = PxOf(b) * 0.95f;
            string marker = b.Ordered ? $"{ordinal}." : "•";
            float baseline = v.PadTop + _ctx.Font.Ascent(PxOf(b)) + (v.Layout.LineAdvance - PxOf(b)) / 2;
            _ctx.Font.AppendText(s, marker, Pad + b.Depth * 18, baseline, mpx, Color2D.White);
            n.Content = s;
        }

        // テキスト (使用している色キーごとに 1 ノード — 1 ノード 1 色制約)
        foreach (int key in UsedColorKeys(b))
        {
            UiNode n = Use(key, z: 2);
            var s = new Scene2D();
            v.Layout.DrawColorRuns(s, Pad + v.Indent, v.PadTop, SpanKeys[key]);
            n.Content = s;
        }

        // 余った旧ノードを除去 (ノード数が減ったときだけ構造が動く)
        for (int k = v.Colored.Count - 1; k >= used; k--)
        {
            _ctx.Canvas.Remove(v.Colored[k].Node);
            v.Colored.RemoveAt(k);
        }

        // インライン widget (IN): 占位ボックスの矩形へ実体化 (下端 = ベースライン)。
        // hole インスタンスは呼び出し側 (Docs) 所有 — Dispose せず再実体化のみ
        foreach ((int off, Widget iw) in inlineWidgets)
        {
            IReadOnlyList<TextRect> rects = v.Layout.SelectionRects(off, off + 1);
            if (rects.Count == 0) continue;
            TextRect r0 = rects[0];
            float ix = Pad + v.Indent + r0.X;
            float iy = v.PadTop + r0.Y + (v.Layout.LineAscentAt(off) - iw.Size.Height);
            iw.Scope?.Release();
            iw.Offset = new Point(ix, iy);
            iw.Realize(_ctx, v.Container, new Point(WorldPos.X + ix, WorldPos.Y + v.Top + iy));
            iw.ParentWidget = this;
            v.Inline.Add(iw);
        }

        UiNode Use(int colorKey, int z)
        {
            UiNode n;
            if (used < v.Colored.Count)
            {
                n = v.Colored[used].Node;
                if (v.Colored[used].ColorKey != colorKey) v.Colored[used] = (n, colorKey);
            }
            else
            {
                n = _ctx.Canvas.AddChild(v.Container);
                v.Colored.Add((n, colorKey));
            }
            used++;
            n.Z = z;
            n.Color = ColorFor(t, colorKey);
            return n;
        }
    }

    private int[] UsedColorKeys(Block b)
    {
        if (b.Kind == BlockKind.Quote)
            return b.Callout is not null ? [CMuted, CalloutKey(b.Callout)] : [CMuted];
        if (b.Kind == BlockKind.Embed) return [CMuted];
        // コードブロック: ハイライト済みならトークン色キー集合 (Text + 使用中の種別)
        if (b.Kind == BlockKind.CodeBlock
            && _hlCache.TryGetValue((b.CodeLang ?? "", b.Text), out SyntaxToken[]? toks))
        {
            var keys = new SortedSet<int> { CText };
            foreach (SyntaxToken t in toks) keys.Add(TokenKey(t.Kind));
            return [.. keys];
        }
        bool link = b.Runs.Any(r => r.Style.Link is not null);
        return link ? [CText, CPrimary] : [CText];
    }

    private void RefreshDecorations()
    {
        int cb = _ed.Caret.Block;
        BlockView cv = _views[cb];
        TextRect cr = cv.Layout.CaretRect(_ed.DisplayCaretOffset);
        _caretLocal = new Rect(Pad + cv.Indent + cr.X, cv.Top + cv.PadTop + cr.Y, 2, cr.Height);
        var cs = new Scene2D();
        cs.FillRect(Color2D.White, 0, 0, 2, cr.Height);
        _caretNode.Content = cs;
        _caretNode.Transform = Affine2D.Translate(_caretLocal.X, _caretLocal.Y);

        var sel = new Scene2D();
        if (_ed.HasSelection && _ed.Composition.Length == 0)
        {
            DocPos a = _ed.SelMin, b = _ed.SelMax;
            for (int i = a.Block; i <= b.Block; i++)
            {
                BlockView v = _views[i];
                int s0 = i == a.Block ? a.Offset : 0;
                int s1 = i == b.Block ? b.Offset : _ed.Doc.Blocks[i].Length;
                foreach (TextRect r in v.Layout.SelectionRects(s0, s1))
                    sel.FillRect(Color2D.White, Pad + v.Indent + r.X, v.Top + v.PadTop + r.Y, r.Width, r.Height);
                if (i < b.Block)
                {
                    TextRect e = v.Layout.CaretRect(_ed.Doc.Blocks[i].Length);
                    sel.FillRect(Color2D.White, Pad + v.Indent + e.X, v.Top + v.PadTop + e.Y, _fs * 0.4f, e.Height);
                }
            }
        }
        _selNode.Content = sel;

        var us = new Scene2D();
        var tg = new Scene2D();
        (int is0, int il) = _ed.CompositionDisplayRange;
        if (il == 0 && _hlLen > 0) { is0 = _hlStart; il = _hlLen; }   // 実 IME (TSF): preedit は文書内、装飾のみ通知
        int dispLen = _ed.DisplayTextOf(cb).Length;
        is0 = Math.Clamp(is0, 0, dispLen);
        il = Math.Clamp(il, 0, dispLen - is0);
        if (il > 0)
        {
            foreach (TextRect r in cv.Layout.SelectionRects(is0, is0 + il))
                us.FillRect(Color2D.White, Pad + cv.Indent + r.X, cv.Top + cv.PadTop + r.Y + r.Height - 2, r.Width, 2);
            (int t0, int tl) = _ed.Composition.Length > 0 ? _ed.TargetDisplayRange : (_hlTargetStart, _hlTargetLen);
            if (tl > 0)
                foreach (TextRect r in cv.Layout.SelectionRects(t0, t0 + tl))
                    tg.FillRect(Color2D.White, Pad + cv.Indent + r.X, cv.Top + cv.PadTop + r.Y, r.Width, r.Height);
        }
        _underlineNode.Content = us;
        _targetNode.Content = tg;
    }

    private int _hlStart, _hlLen, _hlTargetStart, _hlTargetLen;   // TSF display attribute 由来の装飾

    private void EnsureCaretVisible()
    {
        float top = _caretLocal.Y, bottom = _caretLocal.Y + _caretLocal.Height;
        float s = Clamped();
        if (top - s < Pad) s = MathF.Max(0, top - Pad);
        else if (bottom - s > H - Pad) s = bottom - H + Pad;
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
