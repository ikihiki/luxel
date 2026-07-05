using System.Buffers.Binary;
using System.Diagnostics;
using Luxel.Diagnostics;
using Luxel.UI;

namespace Luxel.DevTools;

/// <summary>
/// エンジンの <see cref="DiagnosticListener"/> ("Luxel") を購読するリスナー。
/// 高頻度イベント (frame/tree/stat) は「最新のみ保持」(coalesce) し、離散イベント (input/ime) は
/// ログリングへ追記する。Web からの操作は <see cref="EngineCommands"/> へ Enqueue する。
/// エンジンを一切参照させず、診断イベントと操作レジストリだけで結合する (疎結合)。
/// </summary>
public sealed class DevToolsListener : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly EngineCommands _commands;
    private readonly List<IDisposable> _subs = new();
    private IDisposable? _allListenersSub;

    // coalesce: 最新のみ保持
    private readonly LatestSlot<byte[]> _frame = new();   // HTTP body (8B ヘッダ + RGBA)
    private readonly LatestSlot<string> _tree = new();     // JSON
    private readonly LatestSlot<string> _primitives = new(); // JSON (最終 2D SoA, オンデマンド取得)
    private readonly LatestSlot<string> _gpu = new();        // JSON (GPU 発行コマンド, オンデマンド取得)
    private readonly LatestSlot<string> _resources = new();  // JSON (リソース ロードグラフ, オンデマンド取得)
    private readonly LatestSlot<string> _renderGraph = new(); // JSON (レンダーグラフ Compile 後, オンデマンド取得)
    private readonly LatestSlot<string> _perf = new();         // JSON (per-frame timing、最新のみ)
    private readonly LatestSlot<string> _ecs = new();          // JSON (world × entity、on-demand)
    private readonly LatestSlot<string> _surfaces = new();     // JSON (UiSurface 群、on-demand)
    private readonly LatestSlot<string> _inputState = new();   // JSON (InputStack スナップショット、per-frame)
    private readonly LatestSlot<string> _audio = new();        // JSON (AudioRegistry snapshot、30f/1 回)
    private readonly LatestSlot<string> _runtime = new();      // JSON (.NET GC/Memory/Threads、30f/1 回)
    private readonly LatestSlot<string> _trees = new();        // JSON (複数 UI tree bundle、30f/1 回)
    // per-index UI frame: index → (name, rgba body: 8B header + RGBA)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (string Name, byte[] Body)> _uiFrames = new();
    private volatile bool _paused;                              // engine paused フラグ (poll に含める)
    private volatile StatDto? _stat;
    private string? _lastTreeJson;
    private readonly object _treeLock = new();

    // stream: ログ
    private readonly LogRing _log = new(512);

    public DevToolsListener(EngineCommands commands)
    {
        _commands = commands;
        _allListenersSub = DiagnosticListener.AllListeners.Subscribe(this);
    }

    // ---- IObserver<DiagnosticListener> (AllListeners) ----
    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener dl)
    {
        if (dl.Name == EngineDiagnostics.SourceName)
            _subs.Add(dl.Subscribe(this, (Predicate<string>)(_ => true)));   // 全イベント有効化
    }
    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }

    // ---- IObserver<KeyValuePair> (イベント本体) ----
    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> kv)
    {
        switch (kv.Key)
        {
            case EngineDiagnostics.Input when kv.Value is DiagInput di:
                _log.Add(di.Op, di.Info);
                break;
            case EngineDiagnostics.RenderFlush when kv.Value is DiagFlush f:
                _stat = new StatDto(f.TransformWrites, f.StyleWrites, f.SegmentBytes, f.FullRebuild, (int)f.Width, (int)f.Height);
                break;
            case EngineDiagnostics.Tree when kv.Value is DebugNode root:
                PublishTree(root);
                break;
            case EngineDiagnostics.Frame when kv.Value is DiagFrame fr:
                PublishFrame(fr);
                break;
            case EngineDiagnostics.Primitives when kv.Value is DiagPrimitives prim:
                _primitives.Publish(Json.Serialize(prim));
                break;
            case EngineDiagnostics.Gpu when kv.Value is DiagGpu gpu:
                _gpu.Publish(Json.Serialize(gpu));
                break;
            case EngineDiagnostics.Resources when kv.Value is DiagResources res:
                _resources.Publish(Json.Serialize(res));
                break;
            case EngineDiagnostics.RenderGraph when kv.Value is DiagRenderGraph rg:
                _renderGraph.Publish(Json.Serialize(rg));
                break;
            case EngineDiagnostics.Perf when kv.Value is DiagPerf perf:
                _perf.Publish(Json.Serialize(perf));
                break;
            case EngineDiagnostics.Ecs when kv.Value is DiagEcs ecs:
                _ecs.Publish(Json.Serialize(ecs));
                break;
            case EngineDiagnostics.Surfaces when kv.Value is DiagSurfaces surf:
                _surfaces.Publish(Json.Serialize(surf));
                break;
            case EngineDiagnostics.InputState when kv.Value is DiagInputState inp:
                _inputState.Publish(Json.Serialize(inp));
                break;
            case EngineDiagnostics.EngineState when kv.Value is DiagEngineState es:
                _paused = es.Paused;
                break;
            case EngineDiagnostics.Audio when kv.Value is DiagAudio au:
                _audio.Publish(Json.Serialize(au));
                break;
            case EngineDiagnostics.Runtime when kv.Value is DiagRuntime rt:
                _runtime.Publish(Json.Serialize(rt));
                break;
            case EngineDiagnostics.Trees when kv.Value is Luxel.UI.DebugTreeSet ts:
                _trees.Publish(Json.Serialize(ts));
                break;
            case EngineDiagnostics.UiFrame when kv.Value is DiagUiFrame uf:
                {
                    byte[] body = new byte[8 + uf.Rgba.Length];
                    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0), uf.Width);
                    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), uf.Height);
                    uf.Rgba.CopyTo(body, 8);
                    _uiFrames[uf.Index] = (uf.Name, body);
                }
                break;
        }
    }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

    private void PublishTree(DebugNode root)
    {
        string json = Json.Serialize(root);
        lock (_treeLock)
        {
            if (json == _lastTreeJson) return;   // 内容不変なら rev を進めない (再送回避)
            _lastTreeJson = json;
        }
        _tree.Publish(json);
    }

    private void PublishFrame(DiagFrame fr)
    {
        byte[] body = new byte[8 + fr.Rgba.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0), fr.Width);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), fr.Height);
        fr.Rgba.CopyTo(body, 8);
        _frame.Publish(body);
    }

    // ---- サーバ向けデータアクセス (読み取り専用) ----

    public string BuildPoll(long logSince)
    {
        (LogEntry[] logs, long cursor) = _log.Since(logSince);
        return Json.Serialize(new PollResponse(_frame.Rev, _tree.Rev, _perf.Rev, _ecs.Rev, _surfaces.Rev, _inputState.Rev, _paused, _stat, logs, cursor));
    }

    /// <summary>最新フレーム body と rev。<paramref name="sinceRev"/> と同一なら null (= 304)。</summary>
    public (byte[]? body, long rev) GetFrame(long? sinceRev)
    {
        (byte[]? body, long rev) = _frame.Read();
        if (body == null) return (null, rev);
        if (sinceRev is long s && s == rev) return (null, rev);
        return (body, rev);
    }

    /// <summary>最新ツリー JSON と rev。<paramref name="sinceRev"/> と同一なら null (= 304)。</summary>
    public (string? json, long rev) GetTree(long? sinceRev)
    {
        (string? json, long rev) = _tree.Read();
        if (json == null) return (null, rev);
        if (sinceRev is long s && s == rev) return (null, rev);
        return (json, rev);
    }

    /// <summary>最新の最終 2D プリミティブ JSON (ボタンでオンデマンド取得)。</summary>
    public string? GetPrimitives() => _primitives.Read().value;
    /// <summary>最新の GPU 発行コマンド JSON (ボタンでオンデマンド取得)。</summary>
    public string? GetGpu() => _gpu.Read().value;
    /// <summary>最新のリソース ロードグラフ JSON (ボタンでオンデマンド取得)。</summary>
    public string? GetResources() => _resources.Read().value;
    /// <summary>最新のレンダーグラフ DAG JSON (ボタンでオンデマンド取得)。</summary>
    public string? GetRenderGraph() => _renderGraph.Read().value;
    /// <summary>最新の per-frame 性能内訳 JSON。</summary>
    public string? GetPerf() => _perf.Read().value;
    /// <summary>最新の ECS スナップショット JSON。</summary>
    public string? GetEcs() => _ecs.Read().value;
    /// <summary>最新の UiSurface スナップショット JSON。</summary>
    public string? GetSurfaces() => _surfaces.Read().value;
    /// <summary>最新の InputStack スナップショット JSON。</summary>
    public string? GetInputState() => _inputState.Read().value;
    /// <summary>最新の Audio スナップショット JSON (bus 階層 + source 状態)。</summary>
    public string? GetAudio() => _audio.Read().value;
    /// <summary>最新のランタイムメトリクス JSON (GC / Memory / Threads)。</summary>
    public string? GetRuntime() => _runtime.Read().value;
    /// <summary>最新の UI trees bundle JSON (複数 UiHost 分)。</summary>
    public string? GetTrees() => _trees.Read().value;
    /// <summary>index 番目の UiHost フレーム画像 (8B ヘッダ + RGBA)。存在しなければ null。</summary>
    public byte[]? GetUiFrame(int index) => _uiFrames.TryGetValue(index, out var v) ? v.Body : null;

    /// <summary>受信済み UiFrame の (index, 名前) 一覧 (ネイティブ DevTools のソース切替用)。</summary>
    public (int Index, string Name)[] GetUiFrameList()
        => _uiFrames.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Name)).ToArray();

    /// <summary>Web からの操作をキューへ (app スレッドの Drain で適用)。</summary>
    public void EnqueueCommand(string name, object? arg) => _commands.Enqueue(name, arg);

    public void Dispose()
    {
        foreach (IDisposable s in _subs) s.Dispose();
        _subs.Clear();
        _allListenersSub?.Dispose();
        _allListenersSub = null;
    }
}
