using System.Text.Json;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Settings;
using Luxel.UI;
using Luxel.Workbench;
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
    // サーフェス (framebuffer) は fill/全画面の最大サイズで確保 — SetContent の論理サイズは
    // サーフェス以下にしか広げられない (余白は透過なので通常時の見た目は不変)。
    // 大型モニタの最大化まで追従できるよう余裕を持たせる (DeviceLocal ~14MB×scale²)
    private const float SurfW = 2560, SurfH = 1440;

    private readonly StoryCatalog _catalog;
    private readonly IServiceProvider _storyServices;
    private readonly SurfaceView _preview = SurfaceView(SurfW, SurfH);
    private Exception? _pendingStoryError;
    // ストーリーへ StoryContext.Resources として配布 (キャッシュ共有、Pump は Update が叩く)
    private Luxel.Resources.ResourceSystem? _resources;
    private Luxel.Resources.ResourceSystem Resources => _resources
        ?? throw new InvalidOperationException("Gallery GPU resources have not been configured.");
    private GallerySlangCompilation? _slangCompilation;
    private (GpuDevice Device, Luxel.Typography.VectorFont Font)? _hostGpu;
    private readonly Signal<string> _title = new("(ストーリーを選択)");
    // Log は ListView — 追記時は items signal へ流す (行ノードの差し替えのみ、chrome の SetRoot 不要)
    private readonly Signal<IReadOnlyList<string>> _logItems = new([]);
    private readonly Signal<int> _logCountSig = new(0);
    private int _logCount = -1;

    // ペイン寸法 (Splitter ドラッグで変更 → chrome 再構築)
    private float _sidebarW = 170, _rightW = 360, _logH = 240;   // 右パネルは Knobs テーブル (4 列) が収まる幅
    // ウィンドウの論理クライアントサイズ (ホストが毎フレーム SetWindowSize で同期 — リサイズで chrome 再構築)
    private float _winW = 1280, _winH = 801;
    private ScrollViewer? _sidebarScroll;   // サイドバーのスクロールは chrome 再構築をまたいで位置を保つ
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
    private bool _disposed;
    private Widget? _selected;                                   // Props インスペクタの選択ノード
    private readonly object _editGate = new();
    private readonly List<(Widget W, string Name, string Type, string Value)> _propEdits = new();

    public GalleryApp(StoryCatalog catalog, IFileStore? playgroundFiles = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        playgroundFiles ??= new PhysicalFileStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luxel", "Gallery"));
        _storyServices = GalleryServices.WithFileStore(playgroundFiles);
        _preview.ContentError = error => _pendingStoryError ??= error;
        WireStateForcing();   // Effect は生涯 1 組 (BuildRoot 毎に張ると累積する)
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

    /// <summary>毎フレームの軽い同期: 状態強制の適用 (effect 文脈の外で signal を書く) + 検索適用 + Log の反映 (15f 毎)。</summary>
    public void Update()
    {
        Resources.Pump();
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
        _docsIndex ??= DocsIndex.Build(_catalog.All, Resources, _catalog);
        EnsureDock();
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(6))[_dockHost!];
    }

    // ---- Workbench 化した chrome (ToDo 26 WS-D ドッグフード): レイアウトの真実 = DockTree。
    //      サイドバー/プレビュー/下ペイン (Log/Knobs/Interactions/Console/Source のタブ)/Props が
    //      「ドックされたパネル」になり、下ペインのタブは D&D で動かせる。単一タブのペインは
    //      タブ帯を隠して従来 chrome と同じ見え方 (golden 中立)。ペイン内容は SetRoot ごとに
    //      Build し直す Pane (CompositeControl) — 従来の「_dirty → 全再構築」の意味論を保つ。----

    private Signal<DockTree>? _dock;
    private DockTree? _normalTree;     // zen 中に退避する通常レイアウト
    private DockHost? _dockHost;
    private readonly Dictionary<string, Pane> _panes = new();

    private sealed class Pane : CompositeControl
    {
        public required Func<Widget> Builder;
        protected override Widget Build() => Builder();
    }

    private static readonly (string Id, string Title)[] PaneDefs =
    [
        ("stories", "Stories"), ("preview", "プレビュー"), ("log", "Output"), ("knobs", "Args"),
        ("interactions", "Interactions"), ("console", "Console"), ("source", "Source"), ("props", "Props"),
    ];

    private void EnsureDock()
    {
        if (_dock is null)
        {
            _dock = new Signal<DockTree>(NormalTree());
            // ドック操作 (スプリッタ/タブ移動) → ペイン寸法 px の同期 (アプリ生涯 1 Effect)
            Reactive.Effect(() => { _ = _dock!.Value; SyncPaneSizes(); });
        }
        _dockHost ??= DockHost(_dock, ResolvePane, hideSingleTabStrip: true, closeRemoves: false);
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
                "interactions" => BuildInteractionsPane,
                "console" => BuildConsolePane,
                "source" => BuildSourcePane,
                "props" => BuildPropsPane,
                _ => () => Spacer(),
            } });
    }

    /// <summary>通常レイアウト: H[stories | V[preview | 下ペイン(5 タブ)] | props]。
    /// 割合は現在のペイン寸法 px から。</summary>
    private DockTree NormalTree()
    {
        DockTree t = DockTree.Single("preview", "stories", "props", "knobs", "log", "source", "interactions", "console");
        int pg = t.GroupOf("preview")!.Id;
        t = t.Dock("stories", pg, DockSide.Left);
        t = t.Dock("props", pg, DockSide.Right);
        t = t.Dock("knobs", pg, DockSide.Bottom);
        int bottom = t.GroupOf("knobs")!.Id;
        t = t.MoveTab("log", bottom).MoveTab("source", bottom).MoveTab("interactions", bottom).MoveTab("console", bottom);
        t = t.ActivateTab("knobs");
        // サイズ: 外側 H (sidebar | main | props) と内側 V (preview | bottom)
        float availW = MathF.Max(1, _winW - 12 - Split.Thickness * 2);
        var h = (DockSplit)t.Root;
        t = t.WithSizes(h.Id, [_sidebarW / availW, MathF.Max(0.05f, 1 - (_sidebarW + _rightW) / availW), _rightW / availW]);
        float availH = MathF.Max(1, _winH - 12 - Split.Thickness);
        var v = (DockSplit)((DockSplit)t.Root).Children[1];
        t = t.WithSizes(v.Id, [MathF.Max(0.05f, 1 - _logH / availH), _logH / availH]);
        return t;
    }

    /// <summary>zen レイアウト: H[stories | preview] (Log/右パネルを隠して docs をメイン全面に)。</summary>
    private DockTree ZenTree()
    {
        DockTree t = DockTree.Single("preview", "stories");
        t = t.Dock("stories", t.GroupOf("preview")!.Id, DockSide.Left);
        float availW = MathF.Max(1, _winW - 12 - Split.Thickness);
        var h = (DockSplit)t.Root;
        return t.WithSizes(h.Id, [_sidebarW / availW, MathF.Max(0.05f, 1 - _sidebarW / availW)]);
    }

    /// <summary>ドラッグされた割合 → ペイン寸法 px (従来の Splitter 確定と同じ扱い)。
    /// 変わったらプレビュー再実体化 + chrome 再構築。</summary>
    private void SyncPaneSizes()
    {
        if (_dock?.Peek() is not { } t || t.Root is not DockSplit h || !h.Horizontal) return;
        float availW = MathF.Max(1, _winW - 12 - Split.Thickness * (h.Children.Count - 1));
        bool changed = false;
        void Set(ref float field, float v, float min, float max)
        {
            v = Math.Clamp(v, min, max);
            if (MathF.Abs(field - v) > 0.5f) { field = v; changed = true; }
        }
        // 外側 H: stories を含む子 = サイドバー幅、props を含む子 = 右パネル幅
        for (int i = 0; i < h.Children.Count; i++)
        {
            float px = (i < h.Sizes.Count ? h.Sizes[i] : 1f / h.Children.Count) * availW;
            if (ContainsTab(h.Children[i], "stories")) Set(ref _sidebarW, px, 120, 420);
            else if (ContainsTab(h.Children[i], "props")) Set(ref _rightW, px, 200, 460);
            else if (h.Children[i] is DockSplit { Horizontal: false } v)
            {
                float availH = MathF.Max(1, _winH - 12 - Split.Thickness * (v.Children.Count - 1));
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
        float winH = _winH - 12;
        // ---- サイドバー: Component > Story > 見出し の 3 階層ツリー + 検索 ----
        // 展開状態 (_treeExpanded) は GalleryApp が所有 — chrome 再構築をまたいで保持。
        // 初回は全 Component を展開 (従来の全件表示と同じ見え方から始める)。
        // 見出し (TOC) は DocsIndex から全ページ分を常設 (Tag = (StoryInfo, ブロック index))
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
            List<TreeNode>? heads = page is { Headings.Count: > 0 }
                ? page.Headings.Select(h => new TreeNode($"{s.Path}#{h.Block}", h.Text,
                    Tag: (s, h.Block))).ToList()
                : null;
            level.Add(new TreeNode(s.Path, s.Name, heads, Tag: s, SearchText: page?.Text));
        }
        _treeInit = false;
        TreeView tree = TreeView(roots, _treeExpanded,
            onSelect: (_, n) =>
            {
                if (n.Tag is StoryInfo s) Select(s);
                else if (n.Tag is (StoryInfo hs, int block))
                {
                    if (hs.Path != _currentPath) { Select(hs); _pendingScroll = block; }
                    else if (_storyRoot is not null && DocsIndex.FindMarkdownDoc(_storyRoot) is { } doc)
                        doc.ScrollToSource(block);   // block = 見出しのソースオフセット
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
        // スクロールは永続インスタンス — chrome 再構築 (ストーリー選択/リサイズ) をまたいで位置を保つ
        _sidebarScroll ??= Scroll(winH - 58, width: _sidebarW);
        _sidebarScroll.SetViewportHeight(winH - 58);
        _sidebarScroll.Width.SetOverride(_sidebarW);
        return VStack(2)[
            Heading("Stories"),
            searchBar,
            _sidebarScroll[tree]];
    }

    private Widget BuildPreviewPane()
    {
        // ---- ツールバー + プレビュー ----
        // フレームステップデバッグ: ⏸ で子のアニメ時間を凍結、⏭ で 1 フレームだけ進める
        Func<string> pauseLabel = () => _preview.Paused ? "▶ 再生" : "⏸ 停止";
        Widget toolbar = HStack(8)[
            Text($"{_title}", 14, color: Bind.From(() => UiTheme.T.Text), width: 300),
            Button(_ => ToggleTheme(), "theme"),
            Button(_ => ToggleZen(), _zen ? "元に戻す" : "全画面"),
            Button(_ => { _preview.Paused = !_preview.Paused; _dirty = true; }, pauseLabel,
                   variant: _preview.Paused ? Luxel.UI.Variant.Tonal : Luxel.UI.Variant.Ghost, fontSize: 12f),
            Button(_ => _preview.StepFrame(), "⏭", variant: Luxel.UI.Variant.Ghost, fontSize: 12f),
            Check(_fHover, "hover"),
            Check(_fPressed, "pressed"),
            Check(_fFocused, "focused"),
            Check(_fDisabled, "disabled")];
        toolbar.GridRow(0);
        _preview.GridRow(1);
        return Grid(rows: [GridLength.Px(28), GridLength.Star(1)])[toolbar, _preview];
    }

    /// <summary>メインペイン (プレビュー/下ペイン) の実幅 px。</summary>
    private float MainW() => _zen
        ? _winW - 12 - _sidebarW - Split.Thickness
        : _winW - 12 - _sidebarW - Split.Thickness * 2 - _rightW;

    /// <summary>下ペイン内容の高さ (タブ帯 32 とパディングを引いた内寸)。</summary>
    private float BottomInnerH() => MathF.Max(24, _logH - 56);

    private Widget BuildLogPane()
    {
        float paneW = MathF.Max(140, MainW());
        float innerH = BottomInnerH();
        _logItems.Value = LogLines();
        return VStack(2)[
            Text($"({_logCountSig})", 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            ListView(MathF.Max(24, innerH - 16), 16f, items: _logItems, width: MathF.Max(120, paneW - 40))];
    }

    private Widget BuildKnobsPane()
    {
        // Knobs (autodoc 風テーブル)。編集は StoryContext のキューへ (Update の PumpKnobEdits が適用)
        float paneW = MathF.Max(140, MainW());
        return Scroll(BottomInnerH(), width: paneW - 32)[
            global::Luxel.Gallery.UI.Kit.KnobsTable(_ctx?.Knobs ?? [], width: paneW - 48,
                onEdit: (_, k, v) => _ctx?.QueueKnobEdit(k, v))];
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
                    variant: Luxel.UI.Variant.Ghost, fontSize: 12f, width: 180f));
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

    private Widget BuildSourcePane()
        => BuildStorySourcePane(_currentStory, MathF.Max(140, MainW()) - 32, BottomInnerH());

    private static Widget BuildStorySourcePane(StoryInfo? story, float width = 640f, float height = 240f)
        => GalleryStorySourcePane.Build(story, width, height);

    private Widget BuildPropsPane()
    {
        // ---- 右パネル: Props (ツリー + 選択ノードのプロパティ編集) ----
        float winH = _winH - 12;
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

        return VStack(2)[
            Heading("Props"),
            Scroll(MathF.Max(80, winH - 70), width: _rightW)[VStack(3)[props.ToArray()]]];
    }

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
        TextEditorView? doc = _storyRoot is null ? null : DocsIndex.FindMarkdownDoc(_storyRoot);
        if (_pendingScroll >= 0 && doc is { Scope: not null })
        {
            doc.ScrollToSource(_pendingScroll);
            _pendingScroll = -1;
        }
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

    /// <summary>プレビューの内容サイズ: 通常はストーリー宣言サイズ、fill ストーリー (W/H 未指定 = 0,0
    /// — docs ページ等) はメイン領域いっぱい、全画面はメイン全面
    /// (いずれもサーフェスサイズが上限 — SetContent 側でも clamp される)。</summary>
    private (int W, int H) PreviewSize(StoryInfo story)
    {
        if (_zen)
            return ((int)MathF.Min(SurfW, _winW - 12 - _sidebarW - Split.Thickness),
                    (int)MathF.Min(SurfH, _winH - 12 - 28));
        if (story.Width > 0)
            return (story.Width, story.Height);
        // fill: 通常モードのメイン領域 (サイドバー/右パネル/Log を除いた実寸)
        float w = _winW - 12 - _sidebarW - Split.Thickness * 2 - _rightW;
        float h = _winH - 12 - 28 - Split.Thickness - _logH;
        return ((int)MathF.Min(SurfW, w), (int)MathF.Min(SurfH, h));
    }

    /// <summary>fill/全画面ストーリーの表示中にペイン寸法やウィンドウサイズが変わったら、
    /// プレビューを新しい領域サイズで実体化し直す。固定サイズのストーリー (通常表示) は何もしない。</summary>
    private void RefreshPreviewSize()
    {
        if (_storyRoot is null || _currentStory is not { } s) return;
        if (!_zen && s.Width > 0) return;
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
            StoryResult result = story.BuildResult(newContext);
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
        StoryInfo story = _currentStory ?? new StoryInfo(path, width ?? (int)PreviewW, height ?? (int)PreviewH, null, _ => Spacer());
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
        _treeExpanded.Add(story.Path);   // docs story自身も開いてTOCを見せる
        _title.Value = story.Path;
    }

    private void StorySelectionChanged()
    {
        _logCount = -1;       // 新 StoryContext → Log リストを作り直させる
        _selected = null;     // Props 選択は旧ストーリーの widget なのでクリア
        _dirty = true;        // knobs/props パネルが変わるので chrome を再構築 (SurfaceView は再利用)
        _statesDirty = true;  // 強制中の状態を新ストーリーへ再適用
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
