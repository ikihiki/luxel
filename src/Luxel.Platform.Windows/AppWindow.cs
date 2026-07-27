using System.Diagnostics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.Diagnostics;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Ime;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Luxel.Platform.Windows;

/// <summary>
/// ウィンドウ + 保持型キャンバス + UiHost + GPU スワップチェーン提示を束ねる実行ループ。
/// framebuffer 幅を 64 の倍数にパディングして確保 (D3D12 の 256B 行整列を充足)、可視領域のみ present。
/// </summary>
public sealed class AppWindow : IDisposable
{
    private readonly GpuDevice _device;
    private readonly VectorFont _font;
    private readonly Rasterizer2D _raster;
    private readonly RetainedCanvas _canvas;
    private readonly Win32Window _win;
    private readonly GpuSurface _surface;
    private readonly TsfManager _tsf;

    private GpuBuffer _fb = null!;
    private int _w, _h, _paddedW;
    private bool _resizePending;
    private int _rw, _rh;

    public UiHost Host { get; }
    /// <summary>Win32 keyboard/mouse event を <see cref="Luxel.Input.InputBus"/> に流す IInputSource 実装。
    /// Sample 側で <c>appWindow.InputSource.Poll(bus)</c> → <c>stack.Update(bus)</c> と組み合わせて使う。</summary>
    public Win32InputSource InputSource { get; } = new Win32InputSource();
    public bool TsfActive => _tsf.Active;

    /// <summary>毎フレーム描画前に呼ばれる (DevTools の入力キュー drain 用など)。</summary>
    public Action? BeforeFrame { get; set; }

    private static int Align(int v, int a) => (v + a - 1) / a * a;

    public AppWindow(GpuDevice device, VectorFont font, int width, int height, string title)
    {
        _device = device;
        _font = font;
        _raster = new Rasterizer2D(device);
        _canvas = new RetainedCanvas(_raster);
        Host = new UiHost(_canvas, _font, width, height);

        _win = new Win32Window(title, width, height);
        _w = _win.Width; _h = _win.Height;
        Alloc();
        _surface = _device.CreateSurface(_win.Hwnd, (uint)_w, (uint)_h);
        Wire();

        _tsf = new TsfManager(Host, () => _win.Hwnd);
        _tsf.Focus();
        Console.WriteLine($"TSF: {(_tsf.Active ? "active" : "inactive (WM_CHAR fallback)")}");
    }

    private void Alloc()
    {
        _paddedW = Align(_w, 64);                 // 行ピッチ paddedW*4 を 256B 整列に
        _fb?.Dispose();
        _fb = _device.Malloc((ulong)(_paddedW * _h * 4), GpuMemoryKind.HostMapped);
    }

    private void Wire()
    {
        _win.Resized = (w, h) => { _resizePending = true; _rw = w; _rh = h; };
        _win.PointerMoved = input => { Host.PointerMove(input.X, input.Y, Win32Modifiers.ToUi(input.Modifiers)); InputSource.HandlePointer(input); };
        _win.PointerDown = input =>
        {
            if (input.Button is WindowPointerButton.Left or WindowPointerButton.Middle)
                Host.PointerDown(input.X, input.Y, Win32Modifiers.ToUi(input.Button), Win32Modifiers.ToUi(input.Modifiers));
            InputSource.HandlePointerDown(input);
        };
        _win.PointerUp = input =>
        {
            if (input.Button is WindowPointerButton.Left or WindowPointerButton.Middle)
                Host.PointerUp(input.X, input.Y, Win32Modifiers.ToUi(input.Button), Win32Modifiers.ToUi(input.Modifiers));
            else if (input.Button == WindowPointerButton.Right) Host.ContextClick(input.X, input.Y, Win32Modifiers.ToUi(input.Modifiers));
            InputSource.HandlePointerUp(input);
        };
        _win.Wheel = input => { Host.Wheel(input.X, input.Y, input.Delta * 40f); InputSource.HandleWheel(input); };
        ((IWin32RawKeyInput)_win).KeyPreFilter = (vk, lp) => _tsf.HandleKeyDown(vk, lp);   // TIP 先取り
        _win.KeyDown = input =>
        {
            Host.KeyDown(KeyMap.FromWindowKey(input.Key), input.Modifiers.HasFlag(WindowKeyModifiers.Shift), input.Modifiers.HasFlag(WindowKeyModifiers.Control), input.Modifiers.HasFlag(WindowKeyModifiers.Alt));
            InputSource.HandleKeyDown(input);
        };
        _win.KeyUp = input => InputSource.HandleKeyUp(input);
        _win.TextInput = text => { if (!_tsf.Active) Host.Char(text); };   // TSF 有効時は store 経由
    }

    public void SetRoot(Widget root) => Host.SetRoot(root);

    /// <summary>ウィンドウに閉じ要求を送る。<see cref="Run"/> は次の <c>Pump</c> でループを抜ける
    /// (UI イベントハンドラ内から「終了」を実現するのに使う)。</summary>
    public void Close() => _win.Close();

    /// <summary>キーを TSF→UI 層の順に通す (擬似自動テスト用)。TIP が消費したら true。</summary>
    public bool SimulateKey(ushort vk)
    {
        if (_tsf.HandleKeyDown(vk, 0)) return true;
        Host.KeyDown(KeyMap.FromVk(vk));
        return false;
    }

    /// <summary>テキストを UI 層へ直接入力する (擬似自動テスト用, 非 IME ASCII 相当)。</summary>
    public void SimulateText(string text) => Host.Char(text);

    /// <summary>ウィンドウを前面化 (SendInput の宛先にするため)。</summary>
    public void Foreground() => PInvoke.SetForegroundWindow(_win.Hwnd);

    /// <summary>日本語 IME を有効化し ひらがな/ローマ字入力モードにする (実変換 E2E テスト用)。</summary>
    public void ActivateJapaneseIme()
    {
        PInvoke.LoadKeyboardLayout("00000411", ACTIVATE_KEYBOARD_LAYOUT_FLAGS.KLF_ACTIVATE);
        HIMC himc = PInvoke.ImmGetContext(_win.Hwnd);
        if (!himc.IsNull)
        {
            PInvoke.ImmSetOpenStatus(himc, true);
            PInvoke.ImmSetConversionStatus(himc,
                IME_CONVERSION_MODE.IME_CMODE_NATIVE | IME_CONVERSION_MODE.IME_CMODE_ROMAN, default);
            PInvoke.ImmReleaseContext(_win.Hwnd, himc);
        }
        _tsf.SetJapaneseInputMode();   // ITfKeystrokeMgr 注入経路用に TSF コンパートメントでも設定
    }

    /// <summary>実キーを OS 入力キューへ注入 (SendInput) → アクティブ IME が処理する。</summary>
    public void SendKeyStroke(ushort vk)
    {
        ushort scan = (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        Span<INPUT> inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        inputs[0].Anonymous.ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)vk, wScan = scan };
        inputs[1] = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        inputs[1].Anonymous.ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)vk, wScan = scan, dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP };
        PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>メッセージを処理しつつ n フレーム描画 (閉じない)。IME 非同期処理を進める。</summary>
    public void Step(int frames) { for (int i = 0; i < frames && _win.Pump(); i++) Frame(1f / 60f); }

    /// <summary>
    /// キーを TSF(TIP) へ注入して操作する (ブラウザ等のリモート入力 → OS IME 変換)。
    /// 前面ウィンドウ不要 (ITfKeystrokeMgr 直送)。TIP が消費しなければ UI 層へフォールバック。
    /// </summary>
    public bool ForwardKey(ushort vk)
    {
        ushort scan = (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        nint lParam = ((nint)scan << 16) | 1;          // WM_KEYDOWN lParam (scan code, repeat=1)
        if (_tsf.HandleKeyDown(vk, lParam)) return true;
        Host.KeyDown(KeyMap.FromVk(vk));
        return false;
    }

    /// <summary>対話実行 (閉じるまでループ)。</summary>
    public void Run()
    {
        var sw = Stopwatch.StartNew();
        double prev = sw.Elapsed.TotalSeconds;
        while (_win.Pump())
        {
            double now = sw.Elapsed.TotalSeconds;
            Frame((float)Math.Min(0.1, now - prev));
            prev = now;
        }
    }

    /// <summary>n フレーム回して閉じる (スモーク用)。</summary>
    public void RunFrames(int frames)
    {
        for (int i = 0; i < frames && _win.Pump(); i++) Frame(1f / 60f);
        _win.Close();
        _win.Pump();
    }

    private void Frame(float dt)
    {
        BeforeFrame?.Invoke();          // DevTools 入力キューの drain など
        if (_resizePending)
        {
            _device.MainQueue.WaitIdle();
            _w = _rw; _h = _rh; Alloc();
            _surface.Resize((uint)_w, (uint)_h);
            Host.Resize(_w, _h);
            _resizePending = false;
        }
        Host.Tick(dt);
        using (GpuCommandBuffer cmd = _device.MainQueue.StartCommandRecording())
        {
            _canvas.Render(cmd, Camera2D.Pixels, (uint)_paddedW, (uint)_h, _fb);  // 余白列は present しない
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
        }
        _surface.Present(_fb, (uint)_paddedW, (uint)_w, (uint)_h);
        EmitFrame();                    // DevTools へ可視領域フレームを配信 (購読時のみ)
        Host.EmitTree();
    }

    private byte[]? _emitTight;      // 使い回す tight RGBA (サイズ変化時のみ再確保) — 購読中の毎フレーム割り当てを避ける
    private DiagFrame? _emitFrame;   // 使い回す DiagFrame (バッファ/寸法が同じなら再生成しない)

    /// <summary>可視 w×h の密 RGBA を抜き出して DevTools へ配信 (padded stride → tight)。
    /// 購読者が居るときだけ実行し、バッファは使い回す (F1: main スレッドの毎フレーム割り当てを排除)。</summary>
    private void EmitFrame()
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Frame)) return;
        int len = _w * _h * 4;
        if (_emitTight is null || _emitTight.Length != len)
        {
            _emitTight = new byte[len];
            _emitFrame = new DiagFrame(_w, _h, _emitTight);   // 寸法/バッファが変わったときだけ record を作り直す
        }
        ReadOnlySpan<byte> src = _fb.Span<byte>(_paddedW * _h * 4);
        for (int y = 0; y < _h; y++)
            src.Slice(y * _paddedW * 4, _w * 4).CopyTo(_emitTight.AsSpan(y * _w * 4));
        EngineDiagnostics.Emit(EngineDiagnostics.Frame, _emitFrame!);   // listener が seqlock でリングへコピー
    }

    public void Dispose()
    {
        _tsf.Dispose();
        _surface.Dispose();
        _fb?.Dispose();
        _canvas.Dispose();
        _raster.Dispose();
        _win.Dispose();
    }
}
