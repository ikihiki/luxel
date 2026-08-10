namespace Luxel.UI;

/// <summary>押下されたポインタボタン。右ボタンは従来どおり <see cref="UiHost.ContextClick"/> にも回るが、
/// ドラッグ捕獲やクリックのハンドラでは <see cref="PointerEvent.Button"/> で区別できる。</summary>
public enum PointerButton { Left, Right, Middle }

/// <summary>イベント発生時点の修飾キー (ビットフラグ)。Win32 ではメッセージ処理時に <c>GetKeyState</c> で拾う。</summary>
[System.Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Meta = 8 }

/// <summary>
/// ポインタイベントのコンテキスト — ハンドラへ渡る情報一式。座標は 2 系統:
/// <list type="bullet">
/// <item><b>ローカル</b> (<see cref="X"/>/<see cref="Y"/>): ヒット対象ノード基準。transform/スクロールの
///   逆写像込み — 「矩形のどこを指しているか」はこちら。</item>
/// <item><b>画面絶対</b> (<see cref="ScreenX"/>/<see cref="ScreenY"/>): UiHost に届いた生のクライアント座標。
///   ノードがどう動いても変わらない — メニューの配置やドラッグ移動量はこちら。</item>
/// </list>
/// ドラッグ系 (DragStart/Drag/DragEnd) では開始位置 (<see cref="StartX"/> 等) が捕獲時の値になり、
/// <see cref="DeltaX"/>/<see cref="DeltaY"/> が開始からの移動量になる。それ以外のイベントでは
/// 開始位置 = 現在位置 (差分 0)。
/// <para><b>差分は画面絶対座標の差</b> — ドラッグでノード自身を動かすゴースト (Splitter 等) でも
/// ローカル座標のように自分の移動分が巻き戻って見えず、発振しない。</para>
/// </summary>
public readonly struct PointerEvent
{
    /// <summary>ローカル X (ヒット対象ノード基準)。</summary>
    public float X { get; }
    /// <summary>ローカル Y (ヒット対象ノード基準)。</summary>
    public float Y { get; }
    /// <summary>画面絶対 X (ホストのクライアント座標)。</summary>
    public float ScreenX { get; }
    /// <summary>画面絶対 Y (ホストのクライアント座標)。</summary>
    public float ScreenY { get; }
    /// <summary>ドラッグ開始位置のローカル X (非ドラッグイベントでは現在位置)。</summary>
    public float StartX { get; }
    /// <summary>ドラッグ開始位置のローカル Y。</summary>
    public float StartY { get; }
    /// <summary>ドラッグ開始位置の画面絶対 X。</summary>
    public float StartScreenX { get; }
    /// <summary>ドラッグ開始位置の画面絶対 Y。</summary>
    public float StartScreenY { get; }

    /// <summary>ドラッグ開始からの X 移動量 (画面絶対基準 — ノード自身の移動に影響されない)。</summary>
    public float DeltaX => ScreenX - StartScreenX;
    /// <summary>ドラッグ開始からの Y 移動量 (画面絶対基準)。</summary>
    public float DeltaY => ScreenY - StartScreenY;

    /// <summary>押下ボタン (ドラッグ捕獲中は捕獲時のボタン)。移動/ホバーでは既定 Left。</summary>
    public PointerButton Button { get; }
    /// <summary>イベント発生時点の修飾キー。</summary>
    public KeyModifiers Modifiers { get; }
    /// <summary>Ctrl が押されているか。</summary>
    public bool Ctrl => (Modifiers & KeyModifiers.Ctrl) != 0;
    /// <summary>Shift が押されているか。</summary>
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    /// <summary>Alt が押されているか。</summary>
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
    /// <summary>Meta (Windows キー) が押されているか。</summary>
    public bool Meta => (Modifiers & KeyModifiers.Meta) != 0;

    /// <summary>非ドラッグイベント用 (開始位置 = 現在位置、差分 0)。</summary>
    public PointerEvent(float x, float y, float screenX, float screenY,
        PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None)
        : this(x, y, screenX, screenY, x, y, screenX, screenY, button, modifiers) { }

    /// <summary>ドラッグ系イベント用 (開始位置 = 捕獲時の値)。</summary>
    public PointerEvent(float x, float y, float screenX, float screenY,
        float startX, float startY, float startScreenX, float startScreenY,
        PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        X = x; Y = y; ScreenX = screenX; ScreenY = screenY;
        StartX = startX; StartY = startY; StartScreenX = startScreenX; StartScreenY = startScreenY;
        Button = button; Modifiers = modifiers;
    }
}
