using System.Text.Json;
using System.Numerics;
using Luxel.Controls;
using Luxel.Diagnostics;
using Luxel.NodeGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Editor.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>NodeGraphView — 汎用ノードエディタ (ADR-0009 / ToDo 25) のビュー。ノード + ポート + 接続線を編集する。
/// 編集意味論・座標写像・装飾・幾何は canvas 非依存の Luxel.NodeGraph が持ち、この widget は入力を Transaction にして
/// ジオメトリのベジェ/矩形を塗るだけ (テキスト新スタックの TextEditorView と同じ薄さ)。pan/zoom は world コンテナの
/// Affine2D 変換なのでヒットテストが自動追従する。</summary>
[StoryMeta("Controls/Editor/NodeGraphView")]
public static class NodeGraphViewStory
{
    // Input → Process → Output の 3 ノード + 2 辺
    private static NodeGraphDoc SampleGraph()
    {
        var input = new GraphNode(1, "source", "Input", new Vector2(30, 40),
            [new NodePort(0, PortDir.Out, "v", "value")]);
        var proc = new GraphNode(2, "op", "Process", new Vector2(240, 90),
            [new NodePort(0, PortDir.In, "v", "in"), new NodePort(1, PortDir.Out, "v", "out")]);
        var outp = new GraphNode(3, "sink", "Output", new Vector2(450, 150),
            [new NodePort(0, PortDir.In, "v", "in")]);
        return NodeGraphDoc.Of([input, proc, outp],
            [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0)),
             new GraphEdge(11, new PortId(2, 1), new PortId(3, 0))]);
    }

    [Story(ShortDescription = "ノードの選択、移動、配線、undo を一つのグラフ文書で確認する基本例です。")]
    public static StoryResult Basic(StoryContext ctx)
    {
        NodeGraphView ed = NodeGraphView(source: SampleGraph(), viewWidth: 620f, viewHeight: 360f);

        ctx.Play("basic", async d =>
        {
            await d.Snap();                              // 3 ノード + 2 ワイヤ + グリッド
            Vector2 p2 = ed.NodeScreenCenter(2);
            await d.Click(p2.X, p2.Y);                   // Process を選択
            await d.Expect(() => ed.IsSelected(2), "クリックでノード選択");
            await d.Snap("selected");
            Vector2 from = ed.NodeScreenCenter(2);
            await d.Drag(from.X, from.Y, from.X + 60, from.Y + 90);   // ドラッグで移動
            await d.Expect(() => ed.NodePos(2).Y > 90, "ドラッグで移動 (1 undo)");
            await d.Snap("moved");
        });

        ctx.Play("marquee", async d =>
        {
            // 空白から Input+Process を囲む範囲選択 (world 座標をクライアント座標へ)
            Vector2 a = ed.ClientOf(new Vector2(10, 10));
            Vector2 b = ed.ClientOf(new Vector2(380, 200));
            await d.Drag(a.X, a.Y, b.X, b.Y);
            await d.Expect(() => ed.SelectionCount == 2, "範囲選択で 2 ノード");
            await d.Snap("box");
        });

        ctx.Play("keys", async d =>
        {
            Vector2 p1 = ed.NodeScreenCenter(1);
            await d.Click(p1.X, p1.Y);                   // フォーカス
            await d.Key(Key.A, ctrl: true);              // 全選択
            await d.Expect(() => ed.SelectionCount == 3, "Ctrl+A で全選択");
            await d.Snap("all");
            await d.Key(Key.Delete);                     // 削除
            await d.Expect(() => ed.NodeCount == 0, "Delete で全削除");
            await d.Snap("deleted");
            await d.Key(Key.Z, ctrl: true);              // undo
            await d.Expect(() => ed.NodeCount == 3, "undo で復活");
        });

        ctx.Play("modifiers", async d =>
        {
            // Ctrl+Click で追加選択 (ADR-0011: PointerEvent の修飾キー)
            Vector2 p1 = ed.NodeScreenCenter(1);
            Vector2 p3 = ed.NodeScreenCenter(3);
            await d.Click(p1.X, p1.Y);                                  // Input を選択
            await d.Click(p3.X, p3.Y, KeyModifiers.Ctrl);              // Ctrl+Click で Output も追加
            await d.Expect(() => ed.IsSelected(1) && ed.IsSelected(3) && ed.SelectionCount == 2, "Ctrl+Click で追加選択");
            await d.Snap("ctrl-select");
            // 中ボタンドラッグで pan (ノード上から始めても移動でなく pan になる)
            Vector2 pc = ed.NodeScreenCenter(2);
            Vector2 pan0 = ed.Viewport.Pan;
            await d.Drag(pc.X, pc.Y, pc.X + 70, pc.Y + 40, button: PointerButton.Middle);
            await d.Expect(() => ed.Viewport.Pan != pan0, "中ボタンドラッグで pan");
            await d.Snap("panned");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView (汎用ノードエディタ)"),
                Muted("Luxel.NodeGraph (不変 + Transaction + 純射影) を canvas に載せた薄いビュー。入力/出力 port label、ドラッグ移動 / クリック・範囲選択 / ホイールズーム / 中ボタン pan / Ctrl+Click 追加選択。"),
                ed]];
    }

    // 配線用: A(out num) → B(in/out num) → C(in num)、初期辺 A→B
    private static NodeGraphDoc WireGraph()
    {
        var a = new GraphNode(1, "source", "A", new Vector2(40, 60), [new NodePort(0, PortDir.Out, "num", "out")]);
        var b = new GraphNode(2, "op", "B", new Vector2(280, 60),
            [new NodePort(0, PortDir.In, "num", "in"), new NodePort(1, PortDir.Out, "num", "out")]);
        var c = new GraphNode(3, "sink", "C", new Vector2(280, 210), [new NodePort(0, PortDir.In, "num", "in")]);
        return NodeGraphDoc.Of([a, b, c], [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0))]);
    }

    [Story(ShortDescription = "ポートの型を検査しながら接続を作成・拒否し、配線編集の判断を確認します。")]
    public static StoryResult Wiring(StoryContext ctx)
    {
        NodeGraphView ed = NodeGraphView(source: WireGraph(), viewWidth: 620f, viewHeight: 380f);

        ctx.Play("connect", async d =>
        {
            await d.Snap();                              // 初期: A→B の 1 辺
            Vector2 from = ed.PortScreen(2, 1);          // B の出力
            Vector2 to = ed.PortScreen(3, 0);            // C の入力
            // 進行中ワイヤを互換ポート上で撮る (手動ポインタ列)
            d.Host.PointerDown(from.X, from.Y);
            await d.Step(1);
            d.Host.PointerMove(to.X, to.Y);              // C の入力に重ねる → 緑
            await d.Step(1);
            await d.Snap("dragging");                    // 緑の進行中ワイヤ (互換ポート上)
            d.Host.PointerUp(to.X, to.Y);
            await d.Step(2);
            await d.Expect(() => ed.EdgeCount == 2, "互換ポートで接続");
            await d.Snap("connected");
        });

        ctx.Play("disconnect", async d =>
        {
            Vector2 mid = ed.EdgeMidScreen(10);          // A→B の中点
            await d.Click(mid.X, mid.Y);                 // 辺を選択 (+ フォーカス)
            await d.Expect(() => ed.IsEdgeSelected(10), "辺クリックで選択");
            await d.Snap("selected");
            await d.Key(Key.Delete);                     // 切断
            await d.Expect(() => ed.EdgeCount == 0, "Delete で切断");
            await d.Snap("cut");
        });

        ctx.Play("invalid", async d =>
        {
            Vector2 aOut = ed.PortScreen(1, 0);          // A の出力
            Vector2 bOut = ed.PortScreen(2, 1);          // B の出力 (同方向 = 不可)
            await d.Drag(aOut.X, aOut.Y, bOut.X, bOut.Y);
            await d.Expect(() => ed.EdgeCount == 1, "同方向ポートは接続不可");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView — 配線"),
                Muted("ポートからドラッグ → 進行中ワイヤ (緑=可/赤=不可) → 互換ポートで接続。辺クリック選択 → Delete で切断。"),
                ed]];
    }

    private static GraphNode NumNode(int id, string kind, string title, Vector2 pos, bool hasIn, bool hasOut)
    {
        var ports = new List<NodePort>();
        if (hasIn) ports.Add(new NodePort(0, PortDir.In, "num", "in"));
        if (hasOut) ports.Add(new NodePort(1, PortDir.Out, "num", "out"));
        return new GraphNode(id, kind, title, pos, ports);
    }

    [Story(ShortDescription = "ノード内の parameter widget とグラフ操作を共存させ、入力の優先順位を確認します。")]
    public static StoryResult Widgets(StoryContext ctx)
    {
        const string sliderKey = "gain-slider";
        NodeParameter<float> gain = NodeParameter.Create("gain", 0.6f);

        GraphNode gainNode = NumNode(1, "gain", "Gain", new Vector2(60, 60), hasIn: true, hasOut: true) with
        {
            Data = NodeParameterValues.Empty.Set(gain.Key, 0.6f)
        };
        var document = new GraphDocument(NodeGraphDoc.Of(
            [gainNode, NumNode(2, "out", "Output", new Vector2(380, 90), hasIn: true, hasOut: false)],
            [new GraphEdge(10, new PortId(1, 1), new PortId(2, 0))]));

        NodeGraphView ed = NodeGraphView(document: document, viewWidth: 620f, viewHeight: 390f);
        // resolver は外部 Signal や node.Data を選ばず、slot に結び付いた document-backed Signal を使う。
        ed.WidgetResolver = widget => Equals(widget.Key, sliderKey) ? Slider(widget.Signal<float>()) : null;
        ed.SetDecorations("inline", new GraphDecorationSet([new NodeInlineDecoration(1, 150, 24, sliderKey, gain)]));
        // 右クリック追加パレット
        ed.NodeCatalog = new NodeCatalog(
            new NodeCatalogEntry("gain", "Gain", (id, pos) => NumNode(id, "gain", "Gain", pos, true, true)),
            new NodeCatalogEntry("out", "Output", (id, pos) => NumNode(id, "out", "Output", pos, true, false)));

        ctx.Play("inline", async d =>
        {
            await d.Snap();                              // Gain ノードにスライダ (値 0.6)
            Vector2 c = ed.SlotScreenCenter(sliderKey);
            await d.Click(c.X + 30, c.Y);                // スライダを叩く → document parameter が動く
            await d.Expect(() => Math.Abs(gain.Read(document.Doc.Node(1)) - 0.6f) > 0.01f,
                "ノード内スライダが document parameter を更新");
            await d.Snap("slid");
        });

        ctx.Play("palette", async d =>
        {
            await d.Expect(() => ed.NodeCount == 2, "初期 2 ノード");
            Vector2 empty = ed.ClientOf(new Vector2(180, 250));
            d.Host.ContextClick(empty.X, empty.Y);       // 右クリック → パレット
            await d.Step(2);
            await d.Snap("palette");                     // Gain / Output のメニュー
            await d.Click(empty.X + 50, empty.Y + 18);   // 先頭 "Gain" を選ぶ
            await d.Expect(() => ed.NodeCount == 3, "パレットからノード追加");
            await d.Snap("added");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView — ノード内 UI + パレット"),
                Muted("NodeInlineDecoration + NodeGraph 専用 WidgetResolver で document-backed Signal のスライダをホスト。右クリックで INodeCatalog のパレット追加 (PopupPlacer)。"),
                ed]];
    }

    // 式グラフのドメイン: Const×2 → Add → Output (汎用ノードエディタの一利用例)
    private static NodeGraphDoc ExprGraph()
    {
        NodePort In(int id, string label) => new(id, PortDir.In, "num", label);
        NodePort Out(int id) => new(id, PortDir.Out, "num", "");
        var c1 = new GraphNode(1, "const", "2", new Vector2(300, 40), [Out(0)]);
        var c2 = new GraphNode(2, "const", "3", new Vector2(60, 250), [Out(0)]);
        var add = new GraphNode(3, "add", "Add", new Vector2(430, 170), [In(0, "a"), In(1, "b"), Out(2)]);
        var outN = new GraphNode(4, "out", "Output", new Vector2(170, 60), [In(0, "")]);
        return NodeGraphDoc.Of([c1, c2, add, outN],
            [new GraphEdge(10, new PortId(1, 0), new PortId(3, 0)),
             new GraphEdge(11, new PortId(2, 0), new PortId(3, 1)),
             new GraphEdge(12, new PortId(3, 2), new PortId(4, 0))]);
    }

    [Story(ShortDescription = "同じグラフを自動配置し、手作業の初期整列を減らすレイアウト結果を確認します。")]
    public static StoryResult AutoLayoutStory(StoryContext ctx)
    {
        NodeGraphView ed = NodeGraphView(source: ExprGraph(), viewWidth: 620f, viewHeight: 380f);
        ed.SnapToGrid = true;
        ed.NodeCatalog = new NodeCatalog(
            new NodeCatalogEntry("const", "Const", (id, pos) => new GraphNode(id, "const", "0", pos, [new NodePort(0, PortDir.Out, "num", "")])),
            new NodeCatalogEntry("add", "Add", (id, pos) => new GraphNode(id, "add", "Add", pos,
                [new NodePort(0, PortDir.In, "num", "a"), new NodePort(1, PortDir.In, "num", "b"), new NodePort(2, PortDir.Out, "num", "")])),
            new NodeCatalogEntry("out", "Output", (id, pos) => new GraphNode(id, "out", "Output", pos, [new NodePort(0, PortDir.In, "num", "")])));

        ctx.Play("layout", async d =>
        {
            await d.Snap();                              // ばらけた初期配置
            ed.AutoLayout();                             // 辺の依存に沿って左→右へ整列
            ed.FitToView();                              // 全体が収まるよう pan/zoom
            await d.Step(1);
            await d.Expect(() => ed.NodePos(1).X < ed.NodePos(3).X, "Const は Add の左");
            await d.Snap("arranged");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView — 自動整列 (式グラフ)"),
                Muted("式グラフ (Const×2 → Add → Output) を例に、AutoLayout で辺依存に沿って左→右へ整列 + FitToView。グリッドスナップ有効。"),
                ed]];
    }

    private static readonly NodeParameter<float> PlaygroundGain = NodeParameter.Create("gain", 0.65f);
    private const string PlaygroundGainWidget = "playground-gain";

    private static NodeGraphDoc PlaygroundGraph()
    {
        NodePort In(int id, string type, string label, bool multi = false) => new(id, PortDir.In, type, label, multi);
        NodePort Out(int id, string type, string label) => new(id, PortDir.Out, type, label);

        var texture = new GraphNode(1, "texture", "Texture", new Vector2(30, 45),
            [Out(0, "color", "rgba")]);
        var tint = new GraphNode(2, "color", "Tint", new Vector2(35, 210),
            [Out(0, "color", "color")]);
        var mask = new GraphNode(3, "value", "Mask", new Vector2(40, 340),
            [Out(0, "float", "value")]);
        var blend = new GraphNode(4, "blend", "Blend", new Vector2(270, 125),
            [In(0, "color", "source"), In(1, "color", "tint"), In(2, "float", "mask"), Out(3, "color", "result")]);
        var gain = new GraphNode(5, "gain", "Gain", new Vector2(500, 145),
            [In(0, "color", "input"), Out(1, "color", "adjusted")],
            Data: NodeParameterValues.Empty.Set(PlaygroundGain.Key, 0.65f));
        var preview = new GraphNode(6, "preview", "Preview", new Vector2(735, 145),
            [In(0, "color", "image")]);
        var debug = new GraphNode(7, "debug", "Debug", new Vector2(500, 360),
            [In(0, "color", "inspect", multi: true), Out(1, "string", "text")], Collapsed: true);

        return NodeGraphDoc.Of([texture, tint, mask, blend, gain, preview, debug],
            [new GraphEdge(10, new PortId(1, 0), new PortId(4, 0)),
             new GraphEdge(11, new PortId(2, 0), new PortId(4, 1)),
             new GraphEdge(12, new PortId(3, 0), new PortId(4, 2)),
             new GraphEdge(13, new PortId(4, 3), new PortId(5, 0)),
             new GraphEdge(14, new PortId(5, 1), new PortId(6, 0)),
             new GraphEdge(15, new PortId(4, 3), new PortId(7, 0))]);
    }

    private static JsonElement GraphJson(NodeGraphDoc doc)
    {
        using JsonDocument json = JsonDocument.Parse(NodeGraphJson.Serialize(doc));
        return json.RootElement.Clone();
    }

    private sealed class NodeGraphPlaygroundRoot : CompositeControl
    {
        private readonly StoryContext _story;
        private readonly Signal<JsonElement> _json;
        private readonly GraphDocument _document;
        private readonly NodeGraphView _view;
        private bool _synchronizing;

        public NodeGraphPlaygroundRoot(StoryContext story, Signal<JsonElement> json)
        {
            _story = story;
            _json = json;
            _document = new GraphDocument(NodeGraphJson.Deserialize(json.Peek().GetRawText()));
            _view = NodeGraphView(document: _document, viewWidth: 900f, viewHeight: 500f);
            _view.SnapToGrid = true;
            _view.WidgetResolver = context => Equals(context.Key, PlaygroundGainWidget)
                ? Slider(context.Signal<float>(), min: 0f, max: 2f)
                : null;
            ApplyDecorations();
        }

        public NodeGraphView View => _view;

        protected override Widget Build() => _view;

        protected override void OnRealize(UiBuildContext ctx)
        {
            _json.Changed += OnJsonChanged;
            _document.Changed += OnDocumentChanged;
            ctx.Own(new PlaygroundSubscription(_json, OnJsonChanged, _document, OnDocumentChanged));
        }

        private void OnJsonChanged(JsonElement json)
        {
            if (_synchronizing) return;
            try
            {
                NodeGraphDoc doc = NodeGraphJson.Deserialize(json.GetRawText());
                _synchronizing = true;
                _document.Load(doc);
                ApplyDecorations();
            }
            catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException)
            {
                _story.Log($"graph JSON was ignored: {error.Message}");
            }
            finally { _synchronizing = false; }
        }

        private void OnDocumentChanged(GraphDocument document, bool docChanged)
        {
            if (!docChanged || _synchronizing) return;
            _synchronizing = true;
            try { _json.Value = GraphJson(document.Doc); }
            finally { _synchronizing = false; }
        }

        private void ApplyDecorations()
        {
            GraphNode? gain = _document.Doc.Nodes.FirstOrDefault(node => node.Kind == "gain");
            GraphDecorationSet decorations = gain is null
                ? GraphDecorationSet.Empty
                : new GraphDecorationSet([new NodeInlineDecoration(gain.Id, 150, 24, PlaygroundGainWidget, PlaygroundGain)]);
            _view.SetDecorations("playground-parameters", decorations);
        }

        private sealed class PlaygroundSubscription(
            Signal<JsonElement> json,
            Action<JsonElement> jsonHandler,
            GraphDocument document,
            Action<GraphDocument, bool> documentHandler) : IDisposable
        {
            public void Dispose()
            {
                json.Changed -= jsonHandler;
                document.Changed -= documentHandler;
            }
        }
    }

    public static IReadOnlyList<StoryArgDefinition> PlaygroundArgs() =>
    [
        StoryArgDefinition.Create("graph", "json", GraphJson(PlaygroundGraph()),
            description: "NodeGraph JSON。ArgsのJSON編集とcanvas上の編集が双方向に同期します。",
            order: 0, editor: StoryArgEditorKind.Json),
    ];

    [Story(Args = nameof(PlaygroundArgs),
        ShortDescription = "Args の JSON とキャンバス編集を双方向同期し、構造変更を同じ文書へ反映します。")]
    public static StoryResult Playground(StoryContext ctx)
    {
        Signal<JsonElement> graph = ctx.Arg("graph", GraphJson(PlaygroundGraph()), new StoryArgOptions<JsonElement>
        {
            Description = "NodeGraph JSON。ArgsのJSON編集とcanvas上の編集が双方向に同期します。",
            Editor = StoryArgEditorKind.Json,
            Order = 0,
        });
        var root = new NodeGraphPlaygroundRoot(ctx, graph);
        NodeGraphView ed = root.View;

        ctx.Play("json-two-way", async d =>
        {
            ed.FitToView();
            await d.Step(1);
            await d.Snap();
            await d.Expect(() => ed.NodeCount == 7 && ed.EdgeCount == 6, "豊富な既定JSONを表示");

            var replacement = NodeGraphDoc.Of([
                new GraphNode(20, "source", "JSON Source", new Vector2(40, 70), [new NodePort(0, PortDir.Out, "v", "value")]),
                new GraphNode(21, "sink", "JSON Sink", new Vector2(330, 120), [new NodePort(0, PortDir.In, "v", "input")]),
            ], [new GraphEdge(30, new PortId(20, 0), new PortId(21, 0))]);
            graph.Value = GraphJson(replacement);             // Args JSON → view
            await d.Step(1);
            await d.Expect(() => ed.NodeCount == 2 && ed.EdgeCount == 1, "Args JSONの変更をviewへ反映");

            ed.ApplyEdit(new MoveNode(21, new Vector2(70, 25))); // view → Args JSON
            NodeGraphDoc reflected = NodeGraphJson.Deserialize(graph.Peek().GetRawText());
            await d.Expect(() => reflected.Node(21).Pos == new Vector2(400, 145), "view編集をArgs JSONへ反映");
            await d.Snap("json-applied");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView Playground — JSON双方向編集"),
                Muted("Argsのgraph JSONを編集するとviewを再構築し、ノード移動・追加・削除・配線などview側の編集は同じJSONへ書き戻します。既定値はport label、複数型、分岐、collapsed node、parameter widgetを含みます。"),
                root]];
    }

    // DevTools のレンダーグラフと同型のサンプル診断 (パス×リソース DAG)
    private static DiagRenderGraph SampleRenderGraph()
    {
        DiagRenderGraphPass Pass(int i, string name, int[] reads, int[] writes) => new(i, name, "Graphics", false, reads, writes);
        DiagRenderGraphResource Res(int id, string name) => new(id, name, "Transient", false, 0, 0, 0, 0);
        return new DiagRenderGraph(
            [Pass(0, "Upload", [], [1]),
             Pass(1, "Shadow", [1], [2]),
             Pass(2, "Main", [1, 2], [3]),
             Pass(3, "Present", [3], [])],
            [Res(1, "vbuf"), Res(2, "shadow"), Res(3, "color")], 3, 4);
    }

    [Story(ShortDescription = "描画パスとリソース依存を DAG として可視化し、診断対象の流れを追います。")]
    public static StoryResult RenderGraph(StoryContext ctx)
    {
        // RenderGraphNodes で診断 → ノードグラフ (整列済み) に変換し、読み取り専用ビューで可視化
        NodeGraphView ed = NodeGraphView(source: RenderGraphNodes.Build(SampleRenderGraph()), viewWidth: 620f, viewHeight: 380f);
        ed.ReadOnly = true;

        ctx.Play("view", async d =>
        {
            ed.FitToView();
            await d.Step(1);
            await d.Snap();                              // レンダーグラフ (Upload→Shadow→Main→Present)
            Vector2 before = ed.NodePos(2);
            Vector2 pan0 = ed.Viewport.Pan;
            Vector2 c = ed.NodeScreenCenter(2);
            await d.Drag(c.X, c.Y, c.X + 50, c.Y + 30);  // read-only: ドラッグは pan
            await d.Expect(() => ed.NodePos(2) == before, "ノードは動かない (読み取り専用)");
            await d.Expect(() => ed.Viewport.Pan != pan0, "ドラッグで pan した");
            await d.Snap("panned");
            Vector2 c2 = ed.NodeScreenCenter(2);
            await d.Click(c2.X, c2.Y);                    // クリックで検査選択
            await d.Expect(() => ed.IsSelected(2), "クリックで検査選択");
            await d.Snap("selected");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView — レンダーグラフ (読み取り専用)"),
                Muted("DevTools のレンダーグラフ (パス×リソース DAG) を RenderGraphNodes で変換し、ReadOnly の NodeGraphView で可視化。ドラッグ=pan / クリック=検査 / ホイール=ズーム。"),
                ed]];
    }
}
