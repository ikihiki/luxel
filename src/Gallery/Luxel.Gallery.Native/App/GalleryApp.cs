using System.Text.Json;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Settings;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
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
    // サーフェス (framebuffer) は fill/全画面の最大サイズで確保 — SetContent の論理サイズは
    // サーフェス以下にしか広げられない (余白は透過なので通常時の見た目は不変)。
    // 大型モニタの最大化まで追従できるよう余裕を持たせる (DeviceLocal ~14MB×scale²)
    private const float SurfW = 2560, SurfH = 1440;

    private readonly StoryCatalog _catalog;
    private readonly IServiceProvider _storyServices;
    private readonly SurfaceView _preview = SurfaceView(SurfW, SurfH);
    private readonly Signal<Theme> _storyTheme = new(Theme.Light.Compact());
    private Exception? _pendingStoryError;
    // ストーリーへ StoryContext.Resources として配布 (キャッシュ共有、Pump は Update が叩く)
    private Luxel.Resources.ResourceSystem? _resources;
    private Luxel.Resources.ResourceSystem Resources => _resources
        ?? throw new InvalidOperationException("Gallery GPU resources have not been configured.");
    private GallerySlangCompilation? _slangCompilation;
    private (GpuDevice Device, Luxel.Typography.VectorFont Font)? _hostGpu;
    // Log は構造化 entry を signal へ流す (行ノードの差し替えのみ、chrome の SetRoot 不要)
    private readonly Signal<IReadOnlyList<StoryLogEntry>> _logEntries = new([]);
    private int _logCount = -1;

    // ペイン寸法 (Splitter ドラッグで変更 → chrome 再構築)
    private float _sidebarW = 290, _logH = 260;
    // ウィンドウの論理クライアントサイズ (ホストが毎フレーム SetWindowSize で同期 — リサイズで chrome 再構築)
    private float _winW = 1280, _winH = 801;
    private ScrollViewer? _sidebarScroll;   // サイドバーのスクロールは chrome 再構築をまたいで位置を保つ
    private TextField? _searchField;         // 絞り込み再構築をまたいで focus/caret を保つ
    private bool _dark;
    private StoryContext? _ctx;
    private Widget? _storyRoot;
    private StoryInfo? _currentStory;
    private string? _currentPath;
    private bool _zen;   // 全画面 (docs 読み書き用): 下ペインを隠しプレビューをメイン全面に
    private bool _dirty;
    private string? _pendingNav;   // docs リンク等からの遷移要求 (Update で消費)
    private readonly HashSet<string> _treeExpanded = new();   // サイドバーツリーの展開状態 (chrome 再構築をまたぐ)
    private bool _treeInit = true;
    // 検索 (SR): クエリは TextField と TreeView.Filter が同じ signal を共有 — タイプ毎の chrome
    // 再構築なし (TreeView の TrackBuild だけが反応する)。docs 本文への適用は Update で行う
    private readonly Signal<string> _search = new("");
    private readonly Signal<int> _matchCur = new(0), _matchTotal = new(0);
    private string _appliedQuery = "";
    private Widget? _appliedRoot;   // 適用先ルート (同一パス再選択でもルートは変わる — 参照で比較)
    private Dictionary<string, DocsPage>? _docsIndex;   // 初回 BuildRoot で構築 (path → 検索対象の本文)
    private long _frame;
    private bool _disposed;

    public GalleryApp(StoryCatalog catalog, IFileStore? playgroundFiles = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        playgroundFiles ??= new PhysicalFileStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luxel", "Gallery"));
        _storyServices = GalleryServices.WithFileStore(playgroundFiles);
        _preview.ChildTheme = _storyTheme;
        _preview.ContentError = error => _pendingStoryError ??= error;
    }


    public StoryContext? Context => _ctx;
    public string? CurrentPath => _currentPath;
    /// <summary>プレビューの子 UiHost (ストーリー側)。リモート検証用に UiRegistry へ登録する。</summary>
    public UiHost? StoryHost => _preview.Child;

    /// <summary>ウィンドウの論理クライアントサイズを同期する (ホストのフレームループから毎フレーム)。
    /// 変わったら chrome を作り直し、fill/全画面のプレビューも新サイズで実体化し直す —
    /// ツリー/ドキュメント/Log の表示範囲がウィンドウリサイズに追従する。</summary>
    public void SetWindowSize(float w, float h)
    {
        if (MathF.Abs(w - _winW) < 0.5f && MathF.Abs(h - _winH) < 0.5f) return;
        _winW = w;
        _winH = h;
        _dirty = true;
        RefreshPreviewSize();
    }

    /// <summary>chrome の再構築が必要か (ストーリー選択後に true、消費で false)。</summary>
    public bool ConsumeDirty()
    {
        bool d = _dirty;
        _dirty = false;
        return d;
    }

    // ---- Interactions (play の Gallery 内再生 — 本家 Storybook の Interactions パネル相当) ----
    // 常設 Console — Gallery 起動中ずっと生きる継続 REPL (セッション/履歴は chrome 再構築・ストーリー切替を跨ぐ)。
    // Log の宛先は遅延解決 (() => _ctx) で「今選択中のストーリー」に追従する。
    private Stories.ReplConsole? _console;
    private readonly List<string> _playSteps = new();
    private readonly Signal<int> _playVer = new(0);   // ステップ/状態の再描画トリガ
    private string _playStatus = "";
    private bool _playRunning;
    private TaskCompletionSource? _playWait;
    private int _playFramesLeft;

    /// <summary>play を実行中のフレーム待ち Task を返す (継続は Update の SetResult で
    /// ギャラリースレッド上に同期再開 — StoryFramePacer と同じ手法)。</summary>
    private Task WaitFrames(int n)
    {
        _playFramesLeft = Math.Max(1, n);
        _playWait = new TaskCompletionSource();
        return _playWait.Task;
    }

    private void NoteStep(string s)
    {
        _playSteps.Add(s);
        _playVer.Value++;
    }

    /// <summary>▶: play をプレビューの実ストーリーに対してリアルタイム再生する。
    /// hermetic 契約どおり、実行前にストーリーを作り直す (E2E ランナーと同じ意味論)。</summary>
    private async void StartPlay(int index)
    {
        if (_playRunning || _currentStory is null) return;
        Select(_currentStory);                       // 作り直し — play のクロージャも新インスタンスを掴む
        IReadOnlyList<StoryPlay> plays = _ctx?.Plays ?? [];
        if (index >= plays.Count || StoryHost is not { } sh) return;
        _playRunning = true;
        _playSteps.Clear();
        _playStatus = "実行中…";
        _playVer.Value++;
        var driver = new PlayDriver(sh,
            step: n => WaitFrames(n * 3),            // 実フレーム × 3 — 目で追える速度
            snap: name => NoteStep($"Snap \"{(name.Length > 0 ? name : "default")}\""),
            onOp: NoteStep);
        try
        {
            await plays[index].Body(driver);
            _playStatus = "✓ 完了";
        }
        catch (PlayError e) { _playStatus = $"✗ {e.Message}"; }
        catch (Exception e) { _playStatus = $"✗ {e.GetType().Name}: {e.Message}"; }
        finally
        {
            _playRunning = false;
            _playVer.Value++;
        }
    }

    /// <summary>毎フレームの軽い同期: 検索適用 + Log の反映。</summary>
    public void Update()
    {
        Resources.Pump();
        _ctx?.PumpObservedResources();
        if (_pendingStoryError is { } storyError)
        {
            _pendingStoryError = null;
            ShowStoryError(_currentStory?.Path ?? "(unknown)", storyError);
        }
        // play のフレーム待ちを進める (SetResult の継続 = play 本体がこのスレッドで再開する)
        if (_playWait is { } pw && --_playFramesLeft <= 0)
        {
            _playWait = null;
            pw.SetResult();
        }
        _ctx?.PumpKnobEdits();   // Knobs テーブルの編集適用 (effect 文脈外)
        if (_pendingNav is string nav) { _pendingNav = null; SelectByPath(nav); }
        SyncSearch();
        if (++_frame % 5 != 0) return;
        int count = _ctx?.LogSnapshot().Length ?? 0;
        if (count != _logCount)
        {
            _logCount = count;
            _logEntries.Value = _ctx?.LogSnapshot() ?? [];
        }
    }

    /// <summary>ギャラリー chrome のルート widget を構築する (初回 + ストーリー選択/ペインリサイズ時)。</summary>
    public Widget BuildRoot()
    {
        _docsIndex ??= DocsIndex.Build(_catalog.All, Resources, _catalog);
        EnsureDock();
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(0))[_dockHost!];
    }

    // ---- Workbench 化した chrome (ToDo 26 WS-D ドッグフード): レイアウトの真実 = DockTree。
    //      サイドバー/プレビュー/下ペイン (Args/Output/Source/Tools のタブ) が
    //      「ドックされたパネル」になり、下ペインのタブは D&D で動かせる。単一タブのペインは
    //      タブ帯を隠して従来 chrome と同じ見え方 (golden 中立)。ペイン内容は SetRoot ごとに
    //      Build し直す Pane (CompositeControl) — 従来の「_dirty → 全再構築」の意味論を保つ。----

    private Signal<DockTree>? _dock;
    private DockTree? _normalTree;     // zen 中に退避する通常レイアウト
    private DockHost? _dockHost;
    private readonly Dictionary<string, Pane> _panes = new();
    private readonly Signal<int> _toolsTab = new(0);

    private sealed class Pane : CompositeControl
    {
        public required Func<Widget> Builder;
        protected override Widget Build() => Builder();
    }

    private static readonly (string Id, string Title)[] PaneDefs =
    [
        ("stories", "Stories"), ("preview", "プレビュー"), ("log", "Output"), ("knobs", "Args"),
        ("source", "Source"), ("tools", "Tools"),
    ];

    private void EnsureDock()
    {
        if (_dock is null)
        {
            _dock = new Signal<DockTree>(NormalTree());
            // ドック操作 (スプリッタ/タブ移動) → ペイン寸法 px の同期 (アプリ生涯 1 Effect)
            Reactive.Effect(() => { _ = _dock!.Value; SyncPaneSizes(); });
        }
        _dockHost ??= DockHost(_dock, ResolvePane, hideSingleTabStrip: true, closeRemoves: false,
            showTabClose: false, tabStripHeight: 36, tabActiveBackground: false);
    }

    private DockItem ResolvePane(string id)
    {
        string title = PaneDefs.First(p => p.Id == id).Title;
        return new DockItem(title, () => _panes.TryGetValue(id, out Pane? p) ? p
            : _panes[id] = new Pane { Builder = id switch
            {
                "stories" => BuildSidebarPane,
                "preview" => BuildPreviewPane,
                "log" => BuildLogPane,
                "knobs" => BuildKnobsPane,
                "source" => BuildSourcePane,
                "tools" => BuildToolsPane,
                _ => () => Spacer(),
            } });
    }

    /// <summary>通常レイアウト: H[stories | V[preview | 下ペイン]]。
    /// 割合は現在のペイン寸法 px から。</summary>
    private DockTree NormalTree()
    {
        DockTree t = DockTree.Single("preview", "stories", "knobs", "log", "source", "tools");
        int pg = t.GroupOf("preview")!.Id;
        t = t.Dock("stories", pg, DockSide.Left);
        t = t.Dock("knobs", pg, DockSide.Bottom);
        int bottom = t.GroupOf("knobs")!.Id;
        t = t.MoveTab("log", bottom).MoveTab("source", bottom).MoveTab("tools", bottom);
        t = t.ActivateTab("knobs");
        // サイズ: 外側 H (sidebar | main) と内側 V (preview | bottom)
        float availW = MathF.Max(1, _winW - Split.Thickness);
        var h = (DockSplit)t.Root;
        t = t.WithSizes(h.Id, [_sidebarW / availW, MathF.Max(0.05f, 1 - _sidebarW / availW)]);
        float availH = MathF.Max(1, _winH - Split.Thickness);
        var v = (DockSplit)((DockSplit)t.Root).Children[1];
        t = t.WithSizes(v.Id, [MathF.Max(0.05f, 1 - _logH / availH), _logH / availH]);
        return t;
    }

    /// <summary>zen レイアウト: H[stories | preview] (下ペインを隠して docs をメイン全面に)。</summary>
    private DockTree ZenTree()
    {
        DockTree t = DockTree.Single("preview", "stories");
        t = t.Dock("stories", t.GroupOf("preview")!.Id, DockSide.Left);
        float availW = MathF.Max(1, _winW - Split.Thickness);
        var h = (DockSplit)t.Root;
        return t.WithSizes(h.Id, [_sidebarW / availW, MathF.Max(0.05f, 1 - _sidebarW / availW)]);
    }

    /// <summary>ドラッグされた割合 → ペイン寸法 px (従来の Splitter 確定と同じ扱い)。
    /// 変わったらプレビュー再実体化 + chrome 再構築。</summary>
    private void SyncPaneSizes()
    {
        if (_dock?.Peek() is not { } t || t.Root is not DockSplit h || !h.Horizontal) return;
        float availW = MathF.Max(1, _winW - Split.Thickness * (h.Children.Count - 1));
        bool changed = false;
        void Set(ref float field, float v, float min, float max)
        {
            v = Math.Clamp(v, min, max);
            if (MathF.Abs(field - v) > 0.5f) { field = v; changed = true; }
        }
        // 外側 H: stories を含む子 = サイドバー幅
        for (int i = 0; i < h.Children.Count; i++)
        {
            float px = (i < h.Sizes.Count ? h.Sizes[i] : 1f / h.Children.Count) * availW;
            if (ContainsTab(h.Children[i], "stories")) Set(ref _sidebarW, px, 220, 420);
            else if (h.Children[i] is DockSplit { Horizontal: false } v)
            {
                float availH = MathF.Max(1, _winH - Split.Thickness * (v.Children.Count - 1));
                for (int j = 0; j < v.Children.Count; j++)
                    if (ContainsTab(v.Children[j], "log"))
                        Set(ref _logH, (j < v.Sizes.Count ? v.Sizes[j] : 1f / v.Children.Count) * availH, 60, 440);
            }
        }
        if (changed)
        {
            RefreshPreviewSize();
            _dirty = true;
        }
    }

    private static bool ContainsTab(DockNode n, string tab) => n switch
    {
        DockGroup g => g.Tabs.Contains(tab),
        DockSplit s => s.Children.Any(c => ContainsTab(c, tab)),
        _ => false,
    };

    // ---- ペイン内容 (従来 chrome の各断片。Pane.Build が呼ぶ — SetRoot ごとに現在状態で作り直す) ----

    private Widget BuildSidebarPane()
    {
        float winH = _winH;
        // ---- サイドバー: StoryMeta のパス階層 + ストーリー検索 ----
        // 展開状態 (_treeExpanded) は GalleryApp が所有 — chrome 再構築をまたいで保持。
        // 初回は全 Component を展開 (従来の全件表示と同じ見え方から始める)。
        _docsIndex ??= DocsIndex.Build(_catalog.All, Resources, _catalog);
        // 本家 Storybook と同じく、**パスのスラッシュ区切りがそのまま階層** (title 相当)。
        // 末尾セグメント = ストーリー、手前 = フォルダ (章/コンポーネント/…、深さ任意)。
        // 表示層のマップは持たない — 章替え/整理はパス改名 (+ golden の git mv) で行う。
        var roots = new List<TreeNode>();
        var folders = new Dictionary<string, List<TreeNode>>();   // "Examples/2D" → 子リスト
        foreach (StoryInfo s in _catalog.All)
        {
            string[] seg = s.Path.Split('/');
            List<TreeNode> level = roots;
            string prefix = "";
            for (int i = 0; i < seg.Length - 1; i++)
            {
                prefix = i == 0 ? seg[0] : $"{prefix}/{seg[i]}";
                if (!folders.TryGetValue(prefix, out List<TreeNode>? children))
                {
                    children = new List<TreeNode>();
                    folders[prefix] = children;
                    level.Add(new TreeNode($"g:{prefix}", seg[i], children));
                    if (_treeInit && i == 0) _treeExpanded.Add($"g:{prefix}");   // 章 (トップ) だけ開く
                }
                level = children;
            }
            DocsPage? page = _docsIndex.GetValueOrDefault(s.Path);
            level.Add(new TreeNode(s.Path, s.Name, Tag: s, SearchText: page?.Text));
        }
        _treeInit = false;
        TreeView tree = TreeView(roots, _treeExpanded,
            onSelect: (_, n) =>
            {
                if (n.Tag is StoryInfo s) Select(s);
            },
            selected: _currentPath ?? "", filter: _search,
            appearance: new TreeViewAppearance(
                FolderFontSize: 14,
                LeafFontSize: 14,
                FolderColor: GalleryChromeTheme.TreeFolder,
                LeafColor: GalleryChromeTheme.TreeLeaf,
                HoverColor: GalleryChromeTheme.TreeHoverText,
                SelectedColor: GalleryChromeTheme.TreeSelectedText,
                HoverBackground: GalleryChromeTheme.TreeHover,
                SelectedBackground: GalleryChromeTheme.AccentSoft,
                ChevronColor: GalleryChromeTheme.TreeChevron));
        // Blazor 版と同じ検索 chrome。
        _searchField ??= TextField(_search, "Storyを検索", width: _sidebarW - 28,
            background: GalleryChromeTheme.Search, fontSize: 13)[
                TextFieldSlot.Leading(() => Icon(IconKind.Search, iconSize: 16, stroke: 1.5f,
                    color: Bind.From(() => UiTheme.T.TextMuted))),
                TextFieldSlot.Trailing(() => Icon(IconKind.Close, iconSize: 14, stroke: 1.5f,
                    color: Bind.From(() => UiTheme.T.TextMuted), onClick: _ => _search.Value = ""))];
        _searchField.Width.SetOverride(_sidebarW - 28);
        Widget searchInput = _searchField;
        Widget searchBar = Border(padding: new Thickness(14, 0, 14, 12))[searchInput];

        Widget mark = Border(background: Bind.From(() => UiTheme.T.Primary), rounded: 9,
            width: 34, height: 34)[Center()[Text("L", 17, color: Bind.From(() => UiTheme.T.Background))]];
        Widget brand = Border(padding: new Thickness(18, 18, 18, 14), height: 68)[HStack(12)[
            mark,
            VStack(2)[
                Text("Luxel", 17, color: Bind.From(() => UiTheme.T.Text)),
                Text("GALLERY", 11, color: Bind.From(() => UiTheme.T.TextMuted))]]];

        // スクロールは永続インスタンス — chrome 再構築 (ストーリー選択/リサイズ) をまたいで位置を保つ
        float treeH = MathF.Max(80, winH - 68 - 48 - 34);
        _sidebarScroll ??= Scroll(treeH, width: _sidebarW - 18);
        _sidebarScroll.SetViewportHeight(treeH);
        _sidebarScroll.Width.SetOverride(_sidebarW - 18);
        Widget treeViewport = Border(padding: new Thickness(9, 0))[_sidebarScroll[tree]];
        Widget footer = VStack(0)[
            Border(background: Bind.From(() => UiTheme.T.BorderColor), height: 1),
            Text($"{_catalog.All.Count} 件のStory", 11,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(16, 10))];
        return Border(background: Bind.From(() => UiTheme.T.SurfaceAlt))[
            VStack(0)[
            brand,
            searchBar,
            treeViewport,
            footer]];
    }

    private Widget BuildPreviewPane()
    {
        // ---- ツールバー + プレビュー ----
        string component = _currentStory?.Component ?? "Story";
        string name = _currentStory?.Name ?? "ストーリーを選択";
        Widget title = Border(padding: new Thickness(22, 10, 0, 8))[VStack(2)[
            Text(component.ToUpperInvariant(), 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            Text(name, 19, color: Bind.From(() => UiTheme.T.Text))]];
        Widget actions = Border(padding: new Thickness(0, 12, 14, 10))[HStack(8)[
            Button(_ => ToggleTheme(), _dark ? "Light" : "Dark", variant: Luxel.UI.Variant.Ghost),
            Button(_ => ToggleZen(), _zen ? "キャンバスを閉じる" : "キャンバスを開く")]];
        actions.GridColumn(1);
        Widget toolbarContent = Border(background: GalleryChromeTheme.Main)[
            Grid(columns: [GridLength.Star(1), GridLength.Auto])[title, actions]];
        Widget toolbar = Grid(rows: [GridLength.Px(67), GridLength.Px(1)])[
            toolbarContent,
            Border(background: Bind.From(() => UiTheme.T.BorderColor), height: 1).GridRow(1)];
        toolbar.GridRow(0);
        Widget previewSurface = Border(background: GalleryChromeTheme.Preview)[_preview];
        previewSurface.GridRow(1);
        return Grid(rows: [GridLength.Px(68), GridLength.Star(1)])[toolbar, previewSurface];
    }

    /// <summary>メインペイン (プレビュー/下ペイン) の実幅 px。</summary>
    private float MainW() => _winW - _sidebarW - Split.Thickness;

    /// <summary>下ペイン内容の高さ (Blazor の 36px タブ帯 + 上下 padding を引いた内寸)。</summary>
    private float BottomInnerH() => MathF.Max(24, _logH - 66);

    private static Widget BottomPanel(Widget content) => Border(
        background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(16, 12, 16, 18))[content];

    private Widget BuildLogPane()
    {
        float paneW = MathF.Max(140, MainW());
        float innerH = BottomInnerH();
        _logEntries.Value = _ctx?.LogSnapshot() ?? [];
        return BottomPanel(Scroll(innerH, width: paneW - 32)[
            new GalleryOutputPane(_logEntries, MathF.Max(120, paneW - 48))]);
    }

    private Widget BuildKnobsPane()
    {
        // Knobs (autodoc 風テーブル)。編集は StoryContext のキューへ (Update の PumpKnobEdits が適用)
        float paneW = MathF.Max(140, MainW());
        return BottomPanel(Scroll(BottomInnerH(), width: paneW - 32)[
            global::Luxel.Gallery.UI.Kit.KnobsTable(_ctx?.Knobs ?? [], width: paneW - 48,
                appearance: new global::Luxel.Gallery.UI.KnobsTableAppearance(
                    BorderColor: GalleryChromeTheme.Border,
                    RowBackground: GalleryChromeTheme.Panel,
                    NameColor: GalleryChromeTheme.TreeHoverText,
                    TypeColor: GalleryChromeTheme.TreeChevron,
                    DescriptionColor: GalleryChromeTheme.TreeFolder),
                onEdit: (_, k, v) => _ctx?.QueueKnobEdit(k, v))]);
    }

    private Widget BuildInteractionsPane()
    {
        // Interactions: play 一覧 + ▶ 再生 + ステップログ (Storybook の Interactions 相当)
        float paneW = MathF.Max(140, MainW());
        float innerH = BottomInnerH();
        IReadOnlyList<StoryPlay> storyPlays = _ctx?.Plays ?? [];
        Widget interactionsPane;
        if (storyPlays.Count == 0)
        {
            interactionsPane = Text("このストーリーに play はありません — ctx.Play(d => d.Snap()) で登録します",
                12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8, 8, 0, 0));
        }
        else
        {
            var rows = new List<Widget>();
            for (int i = 0; i < storyPlays.Count; i++)
            {
                int idx = i;
                string label = storyPlays[i].Name.Length > 0 ? storyPlays[i].Name : "default";
                rows.Add(Button(_ => StartPlay(idx), $"▶ {label}",
                    variant: Luxel.UI.Variant.Ghost, fontSize: UiTheme.T.FontSm, width: 180f));
            }
            Func<string> playLog = () =>
            {
                _ = _playVer.Value;   // 依存 — ステップ追加/完了で再評価
                string log = string.Join("\n", _playSteps);
                return _playStatus.Length == 0 ? (log.Length == 0 ? "—" : log) : $"{log}\n{_playStatus}";
            };
            interactionsPane = HStack(10)[
                Scroll(innerH, width: 200f)[VStack(1)[rows.ToArray()]],
                Scroll(innerH, width: MathF.Max(120, paneW - 260))[
                    Text(playLog, 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 2, 0, 0))]];
        }
        return interactionsPane;
    }

    private Widget BuildConsolePane()
    {
        // Console: 常設の継続 REPL (Gallery 起動中ずっと生きる — セッション/履歴を保持)。
        // 開くと前の行で宣言した変数が次に見える。Log(...) は今選択中のストーリーの Log パネルへ。
        _console ??= new Stories.ReplConsole(
            MathF.Max(140, MainW()) - 32,
            (Luxel.Scripting.ScriptHost)GalleryServices.Provider.GetService(typeof(Luxel.Scripting.ScriptHost))!,
            new Stories.ScriptGlobals(() => _ctx),
            initial: "");
        return _console;
    }

    private Widget BuildToolsPane()
    {
        float width = MathF.Max(140, MainW()) - 32;
        return BottomPanel(Tabs(
            ["Interactions", "Console"],
            [BuildInteractionsPane(), BuildConsolePane()],
            _toolsTab,
            width: width,
            height: BottomInnerH()));
    }

    private Widget BuildSourcePane()
    {
        float width = MathF.Max(140, MainW()) - 32;
        float height = BottomInnerH();
        return BottomPanel(Border(background: GalleryChromeTheme.Border, padding: new Thickness(1),
            rounded: 7, clip: true, width: width, height: height)[
                Border(background: GalleryChromeTheme.PanelCode, width: width - 2, height: height - 2)[
                    BuildStorySourcePane(_currentStory, width - 2, height - 2)]]);
    }

    private static Widget BuildStorySourcePane(StoryInfo? story, float width = 640f, float height = 240f)
        => GalleryStorySourcePane.Build(story, width, height);

    /// <summary>全画面 (zen) の切替: DockTree を組み替え (通常レイアウトは退避して復元)、
    /// プレビュー内容もメイン全面サイズで再実体化する。</summary>
    private void ToggleZen()
    {
        _zen = !_zen;
        if (_dock is not null)
        {
            if (_zen) { _normalTree = _dock.Value; _dock.Value = ZenTree(); }
            else if (_normalTree is not null) _dock.Value = _normalTree;
        }
        if (_currentStory is { } s && _storyRoot is not null)
        {
            (int pw, int ph) = PreviewSize(s);
            _preview.SetContent(_storyRoot, pw, ph);
        }
        _dirty = true;
    }

    /// <summary>ストーリープレビューのテーマ切替。Gallery chrome の暗色テーマには影響しない。</summary>
    public void ToggleTheme()
    {
        _dark = !_dark;
        _storyTheme.Value = (_dark ? Theme.Dark : Theme.Light).Compact();
        _dirty = true;   // toolbar label (Dark / Light) is rebuilt with the chrome
    }

    /// <summary>検索状態の同期 (Update 毎): クエリ/ページが変わったら開いている docs へハイライトを
    /// 適用し、n/m を更新する。</summary>
    private void SyncSearch()
    {
        TextEditorView? doc = _storyRoot is null ? null : DocsIndex.FindMarkdownDoc(_storyRoot);
        string q = _search.Value;
        if (q == _appliedQuery && ReferenceEquals(_storyRoot, _appliedRoot)) return;
        _appliedQuery = q;
        _appliedRoot = _storyRoot;
        if (doc is not null)
        {
            doc.SetSearch(q);
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
        if (_storyRoot is null || DocsIndex.FindMarkdownDoc(_storyRoot) is not { } doc) return;
        if (dir > 0) doc.FindNext(); else doc.FindPrev();
        _matchCur.Value = doc.SearchCurrent + 1;
        _matchTotal.Value = doc.SearchMatchCount;
    }

    /// <summary>プレビューの内容サイズ: メイン領域いっぱい、全画面はメイン全面
    /// (いずれもサーフェスサイズが上限 — SetContent 側でも clamp される)。</summary>
    private (int W, int H) PreviewSize(StoryInfo story)
    {
        if (_zen)
            return ((int)MathF.Min(SurfW, _winW - _sidebarW - Split.Thickness),
                    (int)MathF.Min(SurfH, _winH - 68));
        // 通常モードのメイン領域 (サイドバー/Log を除いた実寸)
        float w = _winW - _sidebarW - Split.Thickness;
        float h = _winH - 68 - Split.Thickness - _logH;
        return ((int)MathF.Min(SurfW, w), (int)MathF.Min(SurfH, h));
    }

    /// <summary>ペイン寸法やウィンドウサイズが変わったら、プレビューを新しい領域サイズで実体化し直す。</summary>
    private void RefreshPreviewSize()
    {
        if (_storyRoot is null || _currentStory is not { } s) return;
        (int pw, int ph) = PreviewSize(s);
        _preview.SetContent(_storyRoot, pw, ph);
    }

    /// <summary>実窓ホストの GPU 設備 (Program が結線)。実窓専用ストーリーが ctx.Device/Font で借りる。</summary>
    public (GpuDevice Device, Luxel.Typography.VectorFont Font)? HostGpu
    {
        get => _hostGpu;
        set
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GalleryApp));
            if (value is null)
            {
                if (_resources is not null)
                    throw new InvalidOperationException("Gallery GPU resources are already configured.");
                _hostGpu = null;
                return;
            }

            if (_resources is not null)
            {
                if (!ReferenceEquals(_hostGpu?.Device, value.Value.Device))
                    throw new InvalidOperationException("Gallery GPU resources are already configured for another device.");
                _hostGpu = value;
                return;
            }

            var builder = new Luxel.Resources.ResourceSystemBuilder();
            Luxel.Resources.ResourceSystemDefaultHandles defaults = Luxel.Resources.ResourceSystemDefaults.AddCore(builder);
            Luxel.Resources.ResourceSystemDefaults.AddBuiltinSources(builder, defaults, Environment.CurrentDirectory);
            Luxel.Resources.ResourceSystemDefaults.AddBuiltinSteps(builder, defaults);
            builder.Steps.Add<byte[], Luxel.Resources.CpuImage>(new Luxel.Imaging.ImageSharpDecoder())
                .RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            _slangCompilation = new GallerySlangCompilation();
            _slangCompilation.Register(builder, defaults, value.Value.Device.BackendKind);
            builder.AddAssetGpu(value.Value.Device);
            _resources = builder.Build();
            _hostGpu = value;
        }
    }

    public void Select(StoryInfo story)
    {
        StoryContext? oldContext = _ctx;
        var newContext = new StoryContext(Resources);
        newContext.SetServices(_storyServices);   // DI: shared scripting services + native Playground persistence
        // 遷移は次フレームへキュー — 子ホストの入力ディスパッチ中に SetContent (旧ルート破棄) しない
        newContext.SetNavigator(p => _pendingNav = p);
        if (HostGpu is { } gpu) newContext.SetGpuHost(gpu.Device, gpu.Font);
        (int pw, int ph) = PreviewSize(story);

        Widget newRoot;
        try
        {
            StoryResult result = story.Build(newContext);
            newRoot = result.Kind == StoryResultKind.Markdown
                ? StoryMarkdownRenderer.Build(story, newContext, result)
                : result.Widget ?? throw new InvalidOperationException("Widget story returned no Widget.");
        }
        catch (Exception error)
        {
            newContext.Dispose();
            if (InstallStoryError(story, error, pw, ph)) oldContext?.Dispose();
            else
            {
                _preview.Dispose();
                oldContext?.Dispose();
                _ctx = null;
                _storyRoot = null;
            }
            return;
        }

        try
        {
            // SetContent releases the previous realized UI first. Only then may its context scope be released.
            _preview.SetContent(newRoot, pw, ph);
        }
        catch (Exception error)
        {
            // A failed SetRoot may have partially realized the new tree. Replacing it with the error view
            // releases that UI before either the failed new scope or the previous scope is disposed.
            bool errorInstalled = InstallStoryError(story, error, pw, ph);
            if (!errorInstalled)
            {
                // If even the fallback cannot replace the partial tree, tear down the surface before releasing
                // either scope. The preview is no longer usable, but no realized UI can outlive its resources.
                _preview.Dispose();
            }
            newContext.Dispose();
            oldContext?.Dispose();
            if (!errorInstalled)
            {
                _ctx = null;
                _storyRoot = null;
            }
            return;
        }

        _ctx = newContext;
        _storyRoot = newRoot;
        SetSelectedStory(story);
        oldContext?.Dispose();
        StorySelectionChanged();
    }

    private bool InstallStoryError(StoryInfo story, Exception error, int width, int height)
    {
        Console.Error.WriteLine($"[gallery] story error '{story.Path}': {error}");   // スタック付き (診断用)
        Widget errorRoot = Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(spacing: 8)[
                Text("Story error", 18, color: Color2D.Rgba(220, 60, 60)),
                Text($"{error.GetType().Name}: {error.Message}", 14, color: Color2D.Rgba(220, 60, 60))
            ]];
        try
        {
            _preview.SetContent(errorRoot, width, height);
            _ctx = null;
            _storyRoot = errorRoot;
            SetSelectedStory(story);
            StorySelectionChanged();
            return true;
        }
        catch (Exception fallbackError)
        {
            Console.Error.WriteLine($"[gallery] failed to install error view for '{story.Path}': {fallbackError}");
            return false;
        }
    }

    private void ShowStoryError(string path, Exception error, int? width = null, int? height = null)
    {
        StoryContext? failedContext = _ctx;
        StoryInfo story = _currentStory ?? new StoryInfo(path, _ => Spacer());
        (int pw, int ph) = width.HasValue && height.HasValue
            ? (width.Value, height.Value)
            : _currentStory is { } current ? PreviewSize(current) : ((int)PreviewW, (int)PreviewH);
        if (InstallStoryError(story, error, pw, ph)) failedContext?.Dispose();
    }

    private void SetSelectedStory(StoryInfo story)
    {
        _currentStory = story;
        _currentPath = story.Path;
        // 選択したstoryが折りたたまれたfolder内でも必ず見えるよう、全ancestorを展開する。
        string[] segments = story.Path.Split('/');
        string prefix = "";
        for (int i = 0; i < segments.Length - 1; i++)
        {
            prefix = i == 0 ? segments[i] : $"{prefix}/{segments[i]}";
            _treeExpanded.Add($"g:{prefix}");
        }
    }

    private void StorySelectionChanged()
    {
        _logCount = -1;       // 新 StoryContext → Log リストを作り直させる
        _dirty = true;        // knobs パネルが変わるので chrome を再構築 (SurfaceView は再利用)
    }

    public void SelectByPath(string path)
    {
        if (_catalog.Find(path) is { } s) Select(s);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preview.Dispose();
        _ctx?.Dispose();
        _ctx = null;
        // ResourceSystem owns GPU manager retirement, while the runtime owns the device.
        _resources?.Dispose();
        _slangCompilation?.Dispose();
    }
}
