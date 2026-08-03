using Luxel.Platform.Abstraction;

namespace Luxel.Platform;

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
/// ウィンドウシステムの公開窓口 (グラフィックデバイスに対するウィンドウ側の窓口)。
/// バックエンド (Win32 等) を包み、ウィンドウの生成・一覧・メッセージポンプを提供する。
/// <code>
/// using var windows = new WindowSystem(Luxel.Platform.Windows.Win32WindowBackend.Create());
/// Window main = windows.CreateWindow(new WindowDesc("App", 800, 600));
/// // Low-level callers explicitly connect the selected backend window and graphics backend.
/// while (windows.Pump()) { ...描画して swapchain.Present... }
/// </code>
/// </summary>
public sealed class WindowSystem : IDisposable
{
    private readonly IWindowBackend _backend;
    private readonly List<Window> _windows = new();
    private bool _disposed;

    public WindowSystem(IWindowBackend backend) => _backend = backend;

    public string Name => _backend.Name;

    /// <summary>生存ウィンドウのスナップショット (閉じたものは Pump で除去済み)。</summary>
    public IReadOnlyList<Window> Windows
    {
        get { Prune(); return _windows.ToArray(); }
    }

    /// <summary>ウィンドウを生成する。メッセージポンプと同じスレッドから呼ぶこと。</summary>
    public Window CreateWindow(in WindowDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var win = new Window(_backend.CreateWindow(desc), desc.Title);
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
        foreach (Window w in _windows) w.Dispose();
        _windows.Clear();
        _backend.Dispose();
    }
}

/// <summary>ウィンドウ入力をまとめて受け取るハンドラ。必要なメソッドだけ実装できる。</summary>
public interface IWindowInputHandler
{
    void PointerMoved(WindowPointerEvent input) { }
    void PointerDown(WindowPointerEvent input) { }
    void PointerUp(WindowPointerEvent input) { }
    void Wheel(WindowWheelEvent input) { }
    void KeyDown(WindowKeyEvent input) { }
    void KeyUp(WindowKeyEvent input) { }
    void TextInput(string text) { }
}

/// <summary>
/// ウィンドウ 1 枚の公開ラッパ。ネイティブハンドル、状態、入力イベント、基本操作を公開する。
/// 入力/リサイズのイベントはメッセージポンプのスレッドから呼ばれる。
/// </summary>
public sealed class Window : IDisposable
{
    private readonly IWindowBackendWindow _win;
    private readonly object _inputGate = new();
    private readonly List<IWindowInputHandler> _inputHandlers = new();
    private string _title;

    internal Window(IWindowBackendWindow win, string title)
    {
        _win = win;
        _title = title;
        // バックエンドのコールバックを公開イベントへ中継 (set 上書きでなく購読合成できるよう event 化)
        _win.Resized = (w, h) => Resized?.Invoke(w, h);
        _win.Moved = (x, y) => Moved?.Invoke(x, y);
        _win.Closed = () => Closed?.Invoke();
        _win.FocusChanged = f => FocusChanged?.Invoke(f);
        _win.PointerMoved = input => Dispatch(input, PointerMoved, static (h, value) => h.PointerMoved(value));
        _win.PointerDown = input => Dispatch(input, PointerDown, static (h, value) => h.PointerDown(value));
        _win.PointerUp = input => Dispatch(input, PointerUp, static (h, value) => h.PointerUp(value));
        _win.Wheel = input => Dispatch(input, Wheel, static (h, value) => h.Wheel(value));
        _win.KeyDown = input => Dispatch(input, KeyDown, static (h, value) => h.KeyDown(value));
        _win.KeyUp = input => Dispatch(input, KeyUp, static (h, value) => h.KeyUp(value));
        _win.TextInput = text => Dispatch(text, TextInput, static (h, value) => h.TextInput(value));
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
    public event Action<WindowPointerEvent>? PointerMoved;
    public event Action<WindowPointerEvent>? PointerDown;
    public event Action<WindowPointerEvent>? PointerUp;
    public event Action<WindowWheelEvent>? Wheel;
    public event Action<WindowKeyEvent>? KeyDown;
    public event Action<WindowKeyEvent>? KeyUp;
    public event Action<string>? TextInput;

    /// <summary>
    /// Gets the implementation-owned backend window. The returned object remains owned by this
    /// <see cref="Window"/> and must not be disposed separately.
    /// </summary>
    public IWindowBackendWindow BackendWindow => _win;

    /// <summary>
    /// Gets the backend window as the implementation type selected by the caller.
    /// Presentation setup is intentionally explicit: callers using low-level graphics backends
    /// are responsible for understanding and connecting the chosen window implementation.
    /// </summary>
    public TWindow RequireBackendWindow<TWindow>() where TWindow : class, IWindowBackendWindow
        => _win as TWindow
            ?? throw new InvalidOperationException(
                $"Window backend type mismatch. Expected {typeof(TWindow).FullName}, actual {_win.GetType().FullName}.");

    /// <summary>Gets an optional backend-specific feature without adding it to the portable window ABI.</summary>
    public TFeature? GetFeature<TFeature>() where TFeature : class => _win as TFeature;

    /// <summary>入力ハンドラを登録する。同じインスタンスの重複登録は無視する。</summary>
    public void AddInputHandler(IWindowInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_inputGate)
            if (!_inputHandlers.Contains(handler)) _inputHandlers.Add(handler);
    }

    /// <summary>登録済みの入力ハンドラを解除する。</summary>
    public bool RemoveInputHandler(IWindowInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_inputGate) return _inputHandlers.Remove(handler);
    }

    /// <summary>このウィンドウ用のOS入力メソッドコンテキストを生成する。非対応ならnull。</summary>
    public IWindowTextInputContext? CreateTextInputContext(
        Func<ITextInputClient?> getClient, Func<float>? getScale = null)
    {
        ArgumentNullException.ThrowIfNull(getClient);
        return (_win as IWindowTextInputContextFactory)?.Create(this, getClient, getScale);
    }

    private void Dispatch<T>(T value, Action<T>? observers, Action<IWindowInputHandler, T> dispatch)
    {
        observers?.Invoke(value);
        IWindowInputHandler[] handlers;
        lock (_inputGate) handlers = _inputHandlers.ToArray();
        foreach (IWindowInputHandler handler in handlers) dispatch(handler, value);
    }

    public void SetTitle(string title) { _title = title; _win.SetTitle(title); }
    public void SetBounds(int? x = null, int? y = null, int? clientWidth = null, int? clientHeight = null)
        => _win.SetBounds(x, y, clientWidth, clientHeight);
    public void Show() => _win.Show();
    public void Hide() => _win.Hide();
    public void Focus() => _win.Focus();
    public void Close() => _win.Close();

    public void Dispose()
    {
        lock (_inputGate) _inputHandlers.Clear();
        _win.Dispose();
    }
}
