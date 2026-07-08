using System.Numerics;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>NodeGraphView — 汎用ノードエディタ (ADR-0009 / ToDo 25) のビュー。ノード + ポート + 接続線を編集する。
/// 編集意味論・座標写像・装飾・幾何は canvas 非依存の Luxel.NodeGraph が持ち、この widget は入力を Transaction にして
/// ジオメトリのベジェ/矩形を塗るだけ (テキスト新スタックの TextEditorView と同じ薄さ)。pan/zoom は world コンテナの
/// Affine2D 変換なのでヒットテストが自動追従する。</summary>
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

    [Story("Controls/NodeGraphView/Basic", Height = 440)]
    public static Widget Basic(StoryContext ctx)
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

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("NodeGraphView (汎用ノードエディタ)"),
                Muted("Luxel.NodeGraph (不変 + Transaction + 純射影) を canvas に載せた薄いビュー。ドラッグ移動 / クリック・範囲選択 / ホイールズーム。"),
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

    [Story("Controls/NodeGraphView/Wiring", Height = 460, Order = 2)]
    public static Widget Wiring(StoryContext ctx)
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
}
