using Luxel.UI;

namespace Luxel.UI.App;

/// <summary>
/// ウィンドウ 1 枚分の提示ホスト: <see cref="Window"/> + スワップチェーン + framebuffer。
/// 中身は <see cref="IWindowContent"/> に委譲する (UI 1 つ / 複数 UI 合成 / 3D — ウィンドウは UI と 1:1 ではない)。
/// framebuffer 幅は 64 の倍数にパディング (D3D12 の 256B 行整列)、可視領域のみ present。
/// リモート検証用に最新フレームの tight RGBA を保持する (内容ハッシュで rev 管理、Gallery と同じ流儀)。
/// TSF (実 IME): スレッド共有の <see cref="TsfThread"/> + この窓の <see cref="TsfDocument"/> を持ち、
/// WM_SETFOCUS で IME フォーカス文書を切り替える。初期化失敗時は WM_CHAR フォールバック。
/// </summary>
internal sealed record WindowRemoteInfo(
    string Title, int X, int Y, int Width, int Height, float Scale, bool Visible, bool Focused);

public sealed class WindowHost : IDisposable
{
    private readonly GpuDevice _device;
    private readonly GpuSurface _surface;
    private readonly IWindowTextInputContext _textInput;

    private GpuBuffer _fb = null!;
    private GpuBuffer? _staging;   // 捕獲用 READBACK (HostCached) — WC の _fb を直接読まない
    private int _w, _h, _paddedW;
    private bool _resizePending;
    private int _rw, _rh;
    private bool _rendered;

    // 最新フレーム (8B ヘッダ w,h LE + tight RGBA)。rev は内容変化時のみ進む。
    // 捕獲は要求駆動: GetFrame が呼ばれてから CaptureWindowMs の間だけ有効 (通常使用ではコストゼロ)。
    private readonly object _frameGate = new();
    private byte[]? _frame;
    private long _frameRev;
    private ulong _frameHash;
    private long _captureUntil;          // Environment.TickCount64 基準 (volatile アクセス)
    private bool _fbDirtySinceCapture = true;
    private readonly byte[]?[] _capBufs = new byte[2][];   // 配信バッファのダブルバッファ (毎フレーム確保しない)
    private int _capIdx;
    private const int CaptureWindowMs = 2000;

    private bool CaptureArmed => Environment.TickCount64 < Volatile.Read(ref _captureUntil);

    /// <summary>直近の <see cref="Frame"/> で実際に描画 (present) したか — ループのペーシング判定用
    /// (present は vsync でブロックするため、描画した周回はスリープ不要)。</summary>
    public bool RenderedThisFrame { get; private set; }

    internal WindowRemoteInfo RemoteInfo { get; private set; }

    public int Id { get; }
    public Window Window { get; }
    public IWindowContent Content { get; }

    private static int Align(int v, int a) => (v + a - 1) / a * a;

    /// <summary>DPI スケール (論理 px × S = 物理 px)。</summary>
    private float S => Window.Scale;

    public WindowHost(int id, GpuDevice device, Window window, IWindowContent content)
    {
        Id = id;
        _device = device;
        Window = window;
        Content = content;
        _w = Math.Max(1, window.Width); _h = Math.Max(1, window.Height);
        Content.Resize(_w / S, _h / S, S);   // content の論理サイズを実クライアント (物理/scale) に同期
        RemoteInfo = CaptureRemoteInfo();
        Alloc();
        _surface = device.CreateSurface(window.Handle, (uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
        _textInput = window.CreateTextInputContext(() => Content.ImeTarget, () => S)
            ?? NoWindowTextInputContext.Instance;
        Wire();
    }

    /// <summary>TSF (実 IME) が有効か。false は WM_CHAR フォールバック。</summary>
    public bool TsfActive => _textInput.Active;

    private void Alloc()
    {
        _paddedW = Align(_w, 64);                 // 行ピッチ paddedW*4 を 256B 整列に
        _fb?.Dispose();
        _fb = _device.Malloc((ulong)(_paddedW * _h * 4), GpuMemoryKind.HostMapped);
        _staging?.Dispose();
        _staging = null;                          // 捕獲が要求されたら現在サイズで遅延確保
        _capBufs[0] = _capBufs[1] = null;
        _fbDirtySinceCapture = true;
    }

    private void Wire()
    {
        Window.Resized += (w, h) => { _resizePending = true; _rw = w; _rh = h; };
        // マウスは物理クライアント px で届く → 論理 px へ (UI は論理座標)
        Window.PointerMoved += input => Content.PointerMove(input with { X = input.X / S, Y = input.Y / S });
        Window.PointerDown += input =>
        {
            // 左/中ボタンは PointerDown (中ボタンドラッグ = pan 等)。右は down では何もしない (up で ContextClick)。
            if (input.Button is WindowPointerButton.Left or WindowPointerButton.Middle)
                Content.PointerDown(input with { X = input.X / S, Y = input.Y / S });
        };
        Window.PointerUp += input =>
        {
            WindowPointerEvent logical = input with { X = input.X / S, Y = input.Y / S };
            if (input.Button is WindowPointerButton.Left or WindowPointerButton.Middle) Content.PointerUp(logical);
            else if (input.Button == WindowPointerButton.Right) Content.ContextClick(logical.X, logical.Y, LuxelInput.MapModifiers(logical.Modifiers));
        };
        Window.CursorQuery = () => Content.Cursor;   // WM_SETCURSOR → hover 中ヒットの形状
        Window.Wheel += input => Content.Wheel(input with { X = input.X / S, Y = input.Y / S, Delta = input.Delta * 40f });
        Window.KeyDown += input => Content.KeyDown(input);
        Window.TextInput += text => { if (_textInput.ShouldDispatchTextInput) Content.TextInput(text); };
    }

    /// <summary>1 フレーム: リサイズ反映 → Tick → (変更があれば) 描画+present。
    /// フレーム捕獲は要求駆動 — リモート (/winframe) が最近ポーリングした間だけ行う。</summary>
    public void Frame(float dt)
    {
        RenderedThisFrame = false;
        if (Window.IsClosed) return;
        RemoteInfo = CaptureRemoteInfo();
        if (_resizePending)
        {
            _device.MainQueue.WaitIdle();
            _w = Math.Max(1, _rw); _h = Math.Max(1, _rh);
            Alloc();
            _surface.Resize((uint)_w, (uint)_h);
            Content.Resize(_w / S, _h / S, S);   // 論理サイズ (DPI 変更時は新 Scale で再計算される)
            _resizePending = false;
            _rendered = false;   // 新サイズで必ず 1 回描く
        }
        long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Content.Tick(dt);
        long p1 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_rendered || Content.NeedsRender)
        {
            Render();
            RenderedThisFrame = true;
        }
        if (RenderedThisFrame && Environment.GetEnvironmentVariable("NOGFX_FRAME_PROFILE") == "1")
        {
            double ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _profN++;
            _profTick += ms(p0, p1); _profRec += _lastRec; _profGpu += _lastGpu; _profPresent += _lastPresent;
            if (_profN == 60)
            {
                long nowTs = System.Diagnostics.Stopwatch.GetTimestamp();
                double span = (_profT0 == 0 ? 0 : (nowTs - _profT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _profT0 = nowTs;
                Console.WriteLine($"[prof] avg/frame: tick={_profTick / 60:0.##}ms rec+flush={_profRec / 60:0.##}ms gpuWait={_profGpu / 60:0.##}ms present={_profPresent / 60:0.##}ms | 60f span={span:0}ms ({(span > 0 ? 60000.0 / span : 0):0.#} fps)");
                _profN = 0; _profTick = _profRec = _profGpu = _profPresent = 0;
            }
        }
        // 捕獲 (armed かつ未捕獲の絵があるときだけ)。静止シーンで初回要求が来たケースもここが拾う
        if (CaptureArmed && _fbDirtySinceCapture && _rendered)
        {
            CaptureFrame();
            _fbDirtySinceCapture = false;
        }
    }

    private int _profN;
    private long _profT0;
    private double _profTick, _profRec, _profGpu, _profPresent, _lastRec, _lastGpu, _lastPresent;

    private void Render()
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), t1, t2;
        using (GpuCommandBuffer cmd = _device.MainQueue.StartCommandRecording())
        {
            Content.Render(cmd, (uint)_paddedW, (uint)_w, (uint)_h, _fb, S);
            cmd.Finish();
            t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            _device.MainQueue.SubmitAndWait(cmd);
        }
        t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        _surface.Present(_fb, (uint)_paddedW, (uint)_w, (uint)_h);
        long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        double f = System.Diagnostics.Stopwatch.Frequency;
        _lastRec = (t1 - t0) * 1000.0 / f; _lastGpu = (t2 - t1) * 1000.0 / f; _lastPresent = (t3 - t2) * 1000.0 / f;
        _rendered = true;
        _fbDirtySinceCapture = true;
    }

    private WindowRemoteInfo CaptureRemoteInfo()
        => new(Window.Title, Window.X, Window.Y, Window.Width, Window.Height,
            Window.Scale, Window.IsVisible, Window.IsFocused);

    /// <summary>framebuffer を READBACK staging へ GPU コピーし、キャッシュ可能メモリから tight RGBA へ
    /// 詰め替えて FNV ハッシュで rev を進める。**WC メモリ (_fb) は CPU で読まない** — 直接読みは
    /// 非キャッシュで 9.7MB が 200ms 級になる (これが入力レイテンシの主犯だった)。
    /// 配信バッファはダブルバッファ再利用 (毎フレームの LOH 確保をしない)。</summary>
    private void CaptureFrame()
    {
        int padded = _paddedW * _h * 4;
        _staging ??= _device.Malloc((ulong)padded, GpuMemoryKind.HostCached);
        using (GpuCommandBuffer cmd = _device.MainQueue.StartCommandRecording())
        {
            cmd.CopyBuffer(_fb, _staging, (ulong)padded);   // 別サブミット (raster は提示済み) — 昇格/バリア不要
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
        }

        int len = _w * _h * 4;
        byte[]? body = _capBufs[_capIdx];
        if (body is null || body.Length != 8 + len) body = _capBufs[_capIdx] = new byte[8 + len];
        BitConverter.TryWriteBytes(body.AsSpan(0, 4), _w);
        BitConverter.TryWriteBytes(body.AsSpan(4, 4), _h);
        ReadOnlySpan<byte> src = _staging.Span<byte>(padded);
        for (int y = 0; y < _h; y++)
            src.Slice(y * _paddedW * 4, _w * 4).CopyTo(body.AsSpan(8 + y * _w * 4));

        ulong hash = 14695981039346656037ul;
        ReadOnlySpan<byte> data = body.AsSpan(8);
        foreach (ulong u in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(data))
        { hash ^= u; hash *= 1099511628211ul; }
        for (int i = data.Length - data.Length % 8; i < data.Length; i++)
        { hash ^= data[i]; hash *= 1099511628211ul; }
        if (hash == _frameHash) return;
        _frameHash = hash;
        lock (_frameGate) { _frame = body; _frameRev++; }
        _capIdx ^= 1;   // 配信中の配列には次回書き込まない (HTTP スレッドとの共有回避)
    }

    /// <summary>最新フレーム (8B ヘッダ + tight RGBA)。sinceRev と同じなら body=null (=304)。
    /// 呼ばれた時点から一定時間フレーム捕獲を有効化する (要求駆動 — 初回は 1 フレーム遅れで新鮮になる)。</summary>
    public (byte[]? body, long rev) GetFrame(long? sinceRev)
    {
        Volatile.Write(ref _captureUntil, Environment.TickCount64 + CaptureWindowMs);
        lock (_frameGate)
            return sinceRev.HasValue && sinceRev.Value == _frameRev ? (null, _frameRev) : (_frame, _frameRev);
    }

    private sealed class NoWindowTextInputContext : IWindowTextInputContext
    {
        public static readonly NoWindowTextInputContext Instance = new();
        public bool Active => false;
        public bool ShouldDispatchTextInput => true;
        public void Dispose() { }
    }

    public void Dispose()
    {
        _textInput.Dispose();
        _staging?.Dispose(); _staging = null;
        _fb?.Dispose();
        Content.Dispose();
        _surface.Dispose();
        Window.Dispose();
    }
}
