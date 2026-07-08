using System.Text.Json;
using Luxel.Controls;
using Luxel.Diagnostics;
using Luxel.NodeGraph;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.DevTools;

/// <summary>
/// DevTools ウィンドウの UI (別スレッドの島)。データは <see cref="DevToolsListener"/> の rev ポーリング、
/// エンジン操作は <see cref="EngineCommands.Enqueue"/>。
/// パネル: Frame (ImageView, ◀▶ でソース切替) / Trees / Log / Stat / ECS / Res / Surf / Input / Audio / GPU / Graph
/// — JSON 系は汎用フラット化 (<see cref="JsonPanel"/>) で同型に扱う。
/// **注意**: この島は自前テーマなので、グローバル既定テーマを閉包購読する Kit の複合ヘルパ
/// (Heading/Card 等) や <c>Bind.From(() => UiTheme.T.X)</c> は使わない — 必ず <c>_theme</c> を参照する。
/// </summary>
internal sealed class DevToolsUi
{
    private readonly DevToolsListener _listener;
    private readonly EngineCommands _engine;   // メインスレッドが Drain (Enqueue のみ行う)
    private readonly Signal<Theme> _theme;

    private readonly Signal<string> _status = new("(connecting)");
    private readonly Signal<int> _tab = new(0);

    // ---- Frame パネル (ソース: -1 = main frame, それ以外 = UiFrame index) ----
    private readonly ImageView _frameView = ImageView(700, 460);
    private readonly Signal<string> _frameSrcLabel = new("main");
    private int _frameSource = -1;
    private long _frameRev = -1;
    private byte[]? _lastUiFrame;
    private byte[] _frameBuf = Array.Empty<byte>();   // main フレーム読みの使い回しバッファ (島スレッドの LOH churn 回避、F2)

    // ListView は items signal 駆動 (EV3) — signal へ流し込むと参照変化分だけ再バインドされる。
    // ファクトリはインスタンスフィールドの signal を参照するため ctor で生成する。
    private readonly Signal<IReadOnlyList<string>> _treesItems = new([]);
    private readonly Signal<IReadOnlyList<string>> _logItems = new([]);
    private readonly ListView _treesList;
    private readonly ListView _logList;
    // ECS は一覧 (軽量サマリ) + 詳細 (選択/小規模) の 2 段。ゲーム規模でも一覧が破綻しない。
    private readonly JsonPanel _ecsSummary = new(250f), _ecs = new(250f);
    private readonly JsonPanel _res = new(), _surf = new(), _input = new(), _audio = new(), _gpu = new();

    // Render Graph は読み取り専用のノードグラフで可視化 (パス=ノード / リソース依存=辺)
    private readonly NodeGraphView _graphView;
    private string? _lastGraph;

    // ---- Stat ダッシュボード (カード + グラフ) ----
    private readonly Signal<IReadOnlyList<string>> _statItems = new([]);
    private readonly Signal<IReadOnlyList<string>> _phasesItems = new([]);
    private readonly Signal<IReadOnlyList<string>> _gameItems = new([]);   // ゲーム側 DevStats key-value
    private readonly ListView _statList;     // flush/engine key-value
    private readonly ListView _phasesList;   // perf phases/systems
    private readonly ListView _gameList;     // DevStats (Game セクション)
    private string? _lastCustom;
    private readonly Sparkline _fpsSpark = Sparkline(385, 64);
    private readonly Sparkline _msSpark = Sparkline(385, 48, bars: true);
    private readonly Sparkline _memSpark = Sparkline(385, 64);
    private readonly Signal<string> _perfText = new("fps —");
    private readonly Signal<string> _memText = new("mem —");
    private readonly Signal<string> _gcText = new("gc —");
    private readonly List<float> _fpsHist = new(), _msHist = new(), _memHist = new();
    private const int HistCap = 120;   // ~20 秒分 (160ms 毎)

    private long _frame;
    private long _logCursor;
    private readonly List<string> _logLines = new();
    private string? _lastTrees, _lastStat, _lastPerf, _lastRuntime;

    public DevToolsUi(DevToolsListener listener, EngineCommands engine, Signal<Theme> theme)
    {
        _listener = listener;
        _engine = engine;
        _theme = theme;
        _treesList = ListView(470f, 15f, items: _treesItems, width: 810f);
        _logList = ListView(470f, 15f, items: _logItems, width: 810f);
        _statList = ListView(150f, 15f, items: _statItems, width: 385f);
        _phasesList = ListView(150f, 15f, items: _phasesItems, width: 385f);
        _gameList = ListView(150f, 15f, items: _gameItems, width: 385f);
        _graphView = NodeGraphView(source: NodeGraphDoc.Empty, viewWidth: 820f, viewHeight: 500f);
        _graphView.ReadOnly = true;
    }

    /// <summary>島スレッドの毎フレーム同期 (rev ポーリング → パネル更新)。</summary>
    public void Update()
    {
        UpdateFrame();
        if (++_frame % 10 != 0) return;   // 残りは ~160ms 毎

        try
        {
            using JsonDocument poll = JsonDocument.Parse(_listener.BuildPoll(_logCursor));
            JsonElement root = poll.RootElement;

            // Log (入力イベント LogRing)
            if (root.TryGetProperty("logs", out JsonElement logs) && logs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in logs.EnumerateArray())
                {
                    string op = e.TryGetProperty("op", out var o) ? o.GetString() ?? "" : "";
                    string info = e.TryGetProperty("info", out var inf) ? inf.GetString() ?? "" : "";
                    _logLines.Insert(0, $"{op}  {info}");   // 新しい順
                }
                if (_logLines.Count > 300) _logLines.RemoveRange(300, _logLines.Count - 300);
            }
            if (root.TryGetProperty("cursor", out JsonElement cur) && cur.TryGetInt64(out long c) && c != _logCursor)
            {
                _logCursor = c;
                _logItems.Value = _logLines.ToArray();
            }

            // Engine/Flush カード: paused + flush の key-value
            var stat = new List<string>();
            if (root.TryGetProperty("paused", out JsonElement paused))
                stat.Add($"paused: {paused}");
            if (root.TryGetProperty("stat", out JsonElement st) && st.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in st.EnumerateObject())
                    stat.Add($"flush.{p.Name}: {p.Value}");
            string statKey = string.Join("|", stat);
            if (statKey != _lastStat) { _lastStat = statKey; _statItems.Value = stat.ToArray(); }

            UpdatePerf();
            UpdateRuntime();

            _status.Value = $"frame rev={_frameRev}  log={_logCursor}";
        }
        catch { /* JSON 形状の揺れは無視 */ }

        // Trees (30f 毎 emit) — JSON が変わったときだけ行を作り直す
        string? trees = _listener.GetTrees();
        if (trees is not null && trees != _lastTrees)
        {
            _lastTrees = trees;
            _treesItems.Value = ParseTrees(trees);
        }

        UpdateGameStats();

        // JSON 系パネル (内容変化時のみ items 更新)
        _ecsSummary.Update(_listener.GetEcsSummary());
        _ecs.Update(_listener.GetEcs());
        _res.Update(_listener.GetResources());
        _surf.Update(_listener.GetSurfaces());
        _input.Update(_listener.GetInputState());
        _audio.Update(_listener.GetAudio());
        _gpu.Update(_listener.GetGpu());
        UpdateRenderGraph();
    }

    // レンダーグラフ JSON が変わったら DiagRenderGraph に復元 → ノードグラフへ変換して読み取り専用ビューへ
    private void UpdateRenderGraph()
    {
        string? json = _listener.GetRenderGraph();
        if (json is null || json == _lastGraph) return;
        _lastGraph = json;
        try
        {
            DiagRenderGraph? rg = JsonSerializer.Deserialize<DiagRenderGraph>(json, Json.Options);
            if (rg is not null) _graphView.Load(RenderGraphNodes.Build(rg));
        }
        catch { /* JSON 形状の揺れは無視 */ }
    }

    /// <summary>Perf カード: fps/frameMs 履歴グラフ + phase/system 内訳。</summary>
    private void UpdatePerf()
    {
        string? json = _listener.GetPerf();
        if (json is null || json == _lastPerf) return;
        _lastPerf = json;
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        float fps = r.TryGetProperty("fpsAvg", out var f) ? (float)f.GetDouble() : 0;
        float ms = r.TryGetProperty("frameMs", out var m) ? (float)m.GetDouble() : 0;
        Push(_fpsHist, fps);
        Push(_msHist, ms);
        _fpsSpark.SetValues(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_fpsHist), min: 0);
        _msSpark.SetValues(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_msHist), min: 0);
        _perfText.Value = $"fps {fps:0.0}   frame {ms:0.00} ms";

        var lines = new List<string>();
        if (r.TryGetProperty("fixed", out JsonElement fx) && fx.ValueKind == JsonValueKind.Object)
            lines.Add($"FixedUpdate: {fx.GetProperty("stepsThisFrame").GetInt32()} step/f  α {fx.GetProperty("alpha").GetDouble():0.00}"
                    + $"  accum {fx.GetProperty("accumulatorMs").GetDouble():0.00}ms  dropped {fx.GetProperty("droppedTotal").GetInt64()}");
        if (r.TryGetProperty("phases", out JsonElement phases) && phases.ValueKind == JsonValueKind.Array)
            foreach (JsonElement p in phases.EnumerateArray())
                lines.Add($"{p.GetProperty("name").GetString()}: {p.GetProperty("ms").GetDouble():0.000} ms");
        if (r.TryGetProperty("systems", out JsonElement systems) && systems.ValueKind == JsonValueKind.Array)
            foreach (JsonElement s in systems.EnumerateArray())
                lines.Add($"  {s.GetProperty("phase").GetString()}/{s.GetProperty("name").GetString()}: {s.GetProperty("ms").GetDouble():0.000}");
        _phasesItems.Value = lines;
    }

    /// <summary>Runtime カード: マネージヒープ履歴グラフ + GC/スレッド。</summary>
    private void UpdateRuntime()
    {
        string? json = _listener.GetRuntime();
        if (json is null || json == _lastRuntime) return;
        _lastRuntime = json;
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        float mb = r.TryGetProperty("totalMemoryBytes", out var t) ? t.GetInt64() / (1024f * 1024f) : 0;
        Push(_memHist, mb);
        _memSpark.SetValues(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_memHist));
        _memText.Value = $"heap {mb:0.0} MB   ws {(r.TryGetProperty("workingSetMB", out var w) ? w.GetInt32() : 0)} MB";
        _gcText.Value = $"gc {Get("gen0Collections")}/{Get("gen1Collections")}/{Get("gen2Collections")}   threads {Get("threadCount")}";
        string Get(string name) => r.TryGetProperty(name, out var v) ? v.ToString() : "?";
    }

    /// <summary>Game カード: ゲーム側 DevStats (key-value) を "key: value" 行で表示。</summary>
    private void UpdateGameStats()
    {
        string? json = _listener.GetCustom();
        if (json is null || json == _lastCustom) return;
        _lastCustom = json;
        var lines = new List<string>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("stats", out JsonElement stats) && stats.ValueKind == JsonValueKind.Array)
                foreach (JsonElement s in stats.EnumerateArray())
                    lines.Add($"{s.GetProperty("key").GetString()}: {s.GetProperty("value").GetString()}");
        }
        catch { /* JSON 形状の揺れは無視 */ }
        if (lines.Count == 0) lines.Add("(no game stats — DevStats.Set)");
        _gameItems.Value = lines;
    }

    private static void Push(List<float> hist, float v)
    {
        hist.Add(v);
        if (hist.Count > HistCap) hist.RemoveAt(0);
    }

    private void UpdateFrame()
    {
        if (_frameSource < 0)
        {
            // main フレーム: 使い回しバッファへ詰めて読む (新フレーム時のみ true) — 島スレッドで 2MB/frame を確保しない
            if (_listener.GetFrameInto(ref _frameBuf, out int len, out long rev, _frameRev < 0 ? null : _frameRev) && len > 0)
            {
                _frameRev = rev;
                Show(_frameBuf);
            }
            // メイン frame を emit しないアプリ (Framework 系) は UiFrame 0 へ自動フォールバック
            else if (_listener.FrameRev == 0 && _frame % 10 == 0 && _listener.GetUiFrame(0) is { } uf && !ReferenceEquals(uf, _lastUiFrame))
            {
                _lastUiFrame = uf;
                Show(uf);
            }
        }
        else if (_listener.GetUiFrame(_frameSource) is { } uf && !ReferenceEquals(uf, _lastUiFrame))
        {
            _lastUiFrame = uf;
            Show(uf);
        }

        void Show(byte[] body)
        {
            int w = BitConverter.ToInt32(body, 0), h = BitConverter.ToInt32(body, 4);
            _frameView.SetPixels(w, h, body.AsSpan(8));
        }
    }

    /// <summary>Frame ソースを 巡回切替 (main → UiFrame 0..N → main)。</summary>
    private void CycleFrameSource(int dir)
    {
        (int Index, string Name)[] list = _listener.GetUiFrameList();
        // 候補: -1 (main) + 各 index
        var all = new List<int> { -1 };
        all.AddRange(list.Select(t => t.Index));
        int pos = all.IndexOf(_frameSource);
        pos = ((pos < 0 ? 0 : pos) + dir + all.Count) % all.Count;
        _frameSource = all[pos];
        _lastUiFrame = null;   // 強制再表示
        _frameRev = -1;
        _frameSrcLabel.Value = _frameSource < 0
            ? "main"
            : $"{list.FirstOrDefault(t => t.Index == _frameSource).Name} (#{_frameSource})";
    }

    /// <summary>DebugTreeSet JSON → インデント行 (合成ツリー表示 — ListView で足りる)。</summary>
    private static string[] ParseTrees(string json)
    {
        var lines = new List<string>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("trees", out JsonElement arr))
                foreach (JsonElement t in arr.EnumerateArray())
                {
                    string name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    lines.Add($"■ {name}");
                    if (t.TryGetProperty("root", out JsonElement rootNode) && rootNode.ValueKind == JsonValueKind.Object)
                        Walk(rootNode, 1);

                    void Walk(JsonElement node, int depth)
                    {
                        string type = node.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
                        string detail = node.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
                            ? $"  '{d.GetString()}'" : "";
                        lines.Add($"{new string(' ', depth * 2)}{type}{detail}");
                        if (node.TryGetProperty("children", out JsonElement ch))
                            foreach (JsonElement c in ch.EnumerateArray()) Walk(c, depth + 1);
                    }
                }
        }
        catch { lines.Add("(parse error)"); }
        return lines.ToArray();
    }

    /// <summary>任意 JSON をインデント行へフラット化して ListView に出す汎用パネル。</summary>
    private sealed class JsonPanel
    {
        private readonly Signal<IReadOnlyList<string>> _items = new([]);
        public readonly ListView List;
        private string? _last;

        public JsonPanel(float height = 470f) => List = ListView(height, 15f, items: _items, width: 810f);

        public void Update(string? json)
        {
            if (json is null || json == _last) return;
            _last = json;
            var lines = new List<string>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                Flatten(doc.RootElement, "", 0, lines);
            }
            catch { lines.Add("(parse error)"); }
            if (lines.Count == 0) lines.Add("(empty)");
            _items.Value = lines.ToArray();
        }

        private static void Flatten(JsonElement e, string key, int depth, List<string> into)
        {
            string ind = new(' ', depth * 2);
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    if (key.Length > 0) into.Add($"{ind}{key}:");
                    foreach (JsonProperty p in e.EnumerateObject())
                        Flatten(p.Value, p.Name, key.Length > 0 ? depth + 1 : depth, into);
                    break;
                case JsonValueKind.Array:
                    {
                        into.Add($"{ind}{key}[{e.GetArrayLength()}]");
                        int i = 0;
                        foreach (JsonElement item in e.EnumerateArray())
                        {
                            Flatten(item, $"[{i++}]", depth + 1, into);
                            if (i >= 200) { into.Add($"{ind}  … (以降省略)"); break; }
                        }
                        break;
                    }
                default:
                    into.Add($"{ind}{key}: {e}");
                    break;
            }
        }
    }

    public Widget BuildRoot()
    {
        Widget Title(string t) => Text(t, 15, color: Bind.From(() => _theme.Value.Text));
        Widget Muted(string t, float size = 11) => Text(t, size, color: Bind.From(() => _theme.Value.TextMuted));

        // カード合成 (島テーマ — Kit.Card はグローバル既定テーマ購読なので使わない)
        Widget Card(string title, Widget body) =>
            Border(background: Bind.From(() => _theme.Value.Surface), rounded: 6f, padding: new Thickness(10, 8))[
                VStack(6)[Muted(title, 12), body]];

        Widget framePanel = Card("Frame", VStack(4)[
            HStack(6)[
                Button(_ => CycleFrameSource(-1), "◀"),
                Text($"{_frameSrcLabel}", 12, color: Bind.From(() => _theme.Value.TextMuted), width: 220),
                Button(_ => CycleFrameSource(+1), "▶")],
            _frameView]);

        // Stat = ダッシュボード (WrapPanel にカードを流し込み — 幅で 2 列に折り返す)
        Widget statPanel = Wrap(10, 10, width: 845f)[
            Card("Perf", VStack(5)[
                Text($"{_perfText}", 12, color: Bind.From(() => _theme.Value.Text)),
                _fpsSpark,
                Muted("frame ms", 10),
                _msSpark]),
            Card("Runtime", VStack(5)[
                Text($"{_memText}", 12, color: Bind.From(() => _theme.Value.Text)),
                _memSpark,
                Text($"{_gcText}", 11, color: Bind.From(() => _theme.Value.TextMuted))]),
            Card("Engine / Flush", _statList),
            Card("Phases / Systems", _phasesList),
            Card("Game (DevStats)", _gameList)];

        Tabs tabs = Tabs(
            ["Frame", "Trees", "Log", "Stat", "ECS", "Res", "Surf", "Input", "Audio", "GPU", "Graph"],
            [
                framePanel,
                Card("UI Trees", _treesList),
                Card("Input Log", _logList),
                statPanel,
                Card("ECS", VStack(6)[
                    Muted("一覧 (id · 名前 · アーキタイプ、値なし)", 10), _ecsSummary.List,
                    Muted("詳細 (選択 entity / 小規模フォールバック)", 10), _ecs.List]),
                Card("Resources", _res.List),
                Card("Surfaces", _surf.List),
                Card("Input State", _input.List),
                Card("Audio", _audio.List),
                Card("GPU", _gpu.List),
                Card("Render Graph", _graphView),
            ],
            _tab, width: 860f, height: 590f);

        return Border(background: Bind.From(() => _theme.Value.Background), padding: new Thickness(10))[
            VStack(6)[
                HStack(10)[
                    Title("Luxel DevTools"),
                    Button(_ => _engine.Enqueue("engine.pause", null), "pause"),
                    Button(_ => _engine.Enqueue("engine.resume", null), "resume"),
                    Button(_ => _engine.Enqueue("engine.step", null), "step"),
                    Text($"{_status}", 11, color: Bind.From(() => _theme.Value.TextMuted))],
                tabs]];
    }
}
