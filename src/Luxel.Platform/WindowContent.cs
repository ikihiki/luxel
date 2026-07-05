using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Platform;

/// <summary>
/// ウィンドウに表示する中身。ウィンドウは UI と 1:1 ではない —
/// UI を 1 つ持つ (<see cref="UiContent"/>)、複数 UI や 3D を合成する、UI を持たない、いずれも実装できる。
/// <see cref="Uis"/> は保持する UiHost の列挙 (リモートの UI 一覧/入力ルーティング登録用、空可)。
/// 入力はウィンドウのクライアント座標で <see cref="WindowHost"/> から転送される。
/// </summary>
public interface IWindowContent : IDisposable
{
    /// <summary>この content が保持する UI (名前 + host)。WindowManager が UiRegistry に登録する。</summary>
    IReadOnlyList<(string Name, UiHost Host)> Uis { get; }

    /// <summary>IME (TSF) の対象 UiHost。複数 UI を持つ content はテキストフォーカス中のものを返す。
    /// null は「このウィンドウは IME 不要」。既定は先頭の UI。</summary>
    UiHost? ImeTarget => Uis.Count > 0 ? Uis[0].Host : null;

    /// <summary>論理サイズの変更 (物理クライアント px ÷ DPI スケール)。<paramref name="scale"/> は
    /// DPI スケール — content 側 (UiHost) の RenderScale へ伝える (SurfaceView 等の物理解像度確保用)。</summary>
    void Resize(float width, float height, float scale = 1f);
    void Tick(float dt);

    /// <summary>描画が必要か (false なら描画/present をスキップ = 静止シーン)。</summary>
    bool NeedsRender { get; }

    /// <summary>framebuffer (行幅 <paramref name="paddedWidth"/> px, 可視域 <paramref name="width"/>×<paramref name="height"/> = 物理 px) へ描画する。
    /// <paramref name="scale"/> は DPI スケール — UI は論理座標のままカメラで物理解像度へ拡大される。</summary>
    void Render(GpuCommandBuffer cmd, uint paddedWidth, uint width, uint height, GpuBuffer target, float scale);

    // ---- ウィンドウ入力 (論理 px — WindowHost が物理→論理へ変換して渡す) ----
    void PointerMove(float x, float y);
    void PointerDown(float x, float y);
    void PointerUp(float x, float y);
    void Wheel(float x, float y, float delta);
    void KeyDown(ushort vk);
    void Char(string text);

    /// <summary>右クリック (コンテキストメニュー要求)。既定 no-op。</summary>
    void ContextClick(float x, float y) { }
    /// <summary>hover 中のカーソル形状 (WM_SETCURSOR が参照)。既定 = 矢印。</summary>
    CursorKind Cursor => CursorKind.Arrow;
}

/// <summary>
/// UiHost 1 つをウィンドウの中身にする標準実装 (RetainedCanvas + UiHost を所有)。
/// ウィンドウ無し (オフスクリーン, tree/入力のみ) でも使える — <see cref="WindowManager.AddOffscreenUi"/>。
/// </summary>
public sealed class UiContent : IWindowContent
{
    private readonly RetainedCanvas _canvas;

    public string Name { get; }
    public UiHost Host { get; }

    public UiContent(Rasterizer2D raster, VectorFont font, string name, int width, int height, Widget root,
                     Signal<Theme>? theme = null)
    {
        Name = name;
        _canvas = new RetainedCanvas(raster);
        Host = new UiHost(_canvas, font, Math.Max(1, width), Math.Max(1, height), theme);
        Host.SetRoot(root);
    }

    public IReadOnlyList<(string Name, UiHost Host)> Uis => [(Name, Host)];
    public UiHost? ImeTarget => Host;

    public void Resize(float width, float height, float scale = 1f)
    {
        Host.RenderScale = scale;
        Host.Resize(width, height);
    }
    public void Tick(float dt) => Host.Tick(dt);
    public bool NeedsRender => _canvas.HasPendingChanges;

    public void Render(GpuCommandBuffer cmd, uint paddedWidth, uint width, uint height, GpuBuffer target, float scale)
        => _canvas.Render(cmd, new Camera2D { A = scale, D = scale }, paddedWidth, height, target);

    public void PointerMove(float x, float y) => Host.PointerMove(x, y);
    public void PointerDown(float x, float y) => Host.PointerDown(x, y);
    public void PointerUp(float x, float y) => Host.PointerUp(x, y);
    public void Wheel(float x, float y, float delta) => Host.Wheel(x, y, delta);
    public void KeyDown(ushort vk) => Host.KeyDown(KeyMap.FromVk(vk));
    public void Char(string text) => Host.Char(text);
    public void ContextClick(float x, float y) => Host.ContextClick(x, y);
    public CursorKind Cursor => Host.CurrentCursor;

    public void Dispose()
    {
        Host.Dispose();
        _canvas.Dispose();
    }
}
