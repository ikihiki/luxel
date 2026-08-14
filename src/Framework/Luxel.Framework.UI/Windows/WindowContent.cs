using Luxel.Graphics.RenderSystem;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Framework.UI;

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
    ITextInputClient? ImeTarget => Uis.Count > 0 ? Uis[0].Host : null;

    /// <summary>論理サイズの変更 (物理クライアント px ÷ DPI スケール)。<paramref name="scale"/> は
    /// DPI スケール — content 側 (UiHost) の RenderScale へ伝える (SurfaceView 等の物理解像度確保用)。</summary>
    void Resize(float width, float height, float scale = 1f);

    /// <summary>この content が共有する RenderSystem の Set 世代。</summary>
    RenderFeatureSetStateRegistry FeatureSetStates { get; }

    /// <summary>window presentation Cadence に割り当てる Feature。</summary>
    IRenderFeature RenderFeature { get; }

    /// <summary>active animation 等により、静止中も継続的な opportunity が必要か。</summary>
    bool RequiresContinuousUpdate { get; }

    /// <summary>必要な logical work だけを進め、retained changes を Set invalidation へ反映する。</summary>
    void PrepareFrame(float dt);

    /// <summary>次の presentation target を persistent output として stage する。</summary>
    void SetPresentationTarget(PresentationTarget target, float scale);

    // ---- ウィンドウ入力 (論理 px — WindowHost が物理→論理へ変換して渡す) ----
    // button/mods はイベント発生時点の値 (WindowHost が GetKeyState で拾って渡す)。既定引数で旧呼び出しも互換。
    void PointerMove(WindowPointerEvent input);
    void PointerDown(WindowPointerEvent input);
    void PointerUp(WindowPointerEvent input);
    void Wheel(WindowWheelEvent input);
    void KeyDown(WindowKeyEvent input);
    void TextInput(string text);

    /// <summary>右クリック (コンテキストメニュー要求)。既定 no-op。</summary>
    void ContextClick(float x, float y, KeyModifiers mods = KeyModifiers.None) { }
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
    private readonly IRasterScene2D _rasterScene;
    private readonly GpuDevice _device;
    private readonly UiRendererState _rendererState = new();
    private readonly PersistentUiOutput<GpuBuffer> _output = new();
    private readonly PresentUiRenderFeature _feature;
    private readonly UiSurfaceState _surface;
    private uint _paddedWidth, _height;
    private float _scale = 1f;

    public string Name { get; }
    public UiHost Host { get; }

    public UiContent(GpuDeviceRasterizer2D raster, VectorFont font, string name, int width, int height, Widget root,
                     Signal<Theme>? theme = null)
    {
        Name = name;
        _canvas = new RetainedCanvas();
        _device = raster.Device;
        _rasterScene = raster.CreateScene(_canvas);
        Host = new UiHost(_canvas, font, Math.Max(1, width), Math.Max(1, height), theme, raster, _rendererState);
        _surface = new UiSurfaceState(
            $"window-ui:{name}",
            UiSurfaceRole.Present,
            _canvas,
            _output,
            _rendererState.CreateInvalidationSource(UiSurfaceRole.Present),
            Host.Tick,
            AddRasterPass);
        _rendererState.Add(_surface);
        _feature = new PresentUiRenderFeature(_rendererState);
        Host.SetRoot(root);
    }

    public IReadOnlyList<(string Name, UiHost Host)> Uis => [(Name, Host)];
    public ITextInputClient? ImeTarget => Host;

    public void Resize(float width, float height, float scale = 1f)
    {
        Host.RenderScale = scale;
        Host.Resize(width, height);
    }
    public RenderFeatureSetStateRegistry FeatureSetStates => _rendererState.FeatureSetStates;
    public IRenderFeature RenderFeature => _feature;
    public bool RequiresContinuousUpdate => Host.RequiresContinuousUpdate;

    public void PrepareFrame(float dt)
    {
        if (Host.NeedsLogicalTick) _rendererState.Tick(dt);
        else _rendererState.ObserveChanges();
    }

    public void SetPresentationTarget(PresentationTarget target, float scale)
    {
        ArgumentNullException.ThrowIfNull(target);
        _paddedWidth = target.StridePixels;
        _height = target.Height;
        _scale = scale;
        _surface.StagePending(target.Buffer);
    }

    private void AddRasterPass(global::Luxel.Graphics.RenderGraph.RenderGraph graph, GpuBuffer output)
    {
        var handle = graph.ImportBuffer(output, $"window-ui:{Name}");
        graph.AddPass($"Raster window UI {Name}")
            .Write(handle)
            .SideEffect()
            .Execute(pass => _rasterScene.Render(new Camera2D { A = _scale, D = _scale },
                new GpuRasterTarget2D(pass.Cmd, pass.Buffer(handle), _paddedWidth, _height)));
    }

    public void PointerMove(WindowPointerEvent input) => Host.PointerMove(input.X, input.Y, LuxelInput.MapModifiers(input.Modifiers));
    public void PointerDown(WindowPointerEvent input)
    {
        if (LuxelInput.TryMapButton(input.Button, out PointerButton button))
            Host.PointerDown(input.X, input.Y, button, LuxelInput.MapModifiers(input.Modifiers));
    }
    public void PointerUp(WindowPointerEvent input)
    {
        if (LuxelInput.TryMapButton(input.Button, out PointerButton button))
            Host.PointerUp(input.X, input.Y, button, LuxelInput.MapModifiers(input.Modifiers));
    }
    public void Wheel(WindowWheelEvent input) => Host.Wheel(input.X, input.Y, input.Delta);
    public void KeyDown(WindowKeyEvent input)
        => Host.KeyDown(LuxelInput.MapKey(input.Key), input.Modifiers.HasFlag(WindowKeyModifiers.Shift), input.Modifiers.HasFlag(WindowKeyModifiers.Control), input.Modifiers.HasFlag(WindowKeyModifiers.Alt));
    public void TextInput(string text) => Host.Char(text);
    public void ContextClick(float x, float y, KeyModifiers mods = KeyModifiers.None) => Host.ContextClick(x, y, mods);
    public CursorKind Cursor => Host.CurrentCursor;

    public void Dispose()
    {
        Host.Dispose();
        _rendererState.Dispose();
        _rasterScene.Dispose();
        _canvas.Dispose();
    }
}
