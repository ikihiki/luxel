namespace Luxel.Abstraction;

/// <summary>
/// ウィンドウシステムのバックエンド (Win32 / 将来の他 OS) が実装する低レベル抽象。
/// <see cref="IGpuBackend"/> と同じ流儀: 公開 API (<see cref="WindowSystem"/>/<see cref="NativeWindow"/>) は
/// このインターフェイスのみに依存し、OS 具象 (CsWin32 等) はバックエンドプロジェクトに隔離する。
/// マルチウィンドウ前提 — <see cref="Pump"/> は呼び出しスレッドの全ウィンドウのメッセージを処理する。
/// </summary>
public interface IWindowBackend : IDisposable
{
    /// <summary>バックエンドの人間可読な名前 (例: "Win32")。</summary>
    string Name { get; }

    /// <summary>ウィンドウを生成する (作成スレッド = メッセージポンプのスレッド)。</summary>
    IWindowBackendWindow CreateWindow(in WindowDesc desc);

    /// <summary>保留メッセージを処理する。生存ウィンドウが 1 つでも残っていれば true。</summary>
    bool Pump();
}

/// <summary>ウィンドウ生成パラメータ。X/Y 省略 (null) は OS 既定位置。</summary>
public readonly record struct WindowDesc(string Title, int Width, int Height)
{
    public int? X { get; init; }
    public int? Y { get; init; }
    public bool Visible { get; init; } = true;
}

/// <summary>
/// バックエンドのウィンドウ 1 枚。座標はクライアント領域のピクセル。
/// 入力コールバックはメッセージポンプのスレッドから呼ばれる。
/// </summary>
public interface IWindowBackendWindow : IDisposable
{
    /// <summary>ネイティブハンドル (Win32=HWND)。<see cref="IGpuBackend.CreateSurface"/> に渡す。</summary>
    nint Handle { get; }

    /// <summary>クライアント領域の幅/高さ (物理 px)。</summary>
    int Width { get; }
    int Height { get; }

    /// <summary>モニタの DPI スケール (96dpi=1.0)。論理 px × Scale = 物理 px。既定 1 (非対応バックエンド)。</summary>
    float Scale => 1f;

    /// <summary>ウィンドウ (外枠) のスクリーン座標。</summary>
    int X { get; }
    int Y { get; }

    /// <summary>閉じられた (破棄済み) か。true 以後コールバックは来ない。</summary>
    bool IsClosed { get; }

    /// <summary>表示中か (Hide/最小化前の Show 状態)。</summary>
    bool IsVisible { get; }

    /// <summary>前面 (キーボードフォーカス) か。</summary>
    bool IsFocused { get; }

    void SetTitle(string title);
    /// <summary>外枠の位置とクライアントサイズを変更する (null は現状維持)。</summary>
    void SetBounds(int? x, int? y, int? clientWidth, int? clientHeight);
    void Show();
    void Hide();
    /// <summary>前面化 + キーボードフォーカス。</summary>
    void Focus();
    /// <summary>閉じる要求を送る (WM_CLOSE 相当)。</summary>
    void Close();

    // ---- コールバック (公開ラッパが購読) ----
    Action<int, int>? Resized { get; set; }             // クライアント w,h
    Action<int, int>? Moved { get; set; }               // 外枠 x,y
    Action? Closed { get; set; }                        // 破棄時 (1 回)
    Action<bool>? FocusChanged { get; set; }            // キーボードフォーカス得/喪失 (IME 切替等)
    Action<float, float>? MouseMoved { get; set; }
    Action<float, float, int>? MouseDown { get; set; }  // x,y,button(0=左,1=右)
    Action<float, float, int>? MouseUp { get; set; }
    Action<float, float, float>? Wheeled { get; set; }  // x,y,delta(ノッチ)
    Action<ushort>? KeyDown { get; set; }               // 仮想キーコード (Win32 VK)
    Action<ushort>? KeyUp { get; set; }
    Action<char>? CharTyped { get; set; }
    /// <summary>キーの先取りフック (IME/TSF 用, true=消費)。対応しないバックエンドは無視してよい。</summary>
    Func<ushort, nint, bool>? KeyPreFilter { get; set; }
    /// <summary>クライアント領域のカーソル形状の問い合わせ (WM_SETCURSOR 相当)。
    /// null = 矢印。対応しないバックエンドは無視してよい。</summary>
    Func<CursorKind>? CursorQuery { get => null; set { } }
}
