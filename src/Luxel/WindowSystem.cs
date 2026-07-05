using Luxel.Abstraction;

namespace Luxel;

/// <summary>マウスカーソル形状 (OS カーソルへ写像される)。</summary>
public enum CursorKind
{
    Arrow,
    /// <summary>テキスト編集 (I ビーム)。</summary>
    IBeam,
    /// <summary>リンク等 (手)。</summary>
    Hand,
    /// <summary>水平リサイズ (Splitter 縦分割)。</summary>
    ResizeH,
    /// <summary>垂直リサイズ。</summary>
    ResizeV,
}

/// <summary>
/// ウィンドウシステムの公開窓口 (<see cref="GpuDevice"/> のウィンドウ版)。
/// バックエンド (Win32 等) を包み、ウィンドウの生成・一覧・メッセージポンプを提供する。
/// <code>
/// using var windows = new WindowSystem(Win32WindowBackend.Create());
/// NativeWindow main = windows.CreateWindow(new WindowDesc("App", 800, 600));
/// GpuSurface swapchain = main.CreateSwapchain(device);
/// while (windows.Pump()) { ...描画して swapchain.Present... }
/// </code>
/// </summary>
public sealed class WindowSystem : IDisposable
{
    private readonly IWindowBackend _backend;
    private readonly List<NativeWindow> _windows = new();
    private bool _disposed;

    public WindowSystem(IWindowBackend backend) => _backend = backend;

    public string Name => _backend.Name;

    /// <summary>生存ウィンドウのスナップショット (閉じたものは Pump で除去済み)。</summary>
    public IReadOnlyList<NativeWindow> Windows
    {
        get { Prune(); return _windows.ToArray(); }
    }

    /// <summary>ウィンドウを生成する。メッセージポンプと同じスレッドから呼ぶこと。</summary>
    public NativeWindow CreateWindow(in WindowDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var win = new NativeWindow(_backend.CreateWindow(desc), desc.Title);
        _windows.Add(win);
        return win;
    }

    /// <summary>保留メッセージを処理し、閉じたウィンドウを一覧から外す。生存ウィンドウが残っていれば true。</summary>
    public bool Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool alive = _backend.Pump();
        Prune();
        return alive;
    }

    private void Prune() => _windows.RemoveAll(w => w.IsClosed);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (NativeWindow w in _windows) w.Dispose();
        _windows.Clear();
        _backend.Dispose();
    }
}

/// <summary>
/// ウィンドウ 1 枚の公開ラッパ。スワップチェーンは <see cref="CreateSwapchain"/> で取得する。
/// 入力/リサイズのイベントはメッセージポンプのスレッドから呼ばれる。
/// </summary>
public sealed class NativeWindow : IDisposable
{
    private readonly IWindowBackendWindow _win;
    private string _title;

    internal NativeWindow(IWindowBackendWindow win, string title)
    {
        _win = win;
        _title = title;
        // バックエンドのコールバックを公開イベントへ中継 (set 上書きでなく購読合成できるよう event 化)
        _win.Resized = (w, h) => Resized?.Invoke(w, h);
        _win.Moved = (x, y) => Moved?.Invoke(x, y);
        _win.Closed = () => Closed?.Invoke();
        _win.FocusChanged = f => FocusChanged?.Invoke(f);
        _win.MouseMoved = (x, y) => MouseMoved?.Invoke(x, y);
        _win.MouseDown = (x, y, b) => MouseDown?.Invoke(x, y, b);
        _win.MouseUp = (x, y, b) => MouseUp?.Invoke(x, y, b);
        _win.Wheeled = (x, y, d) => Wheeled?.Invoke(x, y, d);
        _win.KeyDown = vk => KeyDown?.Invoke(vk);
        _win.KeyUp = vk => KeyUp?.Invoke(vk);
        _win.CharTyped = c => CharTyped?.Invoke(c);
    }

    /// <summary>クライアント領域のカーソル形状の問い合わせ先 (WM_SETCURSOR 相当で呼ばれる)。null = 矢印。</summary>
    public Func<CursorKind>? CursorQuery
    {
        set => _win.CursorQuery = value;
    }

    public nint Handle => _win.Handle;
    public int Width => _win.Width;
    public int Height => _win.Height;
    /// <summary>モニタの DPI スケール (96dpi=1.0)。クライアント物理 px = 論理 px × Scale。</summary>
    public float Scale => _win.Scale;
    public int X => _win.X;
    public int Y => _win.Y;
    public bool IsClosed => _win.IsClosed;
    public bool IsVisible => _win.IsVisible;
    public bool IsFocused => _win.IsFocused;
    public string Title => _title;

    // ---- イベント ----
    public event Action<int, int>? Resized;
    public event Action<int, int>? Moved;
    public event Action? Closed;
    public event Action<bool>? FocusChanged;
    public event Action<float, float>? MouseMoved;
    public event Action<float, float, int>? MouseDown;
    public event Action<float, float, int>? MouseUp;
    public event Action<float, float, float>? Wheeled;
    public event Action<ushort>? KeyDown;
    public event Action<ushort>? KeyUp;
    public event Action<char>? CharTyped;

    /// <summary>キー先取りフック (IME/TSF 用, true=消費)。イベントでなく単一 Func (消費判定があるため)。</summary>
    public Func<ushort, nint, bool>? KeyPreFilter
    {
        get => _win.KeyPreFilter;
        set => _win.KeyPreFilter = value;
    }

    /// <summary>このウィンドウへのスワップチェーン提示面を取得する (現在のクライアントサイズ)。</summary>
    public GpuSurface CreateSwapchain(GpuDevice device)
        => device.CreateSurface(Handle, (uint)Math.Max(1, Width), (uint)Math.Max(1, Height));

    public void SetTitle(string title) { _title = title; _win.SetTitle(title); }
    public void SetBounds(int? x = null, int? y = null, int? clientWidth = null, int? clientHeight = null)
        => _win.SetBounds(x, y, clientWidth, clientHeight);
    public void Show() => _win.Show();
    public void Hide() => _win.Hide();
    public void Focus() => _win.Focus();
    public void Close() => _win.Close();

    public void Dispose() => _win.Dispose();
}
