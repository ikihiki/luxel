using System.Text.Json;
using Luxel.Abstraction;
using Luxel.Diagnostics;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Platform;

/// <summary>
/// マルチウィンドウの駆動役: <see cref="WindowSystem"/> + ウィンドウ毎の <see cref="WindowHost"/> + オフスクリーン UI。
/// ゲーム型ループは <see cref="RunFrame"/> を毎フレーム呼べる。UI型ループは
/// <see cref="WaitForNextFrame"/> → <see cref="RunFrame"/> とし、静止中はOSイベント待機へ入る。
///
/// リモート制御は既存 DevTools に統合する:
///  - ウィンドウ一覧/ピクセル = <see cref="IWindowRemoteHost"/> 実装 → DebugServer の /windows, /winframe
///  - UI (ウィンドウと 1:1 ではない) = <see cref="UiRegistry"/> に登録 → /trees (Luxel.Trees を 30f 毎に emit)
///  - 操作 = 共有 <see cref="EngineCommands"/>: window.* (ここで登録) + UI 入力/ui.set
///    (<see cref="UiHostCommands.RegisterDefaults(EngineCommands, UiRegistry)"/>, "ui" index/名前でルーティング)
/// </summary>
public sealed class WindowManager : IWindowRemoteHost, IDisposable
{
    private readonly GpuDevice _device;
    private readonly VectorFont _font;
    private readonly Rasterizer2D _raster;   // パイプライン生成が重いので device で 1 個を共有
    private readonly WindowSystem _windows;
    private readonly object _gate = new();   // _hosts/_offscreen のスナップショット用 (server スレッドが読む)
    private readonly List<WindowHost> _hosts = new();
    private readonly List<IWindowContent> _offscreen = new();
    private readonly Dictionary<string, Func<Widget>> _contents = new();
    private readonly AutoResetEvent _wake = new(false);
    private string? _defaultContent;
    private int _nextId = 1;
    private long _frameCount;
    private bool _disposed;

    public EngineCommands Commands { get; }
    /// <summary>全 UI の登録簿 (ウィンドウ付き/オフスクリーンを問わない)。Framework と共有可。</summary>
    public UiRegistry UiRegistry { get; }

    public WindowManager(GpuDevice device, VectorFont font, WindowSystem windows,
                         EngineCommands? commands = null, UiRegistry? uiRegistry = null)
    {
        _device = device;
        _font = font;
        _windows = windows;
        _raster = new Rasterizer2D(device);
        Commands = commands ?? new EngineCommands();
        UiRegistry = uiRegistry ?? new UiRegistry();
        Commands.Enqueued += RequestFrame;
        RegisterCommands();
    }

    /// <summary>window.create の content 名 → UI 構築関数を登録する (最初の登録が既定)。</summary>
    public void RegisterContent(string name, Func<Widget> build)
    {
        _contents[name] = build;
        _defaultContent ??= name;
    }

    /// <summary>ウィンドウを生成し中身を結びつける。content の UI は <see cref="UiRegistry"/> へ登録される。
    /// ポンプスレッド (app スレッド) から呼ぶ。</summary>
    public WindowHost CreateWindow(in WindowDesc desc, IWindowContent content)
    {
        NativeWindow win = _windows.CreateWindow(desc);
        // 論理サイズ同期は WindowHost ctor が行う (物理クライアント ÷ DPI スケール)
        var host = new WindowHost(_nextId++, _device, win, content);
        foreach ((string name, UiHost ui) in content.Uis) UiRegistry.Register(name, ui);
        AttachFrameDemand(content);
        lock (_gate) _hosts.Add(host);
        RequestFrame();
        return host;
    }

    /// <summary>UI 1 つのウィンドウを生成する標準ヘルパ (<see cref="UiContent"/>)。
    /// <paramref name="theme"/> でこの UI 島のテーマ signal を指定できる (省略 = プロセス既定)。</summary>
    public WindowHost CreateUiWindow(in WindowDesc desc, string uiName, Func<Widget> build, Signal<Theme>? theme = null)
        => CreateWindow(desc, new UiContent(_raster, _font, uiName, desc.Width, desc.Height, build(), theme));

    /// <summary>ウィンドウを持たない UI を登録する (tree/入力/ui.set の対象になるが描画はされない)。
    /// 「ウィンドウ 1 : UI 1」ではないことの明示 — HUD 等をオフスクリーンで組み立てておく用途。</summary>
    public UiContent AddOffscreenUi(string name, int width, int height, Func<Widget> build)
    {
        var content = new UiContent(_raster, _font, name, width, height, build());
        foreach ((string n, UiHost ui) in content.Uis) UiRegistry.Register(n, ui);
        AttachFrameDemand(content);
        lock (_gate) _offscreen.Add(content);
        RequestFrame();
        return content;
    }

    public IReadOnlyList<WindowHost> Hosts
    {
        get { lock (_gate) return _hosts.ToArray(); }
    }

    /// <summary>直近の <see cref="RunFrame"/> でいずれかの窓が描画 (present) したか。
    /// present は vsync でブロックしループを律速するため、true の周回は追加スリープ不要
    /// (スリープを足すと入力レイテンシに直列加算される)。</summary>
    public bool AnyRendered { get; private set; }

    /// <summary>別スレッドの非同期完了などから、次の UI フレームを明示的に要求する。</summary>
    public void RequestFrame()
    {
        if (_disposed) return;
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// 即時要求があればそのまま返り、アイドルなら OS の入力/ウィンドウイベントまたは
    /// <see cref="RequestFrame"/> を待つ。連続 animation 中は present の vsync をペーサーとして使い、
    /// 前フレームが描画されなかった場合だけ約 60Hz に制限する。
    /// </summary>
    public bool WaitForNextFrame(int timeoutMilliseconds = Timeout.Infinite)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeoutMilliseconds < Timeout.Infinite)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

        WindowHost[] hosts;
        IWindowContent[] offscreen;
        lock (_gate) { hosts = _hosts.ToArray(); offscreen = _offscreen.ToArray(); }
        if (hosts.Length == 0) return false;

        bool explicitlyRequested = _wake.WaitOne(0);
        bool immediate = explicitlyRequested || Commands.HasPending
            || hosts.Any(h => h.HasPendingFrame)
            || offscreen.OfType<IFrameDemandSource>().Any(d => d.HasPendingFrame);
        if (immediate) return true;

        bool continuous = hosts.Any(h => h.RequiresContinuousFrames)
            || offscreen.OfType<IFrameDemandSource>().Any(d => d.RequiresContinuousFrames);
        if (continuous && AnyRendered) return true; // 直前の present が vsync 待機済み

        int wait = continuous
            ? timeoutMilliseconds == Timeout.Infinite ? 16 : Math.Min(16, timeoutMilliseconds)
            : timeoutMilliseconds;
        return _windows.WaitForEvents(_wake, wait);
    }

    /// <summary>1 フレーム: コマンド適用 → メッセージポンプ → 各窓描画 (+オフスクリーン Tick) → 閉じた窓の破棄。
    /// ウィンドウが残っていれば true。</summary>
    public bool RunFrame(float dt)
    {
        Commands.Drain();
        bool alive = _windows.Pump();
        WindowHost[] hosts;
        IWindowContent[] offscreen;
        lock (_gate) { hosts = _hosts.ToArray(); offscreen = _offscreen.ToArray(); }
        AnyRendered = false;
        foreach (WindowHost h in hosts)
        {
            if (h.Window.IsClosed)
            {
                lock (_gate) _hosts.Remove(h);
                foreach ((string _, UiHost ui) in h.Content.Uis) UiRegistry.Unregister(ui);
                DetachFrameDemand(h.Content);
                h.Dispose();
                continue;
            }
            h.Frame(dt);
            AnyRendered |= h.RenderedThisFrame;
        }
        foreach (IWindowContent c in offscreen) c.Tick(dt);

        // UI tree bundle を 30f に 1 回 emit (Framework の Scene と同じ cadence / 経路 → DevTools の /trees)
        if (++_frameCount % 30 == 0 && EngineDiagnostics.IsEnabled(EngineDiagnostics.Trees))
            EmitTrees();
        return alive;
    }

    private void EmitTrees()
    {
        IReadOnlyList<(string Name, UiHost Host)> entries = UiRegistry.Entries;
        var arr = new DebugTreeEntry[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            (string name, UiHost host) = entries[i];
            arr[i] = new DebugTreeEntry(name, "Screen", host.DebugSnapshot(), (int)host.Width, (int)host.Height);
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Trees, new DebugTreeSet(arr));
    }

    private WindowHost? Find(int id)
    {
        lock (_gate) return _hosts.FirstOrDefault(h => h.Id == id);
    }

    // ---- コマンド登録 (browser/AI → 共有 EngineCommands → app スレッド Drain) ----
    private void RegisterCommands()
    {
        // UI 入力 + ui.set は registry ルーティング版 (任意の "ui" index/名前で対象選択)
        UiHostCommands.RegisterDefaults(Commands, UiRegistry);

        Commands.Register("window.create", a =>
        {
            string content = S(a, "content");
            Func<Widget>? build =
                _contents.TryGetValue(content, out Func<Widget>? f) ? f :
                _defaultContent is not null ? _contents[_defaultContent] : null;
            if (build is null) return;
            string uiName = $"{(content.Length > 0 ? content : _defaultContent)}#{_nextId}";
            var desc = new WindowDesc(
                S(a, "title") is { Length: > 0 } t ? t : "Luxel",
                I(a, "w") is > 0 and var w ? w : 480,
                I(a, "h") is > 0 and var h ? h : 320)
            { X = NI(a, "x"), Y = NI(a, "y") };
            CreateUiWindow(desc, uiName, build);
        });
        Commands.Register("window.close", a => Find(I(a, "id"))?.Window.Close());
        Commands.Register("window.move", a => Find(I(a, "id"))?.Window.SetBounds(x: I(a, "x"), y: I(a, "y")));
        Commands.Register("window.resize", a => Find(I(a, "id"))?.Window.SetBounds(clientWidth: I(a, "w"), clientHeight: I(a, "h")));
        Commands.Register("window.title", a => Find(I(a, "id"))?.Window.SetTitle(S(a, "title")));
        Commands.Register("window.focus", a => Find(I(a, "id"))?.Window.Focus());
        Commands.Register("window.show", a => Find(I(a, "id"))?.Window.Show());
        Commands.Register("window.hide", a => Find(I(a, "id"))?.Window.Hide());
    }

    // ---- IWindowRemoteHost (server スレッドから; 読み取りのみ) ----
    public string ListWindowsJson()
    {
        WindowHost[] hosts;
        lock (_gate) hosts = _hosts.ToArray();
        var buf = new MemoryStream();
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartArray();
            foreach (WindowHost h in hosts)
            {
                w.WriteStartObject();
                w.WriteNumber("id", h.Id);
                w.WriteString("title", h.Window.Title);
                w.WriteNumber("x", h.Window.X);
                w.WriteNumber("y", h.Window.Y);
                w.WriteNumber("w", h.Window.Width);
                w.WriteNumber("h", h.Window.Height);
                w.WriteNumber("scale", h.Window.Scale);   // DPI (物理 px = 論理 px × scale)
                w.WriteBoolean("visible", h.Window.IsVisible);
                w.WriteBoolean("focused", h.Window.IsFocused);
                w.WriteStartArray("uis");   // このウィンドウに載っている UI 名 (0..N — 1:1 ではない)
                foreach ((string name, UiHost _) in h.Content.Uis) w.WriteStringValue(name);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buf.ToArray());
    }

    public (byte[]? body, long rev) GetFrame(int id, long? sinceRev)
        => Find(id) is { } h ? h.GetFrame(sinceRev) : (null, -1);

    // ---- JsonElement 引数読み出し ----
    private static JsonElement E(object? a) => a is JsonElement e ? e : default;
    private static int I(object? a, string n)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(n, out JsonElement v) && v.TryGetInt32(out int i) ? i : 0;
    private static int? NI(object? a, string n)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(n, out JsonElement v) && v.TryGetInt32(out int i) ? i : null;
    private static string S(object? a, string n)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Commands.Enqueued -= RequestFrame;
        WindowHost[] hosts;
        IWindowContent[] offscreen;
        lock (_gate)
        {
            hosts = _hosts.ToArray(); _hosts.Clear();
            offscreen = _offscreen.ToArray(); _offscreen.Clear();
        }
        foreach (WindowHost h in hosts)
        {
            foreach ((string _, UiHost ui) in h.Content.Uis) UiRegistry.Unregister(ui);
            DetachFrameDemand(h.Content);
            h.Dispose();
        }
        foreach (IWindowContent c in offscreen)
        {
            foreach ((string _, UiHost ui) in c.Uis) UiRegistry.Unregister(ui);
            DetachFrameDemand(c);
            c.Dispose();
        }
        _wake.Dispose();
        _raster.Dispose();
    }

    private void AttachFrameDemand(IWindowContent content)
    {
        if (content is IFrameDemandSource demand) demand.FrameRequested += RequestFrame;
    }

    private void DetachFrameDemand(IWindowContent content)
    {
        if (content is IFrameDemandSource demand) demand.FrameRequested -= RequestFrame;
    }
}
