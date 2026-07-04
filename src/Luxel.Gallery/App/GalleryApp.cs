using System.Text.Json;
using Luxel.Controls;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using CP = Luxel.Controls.ColorPicker;   // using static Kit とファクトリ名が衝突するため (CS0119)
using Split = Luxel.Controls.Splitter;   // 同上 (静的メンバ Thickness 参照用)

namespace Luxel.Gallery;

/// <summary>
/// ネイティブ版ギャラリー (Storybook 風) の UI。**Luxel.Controls 自身で構築する** (ドッグフーディング) —
/// gallery.html の置き換え。プレビューは <see cref="Luxel.Controls.SurfaceView"/> (iframe 相当):
///  - ストーリー切替 = 子 SetRoot + 論理サイズ変更 (StoryAttribute の W/H で実寸表示)
///  - knobs パネル等はストーリー毎に変わるため、選択時のみ chrome を SetRoot 再構築する
///    (SurfaceView は同一インスタンスを再利用するのでストーリー側の状態は生存)
/// リモート検証は DevTools (/windows /winframe /trees /cmd、ui:"gallery"/"story")。
/// </summary>
public sealed class GalleryApp : IDisposable
{
    public const float PreviewW = 800, PreviewH = 480;
    // サーフェス (framebuffer) は全画面モードの最大サイズで確保 — SetContent の論理サイズは
    // サーフェス以下にしか広げられない (余白は透過なので通常時の見た目は不変)
    private const float SurfW = 1092, SurfH = 760;
    private const float WinH = 789f;   // クライアント 801 - Border padding 12

    private readonly SurfaceView _preview = SurfaceView(SurfW, SurfH);
    // ストーリーへ StoryContext.Resources として配布 (キャッシュ共有、Pump は Update が叩く)
    private readonly Luxel.Resources.ResourceSystem _resources = new(
        sources: Luxel.Resources.ResourceSystemDefaults.BuiltinSources(assetRoot: Environment.CurrentDirectory),
        steps: [.. Luxel.Resources.ResourceSystemDefaults.BuiltinSteps(), new Luxel.Imaging.ImageSharpDecoder()]);
    private readonly Signal<string> _title = new("(ストーリーを選択)");
    // Log は ListView — 追記時は items signal へ流す (行ノードの差し替えのみ、chrome の SetRoot 不要)
    private readonly Signal<IReadOnlyList<string>> _logItems = new([]);
    private readonly Signal<int> _logCountSig = new(0);
    private int _logCount = -1;

    // ペイン寸法 (Splitter ドラッグで変更 → chrome 再構築)
    private float _sidebarW = 170, _rightW = 360, _logH = 240;   // 右パネルは Knobs テーブル (4 列) が収まる幅
    private readonly Signal<bool> _fHover = new(false), _fPressed = new(false), _fFocused = new(false), _fDisabled = new(false);
    private bool _dark;
    private StoryContext? _ctx;
    private Widget? _storyRoot;
    private StoryInfo? _currentStory;
    private string? _currentPath;
    private bool _zen;   // 全画面 (docs 読み書き用): 右パネル/Log を隠しプレビューをメイン全面に
    private bool _dirty;
    private bool _statesDirty;
    private string? _pendingNav;   // docs リンク等からの遷移要求 (Update で消費)
    private readonly HashSet<string> _treeExpanded = new();   // サイドバーツリーの展開状態 (chrome 再構築をまたぐ)
    private bool _treeInit = true;
    // 検索 (SR): クエリは TextField と TreeView.Filter が同じ signal を共有 — タイプ毎の chrome
    // 再構築なし (TreeView の TrackBuild だけが反応する)。docs 本文への適用は Update で行う
    private readonly Signal<string> _search = new("");
    private readonly Signal<int> _matchCur = new(0), _matchTotal = new(0);
    private string _appliedQuery = "";
    private Widget? _appliedRoot;   // 適用先ルート (同一パス再選択でもルートは変わる — 参照で比較)
    private int _pendingScroll = -1;      // 見出しクリックでページ遷移した後のスクロール先ブロック
    private Dictionary<string, DocsPage>? _docsIndex;   // 初回 BuildRoot で構築 (path → 本文+TOC)
    private long _frame;
    private Widget? _selected;                                   // Props インスペクタの選択ノード
    private readonly object _editGate = new();
    private readonly List<(Widget W, string Name, string Type, string Value)> _propEdits = new();

    public GalleryApp() => WireStateForcing();   // Effect は生涯 1 組 (BuildRoot 毎に張ると累積する)

    public StoryContext? Context => _ctx;
    public string? CurrentPath => _currentPath;
    /// <summary>プレビューの子 UiHost (ストーリー側)。リモート検証用に UiRegistry へ登録する。</summary>
    public UiHost? StoryHost => _preview.Child;

    /// <summary>chrome の再構築が必要か (ストーリー選択後に true、消費で false)。</summary>
    public bool ConsumeDirty()
    {
        bool d = _dirty;
        _dirty = false;
        return d;
    }

    /// <summary>毎フレームの軽い同期: 状態強制の適用 (effect 文脈の外で signal を書く) + 検索適用 + Log の反映 (15f 毎)。</summary>
    public void Update()
    {
        _resources.Pump();
        _ctx?.PumpKnobEdits();   // Knobs テーブルの編集適用 (effect 文脈外)
        if (_pendingNav is string nav) { _pendingNav = null; SelectByPath(nav); }
        SyncSearch();
        if (_statesDirty)
        {
            _statesDirty = false;
            ApplyStates();
        }
        ApplyPropEdits();
        if (++_frame % 5 != 0) return;
        int count = _ctx?.LogSnapshot().Length ?? 0;
        if (count != _logCount)
        {
            _logCount = count;
            _logCountSig.Value = count;
            _logItems.Value = LogLines();
        }
    }

    /// <summary>ログ行 (全件、新しい順)。</summary>
    private string[] LogLines()
    {
        StoryLogEntry[] entries = _ctx?.LogSnapshot() ?? [];
        var lines = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            lines[i] = $"{entries[^(i + 1)].Time}  {entries[^(i + 1)].Message}";
        return lines;
    }

    /// <summary>ギャラリー chrome のルート widget を構築する (初回 + ストーリー選択/ペインリサイズ時)。
    /// 骨格は Grid — 列 [サイドバー | Splitter | メイン | Splitter | 右パネル]、
    /// メインは行 [ツールバー | プレビュー(Star) | Splitter | Log]。Splitter のドラッグ確定で寸法を更新して再構築する。</summary>
    public Widget BuildRoot()
    {
        const float winH = WinH;

        // ---- サイドバー (col 0): Component > Story > 見出し の 3 階層ツリー + 検索 ----
        // 展開状態 (_treeExpanded) は GalleryApp が所有 — chrome 再構築をまたいで保持。
        // 初回は全 Component を展開 (従来の全件表示と同じ見え方から始める)。
        // 見出し (TOC) は DocsIndex から全ページ分を常設 (Tag = (StoryInfo, ブロック index))
        _docsIndex ??= DocsIndex.Build(StoryRegistry.All, _resources);
        var roots = new List<TreeNode>();
        foreach (var group in StoryRegistry.All.GroupBy(s => s.Component))
        {
            string groupKey = $"g:{group.Key}";
            if (_treeInit) _treeExpanded.Add(groupKey);
            var stories = new List<TreeNode>();
            foreach (StoryInfo s in group)
            {
                DocsPage? page = _docsIndex.GetValueOrDefault(s.Path);
                List<TreeNode>? heads = page is { Headings.Count: > 0 }
                    ? page.Headings.Select(h => new TreeNode($"{s.Path}#{h.Block}", h.Text,
                        Tag: (s, h.Block))).ToList()
                    : null;
                stories.Add(new TreeNode(s.Path, s.Name, heads, Tag: s, SearchText: page?.Text));
            }
            roots.Add(new TreeNode(groupKey, group.Key, stories));
        }
        _treeInit = false;
        TreeView tree = TreeView(roots, _treeExpanded,
            onSelect: (_, n) =>
            {
                if (n.Tag is StoryInfo s) Select(s);
                else if (n.Tag is (StoryInfo hs, int block))
                {
                    if (hs.Path != _currentPath) { Select(hs); _pendingScroll = block; }
                    else if (_storyRoot is not null && DocsIndex.FindDocEditor(_storyRoot) is { } doc)
                        doc.ScrollTo(block);
                }
            },
            selected: _currentPath ?? "", filter: _search);
        // 検索バー: 前へ/次へは開いている docs ページ内のマッチ移動、n/m は現在/総数
        Func<string> matchLabel = () => _matchTotal.Value > 0 ? $"{_matchCur.Value}/{_matchTotal.Value}" : "-";
        Widget searchBar = HStack(2)[
            TextField(_search, "検索", width: _sidebarW - 62),
            Button(_ => MoveSearch(-1), "‹", fontSize: 12f, padding: new Thickness(6, 2)),
            Text(matchLabel, 10, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 5, 0, 0)),
            Button(_ => MoveSearch(+1), "›", fontSize: 12f, padding: new Thickness(6, 2))];
        Widget sidebar = VStack(2)[
            Heading("Stories"),
            searchBar,
            Scroll(winH - 58, width: _sidebarW)[tree]];
        sidebar.GridColumn(0);

        var splitSidebar = Splitter(vertical: true,
            onResized: (_, d) => { _sidebarW = Math.Clamp(_sidebarW + d, 120, 420); RefreshPreviewSize(); _dirty = true; });
        splitSidebar.GridColumn(1);

        // ---- メイン (col 2): ツールバー / プレビュー / Splitter / Log ----
        Widget toolbar = HStack(8)[
            Text($"{_title}", 14, color: Bind.From(() => UiTheme.T.Text), width: 300),
            Button(_ => ToggleTheme(), "theme"),
            Button(_ => ToggleZen(), _zen ? "元に戻す" : "全画面"),
            Check(_fHover, "hover"),
            Check(_fPressed, "pressed"),
            Check(_fFocused, "focused"),
            Check(_fDisabled, "disabled")];
        toolbar.GridRow(0);
        _preview.GridRow(1);
        var splitLog = Splitter(vertical: false,
            onResized: (_, d) => { _logH = Math.Clamp(_logH - d, 60, 440); RefreshPreviewSize(); _dirty = true; });   // 上へドラッグ = Log 拡大
        splitLog.GridRow(2);
        _logItems.Value = LogLines();
        ListView logList = ListView(MathF.Max(24, _logH - 36), 16f, items: _logItems, width: PreviewW - 24);
        Widget logPanel = Border(background: Bind.From(() => UiTheme.T.Surface), rounded: UiTheme.T.Radius,
                                 padding: new Thickness(8, 4), width: PreviewW)[
            VStack(2)[
                Text($"Log ({_logCountSig})", 14, color: Bind.From(() => UiTheme.T.Text)),
                logList]];
        logPanel.GridRow(3);

        // 全画面 (zen): ツールバー + プレビューのみ (Log/右パネルを隠して docs をメイン全面に)
        Widget main = _zen
            ? Grid([GridLength.Star(1)], [GridLength.Px(28), GridLength.Star(1)])[toolbar, _preview]
            : Grid(
                [GridLength.Star(1)],
                [GridLength.Px(28), GridLength.Star(1), GridLength.Px(Split.Thickness), GridLength.Px(_logH)])[
                toolbar, _preview, splitLog, logPanel];
        main.GridColumn(2);

        var splitPanel = Splitter(vertical: true,
            onResized: (_, d) => { _rightW = Math.Clamp(_rightW - d, 200, 460); RefreshPreviewSize(); _dirty = true; });
        splitPanel.GridColumn(3);

        // ---- 右パネル (col 4): Knobs (autodoc 風テーブル) + Props (個別スクロール) ----
        // 編集は StoryContext のキューへ (Update の PumpKnobEdits が effect 文脈外で適用)。
        // 説明列はパネルが狭いと詰まる — Splitter で広げられる
        Widget knobsTable = KnobsTable(_ctx?.Knobs ?? [], width: _rightW - 8,
            onEdit: (_, k, v) => _ctx?.QueueKnobEdit(k, v));

        var props = new List<Widget>();
        if (_storyRoot is not null)
        {
            void AddRows(Widget w, int depth)
            {
                string label = $"{w.DebugType}{(string.IsNullOrEmpty(w.DebugDetail) ? "" : $" {Trim(w.DebugDetail!, 14)}")}";
                Widget captured = w;
                props.Add(Button(_ => { _selected = captured; _dirty = true; }, label,
                    variant: _selected == w ? Luxel.UI.Variant.Tonal : Luxel.UI.Variant.Ghost,
                    hAlign: Align.Stretch, margin: new Thickness(4 + depth * 8, 0, 0, 0)));
                foreach (Widget c in w.DebugChildren()) AddRows(c, depth + 1);
            }
            AddRows(_storyRoot, 0);
            if (_selected is { } sel)
                foreach (DebugProp p in sel.DebugProps())
                    props.Add(PropEditor(sel, p));
        }

        Widget panel = VStack(2)[
            Heading("Knobs"),
            Scroll(260f, width: _rightW)[knobsTable],   // テーブル ~7 行分 (それ以上はスクロール)
            Heading("Props"),
            Scroll(winH - 330, width: _rightW)[VStack(3)[props.ToArray()]]];
        panel.GridColumn(4);

        Widget root = _zen
            ? Grid([GridLength.Px(_sidebarW), GridLength.Px(Split.Thickness), GridLength.Star(1)])[
                sidebar, splitSidebar, main]
            : Grid(
                [GridLength.Px(_sidebarW), GridLength.Px(Split.Thickness), GridLength.Star(1),
                 GridLength.Px(Split.Thickness), GridLength.Px(_rightW)])[
                sidebar, splitSidebar, main, splitPanel, panel];

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(6))[root];
    }

    /// <summary>全画面 (zen) の切替: chrome を組み替え、プレビュー内容もメイン全面サイズで再実体化する。</summary>
    private void ToggleZen()
    {
        _zen = !_zen;
        if (_currentStory is { } s && _storyRoot is not null)
        {
            (int pw, int ph) = PreviewSize(s);
            _preview.SetContent(_storyRoot, pw, ph);
        }
        _dirty = true;
    }

    // ---- 型 → エディタ (knob / DebugProps 共通)。bool=Check / enum=Select / color=ColorPicker /
    //      int,float=正規表現規制付き TextField / string=TextField。commit は effect からキュー経由。----
    private const string FloatPattern = @"^-?[0-9]*\.?[0-9]*$";
    private const string IntPattern = "^-?[0-9]*$";

    private static Widget ValueEditor(string name, string type, string value, Action<string> commit)
    {
        Widget Label() => Text($"{name}", 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 3, 0, 0));

        if (type == "bool")
        {
            var b = new Signal<bool>(value == "true");
            bool first = true;
            Reactive.Effect(() => { bool v = b.Value; if (first) { first = false; return; } commit(v ? "true" : "false"); });
            return Check(b, name, margin: new Thickness(4, 0, 0, 0));
        }
        if (type == "color")
        {
            var col = new Signal<uint>(CP.TryParseHex(value, out uint c) ? c : 0xFF000000u);
            bool first = true;
            Reactive.Effect(() => { uint v = col.Value; if (first) { first = false; return; } commit(CP.ToHex(v)); });
            CP picker = ColorPicker(col, margin: new Thickness(4, 0, 0, 0));
            return VStack(1)[Label(), picker];
        }
        if (type == "length")
        {
            // Length は数値 + 単位 (px/%/em/vw/vh) のコンボ 1 コントロール
            var len = new Signal<Length>(Length.TryParse(value, null, out Length l) ? l : default);
            bool firstL = true;
            Reactive.Effect(() => { Length v = len.Value; if (firstL) { firstL = false; return; } commit(v.ToString()); });
            LengthField lf = LengthField(len, margin: new Thickness(4, 0, 0, 0));
            return VStack(1)[Label(), lf];
        }
        if (type.StartsWith("enum:"))
        {
            string[] opts = type[5..].Split('|');
            var sel = new Signal<int>(Math.Max(0, Array.IndexOf(opts, value)));
            bool first = true;
            Reactive.Effect(() => { int i = sel.Value; if (first) { first = false; return; } commit(opts[Math.Clamp(i, 0, opts.Length - 1)]); });
            // GalleryApp.Select(StoryInfo) がメンバー解決で Kit.Select を隠すため修飾が必要
            Select dd = Kit.Select(opts, sel, margin: new Thickness(4, 0, 0, 0));
            return VStack(1)[Label(), dd];
        }
        var txt = new Signal<string>(value);
        bool firstT = true;
        Reactive.Effect(() => { string v = txt.Value; if (firstT) { firstT = false; return; } commit(v); });
        TextField tf = TextField(txt, width: 200, margin: new Thickness(4, 0, 0, 0));
        tf.Pattern = type switch { "int" => IntPattern, "float" => FloatPattern, _ => null };
        return VStack(1)[Label(), tf];
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>DebugProp 1 つのエディタ。編集は Effect からキューに積み、適用は <see cref="Update"/>
    /// (effect 文脈外) で SetDebugProp + 再実体化する (ui.set と同じ理由 — override signal は遅延生成)。</summary>
    private Widget PropEditor(Widget target, DebugProp p)
        => ValueEditor(p.Name, p.Type, p.Value,
            v => { lock (_editGate) _propEdits.Add((target, p.Name, p.Type, v)); });

    /// <summary>キューされた prop 編集を適用する (フレームループから、effect 文脈外)。prop は子を再実体化。
    /// knob 編集は StoryContext のキュー (PumpKnobEdits) へ移行済み。</summary>
    private void ApplyPropEdits()
    {
        (Widget W, string Name, string Type, string Value)[] edits;
        lock (_editGate)
        {
            if (_propEdits.Count == 0) return;
            edits = _propEdits.ToArray(); _propEdits.Clear();
        }
        if (edits.Length == 0) return;
        bool any = false;
        foreach ((Widget w, string name, string type, string value) in edits)
        {
            try
            {
                w.SetDebugProp(name, type, ToElement(type, value));
                any = true;
            }
            catch { /* 不正値は無視 */ }
        }
        if (any && _storyRoot is not null) StoryHost?.SetRoot(_storyRoot);   // override signal を Effect に読ませる
    }

    private static JsonElement ToElement(string type, string v)
        => type == "bool" && bool.TryParse(v, out bool b)
            ? JsonSerializer.SerializeToElement(b)
            : JsonSerializer.SerializeToElement(v);

    /// <summary>テーマ切替 (ツールバーの "theme" ボタンと Ctrl+D ショートカットの両方から)。</summary>
    public void ToggleTheme()
    {
        _dark = !_dark;
        UiTheme.Current.Value = (_dark ? Theme.Dark : Theme.Light).Compact();   // global (chrome も切替わる — 既知の制限)
    }

    /// <summary>状態強制: Effect はフラグを立てるだけにし、signal の書き込みは <see cref="Update"/>
    /// (effect 文脈の外) で行う。**Effect 内から他 widget の状態 signal を書くと、依存追跡や
    /// 子ホストの effect 連鎖と干渉する**ため (自己依存の無限ループ/反映漏れ)。</summary>
    private void WireStateForcing()
    {
        Reactive.Effect(() => { _ = _fHover.Value; _ = _fPressed.Value; _ = _fFocused.Value; _ = _fDisabled.Value; _statesDirty = true; });
    }

    /// <summary>ストーリー部分木へ状態を適用する (フレームループから、effect 文脈外)。chrome には波及しない。</summary>
    private void ApplyStates()
    {
        bool hover = _fHover.Value, pressed = _fPressed.Value, focused = _fFocused.Value, disabled = _fDisabled.Value;
        ForEachStory(w =>
        {
            w.Enabled = !disabled;
            // Enabled は plain bool (非 signal) のため、hover を揺らして色解決 Effect を再評価させる
            w.Hovered.Value = !hover; w.Hovered.Value = hover;
            w.Pressed.Value = pressed;
            w.Focused.Value = focused;
        });
    }

    private void ForEachStory(Action<Widget> f)
    {
        if (_storyRoot is null) return;
        static void Walk(Widget w, Action<Widget> f)
        {
            f(w);
            foreach (Widget c in w.DebugChildren()) Walk(c, f);
        }
        Walk(_storyRoot, f);
    }

    /// <summary>検索状態の同期 (Update 毎): クエリ/ページが変わったら開いている docs へハイライトを
    /// 適用し、n/m を更新する。見出しクリックのページ跨ぎスクロールも実体化を待ってここで消費。</summary>
    private void SyncSearch()
    {
        RichTextEditor? doc = _storyRoot is null ? null : DocsIndex.FindDocEditor(_storyRoot);
        if (_pendingScroll >= 0 && doc is { Realized: true })
        {
            doc.ScrollTo(_pendingScroll);
            _pendingScroll = -1;
        }
        string q = _search.Value;
        if (q == _appliedQuery && ReferenceEquals(_storyRoot, _appliedRoot)) return;
        _appliedQuery = q;
        _appliedRoot = _storyRoot;
        if (doc is not null)
        {
            doc.SetSearchHighlight(q);
            _matchTotal.Value = doc.SearchMatchCount;
            _matchCur.Value = doc.SearchCurrent + 1;
        }
        else
        {
            _matchTotal.Value = 0;
            _matchCur.Value = 0;
        }
    }

    /// <summary>前へ/次へ: 開いている docs ページ内でマッチを移動する。</summary>
    private void MoveSearch(int dir)
    {
        if (_storyRoot is null || DocsIndex.FindDocEditor(_storyRoot) is not { } doc) return;
        if (dir > 0) doc.SearchNext(); else doc.SearchPrev();
        _matchCur.Value = doc.SearchCurrent + 1;
        _matchTotal.Value = doc.SearchMatchCount;
    }

    /// <summary>プレビューの内容サイズ: 通常はストーリー宣言サイズ、fill ストーリー (W/H 未指定 = 0,0
    /// — docs ページ等) はメイン領域いっぱい、全画面はメイン全面
    /// (いずれもサーフェスサイズが上限 — SetContent 側でも clamp される)。</summary>
    private (int W, int H) PreviewSize(StoryInfo story)
    {
        if (_zen)
            return ((int)MathF.Min(SurfW, 1280 - 12 - _sidebarW - Split.Thickness), (int)SurfH);
        if (story.Width > 0)
            return (story.Width, story.Height);
        // fill: 通常モードのメイン領域 (サイドバー/右パネル/Log を除いた実寸)
        float w = 1280 - 12 - _sidebarW - Split.Thickness * 2 - _rightW;
        float h = WinH - 28 - Split.Thickness - _logH;
        return ((int)MathF.Min(SurfW, w), (int)MathF.Min(SurfH, h));
    }

    /// <summary>fill ストーリーの表示中にペイン寸法が変わったら、プレビューを新しい領域サイズで
    /// 実体化し直す (Splitter ドラッグ確定時)。固定サイズのストーリーは何もしない。</summary>
    private void RefreshPreviewSize()
    {
        if (_currentStory is { Width: <= 0 } s && _storyRoot is not null)
        {
            (int pw, int ph) = PreviewSize(s);
            _preview.SetContent(_storyRoot, pw, ph);
        }
    }

    /// <summary>実窓ホストの GPU 設備 (Program が結線)。実窓専用ストーリーが ctx.Device/Font で借りる。</summary>
    public (GpuDevice Device, Luxel.Typography.VectorFont Font)? HostGpu { get; set; }

    public void Select(StoryInfo story)
    {
        _currentStory = story;
        _currentPath = story.Path;
        _treeExpanded.Add(story.Path);   // 選択したページの TOC をツリーで開いて見せる
        _title.Value = story.Path;
        _ctx = new StoryContext(_resources);
        // 遷移は次フレームへキュー — 子ホストの入力ディスパッチ中に SetContent (旧ルート破棄) しない
        _ctx.SetNavigator(p => _pendingNav = p);
        if (HostGpu is { } gpu) _ctx.SetGpuHost(gpu.Device, gpu.Font);
        (int pw, int ph) = PreviewSize(story);
        try
        {
            _storyRoot = story.Build(_ctx);
            _preview.SetContent(_storyRoot, pw, ph);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[gallery] story error '{story.Path}': {e}");   // スタック付き (診断用)
            _storyRoot = Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
                Text($"story error: {e.Message}", 14, color: Color2D.Rgba(220, 60, 60))];
            _preview.SetContent(_storyRoot, pw, ph);
        }
        _logCount = -1;   // 新 StoryContext → Log リストを作り直させる
        _selected = null;     // Props 選択は旧ストーリーの widget なのでクリア
        _dirty = true;        // knobs/props パネルが変わるので chrome を再構築 (SurfaceView は再利用)
        _statesDirty = true;  // 強制中の状態を新ストーリーへ再適用
    }

    public void SelectByPath(string path)
    {
        if (StoryRegistry.Find(path) is { } s) Select(s);
    }

    public void Dispose()
    {
        _preview.Dispose();
        _resources.Dispose();
    }
}
