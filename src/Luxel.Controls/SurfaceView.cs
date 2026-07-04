using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// iframe 相当の埋め込みサーフェス。**子 RetainedCanvas + 子 UiHost + 専用 framebuffer** を所有し、
/// 親キャンバス上では image ノード 1 つ (Kind=2, bindless バッファサンプリング) として描かれる。
/// - 子は透過 (premultiplied) で描かれ、変更があったフレームだけ GPU 内で再ラスタライズされる (CPU 読み戻しなし)
/// - 入力はローカル座標のまま子 UiHost へ転送 (pointer/drag/wheel、フォーカス時は key/文字/IME)
/// - <see cref="SetContent"/> で子ルートを差し替え — 親ツリーの再構築は不要 (ギャラリーのプレビュー切替用)
/// - フォーカス/オーバーレイ/状態は子側に閉じる。子の Dialog 等はこの矩形内にクリップされる
/// 制限: 親の Tab/Escape は親 UiHost が先に消費するため子へ届かない (v1)。破棄は所有者が <see cref="Dispose"/>。
/// </summary>
[UiComponent]
public sealed partial class SurfaceView : Widget, IDisposable
{
    private readonly float _w, _h;
    private float _pendingW, _pendingH;   // 子の論理サイズ (既定 = サーフェスサイズ)
    private RetainedCanvas? _childCanvas;
    private GpuDevice? _device;
    private GpuBuffer? _fb;
    private Widget? _pendingRoot;
    private bool _rendered;
    private float _scale = 1f;            // 親 UI の DPI スケール — 子 fb は物理解像度 (_pw×_ph) で持つ
    private uint _pw, _ph;

    /// <summary>子 UiHost (実体化後に有効)。状態強制やツリー検査はこれ経由で行う。</summary>
    public UiHost? Child { get; private set; }

    [UiCtor]
    internal SurfaceView(float surfaceWidth, float surfaceHeight)
    {
        _w = MathF.Max(1, surfaceWidth);
        _h = MathF.Max(1, surfaceHeight);
        _pendingW = _w; _pendingH = _h;
    }

    /// <summary>子ルートを設定/差し替える (実体化前でも可)。親ツリーは再構築されない。
    /// <paramref name="logicalWidth"/>/<paramref name="logicalHeight"/> で子の論理サイズを変更できる
    /// (framebuffer はサーフェスサイズのまま — 論理サイズが小さい場合は左上に配置され余白は透過)。</summary>
    public void SetContent(Widget root, float? logicalWidth = null, float? logicalHeight = null)
    {
        _pendingRoot = root;
        if (logicalWidth is float lw) _pendingW = MathF.Min(lw, _w);
        if (logicalHeight is float lh) _pendingH = MathF.Min(lh, _h);
        if (Child is not null)
        {
            Child.Resize(_pendingW, _pendingH);   // 旧 root の再レイアウトは無害 (直後に差し替え)
            Child.SetRoot(root);
        }
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(_w, _h));
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _w;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        // 子環境は初回のみ生成 (親の再実体化を跨いで生存 — 子の状態は保持される)
        if (Child is null)
        {
            Rasterizer2D raster = ctx.Canvas.Rasterizer;
            _device = raster.Device;
            _childCanvas = new RetainedCanvas(raster);
            Child = new UiHost(_childCanvas, ctx.Font, _pendingW, _pendingH, ctx.Theme);   // 親島とテーマ共有 (同一スレッド)
            if (_pendingRoot is not null) Child.SetRoot(_pendingRoot);
        }
        // fb は親のラスタライズ解像度 (論理 × RenderScale) で持つ — 150% でも子のテキストが鮮明
        if (_fb is null || _scale != ctx.RenderScale)
        {
            _scale = ctx.RenderScale;
            _pw = (uint)MathF.Ceiling(_w * _scale);
            _ph = (uint)MathF.Ceiling(_h * _scale);
            _fb?.Dispose();
            _fb = _device!.Malloc((ulong)((long)_pw * _ph * 4), GpuMemoryKind.DeviceLocal);   // GPU 内で完結
            _rendered = false;
        }

        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Content = new Scene2D().ImageRect(_fb!.BindlessIndex, _pw, _pw, _ph, 0, 0, _w, _h);
        node.Clip = new RectClip(0, 0, Size.Width, Size.Height);   // レイアウトサイズが surface より小さい場合にはみ出さない

        // ---- 入力ブリッジ (ローカル座標 = 子のクライアント座標) ----
        // textInput: TSF (実 IME) は ActiveTextInput 経由で文書を読み書きする — 転送しないと
        // 親ホストの TSF 文書が空 + CaretRect null (GetTextExt が常に TS_E_NOLAYOUT) になり、
        // 候補ウィンドウが既定位置に落ち、確定テキストも捨てられる。
        FocusTarget focus = ctx.AddFocusable(
            onFocus: f => Focused.Value = f,
            onKey: ke => Child!.KeyDown(ke.Key, ke.Shift, ke.Ctrl, ke.Alt),
            onText: t => Child!.Char(t),
            onComposeEx: comp => Child!.Compose(comp),
            onCommit: t => Child!.Commit(t),
            textInput: new ChildTextInput(this));
        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            focus: focus,
            onHover: h => { Hovered.Value = h; if (!h) Child!.PointerMove(-10000, -10000); },   // leave で子 hover 解除
            onMovePos: (lx, ly) => Child!.PointerMove(lx, ly),
            onDragStart: (lx, ly) => Child!.PointerDown(lx, ly),
            onDrag: (lx, ly) => Child!.PointerMove(lx, ly),
            onDragEnd: (lx, ly) => Child!.PointerUp(lx, ly),
            cursorFunc: () => Child?.CurrentCursor ?? CursorKind.Arrow,   // 子のカーソルを親へ転送
            onContext: (lx, ly) => Child!.ContextClick(lx, ly));          // 右クリックも子へ (メニューは子 canvas 内)
        ctx.AddScroll(node, new Rect(0, 0, Size.Width, Size.Height), onScrollPos: (lx, ly, d) => Child!.Wheel(lx, ly, d));

        // ---- 子の駆動: 親 UiHost の Tick で子を進め、変更があれば fb へ再ラスタライズ ----
        ctx.AddAnimation(dt =>
        {
            Child!.Tick(dt);
            if (!_rendered || _childCanvas!.HasPendingChanges)
            {
                RenderChild();
                node.Touch();   // 親 image ノードを dirty に (親の再合成を促す)
            }
            return false;   // 常駐
        });
    }

    private void RenderChild()
    {
        using GpuCommandBuffer cmd = _device!.MainQueue.StartCommandRecording();
        _childCanvas!.Render(cmd, new Camera2D { A = _scale, D = _scale }, _pw, _ph, _fb!, transparent: true);
        cmd.Finish();
        _device.MainQueue.SubmitAndWait(cmd);
        _rendered = true;
    }

    /// <summary>TSF/IME → 子 UiHost のフォーカス中テキスト入力へ委譲する面。
    /// caret 矩形は子キャンバス座標 → 親キャンバス座標へ平行移動 (子は 1:1 論理表示)。
    /// 子にテキスト入力フォーカスが無い間は空文書として振る舞う。</summary>
    private sealed class ChildTextInput(SurfaceView owner) : ITextInput
    {
        private UiHost? H => owner.Child;
        public string Text => H?.InputText ?? "";
        public (int start, int length) Selection => H?.InputSelection ?? (0, 0);
        public void Select(int start, int end) => H?.InputSelect(start, end);
        public void Replace(int start, int end, string s) => H?.InputReplace(start, end, s);
        public void SetComposition(ImeComposition comp) => H?.InputSetComposition(comp);
        public void CommitComposition(string final) => H?.InputCommit(final);
        public void SetCompositionHighlight(int start, int length, int targetStart, int targetLength)
            => H?.InputSetCompositionHighlight(start, length, targetStart, targetLength);
        public Rect CaretRect
        {
            get
            {
                Rect c = H?.CaretRect ?? new Rect(0, 0, 0, 0);
                return new Rect(owner.WorldPos.X + c.X, owner.WorldPos.Y + c.Y, c.Width, c.Height);
            }
        }
    }

    public override string DebugType => "SurfaceView";
    public override string? DebugDetail => $"{_w:0}x{_h:0}";

    public void Dispose()
    {
        Child?.Dispose();
        _childCanvas?.Dispose();
        _fb?.Dispose();
        Child = null; _childCanvas = null; _fb = null;
    }
}
