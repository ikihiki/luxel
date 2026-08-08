using System.Text.Json;
using Luxel;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.AssetsGpu;
using Luxel.Diagnostics;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// 選択中ストーリー 1 つを offscreen で実体化・描画するホスト。
/// ループ (ギャラリースレッド) = <see cref="Step"/>: コマンド適用 → Tick → 描画 → フレーム更新。
/// ブラウザからの操作は <see cref="EngineCommands"/> 経由 (単一書込者)。
/// </summary>
public sealed class GalleryHost : IDisposable
{
    private readonly StoryCatalog? _catalog;
    private readonly GpuDevice? _device;
    private readonly VectorFont _font;
    private readonly IRasterizer2D _raster;
    private readonly GpuDeviceRasterizer2D? _gpuRasterizer;
    private readonly AssetGpuInstallation? _assetGpuInstallation;
    private readonly GallerySlangCompilation? _slangCompilation;
    // ストーリーへ StoryContext.Resources として配布 (キャッシュはストーリー横断で共有、Pump は Step が叩く)
    private readonly Luxel.Resources.ResourceSystem _resources = new(
        sources: Luxel.Resources.ResourceSystemDefaults.BuiltinSources(assetRoot: Environment.CurrentDirectory),
        steps: [.. Luxel.Resources.ResourceSystemDefaults.BuiltinSteps(), new Luxel.Imaging.ImageSharpDecoder()]);
    public EngineCommands Commands { get; } = new();

    // 選択中ストーリーの実体 (Select で作り直す)
    private StoryInfo? _story;
    private StoryContext? _ctx;
    private RetainedCanvas? _canvas;
    private UiHost? _host;
    private Widget? _root;
    private IRasterScene2D? _rasterScene;
    private IRasterTarget2D? _rasterTarget;
    private GpuBuffer? _gpuFramebuffer;
    private static readonly JsonSerializerOptions TreeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private int _w, _h;
    private bool _dark;
    private readonly bool _publishFrames;
    private bool _hasRenderedFrame;
    private bool _disposed;

    // 最新フレーム (8B ヘッダ w,h LE + RGBA)。rev は内容変化時のみ進む
    private readonly object _frameGate = new();
    private byte[]? _frame;
    private long _frameRev;
    private ulong _frameHash;

    public GalleryHost(GpuDevice device, VectorFont font, StoryCatalog? catalog = null, bool publishFrames = true)
        : this(new GpuDeviceRasterizer2D(device), font, device, catalog, publishFrames) { }

    public GalleryHost(IRasterizer2D rasterizer, VectorFont font, StoryCatalog? catalog = null, bool publishFrames = true)
        : this(rasterizer, font, rasterizer is GpuDeviceRasterizer2D gpu ? gpu.Device : null, catalog, publishFrames) { }

    private GalleryHost(IRasterizer2D rasterizer, VectorFont font, GpuDevice? device, StoryCatalog? catalog, bool publishFrames)
    {
        _catalog = catalog;
        _device = device;
        _font = font;
        _publishFrames = publishFrames;
        _raster = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _gpuRasterizer = rasterizer as GpuDeviceRasterizer2D;
        _resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        if (device is not null)
        {
            _slangCompilation = new GallerySlangCompilation();
            _slangCompilation.Install(_resources, device.BackendKind);
            _assetGpuInstallation = _resources.InstallAssetGpuLifecycle(device);
        }
        Commands.Register("story.select", a => { if (a is JsonElement el && el.TryGetProperty("id", out JsonElement id)) Select(id.GetString() ?? ""); });
        Commands.Register("story.theme", a => { _dark = a is JsonElement el && el.TryGetProperty("dark", out JsonElement d) && d.ValueKind == JsonValueKind.True; ApplyTheme(); });
        Commands.Register("story.state", a => SetState(a));
        Commands.Register("story.resize", a =>
        {
            if (a is not JsonElement el) return;
            int w = el.TryGetProperty("w", out JsonElement we) && we.TryGetInt32(out int wi) ? wi : _w;
            int h = el.TryGetProperty("h", out JsonElement he) && he.TryGetInt32(out int hi) ? hi : _h;
            Resize(w, h);
        });
        Commands.Register("story.knob", a =>
        {
            if (a is not JsonElement el || _ctx is null) return;
            string name = el.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
            if (!el.TryGetProperty("value", out JsonElement v)) return;
            foreach (StoryKnob k in _ctx.Knobs)
                if (k.Name == name) { k.Set(v); break; }
        });
        // ui.set は UiHost の購読 (Luxel.UiSetRequest) 経由で配送する (DevTools と同じ経路)
        Commands.Register("ui.set", a => { if (a is JsonElement el) EngineDiagnostics.Emit("Luxel.UiSetRequest", el); });
    }

    internal int StorySelectionCount { get; private set; }
    public string? CurrentId => _story?.Path;
    public (int w, int h) Size => (_w, _h);
    public bool Dark => _dark;

    /// <summary>選択中ストーリーの canvas/host (bench の統計読み取り/直接入力用)。</summary>
    internal RetainedCanvas? Canvas => _canvas;
    internal UiHost? Host => _host;
    /// <summary>選択中ストーリーの StoryContext (E2E ランナーが play を読む)。</summary>
    internal StoryContext? Context => _ctx;

    /// <summary>ストーリーを選択して実体化する (既存は破棄)。ギャラリースレッドから呼ぶ。</summary>
    public void Select(string path)
    {
        StoryInfo? story = FindStory(path);
        if (story is null) return;
        SelectCore(story, e2e: false);
    }

    /// <summary>Selects exactly one registered story or throws instead of silently retaining the previous story.</summary>
    public void SelectExact(string path)
        => SelectExact(FindStory(path)
            ?? throw new KeyNotFoundException($"Story not found: {path}"));

    /// <summary>Selects the supplied explicit-catalog story without consulting the global registry.</summary>
    public void SelectExact(StoryInfo story)
    {
        ArgumentNullException.ThrowIfNull(story);
        SelectForE2e(story);
    }

    public bool ContainsStory(string path) => FindStory(path) is not null;

    private StoryInfo? FindStory(string path) => _catalog?.Find(path) ?? StoryRegistry.Find(path);

    /// <summary>The currently realized widget tree. Intended for deterministic export/introspection.</summary>
    public Widget? CurrentRoot => _root;

    /// <summary>Realizes a standalone widget for deterministic documentation capture.</summary>
    public void SelectWidget(Widget widget, int width = 800, int height = 480, bool dark = false)
    {
        TearDown();
        try
        {
            _w = width;
            _h = height;
            _dark = dark;
            ApplyTheme();
            _canvas = new RetainedCanvas();
            _rasterScene = _raster.CreateScene(_canvas);
            _host = new UiHost(_canvas, _font, _w, _h, gpuRasterizer: _gpuRasterizer);
            UiHostCommands.RegisterDefaults(Commands, _host);
            _ctx = new StoryContext(_resources);
            _ctx.SetServices(GalleryServices.Provider);
            if (_device is not null) _ctx.SetGpuHost(_device, _font);
            _root = widget;
            _host.SetRoot(widget);
            CreateRasterTarget();
            _frameHash = 0;
            Render();
        }
        catch
        {
            TearDown();
            throw;
        }
    }

    internal void SelectForE2e(StoryInfo story) => SelectCore(story, e2e: true);

    private void SelectCore(StoryInfo story, bool e2e)
    {
        StorySelectionCount++;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TearDown();
        _story = story;
        // fill (W/H 未指定 = 0,0) は snap/bench の決定性のため 800×480 固定で描く
        _w = story.Width > 0 ? story.Width : 800;
        _h = story.Height > 0 ? story.Height : 480;
        if (e2e) _dark = string.Equals(story.Theme, "dark", StringComparison.OrdinalIgnoreCase);
        else if (story.Theme is not null) _dark = story.Theme == "dark";
        ApplyTheme();
        if (e2e) Stories.StrudelStory.ResetForE2e();
        try
        {
            BuildCurrent();
        }
        catch
        {
            TearDown();
            throw;
        }
        Console.WriteLine($"[gallery] select '{story.Path}' {sw.ElapsedMilliseconds}ms");
    }

    private void BuildCurrent()
    {
        if (_story is null) return;
        _canvas = new RetainedCanvas();
        _rasterScene = _raster.CreateScene(_canvas);
        _host = new UiHost(_canvas, _font, _w, _h, gpuRasterizer: _gpuRasterizer);
        UiHostCommands.RegisterDefaults(Commands, _host);   // click/pointermove/key/char/... (同名上書き)
        _ctx = new StoryContext(_resources);
        _ctx.SetServices(GalleryServices.Provider);   // DI: ScriptHost / ICodeLanguage をストーリー引数へ注入
        if (_device is not null) _ctx.SetGpuHost(_device, _font);
        // 遷移はコマンドキュー経由 — 入力ディスパッチ中の即時 TearDown を避ける (次の Drain で適用)
        _ctx.SetNavigator(p => Commands.Enqueue("story.select", JsonSerializer.SerializeToElement(new { id = p })));
        _root = _story.Build(_ctx);
        _host.SetRoot(_root);
        CreateRasterTarget();
        _frameHash = 0;   // 次の Render で必ず配信
        Render();         // 選択/リサイズ直後から新フレームを配信する
    }

    private void Resize(int w, int h)
    {
        if (_story is null || w < 16 || h < 16 || (w == _w && h == _h)) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _w = w; _h = h;
        TearDownStoryInstance();
        try
        {
            BuildCurrent();
        }
        catch
        {
            TearDown();
            throw;
        }
        Console.WriteLine($"[gallery] resize {w}x{h} {sw.ElapsedMilliseconds}ms");
    }

    private void ApplyTheme() => UiTheme.Current.Value = _dark ? Theme.Dark : Theme.Light;

    /// <summary>root widget の状態 signal を強制する ({state:"hover"|"pressed"|"focused"|"disabled", on:bool})。</summary>
    private void SetState(object? a)
    {
        if (a is not JsonElement el || _root is not Widget root) return;
        string state = el.TryGetProperty("state", out JsonElement s) ? s.GetString() ?? "" : "";
        bool on = el.TryGetProperty("on", out JsonElement o) && o.ValueKind == JsonValueKind.True;
        switch (state)
        {
            case "hover": ForEach(root, w => w.Hovered.Value = on); break;
            case "pressed": ForEach(root, w => w.Pressed.Value = on); break;
            case "focused": ForEach(root, w => w.Focused.Value = on); break;
            case "disabled": ForEach(root, w => w.Enabled = !on); RequestRebuildColors(root); break;
        }
    }

    private static void ForEach(Widget w, Action<Widget> f)
    {
        f(w);
        foreach (Widget c in w.DebugChildren()) ForEach(c, f);
    }

    /// <summary>Enabled は plain bool で signal 追跡されないため、状態 signal を揺らして Effect を再評価させる。</summary>
    private static void RequestRebuildColors(Widget root)
        => ForEach(root, w => { bool h = w.Hovered.Value; w.Hovered.Value = !h; w.Hovered.Value = h; });

    /// <summary>1 ステップ: コマンド適用 → アニメ Tick → 描画 → フレーム更新。ギャラリースレッドから毎フレーム呼ぶ。
    /// 変更がなければ描画自体をスキップする (静止シーンで raster/readback/ハッシュを払わない)。</summary>
    public void Step(float dt)
    {
        Commands.Drain();
        _resources.Pump();   // リロード/遅延 Dispose の処理 (初回ロードの publish は Pump 不要)
        _ctx?.PumpKnobEdits();   // Knobs テーブル (docs 埋め込み) の編集適用 (effect 文脈外)
        if (_host is null || _canvas is null || _rasterScene is null) return;
        _host.Tick(dt);
        if (_hasRenderedFrame && !_canvas.HasPendingChanges) return;   // 前回と同じ絵になるだけ
        Render();
    }

    private void Render()
    {
        if (_rasterScene is null) return;
        if (_gpuRasterizer is not null)
        {
            using GpuCommandBuffer cmd = _device!.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels,
                new GpuRasterTarget2D(cmd, _gpuFramebuffer!, (uint)_w, (uint)_h));
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
        }
        else if (_rasterTarget is not null)
        {
            _rasterScene.Render(Camera2D.Pixels, _rasterTarget);
        }
        else
        {
            throw new InvalidOperationException($"No target is available for {_raster.Name}.");
        }

        _hasRenderedFrame = true;
        if (!_publishFrames) return;

        int len = _w * _h * 4;
        byte[] body = new byte[8 + len];
        BitConverter.TryWriteBytes(body.AsSpan(0, 4), _w);
        BitConverter.TryWriteBytes(body.AsSpan(4, 4), _h);
        CopyPixels(body.AsSpan(8));

        // 内容ハッシュ (FNV-1a, ulong 単位 + 端数) — 不変フレームは rev を進めず 304 で済ませる
        ulong hash = 14695981039346656037ul;
        ReadOnlySpan<byte> data = body.AsSpan(8);
        foreach (ulong u in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(data))
        { hash ^= u; hash *= 1099511628211ul; }
        for (int i = data.Length - data.Length % 8; i < data.Length; i++)
        { hash ^= data[i]; hash *= 1099511628211ul; }
        if (hash == _frameHash) return;
        _frameHash = hash;

        lock (_frameGate) { _frame = body; _frameRev++; }
    }

    /// <summary>最後に描いたフレームの PNG 用生データ (スナップショット用)。</summary>
    public (byte[] rgba, int w, int h)? SnapshotRgba()
    {
        if (_gpuFramebuffer is null && _rasterTarget is null) return null;
        byte[] rgba = new byte[_w * _h * 4];
        CopyPixels(rgba);
        return (rgba, _w, _h);
    }

    private void CreateRasterTarget()
    {
        _gpuFramebuffer?.Dispose();
        _gpuFramebuffer = null;
        if (_gpuRasterizer is not null)
        {
            _gpuFramebuffer = _device!.Malloc((ulong)(_w * _h * 4), GpuMemoryKind.HostMapped);
            _rasterTarget = null; // GPU target owns a per-frame command buffer and is created in Render().
        }
        else if (_raster is SkiaRasterizer2D)
        {
            _rasterTarget = new SkiaRasterTarget2D((uint)_w, (uint)_h);
        }
        else
        {
            throw new NotSupportedException($"GalleryHost does not know how to create a target for {_raster.Name}.");
        }
    }

    private void CopyPixels(Span<byte> destination)
    {
        if (_rasterTarget is SkiaRasterTarget2D skia)
            skia.Pixels.Span.CopyTo(destination);
        else
            _gpuFramebuffer!.Span<byte>(_w * _h * 4).CopyTo(destination);
    }

    public (byte[]? body, long rev) GetFrame(long? sinceRev)
    {
        lock (_frameGate)
            return sinceRev.HasValue && sinceRev.Value == _frameRev ? (null, _frameRev) : (_frame, _frameRev);
    }

    /// <summary>knobs + ルート widget の DebugProps ツリーを JSON で返す (サーバスレッドから; 読み取りのみ)。</summary>
    public string BuildPropsJson()
    {
        var buf = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("story", _story?.Path ?? "");
            w.WriteBoolean("dark", _dark);
            w.WriteNumber("w", _w);
            w.WriteNumber("h", _h);
            w.WriteStartArray("knobs");
            if (_ctx is not null)
                foreach (StoryKnob k in _ctx.Knobs)
                {
                    w.WriteStartObject();
                    w.WriteString("name", k.Name);
                    w.WriteString("type", k.Type);
                    w.WriteString("value", k.Value);
                    w.WriteEndObject();
                }
            w.WriteEndArray();
            w.WriteStartArray("logs");
            if (_ctx is not null)
                foreach (StoryLogEntry e in _ctx.LogSnapshot())
                {
                    w.WriteStartObject();
                    w.WriteNumber("seq", e.Seq);
                    w.WriteString("t", e.Time);
                    w.WriteString("m", e.Message);
                    w.WriteEndObject();
                }
            w.WriteEndArray();
            w.WritePropertyName("tree");
            DebugNode? snap = _host?.DebugSnapshot();
            if (snap is null) w.WriteNullValue();
            else JsonSerializer.Serialize(w, snap, TreeJson);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buf.ToArray());
    }

    private void TearDownCanvasOnly()
    {
        _rasterScene?.Dispose(); _rasterScene = null;
        _gpuFramebuffer?.Dispose(); _gpuFramebuffer = null;
        _rasterTarget = null;
        _host?.Dispose(); _host = null;
        _root = null;
        _canvas?.Dispose(); _canvas = null;
    }

    private void TearDownStoryInstance()
    {
        // The realized UI may still hold resource handles, so release it before the context-owned scope.
        TearDownCanvasOnly();
        _ctx?.Dispose();
        _ctx = null;
    }

    private void TearDown()
    {
        TearDownStoryInstance();
        _story = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TearDown();
        // The host owns the AssetsGpu installation, but only borrows the device.
        // Wait for its queue before ResourceSystem disposes scoped GPU values.
        _assetGpuInstallation?.Dispose();
        _resources.Dispose();
        _slangCompilation?.Dispose();
        _raster.Dispose();
    }
}
