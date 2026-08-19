using System.Numerics;
using Luxel.NodeGraph;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;

using Luxel.Typography.TwoD;
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
    /// <summary>共有 document/controller。指定時は source より優先し、履歴も view 間で共有する。</summary>
    [UiParam] private readonly Bindable<GraphDocument?> _document = new();
    /// <summary>ビュー幅 (px、既定 480)。</summary>
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    /// <summary>ビュー高さ (px、既定 360)。</summary>
    [UiParam] private readonly Bindable<float> _viewHeight = new();

    /// <summary>ノード配置の設定 (タイトルバー高・ポート行高・ワイヤ接線など)。</summary>
    public GraphConfig Config { get; set; } = new();

    /// <summary>
    /// ノード内インライン枠を解決する NodeGraph 専用 resolver。context は node/key を常に提供し、
    /// decoration が parameter を宣言した場合は document-backed Signal も提供する。
    /// </summary>
    public Func<NodeWidgetContext, Widget?>? WidgetResolver { get; set; }

    /// <summary>ホストしたインライン widget のクリップ方式 (既定 = 枠でクリップ)。</summary>
    public WidgetClipMode WidgetClip { get; set; } = WidgetClipMode.Box;

    /// <summary>右クリック追加パレットのノードカタログ (null = パレット無効)。</summary>
    public INodeCatalog? NodeCatalog { get; set; }

    /// <summary>true = ノードドラッグのドロップ位置をグリッドにスナップする。</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>true = 読み取り専用 (編集不可)。ドラッグ = pan、クリック = 選択 (検査)、ホイール = ズームのみ。
    /// 移動・配線・削除・パレット追加を無効化する (DevTools のレンダーグラフ可視化など、閲覧用途)。</summary>
    public bool ReadOnly { get; set; }

    // ノード内にホストした実 Widget (画面空間に配置し毎 Refresh で WorldToScreen 追従)
    private readonly record struct WidgetHostKey(int NodeId, object Key, NodeParameter? Parameter);
    private sealed class Hosted
    {
        public required Widget Widget;
        public required UiNode Container;
        public required NodeWidgetContext Context;
        public Rect ScreenRect;
    }
    private readonly Dictionary<WidgetHostKey, Hosted> _widgets = new();
    private UiNode _widgetLayer = null!;

    private NodeGraphState _state = NodeGraphState.Create();
    private GraphDocument _documentModel = null!;
    private GraphGeometry? _geo;
    private bool _init;
    private float _fs = 13;
    private VectorFont? _font;

    // ドラッグ状態
    private enum Drag { None, Nodes, Marquee, Wire, Pan }
    private Drag _drag;
    private Vector2 _panStartPan;               // pan ドラッグ開始時の viewport.Pan
    private Vector2 _dragDelta;                 // ノードドラッグの累積移動量 (world)
    private int[] _dragNodes = [];              // ドラッグ中のノード id
    private Vector2 _marqStart, _marqCur;       // marquee の対角 (world)
    private NodeGraphState? _preview;           // ドラッグ中の描画用一時状態 (履歴に積まない)
    private PortId _wireFrom;                    // 配線ドラッグの出発ポート
    private Vector2 _wireEnd;                    // 配線ドラッグの現在端 (world)

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _world = null!, _overlay = null!;
    private UiNode _gridN = null!, _wireN = null!, _selWireN = null!, _pendingN = null!, _fillN = null!, _headerN = null!, _strokeN = null!, _selStrokeN = null!, _portN = null!, _titleN = null!;
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
    /// <summary>辺 (接続) 数。</summary>
    public int EdgeCount => _state.Doc.Edges.Count;
    /// <summary>選択ノード数。</summary>
    public int SelectionCount => _state.Selection.Nodes.Count;
    /// <summary>辺が選択されているか。</summary>
    public bool IsEdgeSelected(int edgeId) => _state.Selection.ContainsEdge(edgeId);
    /// <summary>ノードが選択されているか。</summary>
    public bool IsSelected(int nodeId) => _state.Selection.ContainsNode(nodeId);
    /// <summary>ノードの現在位置 (world、左上)。</summary>
    public Vector2 NodePos(int nodeId) => _state.Doc.Node(nodeId).Pos;
    /// <summary>pan/zoom。</summary>
    public GraphViewport Viewport => _state.Viewport;
    /// <summary>undo できるか。</summary>
    public bool CanUndo => _documentModel?.CanUndo ?? false;
    /// <summary>redo できるか。</summary>
    public bool CanRedo => _documentModel?.CanRedo ?? false;

    /// <summary>グラフが変わった (編集/undo/redo)。IEditorDocument アダプタのダーティ検知用。</summary>
    public Action<NodeGraphView>? OnEdit { get; set; }

    /// <summary>グラフを丸ごと差し替える (選択・履歴はリセット)。</summary>
    public void Load(NodeGraphDoc doc)
    {
        EnsureInit();
        _preview = null; _drag = Drag.None;
        _documentModel.Load(doc);
        SynchronizeBeforeRealize();
    }

    /// <summary>owner の装飾を差し替える (バッジ/ハイライト/ノード内インライン枠など)。文書は変えない。</summary>
    public void SetDecorations(string owner, GraphDecorationSet set)
    {
        EnsureInit();   // realize 前でも source doc を先に読み込む (でないと空グラフに装飾が乗り消える)
        _documentModel.Apply(_state.WithDecorations(owner, set));
        SynchronizeBeforeRealize();
    }

    /// <summary>ノードの画面中心 (ストーリーのクライアント座標) — play が d.Click/d.Drag に渡す用。</summary>
    public Vector2 NodeScreenCenter(int nodeId)
    {
        Vector2 local = _geo?.WorldToScreen(_state.Doc.Node(nodeId).Pos + HalfSize(nodeId)) ?? default;
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    /// <summary>ビューの空白点のクライアント座標 (world 原点付近など) — marquee/クリック空白の play 用。</summary>
    public Vector2 ClientOf(Vector2 world) => ClientOfWorld(world);

    /// <summary>ポートアンカーのクライアント座標 — 配線ドラッグの play 用。</summary>
    public Vector2 PortScreen(int nodeId, int portId)
        => ClientOfWorld(_geo?.PortAnchor(new PortId(nodeId, portId)) ?? default);

    /// <summary>辺の中点のクライアント座標 — 辺クリックの play 用。</summary>
    public Vector2 EdgeMidScreen(int edgeId)
        => ClientOfWorld(_geo?.Wire(edgeId).At(0.5f) ?? default);

    /// <summary>インライン枠 (Key) の中心クライアント座標 — ホストした widget を叩く play 用。</summary>
    public Vector2 SlotScreenCenter(object key)
    {
        foreach (WidgetSlot s in _geo?.WidgetSlots() ?? [])
            if (Equals(s.Key, key)) return ClientOfWorld(s.Rect.Center);
        return default;
    }

    private Vector2 ClientOfWorld(Vector2 world)
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
    public void Undo() { EnsureInit(); _documentModel.Undo(); SynchronizeBeforeRealize(); }
    /// <summary>redo 1 手。</summary>
    public void Redo() { EnsureInit(); _documentModel.Redo(); SynchronizeBeforeRealize(); }

    private void SetViewport(GraphViewport vp)
    {
        _documentModel.Apply(_state.WithViewport(vp));
        SynchronizeBeforeRealize();
    }

    // ---- レイアウト ----

    private void EnsureInit()
    {
        if (_init) return;
        _init = true;
        _documentModel = Document.Get() ?? new GraphDocument(Source.Get());
        _state = _documentModel.State;
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
        float inputLabelW = 0, outputLabelW = 0;
        foreach (NodePort p in n.Ports)
        {
            if (p.Dir == PortDir.In) inN++; else outN++;
            float labelW = _font?.Measure(p.Label, _fs).width ?? p.Label.Length * _fs * 0.6f;
            if (p.Dir == PortDir.In) inputLabelW = MathF.Max(inputLabelW, labelW);
            else outputLabelW = MathF.Max(outputLabelW, labelW);
        }
        float portLabelsW = inputLabelW + outputLabelW + Config.PortRadius * 4 + 20;
        float w = MathF.Max(120, MathF.Max(titleW + 28, portLabelsW));
        float h;
        if (n.Collapsed) h = Config.TitleBarHeight;
        else
        {
            int rows = Math.Max(inN, outN);
            h = Config.TitleBarHeight + Config.PortStartY + rows * Config.PortRowHeight + 6;
            // インライン枠: 高さを足し、宣言幅がノードに収まるようノード幅も広げる (スロット幅 = ノード内幅 − 2·padding)
            foreach (GraphDecoration d in _state.Decorations.All())
                if (d is NodeInlineDecoration nid && nid.NodeId == n.Id)
                {
                    h += nid.Height + Config.SlotGap;
                    w = MathF.Max(w, nid.Width + Config.SlotPadding * 2);
                }
        }
        return new NodeSize(w, h);
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        EnsureInit();
        _ctx = ctx;
        _documentModel.Changed += OnDocumentChanged;
        ctx.Own(new GraphDocumentSubscription(_documentModel, OnDocumentChanged));
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
        _selWireN = ctx.Canvas.AddChild(_world); _selWireN.Z = 2;
        _fillN = ctx.Canvas.AddChild(_world); _fillN.Z = 3;
        _headerN = ctx.Canvas.AddChild(_world); _headerN.Z = 4;
        _strokeN = ctx.Canvas.AddChild(_world); _strokeN.Z = 5;
        _selStrokeN = ctx.Canvas.AddChild(_world); _selStrokeN.Z = 6;
        _portN = ctx.Canvas.AddChild(_world); _portN.Z = 7;
        _titleN = ctx.Canvas.AddChild(_world); _titleN.Z = 8;
        _pendingN = ctx.Canvas.AddChild(_world); _pendingN.Z = 9;   // 進行中ワイヤ (色は DrawPending が可否で node.Color を設定)

        // ノード内 widget は画面空間レイヤ (WorldToScreen で配置 → zoom でヒットがずれない)。クリップは view 内。
        _widgetLayer = ctx.Canvas.AddChild(_root); _widgetLayer.Z = 50; _widgetLayer.Clip = new RectClip(0, 0, W, H);
        _widgets.Clear();   // 旧ホストは再実体化でスコープごと破棄済み — dict を捨てて Refresh で作り直す

        // marquee は画面空間のオーバーレイ (world 変換を受けない)
        _overlay = ctx.Canvas.AddChild(_root); _overlay.Z = 100; _overlay.ContentColors = true;

        ctx.Effect(() =>
        {
            Theme t = _theme.Value;
            _gridN.Color = Styles.WithAlpha(t.BorderColor, 40);
            _wireN.Color = t.TextMuted;
            _selWireN.Color = t.Primary;
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
            onDragStart: OnDragStart, onDrag: OnDrag, onDragEnd: OnDragEnd, onContext: OpenPalette);
        ctx.AddScroll(_root, new Rect(0, 0, W, H), onScrollPos: (d, x, y) => ZoomAt(d, new Vector2(x, y)));

        Refresh();
    }

    // ---- 入力 ----

    private Vector2 WorldAt(PointerEvent e) => _geo!.ScreenToWorld(new Vector2(e.X, e.Y));

    private void OnDragStart(PointerEvent e)
    {
        if (_geo is null) return;
        Focused.Value = true;
        // 中ボタンドラッグ = pan (編集モードでも。空白ドラッグは範囲選択に使うため別ボタンで対話 pan する)
        if (ReadOnly || e.Button == PointerButton.Middle)   // 閲覧: ドラッグは pan、離した位置が動いていなければクリック選択 (検査)
        {
            _drag = Drag.Pan;
            _panStartPan = _state.Viewport.Pan;
            return;
        }
        Vector2 world = WorldAt(e);
        GraphHit hit = _geo.HitTest(world);
        switch (hit.Kind)
        {
            case GraphHitKind.InputPort or GraphHitKind.OutputPort:
                // ポートから配線を引き始める (ノード選択・移動はしない)
                _drag = Drag.Wire;
                _wireFrom = new PortId(hit.NodeId, hit.PortId);
                _wireEnd = world;
                break;
            case GraphHitKind.Node when e.Ctrl:
                // Ctrl+Click = 選択トグル (追加/解除のみ、移動はしない)
                {
                    var ids = _state.Selection.Nodes.ToList();
                    bool had = ids.Remove(hit.NodeId);
                    if (!had) ids.Add(hit.NodeId);
                    _documentModel.Apply(GraphCommands.SelectNodes(_state, ids, had ? -1 : hit.NodeId));
                    _drag = Drag.None;
                }
                break;
            case GraphHitKind.Node:
                // ノードを掴む — 未選択なら単独選択に置換してから移動開始
                if (!_state.Selection.ContainsNode(hit.NodeId))
                    _documentModel.Apply(GraphCommands.SelectNodes(_state, [hit.NodeId], hit.NodeId));
                _drag = Drag.Nodes;
                _dragNodes = _state.Selection.Nodes.ToArray();
                _dragDelta = Vector2.Zero;
                _preview = _state;
                break;
            case GraphHitKind.Edge:
                // 辺をクリック → 選択 (Delete で切断できる)
                _documentModel.Apply(GraphCommands.Select(_state, GraphSelection.Edge(hit.EdgeId)));
                _drag = Drag.None;
                break;
            default:
                // 空白 — 選択を解除して marquee 開始
                _documentModel.Apply(GraphCommands.SelectNone(_state));
                _drag = Drag.Marquee;
                _marqStart = _marqCur = world;
                break;
        }
        Refresh();
    }

    private void OnDrag(PointerEvent e)
    {
        if (_geo is null) return;
        switch (_drag)
        {
            case Drag.Nodes:
                float zoom = MathF.Max(_state.Viewport.Zoom, 1e-4f);
                _dragDelta = new Vector2(e.DeltaX, e.DeltaY) / zoom;   // 画面移動量を world に
                _preview = GraphCommands.MoveNodes(_state, _dragNodes, _dragDelta).State;
                break;
            case Drag.Marquee:
                _marqCur = WorldAt(e);
                break;
            case Drag.Wire:
                _wireEnd = WorldAt(e);
                break;
            case Drag.Pan:
                _documentModel.Apply(_state.WithViewport(_state.Viewport with { Pan = _panStartPan + new Vector2(e.DeltaX, e.DeltaY) }));
                break;
            default: return;
        }
        Refresh();
    }

    private void OnDragEnd(PointerEvent e)
    {
        switch (_drag)
        {
            case Drag.Nodes:
                if (_dragDelta != Vector2.Zero) Apply(BuildMove());
                break;
            case Drag.Marquee:
                GraphRect box = GraphRect.FromCorners(_marqStart, _marqCur);
                if (box.Width > 2 || box.Height > 2)
                {
                    var ids = _state.Doc.Nodes.Where(n => _geo!.NodeRect(n.Id).Intersects(box)).Select(n => n.Id).ToList();
                    _documentModel.Apply(GraphCommands.SelectNodes(_state, ids));
                }
                break;
            case Drag.Wire:
                TryConnect(_geo!.HitTest(_wireEnd));
                break;
            case Drag.Pan:
                // ほぼ移動なし = クリック → 直下の対象を選択 (読み取り専用の検査)
                if (new Vector2(e.DeltaX, e.DeltaY).LengthSquared() < 9) SelectAt(WorldAt(e));
                break;
        }
        _drag = Drag.None; _preview = null; _dragDelta = Vector2.Zero;
        Refresh();
    }

    // world 点の直下を選択する (ノード/辺/空白)。文書非変更なので履歴には積まれない。
    private void SelectAt(Vector2 world)
    {
        GraphHit hit = _geo!.HitTest(world);
        GraphTransaction selection = hit.Kind switch
        {
            GraphHitKind.Node or GraphHitKind.InputPort or GraphHitKind.OutputPort => GraphCommands.SelectNodes(_state, [hit.NodeId], hit.NodeId),
            GraphHitKind.Edge => GraphCommands.Select(_state, GraphSelection.Edge(hit.EdgeId)),
            _ => GraphCommands.SelectNone(_state),
        };
        _documentModel.Apply(selection);
    }

    // 配線ドラッグの終端が互換ポートなら接続する (単入力ポートは既存の入力辺を置換 = 1 undo)
    private void TryConnect(GraphHit endHit)
    {
        if (endHit.Kind is not (GraphHitKind.InputPort or GraphHitKind.OutputPort)) return;
        var target = new PortId(endHit.NodeId, endHit.PortId);
        if (!GraphConnect.TryResolve(_state.Doc, _wireFrom, target, out PortId outP, out PortId inP)) return;

        var changes = new List<GraphChange>();
        // 単入力の In ポートが既に埋まっていれば付け替え (Multi は許容)
        NodePort inPort = _state.Doc.Port(inP)!;
        if (!inPort.Multi)
            foreach (GraphEdge ex in _state.Doc.Edges)
                if (ex.To == inP) changes.Add(new Disconnect(ex.Id));
        int id = NextEdgeId();
        changes.Add(new Connect(new GraphEdge(id, outP, inP)));
        Apply(_state.Update(new GraphTransactionSpec { Changes = changes, Selection = GraphSelection.Edge(id) }));
    }

    private int NextEdgeId()
    {
        int max = 0;
        foreach (GraphEdge e in _state.Doc.Edges) max = Math.Max(max, e.Id);
        return max + 1;
    }

    private int NextNodeId()
    {
        int max = 0;
        foreach (GraphNode n in _state.Doc.Nodes) max = Math.Max(max, n.Id);
        return max + 1;
    }

    // 右クリック → PopupPlacer 上の ContextMenu でノード追加パレット (クリック位置に生成)
    private void OpenPalette(PointerEvent e)
    {
        if (ReadOnly || NodeCatalog is null || _geo is null || NodeCatalog.Entries.Count == 0) return;
        Focused.Value = true;
        Vector2 world = WorldAt(e);
        var items = NodeCatalog.Entries
            .Select(entry => (entry.Label, (Action)(() => Apply(GraphCommands.AddNode(_state, entry.Create(NextNodeId(), world))))))
            .ToArray();
        ContextMenu.Open(_ctx, e.ScreenX, e.ScreenY, items);
    }

    // ---- ノード内インライン widget のホスト (WidgetResolver → 画面空間へ Realize、TextEditorView 移植) ----

    private void HostWidgets()
    {
        if (WidgetResolver is null) { if (_widgets.Count > 0) ClearWidgets(); return; }
        var seen = new HashSet<WidgetHostKey>();
        float zoom = _state.Viewport.Zoom;
        foreach (WidgetSlot slot in _geo!.WidgetSlots())
        {
            NodeInlineDecoration? decoration = _state.Decorations.All().OfType<NodeInlineDecoration>()
                .FirstOrDefault(candidate => candidate.NodeId == slot.NodeId && Equals(candidate.Key, slot.Key));
            var hostKey = new WidgetHostKey(slot.NodeId, slot.Key, decoration?.Parameter);
            seen.Add(hostKey);
            if (!_widgets.TryGetValue(hostKey, out Hosted? h))
            {
                var context = new NodeWidgetContext(_documentModel, slot, decoration?.Parameter, ReadOnly);
                if (WidgetResolver(context) is not { } w) { context.Dispose(); continue; }
                UiNode container = _ctx.Canvas.AddChild(_widgetLayer);
                h = new Hosted { Widget = w, Container = container, Context = context };
                _widgets[hostKey] = h;
            }
            Vector2 pos = _geo.WorldToScreen(slot.Rect.Min);   // world 枠 → 画面矩形 (zoom 反映)
            h.ScreenRect = new Rect(pos.X, pos.Y, slot.Rect.Width * zoom, slot.Rect.Height * zoom);
            RealizeWidget(h);
        }
        if (_widgets.Count > seen.Count)
            foreach (WidgetHostKey k in _widgets.Keys.Where(k => !seen.Contains(k)).ToList())
            { DisposeHosted(_widgets[k]); _widgets.Remove(k); }
    }

    private void RealizeWidget(Hosted h)
    {
        Widget w = h.Widget;
        w.Scope?.Release();
        var lc = new LayoutContext { Font = _ctx.Font, Theme = _theme.Peek(), ViewportW = W, ViewportH = H };
        w.Layout(new Constraints(0, h.ScreenRect.Width, 0, h.ScreenRect.Height), lc);
        w.Offset = new Point(h.ScreenRect.X, h.ScreenRect.Y);
        // 枠でクリップ (はみ出しを隠す)。RectClip は container のローカル空間 = _widgetLayer と同じ view-local px。
        h.Container.Clip = WidgetClip == WidgetClipMode.Box
            ? new RectClip(h.ScreenRect.X, h.ScreenRect.Y, h.ScreenRect.Width, h.ScreenRect.Height) : null;
        // container (=_widgetLayer 直下・無変換) の world 原点は view の WorldPos。CreateRoot が
        // WorldPos = worldOrigin + Offset とするので rect を二重に足さない (d.Click が外れないように)。
        w.Realize(_ctx, h.Container, new Point(WorldPos.X, WorldPos.Y));
        w.ParentWidget = this;
    }

    private void ClearWidgets()
    {
        foreach (Hosted h in _widgets.Values) DisposeHosted(h);
        _widgets.Clear();
    }

    private void DisposeHosted(Hosted h)
    {
        h.Context.Dispose();
        h.Widget.Scope?.Release();
        (h.Widget as IDisposable)?.Dispose();
        _ctx.Canvas.Remove(h.Container);
    }

    protected override bool OnChildNeedsRealize(Widget child)
    {
        foreach (Hosted h in _widgets.Values)
            if (ReferenceEquals(h.Widget, child)) { RealizeWidget(h); return true; }
        return false;
    }

    // ドラッグ移動を 1 トランザクションに (スナップ時はノードごとにグリッド整合させる)
    private GraphTransaction BuildMove()
    {
        if (!SnapToGrid) return GraphCommands.MoveNodes(_state, _dragNodes, _dragDelta);
        var changes = _dragNodes.Select(id =>
        {
            Vector2 cur = _state.Doc.Node(id).Pos;
            Vector2 tgt = SnapPos(cur + _dragDelta);
            return (GraphChange)new MoveNode(id, tgt - cur);
        }).ToList();
        return _state.Update(new GraphTransactionSpec { Changes = changes });
    }

    private static Vector2 SnapPos(Vector2 p)
        => new(MathF.Round(p.X / GridStep) * GridStep, MathF.Round(p.Y / GridStep) * GridStep);

    /// <summary>辺の依存に沿ってノードを左→右に自動整列する (1 undo)。</summary>
    public void AutoLayout()
    {
        if (_geo is null) return;
        Apply(GraphCommands.AutoLayout(_state, MeasureNode));
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
        if (ReadOnly)   // 閲覧: 編集キーは無効、Esc の選択解除のみ
        {
            if (ev.Key == Key.Escape) { _documentModel.Apply(GraphCommands.SelectNone(_state)); Refresh(); return true; }
            return false;
        }
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

    /// <summary>外部 command/automation から同じ document/history 経路で編集する。</summary>
    public void ApplyEdit(params GraphChange[] changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (ReadOnly) throw new InvalidOperationException("A read-only node graph cannot be edited.");
        EnsureInit();
        _documentModel.Apply(changes);
        SynchronizeBeforeRealize();
    }

    private void Apply(GraphTransaction tr)
    {
        _documentModel.Apply(tr);
        SynchronizeBeforeRealize();
    }

    private void SynchronizeBeforeRealize()
    {
        if (_ctx is null) _state = _documentModel.State;
    }

    private void OnDocumentChanged(GraphDocument document, bool docChanged)
    {
        _state = document.State;
        Refresh();
        if (docChanged) OnEdit?.Invoke(this);
    }

    private sealed class GraphDocumentSubscription(GraphDocument document, Action<GraphDocument, bool> handler) : IDisposable
    {
        public void Dispose() => document.Changed -= handler;
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
        DrawPending();
        DrawMarquee();
        HostWidgets();
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
        var sel = new Scene2D();
        foreach (GraphEdge e in eff.Doc.Edges)
        {
            GraphWire w = _geo!.Wire(e.Id);
            Scene2D dst = _state.Selection.ContainsEdge(e.Id) ? sel : s;
            dst.BeginStroke(Color2D.White, _state.Selection.ContainsEdge(e.Id) ? 2.5f : 2f)
               .MoveTo(w.P0.X, w.P0.Y).CubicTo(w.C0.X, w.C0.Y, w.C1.X, w.C1.Y, w.P1.X, w.P1.Y).End();
        }
        _wireN.Content = s;
        _selWireN.Content = sel;
    }

    // 進行中ワイヤ (配線ドラッグ中) — 終端が互換ポートなら緑、そうでなければ赤で焼き込む
    private void DrawPending()
    {
        if (_drag != Drag.Wire || _geo is null) { _pendingN.Content = null; return; }
        Vector2 p0 = _geo.PortAnchor(_wireFrom);
        GraphHit end = _geo.HitTest(_wireEnd);
        bool valid = end.Kind is GraphHitKind.InputPort or GraphHitKind.OutputPort
                     && GraphConnect.CanConnect(_state.Doc, _wireFrom, new PortId(end.NodeId, end.PortId));
        _pendingN.Color = valid ? Color2D.Rgba(63, 185, 80) : Color2D.Rgba(229, 83, 75);   // 緑=可 / 赤=不可
        float t = MathF.Max(Config.WireTangent, MathF.Abs(_wireEnd.X - p0.X) * 0.5f);
        // 出発が入力なら接線を左へ、出力なら右へ
        float dir = _state.Doc.Port(_wireFrom)!.Dir == PortDir.Out ? 1 : -1;
        var s = new Scene2D();
        s.BeginStroke(Color2D.White, 2).MoveTo(p0.X, p0.Y)
         .CubicTo(p0.X + t * dir, p0.Y, _wireEnd.X - t * dir, _wireEnd.Y, _wireEnd.X, _wireEnd.Y).End();
        s.FillCircle(Color2D.White, _wireEnd.X, _wireEnd.Y, 3);
        _pendingN.Content = s;
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

            if (_font is { } font)
            {
                if (node.Title.Length > 0)
                {
                    (float tw, float th) = font.Measure(node.Title, _fs);
                    float baseline = box.Y + (MathF.Min(tb, box.Height) - th) / 2 + font.Ascent(_fs);
                    font.AppendText(titles, node.Title, box.X + 8, baseline, _fs, Color2D.White);
                }

                if (!node.Collapsed)
                {
                    foreach (PortGeometry p in nl.Inputs) AppendPortLabel(font, titles, node.Port(p.Port.Port)!, p.Anchor, input: true, pr);
                    foreach (PortGeometry p in nl.Outputs) AppendPortLabel(font, titles, node.Port(p.Port.Port)!, p.Anchor, input: false, pr);
                }
            }
        }

        _fillN.Content = fill;
        _headerN.Content = header;
        _strokeN.Content = stroke;
        _selStrokeN.Content = selStroke;
        _portN.Content = ports;
        _titleN.Content = titles;
    }

    private void AppendPortLabel(VectorFont font, Scene2D scene, NodePort port, Vector2 anchor, bool input, float portRadius)
    {
        if (port.Label.Length == 0) return;
        (float width, float height) = font.Measure(port.Label, _fs);
        float x = input ? anchor.X + portRadius + 4 : anchor.X - portRadius - 4 - width;
        float baseline = anchor.Y - height * 0.5f + font.Ascent(_fs);
        font.AppendText(scene, port.Label, x, baseline, _fs, Color2D.White);
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
