using System.Runtime.InteropServices;
using Luxel.Abstraction;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Luxel.Platform;

/// <summary>
/// 生 Win32 ウィンドウ (CsWin32)。<see cref="IWindowBackendWindow"/> 実装。
/// WndProc→インスタンスの解決は **GWLP_USERDATA** (HWND 自身に GCHandle を格納) — 静的マップや
/// スレッドローカルを持たない (どのスレッドが何枚窓を持っても所有はインスタンスに閉じる)。
/// メッセージポンプは呼び出しスレッドのキューを処理する (Win32 の意味論どおりスレッド所有)。
/// </summary>
internal sealed unsafe class Win32Window : IWindowBackendWindow
{
    private static readonly WNDPROC _wndProcThunk = StaticWndProc;         // GC で回収されないよう静的保持
    private static readonly object ClassGate = new();                      // クラス登録はプロセスで 1 回
    private static bool _classRegistered;
    private static bool _dpiAware;                                         // PMv2 宣言はプロセスで 1 回
    private GCHandle _self;                                                // GWLP_USERDATA に入れる自身への参照
    private uint _dpi = 96;

    /// <summary>プロセスを Per-Monitor V2 DPI 対応として宣言する (最初の窓の前に 1 回)。
    /// これが無いと OS が GetClientRect を 96DPI 仮想 px で返し、DWM が最終出力を
    /// ビットマップ拡大するため全体がぼやける。失敗 (旧 OS / マニフェスト済) は無視。</summary>
    private static void EnsureDpiAwareness()
    {
        lock (ClassGate)
        {
            if (_dpiAware) return;
            _dpiAware = true;
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            PInvoke.SetProcessDpiAwarenessContext((Windows.Win32.UI.HiDpi.DPI_AWARENESS_CONTEXT)(nint)(-4));
        }
    }

    // CsWin32 は AnyCPU で Set/GetWindowLongPtr を生成しないため手動 P/Invoke (64bit 前提)
    private const int GWLP_USERDATA = -21;
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    public HWND Hwnd { get; private set; }
    public nint Handle => (nint)Hwnd.Value;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsVisible => !IsClosed && PInvoke.IsWindowVisible(Hwnd);
    public bool IsFocused => !IsClosed && PInvoke.GetForegroundWindow() == Hwnd;
    /// <summary>旧 API 互換 (AppWindow)。IsClosed と同じ。</summary>
    public bool ShouldClose => IsClosed;
    /// <summary>モニタの DPI スケール (96dpi=1.0, 150%=1.5)。クライアント物理 px = 論理 px × Scale。</summary>
    public float Scale => _dpi / 96f;

    // ---- IWindowBackendWindow コールバック ----
    public Action<int, int>? Resized { get; set; }
    public Action<int, int>? Moved { get; set; }
    public Action? Closed { get; set; }
    public Action<bool>? FocusChanged { get; set; }
    public Action<float, float>? MouseMoved { get; set; }
    public Action<float, float, int>? MouseDown { get; set; }
    public Action<float, float, int>? MouseUp { get; set; }
    public Action<float, float, float>? Wheeled { get; set; }
    public Action<ushort>? KeyDown { get; set; }
    public Action<ushort>? KeyUp { get; set; }
    public Action<char>? CharTyped { get; set; }
    public Func<ushort, nint, bool>? KeyPreFilter { get; set; }
    public Func<CursorKind>? CursorQuery { get; set; }

    private const string ClassName = "LuxelWindow";
    private const int UseDefault = int.MinValue;   // CW_USEDEFAULT

    public Win32Window(string title, int width, int height, int? x = null, int? y = null, bool visible = true)
    {
        EnsureDpiAwareness();
        Width = width; Height = height;

        fixed (char* cls = ClassName)
        {
            lock (ClassGate)
            {
                if (!_classRegistered)
                {
                    var wc = new WNDCLASSEXW
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                        style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                        lpfnWndProc = _wndProcThunk,
                        hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                        lpszClassName = cls,
                    };
                    PInvoke.RegisterClassEx(in wc);
                    _classRegistered = true;
                }
            }

            fixed (char* wnd = title)
            {
                Hwnd = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_APPWINDOW,
                    cls, wnd,
                    WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                    x ?? UseDefault, y ?? UseDefault, width, height,
                    default, default, default, null);
            }
        }
        // 自身を HWND に紐付け (以後のメッセージは WndProc がここから引く)。
        // 生成中 (WM_NCCREATE〜) のメッセージは DefWindowProc に流れるが、
        // サイズ/位置は直後の UpdateClientSize/UpdateOuterPos で取り直すので問題ない。
        _self = GCHandle.Alloc(this);
        SetWindowLongPtrW(Handle, GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        // 要求サイズは**論理クライアント px** — モニタ DPI を取得し、物理 px でクライアント領域を合わせる
        _dpi = PInvoke.GetDpiForWindow(Hwnd);
        if (_dpi == 0) _dpi = 96;
        SetBounds(null, null, (int)MathF.Round(width * Scale), (int)MathF.Round(height * Scale));
        if (visible) PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_SHOW);
        UpdateClientSize();
        UpdateOuterPos();
    }

    private void UpdateClientSize()
    {
        if (PInvoke.GetClientRect(Hwnd, out RECT r)) { Width = r.right - r.left; Height = r.bottom - r.top; }
    }

    private void UpdateOuterPos()
    {
        if (PInvoke.GetWindowRect(Hwnd, out RECT r)) { X = r.left; Y = r.top; }
    }

    /// <summary>呼び出しスレッドの保留メッセージを処理する (このスレッドの全ウィンドウ分)。</summary>
    public static void PumpThreadMessages()
    {
        while (PInvoke.PeekMessage(out MSG msg, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (msg.message == PInvoke.WM_QUIT) return;
            PInvoke.TranslateMessage(in msg);
            PInvoke.DispatchMessage(in msg);
        }
    }

    /// <summary>旧 API 互換 (AppWindow, 単一ウィンドウ用)。この窓が閉じたら false。</summary>
    public bool Pump()
    {
        PumpThreadMessages();
        return !IsClosed;
    }

    // ---- 操作 ----
    public void SetTitle(string title) => PInvoke.SetWindowText(Hwnd, title);

    public void SetBounds(int? x, int? y, int? clientWidth, int? clientHeight)
    {
        if (IsClosed) return;
        // クライアントサイズ→外枠サイズは現在の枠オフセット差分で変換 (AdjustWindowRect 相当)
        PInvoke.GetWindowRect(Hwnd, out RECT wr);
        PInvoke.GetClientRect(Hwnd, out RECT cr);
        int frameW = (wr.right - wr.left) - (cr.right - cr.left);
        int frameH = (wr.bottom - wr.top) - (cr.bottom - cr.top);
        int nx = x ?? wr.left, ny = y ?? wr.top;
        int nw = (clientWidth ?? (cr.right - cr.left)) + frameW;
        int nh = (clientHeight ?? (cr.bottom - cr.top)) + frameH;
        PInvoke.SetWindowPos(Hwnd, default, nx, ny, nw, nh,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    public void Show() => PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_SHOW);
    public void Hide() => PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    public void Focus() => PInvoke.SetForegroundWindow(Hwnd);

    /// <summary>閉じる要求 (WM_CLOSE) を送る。</summary>
    public void Close() => PInvoke.PostMessage(Hwnd, PInvoke.WM_CLOSE, default, default);

    private static LRESULT StaticWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        nint ud = GetWindowLongPtrW((nint)hwnd.Value, GWLP_USERDATA);
        Win32Window? win = ud != 0 ? GCHandle.FromIntPtr(ud).Target as Win32Window : null;
        return win?.WndProc(hwnd, msg, wParam, lParam) ?? PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        int lp = (int)lParam.Value;
        switch (msg)
        {
            case PInvoke.WM_SIZE:
                Width = lp & 0xFFFF; Height = (lp >> 16) & 0xFFFF;
                if (Width > 0 && Height > 0) Resized?.Invoke(Width, Height);
                return new LRESULT(0);
            case PInvoke.WM_MOVE:
                UpdateOuterPos();
                Moved?.Invoke(X, Y);
                return new LRESULT(0);
            case PInvoke.WM_DPICHANGED:
            {
                // モニタ間移動等で DPI が変わった: 新 DPI を取り込み、OS 提案の矩形へ追従
                // (SetWindowPos → WM_SIZE → Resized が流れ、ホスト側が新 Scale で論理サイズを再計算する)
                _dpi = (uint)(wParam.Value & 0xFFFF);
                var r = *(RECT*)lParam.Value;
                PInvoke.SetWindowPos(hwnd, default, r.left, r.top, r.right - r.left, r.bottom - r.top,
                    SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                return new LRESULT(0);
            }
            case PInvoke.WM_SETFOCUS:
                FocusChanged?.Invoke(true); break;    // IME/TSF の既定処理があるので DefWindowProc へ流す
            case PInvoke.WM_KILLFOCUS:
                FocusChanged?.Invoke(false); break;
            case PInvoke.WM_SETCURSOR:
                // クライアント領域 (HTCLIENT=1) だけ UI 側のカーソル形状を適用。他 (縁/タイトル) は既定処理。
                if (((nint)lp & 0xFFFF) == 1)
                {
                    PInvoke.SetCursor(PInvoke.LoadCursor(default, (CursorQuery?.Invoke() ?? CursorKind.Arrow) switch
                    {
                        CursorKind.IBeam => PInvoke.IDC_IBEAM,
                        CursorKind.Hand => PInvoke.IDC_HAND,
                        CursorKind.ResizeH => PInvoke.IDC_SIZEWE,
                        CursorKind.ResizeV => PInvoke.IDC_SIZENS,
                        _ => PInvoke.IDC_ARROW,
                    }));
                    return new LRESULT(1);
                }
                break;
            case PInvoke.WM_MOUSEMOVE:
                MouseMoved?.Invoke((short)(lp & 0xFFFF), (short)(lp >> 16)); return new LRESULT(0);
            case PInvoke.WM_LBUTTONDOWN:
                MouseDown?.Invoke((short)(lp & 0xFFFF), (short)(lp >> 16), 0); return new LRESULT(0);
            case PInvoke.WM_LBUTTONUP:
                MouseUp?.Invoke((short)(lp & 0xFFFF), (short)(lp >> 16), 0); return new LRESULT(0);
            case PInvoke.WM_RBUTTONDOWN:
                MouseDown?.Invoke((short)(lp & 0xFFFF), (short)(lp >> 16), 1); return new LRESULT(0);
            case PInvoke.WM_RBUTTONUP:
                MouseUp?.Invoke((short)(lp & 0xFFFF), (short)(lp >> 16), 1); return new LRESULT(0);
            case PInvoke.WM_MOUSEWHEEL:
            {
                short delta = (short)(wParam.Value >> 16);
                var p = new System.Drawing.Point((short)(lp & 0xFFFF), (short)(lp >> 16));   // wheel は screen 座標
                PInvoke.ScreenToClient(hwnd, ref p);
                Wheeled?.Invoke(p.X, p.Y, delta / 120f); return new LRESULT(0);
            }
            case PInvoke.WM_KEYDOWN:
            {
                ushort vk = (ushort)wParam.Value;
                if (KeyPreFilter?.Invoke(vk, (nint)lParam.Value) == true) return new LRESULT(0);   // TIP が消費
                KeyDown?.Invoke(vk); return new LRESULT(0);
            }
            case PInvoke.WM_KEYUP:
                KeyUp?.Invoke((ushort)wParam.Value); return new LRESULT(0);
            case PInvoke.WM_CHAR:
            {
                char c = (char)(ushort)wParam.Value;
                if (c >= ' ') CharTyped?.Invoke(c);
                return new LRESULT(0);
            }
            case PInvoke.WM_CLOSE:
                PInvoke.DestroyWindow(hwnd); return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                // マルチウィンドウ: PostQuitMessage はしない (生存判定はバックエンドのインスタンスリスト)
                IsClosed = true;
                Closed?.Invoke();
                return new LRESULT(0);
            case PInvoke.WM_NCDESTROY:
                // 最後のメッセージ — HWND との紐付けを解いて GCHandle を解放
                SetWindowLongPtrW((nint)hwnd.Value, GWLP_USERDATA, 0);
                if (_self.IsAllocated) _self.Free();
                break;
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!Hwnd.IsNull && !IsClosed) PInvoke.DestroyWindow(Hwnd);   // WM_NCDESTROY で GCHandle は解放される
        Hwnd = default;
    }
}

/// <summary>Win32 のウィンドウバックエンド。<c>new WindowSystem(Win32WindowBackend.Create())</c> で使う。
/// 生成した窓は**このインスタンスが所有** (静的状態なし) — 利用は生成スレッドから (Win32 の意味論)。</summary>
public sealed class Win32WindowBackend : IWindowBackend
{
    private readonly List<Win32Window> _windows = new();

    private Win32WindowBackend() { }

    public static Win32WindowBackend Create() => new();

    public string Name => "Win32";

    public IWindowBackendWindow CreateWindow(in WindowDesc desc)
    {
        var w = new Win32Window(desc.Title, desc.Width, desc.Height, desc.X, desc.Y, desc.Visible);
        _windows.Add(w);
        return w;
    }

    public bool Pump()
    {
        Win32Window.PumpThreadMessages();
        _windows.RemoveAll(w => w.IsClosed);
        return _windows.Count > 0;
    }

    public void Dispose()
    {
        foreach (Win32Window w in _windows) w.Dispose();
        _windows.Clear();
    }
}
