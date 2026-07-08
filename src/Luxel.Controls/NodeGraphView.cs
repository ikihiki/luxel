using System.Numerics;
using Luxel.NodeGraph;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 汎用ノードエディタのビュー (ADR-0009 / ToDo 25) — ノード + ポート + 接続線を編集するキャンバス。編集意味論・座標写像・
/// 装飾・幾何は canvas 非依存の <see cref="Luxel.NodeGraph"/> が持ち、この widget は入力を <see cref="GraphTransaction"/> にして
/// <see cref="GraphGeometry"/> の矩形/ベジェを塗るだけ (テキストの <see cref="TextEditorView"/> と同じ薄さ)。pan/zoom は
/// world コンテナノードの <see cref="Affine2D"/> 変換なのでヒットテストが自動追従する。
/// </summary>
[UiComponent]
public sealed partial class NodeGraphView : Widget
{
    /// <summary>初期グラフ (ノード + 辺)。null/空なら空グラフ。</summary>
    [UiParam] private readonly Bindable<NodeGraphDoc> _source = new();
    /// <summary>ビュー幅 (px、既定 480)。</summary>
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    /// <summary>ビュー高さ (px、既定 360)。</summary>
    [UiParam] private readonly Bindable<float> _viewHeight = new();

    /// <summary>ノード配置の設定 (タイトルバー高・ポート行高・ワイヤ接線など)。</summary>
    public GraphConfig Config { get; set; } = new();

    private NodeGraphState _state = NodeGraphState.Create();
    private readonly GraphHistory _history = new();
    private GraphGeometry? _geo;
    private bool _init;
    private float _fs = 13;
    private VectorFont? _font;

    // ドラッグ状態
    private enum Drag { None, Nodes, Marquee }
    private Drag _drag;
    private Vector2 _dragDelta;                 // ノードドラッグの累積移動量 (world)
    private int[] _dragNodes = [];              // ドラッグ中のノード id
    private Vector2 _marqStart, _marqCur;       // marquee の対角 (world)
    private NodeGraphState? _preview;           // ドラッグ中の描画用一時状態 (履歴に積まない)

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _world = null!, _overlay = null!;
    private UiNode _gridN = null!, _wireN = null!, _fillN = null!, _headerN = null!, _strokeN = null!, _selStrokeN = null!, _portN = null!, _titleN = null!;
    private FocusTarget? _focus;

    private const float GridStep = 40f;

    private float W => MathF.Max(160, ViewWidth.Or(480));
    private float H => MathF.Max(120, ViewHeight.Or(360));

    public override string DebugType => "NodeGraphView";
    public override string? DebugDetail => $"{_state.Doc.Nodes.Count} ノード";

    // ---- 公開 API (テスト/play/外部) ----

    /// <summary>現在の状態 (不変スナップショット)。</summary>
    public NodeGraphState Graph => _state;
    /// <summary>ノード数。</summary>
    public int NodeCount => _state.Doc.Nodes.Count;
    /// <summary>選択ノード数。</summary>
    public int SelectionCount => _state.Selection.Nodes.Count;
    /// <summary>ノードが選択されているか。</summary>
    public bool IsSelected(int nodeId) => _state.Selection.ContainsNode(nodeId);
    /// <summary>ノードの現在位置 (world、左上)。</summary>
    public Vector2 NodePos(int nodeId) => _state.Doc.Node(nodeId).Pos;
    /// <summary>pan/zoom。</summary>
    public GraphViewport Viewport => _state.Viewport;
    /// <summary>undo できるか。</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>グラフを丸ごと差し替える (選択・履歴はリセット)。</summary>
    public void Load(NodeGraphDoc doc)
    {
        _state = NodeGraphState.Create(doc);
        _history.Clear();
        _preview = null; _drag = Drag.None;
        Refresh();
    }

    /// <summary>ノードの画面中心 (ストーリーのクライアント座標) — play が d.Click/d.Drag に渡す用。</summary>
    public Vector2 NodeScreenCenter(int nodeId)
    {
        Vector2 local = _geo?.WorldToScreen(_state.Doc.Node(nodeId).Pos + HalfSize(nodeId)) ?? default;
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    /// <summary>ビューの空白点のクライアント座標 (world 原点付近など) — marquee/クリック空白の play 用。</summary>
    public Vector2 ClientOf(Vector2 world)
    {
        Vector2 local = _geo?.WorldToScreen(world) ?? world;
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    private Vector2 HalfSize(int nodeId)
    {
        GraphRect r = _geo!.NodeRect(nodeId);
        return new Vector2(r.Width * 0.5f, r.Height * 0.5f);
    }

    /// <summary>pan を相対移動する。</summary>
    public void PanBy(Vector2 deltaScreen) => SetViewport(_state.Viewport with { Pan = _state.Viewport.Pan + deltaScreen });
    /// <summary>viewport を原点・等倍に戻す。</summary>
    public void ResetView() => SetViewport(GraphViewport.Default);

    /// <summary>全ノードが収まるよう pan/zoom を合わせる。</summary>
    public void FitToView()
    {
        if (_geo is null || _state.Doc.Nodes.Count == 0) { ResetView(); return; }
        GraphRect b = _geo.ContentBounds();
        const float margin = 30f;
        float zx = (W - margin * 2) / MathF.Max(1, b.Width), zy = (H - margin * 2) / MathF.Max(1, b.Height);
        float zoom = Math.Clamp(MathF.Min(zx, zy), 0.25f, 2f);
        var pan = new Vector2(margin - b.X * zoom + (W - margin * 2 - b.Width * zoom) * 0.5f,
                              margin - b.Y * zoom + (H - margin * 2 - b.Height * zoom) * 0.5f);
        SetViewport(new GraphViewport(pan, zoom));
    }

    /// <summary>undo 1 手。</summary>
    public void Undo() { _state = _history.Undo(_state); Refresh(); }
    /// <summary>redo 1 手。</summary>
    public void Redo() { _state = _history.Redo(_state); Refresh(); }

    private void SetViewport(GraphViewport vp) { _state = _state.WithViewport(vp).State; Refresh(); }

    // ---- レイアウト ----

    private void EnsureInit()
    {
        if (_init) return;
        _init = true;
        NodeGraphDoc? doc = Source.Get();
        if (doc is not null) _state = NodeGraphState.Create(doc);
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        EnsureInit();
        _fs = ctx.Theme.FontSm;
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    // view が注入する測定: タイトル幅 + ポート行数 + インライン枠から実寸を決める (core を Typography 非依存に保つ境界)
    private NodeSize MeasureNode(GraphNode n)
    {
        float titleW = _font?.Measure(n.Title, _fs).width ?? n.Title.Length * _fs * 0.6f;
        int inN = 0, outN = 0;
        foreach (NodePort p in n.Ports) { if (p.Dir == PortDir.In) inN++; else outN++; }
        float w = MathF.Max(120, titleW + 28);
        float h;
        if (n.Collapsed) h = Config.TitleBarHeight;
        else
        {
            int rows = Math.Max(inN, outN);
            h = Config.TitleBarHeight + Config.PortStartY + rows * Config.PortRowHeight + 6;
            foreach (GraphDecoration d in _state.Decorations.All())
                if (d is NodeInlineDecoration nid && nid.NodeId == n.Id) h += nid.Height + Config.SlotGap;
        }
        return new NodeSize(w, h);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        EnsureInit();
        _ctx = ctx;
        _theme = ctx.Theme;
        _fs = _theme.Peek().FontSm;
        _font = ctx.Font;
        _geo = new GraphGeometry(Config, MeasureNode, _state);

        _root = CreateRoot(ctx, parent, worldOrigin);
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = _theme.Value.Background);
        FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        // クリップ内に world コンテナ (pan/zoom 変換)。world 座標の内容はすべてこの子。
        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 1;
        clip.Clip = new RectClip(0, 0, W, H);
        _world = ctx.Canvas.AddChild(clip);

        _gridN = ctx.Canvas.AddChild(_world); _gridN.Z = 0;
        _wireN = ctx.Canvas.AddChild(_world); _wireN.Z = 1;
        _fillN = ctx.Canvas.AddChild(_world); _fillN.Z = 2;
        _headerN = ctx.Canvas.AddChild(_world); _headerN.Z = 3;
        _strokeN = ctx.Canvas.AddChild(_world); _strokeN.Z = 4;
        _selStrokeN = ctx.Canvas.AddChild(_world); _selStrokeN.Z = 5;
        _portN = ctx.Canvas.AddChild(_world); _portN.Z = 6;
        _titleN = ctx.Canvas.AddChild(_world); _titleN.Z = 7;

        // marquee は画面空間のオーバーレイ (world 変換を受けない)
        _overlay = ctx.Canvas.AddChild(_root); _overlay.Z = 100; _overlay.ContentColors = true;

        ctx.Effect(() =>
        {
            Theme t = _theme.Value;
            _gridN.Color = Styles.WithAlpha(t.BorderColor, 40);
            _wireN.Color = t.TextMuted;
            _fillN.Color = t.Surface;
            _headerN.Color = t.SurfaceAlt;
            _strokeN.Color = t.BorderColor;
            _selStrokeN.Color = t.Primary;
            _portN.Color = t.TextMuted;
            _titleN.Color = t.Text;
        });

        // テーマ変化でフォント/幾何を作り直す (稀なので全再構築)
        ctx.Effect(() => { _ = _theme.Value; _geo?.Configure(Config, MeasureNode); Refresh(); });

        _focus ??= new FocusTarget { OnFocus = on => Focused.Value = on, OnKey = OnKey };
        FocusTarget f = ctx.AddFocusable(_focus);

        ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f, cursor: CursorKind.Arrow,
            onDragStart: OnDragStart, onDrag: OnDrag, onDragEnd: OnDragEnd);
        ctx.AddScroll(_root, new Rect(0, 0, W, H), onScrollPos: (d, x, y) => ZoomAt(d, new Vector2(x, y)));

        Refresh();
    }

    // ---- 入力 ----

    private Vector2 WorldAt(PointerEvent e) => _geo!.ScreenToWorld(new Vector2(e.X, e.Y));

    private void OnDragStart(PointerEvent e)
    {
        if (_geo is null) return;
        Focused.Value = true;
        GraphHit hit = _geo.HitTest(WorldAt(e));
        if (hit.Kind is GraphHitKind.Node or GraphHitKind.InputPort or GraphHitKind.OutputPort)
        {
            // ノード (or そのポート) を掴む — 未選択なら単独選択に置換してから移動開始
            if (!_state.Selection.ContainsNode(hit.NodeId))
                _state = GraphCommands.SelectNodes(_state, [hit.NodeId], hit.NodeId).State;
            _drag = Drag.Nodes;
            _dragNodes = _state.Selection.Nodes.ToArray();
            _dragDelta = Vector2.Zero;
            _preview = _state;
        }
        else
        {
            // 空白 (or 辺) — 選択を解除して marquee 開始
            _state = GraphCommands.SelectNone(_state).State;
            _drag = Drag.Marquee;
            _marqStart = _marqCur = WorldAt(e);
            _preview = null;
        }
        Refresh();
    }

    private void OnDrag(PointerEvent e)
    {
        if (_geo is null || _drag == Drag.None) return;
        float zoom = MathF.Max(_state.Viewport.Zoom, 1e-4f);
        if (_drag == Drag.Nodes)
        {
            _dragDelta = new Vector2(e.DeltaX, e.DeltaY) / zoom;   // 画面移動量を world に
            _preview = GraphCommands.MoveNodes(_state, _dragNodes, _dragDelta).State;
        }
        else
        {
            _marqCur = WorldAt(e);
        }
        Refresh();
    }

    private void OnDragEnd(PointerEvent e)
    {
        if (_drag == Drag.Nodes)
        {
            if (_dragDelta != Vector2.Zero)
                Apply(GraphCommands.MoveNodes(_state, _dragNodes, _dragDelta));
        }
        else if (_drag == Drag.Marquee)
        {
            GraphRect box = GraphRect.FromCorners(_marqStart, _marqCur);
            if (box.Width > 2 || box.Height > 2)
            {
                var ids = _state.Doc.Nodes.Where(n => _geo!.NodeRect(n.Id).Intersects(box)).Select(n => n.Id).ToList();
                _state = GraphCommands.SelectNodes(_state, ids).State;
            }
        }
        _drag = Drag.None; _preview = null; _dragDelta = Vector2.Zero;
        Refresh();
    }

    private void ZoomAt(float delta, Vector2 cursorLocal)
    {
        if (_geo is null) return;
        GraphViewport vp = _state.Viewport;
        float nz = Math.Clamp(vp.Zoom * MathF.Pow(1.1f, delta), 0.25f, 4f);
        Vector2 world = (cursorLocal - vp.Pan) / vp.Zoom;      // カーソル下の world 点を固定
        Vector2 pan = cursorLocal - world * nz;
        SetViewport(new GraphViewport(pan, nz));
    }

    private bool OnKey(KeyEvent ev)
    {
        switch (ev.Key)
        {
            case Key.Z when ev.Ctrl: Undo(); return true;
            case Key.Y when ev.Ctrl: Redo(); return true;
            case Key.A when ev.Ctrl: Apply(GraphCommands.SelectAll(_state)); return true;
            case Key.Delete or Key.Backspace: Apply(GraphCommands.DeleteSelection(_state)); return true;
            case Key.Escape: Apply(GraphCommands.SelectNone(_state)); return true;
            default: return false;
        }
    }

    private void Apply(GraphTransaction tr)
    {
        if (tr.DocChanged) _history.Record(tr);
        _state = tr.State;
        Refresh();
    }

    // ---- 描画 ----

    private NodeGraphState Effective() => _preview ?? _state;

    private Affine2D ViewportAffine(GraphViewport vp)
        => new() { A = vp.Zoom, B = 0, C = 0, D = vp.Zoom, E = vp.Pan.X, F = vp.Pan.Y };

    private void Refresh()
    {
        if (_ctx is null || _geo is null) return;
        NodeGraphState eff = Effective();
        _geo.SetState(eff);
        _world.Transform = ViewportAffine(eff.Viewport);
        DrawGrid(eff.Viewport);
        DrawWires(eff);
        DrawNodes(eff);
        DrawMarquee();
    }

    private void DrawGrid(GraphViewport vp)
    {
        var s = new Scene2D();
        if (vp.Zoom * GridStep >= 6f)   // ズームアウトしすぎたらグリッドは省く
        {
            Vector2 tl = _geo!.ScreenToWorld(new Vector2(0, 0)), br = _geo.ScreenToWorld(new Vector2(W, H));
            float x0 = MathF.Floor(tl.X / GridStep) * GridStep, x1 = br.X;
            float y0 = MathF.Floor(tl.Y / GridStep) * GridStep, y1 = br.Y;
            int guard = 0;
            for (float x = x0; x <= x1 && guard < 500; x += GridStep, guard++) s.StrokeLine(Color2D.White, 1, x, tl.Y, x, br.Y);
            for (float y = y0; y <= y1 && guard < 1000; y += GridStep, guard++) s.StrokeLine(Color2D.White, 1, tl.X, y, br.X, y);
        }
        _gridN.Content = s;
    }

    private void DrawWires(NodeGraphState eff)
    {
        var s = new Scene2D();
        foreach (GraphEdge e in eff.Doc.Edges)
        {
            GraphWire w = _geo!.Wire(e.Id);
            s.BeginStroke(Color2D.White, 2).MoveTo(w.P0.X, w.P0.Y).CubicTo(w.C0.X, w.C0.Y, w.C1.X, w.C1.Y, w.P1.X, w.P1.Y).End();
        }
        _wireN.Content = s;
    }

    private void DrawNodes(NodeGraphState eff)
    {
        var fill = new Scene2D();
        var header = new Scene2D();
        var stroke = new Scene2D();
        var selStroke = new Scene2D();
        var ports = new Scene2D();
        var titles = new Scene2D();
        float r = Config.NodeCornerRadius, tb = Config.TitleBarHeight, pr = Config.PortRadius;

        foreach (NodeLayout nl in _geo!.Layouts)
        {
            GraphRect box = nl.Rect;
            GraphNode node = eff.Doc.Node(nl.NodeId);
            fill.FillRoundedRect(Color2D.White, box.X, box.Y, box.Width, box.Height, r);
            header.FillRect(Color2D.White, box.X, box.Y, box.Width, MathF.Min(tb, box.Height));

            bool sel = _state.Selection.ContainsNode(nl.NodeId);
            (sel ? selStroke : stroke).StrokeRoundedRect(Color2D.White, sel ? 2f : 1.4f, box.X, box.Y, box.Width, box.Height, r);

            foreach (PortGeometry p in nl.Inputs) ports.FillCircle(Color2D.White, p.Anchor.X, p.Anchor.Y, pr - 1);
            foreach (PortGeometry p in nl.Outputs) ports.FillCircle(Color2D.White, p.Anchor.X, p.Anchor.Y, pr - 1);

            if (_font is { } font && node.Title.Length > 0)
            {
                (float tw, float th) = font.Measure(node.Title, _fs);
                float baseline = box.Y + (MathF.Min(tb, box.Height) - th) / 2 + font.Ascent(_fs);
                font.AppendText(titles, node.Title, box.X + 8, baseline, _fs, Color2D.White);
            }
        }

        _fillN.Content = fill;
        _headerN.Content = header;
        _strokeN.Content = stroke;
        _selStrokeN.Content = selStroke;
        _portN.Content = ports;
        _titleN.Content = titles;
    }

    private void DrawMarquee()
    {
        if (_drag != Drag.Marquee) { _overlay.Content = null; return; }
        Vector2 a = _geo!.WorldToScreen(_marqStart), b = _geo.WorldToScreen(_marqCur);
        float x = MathF.Min(a.X, b.X), y = MathF.Min(a.Y, b.Y), w = MathF.Abs(a.X - b.X), h = MathF.Abs(a.Y - b.Y);
        Theme t = _theme.Peek();
        var s = new Scene2D();
        s.FillRect(Styles.WithAlpha(t.Primary, 40), x, y, w, h);
        s.StrokeRoundedRect(t.Primary, 1, x, y, w, h, 0);
        _overlay.Content = s;
    }
}
