using System.Text.Json;
using Luxel;
using Luxel.Diagnostics;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// 選択中ストーリー 1 つを offscreen で実体化・描画するホスト。
/// ループ (ギャラリースレッド) = <see cref="Step"/>: コマンド適用 → Tick → 描画 → フレーム更新。
/// ブラウザからの操作は <see cref="EngineCommands"/> 経由 (単一書込者)。
/// </summary>
public sealed class GalleryHost : IDisposable
{
    private readonly GpuDevice _device;
    private readonly VectorFont _font;
    private readonly Rasterizer2D _raster;   // パイプライン生成が重いので device で 1 個を使い回す
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
    private GpuBuffer? _fb;
    private static readonly JsonSerializerOptions TreeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private int _w, _h;
    private bool _dark;

    // 最新フレーム (8B ヘッダ w,h LE + RGBA)。rev は内容変化時のみ進む
    private readonly object _frameGate = new();
    private byte[]? _frame;
    private long _frameRev;
    private ulong _frameHash;

    public GalleryHost(GpuDevice device, VectorFont font)
    {
        _device = device;
        _font = font;
        _raster = new Rasterizer2D(device);
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

    public string? CurrentId => _story?.Path;
    public (int w, int h) Size => (_w, _h);
    public bool Dark => _dark;

    /// <summary>選択中ストーリーの canvas/host (bench の統計読み取り/直接入力用)。</summary>
    internal RetainedCanvas? Canvas => _canvas;
    internal UiHost? Host => _host;

    /// <summary>ストーリーを選択して実体化する (既存は破棄)。ギャラリースレッドから呼ぶ。</summary>
    public void Select(string path)
    {
        StoryInfo? story = StoryRegistry.Find(path);
        if (story is null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TearDown();
        _story = story;
        // fill (W/H 未指定 = 0,0) は snap/bench の決定性のため 800×480 固定で描く
        _w = story.Width > 0 ? story.Width : (int)GalleryApp.PreviewW;
        _h = story.Height > 0 ? story.Height : (int)GalleryApp.PreviewH;
        if (story.Theme is not null) _dark = story.Theme == "dark";
        ApplyTheme();
        BuildCurrent();
        Console.WriteLine($"[gallery] select '{path}' {sw.ElapsedMilliseconds}ms");
    }

    private void BuildCurrent()
    {
        if (_story is null) return;
        _canvas = new RetainedCanvas(_raster);
        _host = new UiHost(_canvas, _font, _w, _h);
        UiHostCommands.RegisterDefaults(Commands, _host);   // click/pointermove/key/char/... (同名上書き)
        _ctx = new StoryContext(_resources);
        // 遷移はコマンドキュー経由 — 入力ディスパッチ中の即時 TearDown を避ける (次の Drain で適用)
        _ctx.SetNavigator(p => Commands.Enqueue("story.select", JsonSerializer.SerializeToElement(new { id = p })));
        _root = _story.Build(_ctx);
        _host.SetRoot(_root);
        _fb = _device.Malloc((ulong)(_w * _h * 4), GpuMemoryKind.HostMapped);
        _frameHash = 0;   // 次の Render で必ず配信
        Render();         // 選択/リサイズ直後から新フレームを配信する
    }

    private void Resize(int w, int h)
    {
        if (_story is null || w < 16 || h < 16 || (w == _w && h == _h)) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _w = w; _h = h;
        TearDownCanvasOnly();
        BuildCurrent();
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
        if (_host is null || _canvas is null || _fb is null) return;
        _host.Tick(dt);
        if (_frame is not null && !_canvas.HasPendingChanges) return;   // 前回と同じ絵になるだけ
        Render();
    }

    private void Render()
    {
        using (GpuCommandBuffer cmd = _device.MainQueue.StartCommandRecording())
        {
            _canvas!.Render(cmd, Camera2D.Pixels, (uint)_w, (uint)_h, _fb!);
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
        }

        // host-mapped (write-combined) メモリは CPU 読み取りが非キャッシュで極端に遅いので、
        // 一括コピー 1 回でキャッシュ可能メモリへ移してからハッシュする (バイト毎の直接読みは 100ms 級)。
        int len = _w * _h * 4;
        byte[] body = new byte[8 + len];
        BitConverter.TryWriteBytes(body.AsSpan(0, 4), _w);
        BitConverter.TryWriteBytes(body.AsSpan(4, 4), _h);
        _fb!.Span<byte>(len).CopyTo(body.AsSpan(8));

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
        if (_fb is null) return null;
        return (_fb.Span<byte>(_w * _h * 4).ToArray(), _w, _h);
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
        _fb?.Dispose(); _fb = null;
        _host?.Dispose(); _host = null;
        _root = null;
        _canvas?.Dispose(); _canvas = null;
    }

    private void TearDown()
    {
        TearDownCanvasOnly();
        _story = null; _ctx = null;
    }

    public void Dispose()
    {
        TearDown();
        _resources.Dispose();
        _raster.Dispose();
    }
}
