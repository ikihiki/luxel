using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>寸法 (幅・高さ) — view が <see cref="NodeMeasure"/> で返すノードの実寸。
/// UI レイアウトの <c>Size</c> と衝突しないよう <c>NodeSize</c> と名付ける。</summary>
public readonly record struct NodeSize(float Width, float Height);

/// <summary>軸並行矩形 (world 座標)。core は Luxel.TwoD/Typography に依存しないため自前の軽量版。</summary>
public readonly record struct GraphRect(float X, float Y, float Width, float Height)
{
    /// <summary>右端 x。</summary>
    public float Right => X + Width;
    /// <summary>下端 y。</summary>
    public float Bottom => Y + Height;
    /// <summary>左上。</summary>
    public Vector2 Min => new(X, Y);
    /// <summary>中心。</summary>
    public Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);

    /// <summary>点を含むか (端を含む)。</summary>
    public bool Contains(Vector2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    /// <summary>2 矩形が交差するか (端の接触を含む)。box 選択の判定に使う。</summary>
    public bool Intersects(GraphRect o) => X <= o.Right && Right >= o.X && Y <= o.Bottom && Bottom >= o.Y;

    /// <summary>2 点を対角とする正規化矩形 (marquee 用、負の幅/高さを作らない)。</summary>
    public static GraphRect FromCorners(Vector2 a, Vector2 b)
        => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

    /// <summary>左上 + 寸法から作る。</summary>
    public static GraphRect FromMinSize(Vector2 min, NodeSize size) => new(min.X, min.Y, size.Width, size.Height);

    /// <summary>2 矩形を包む最小矩形。</summary>
    public GraphRect Union(GraphRect o)
    {
        float x = MathF.Min(X, o.X), y = MathF.Min(Y, o.Y);
        return new GraphRect(x, y, MathF.Max(Right, o.Right) - x, MathF.Max(Bottom, o.Bottom) - y);
    }
}

/// <summary>接続線 1 本の 3 次ベジェ (world) — <see cref="P0"/> 出力ポート → <see cref="P1"/> 入力ポート、
/// <see cref="C0"/>/<see cref="C1"/> は水平接線の制御点。view は <c>MoveTo(P0); CubicTo(C0,C1,P1)</c> で描く。</summary>
public readonly record struct GraphWire(Vector2 P0, Vector2 C0, Vector2 C1, Vector2 P1)
{
    /// <summary>パラメータ t∈[0,1] の点 (ヒット判定・矢印用)。</summary>
    public Vector2 At(float t)
    {
        float u = 1 - t;
        return u * u * u * P0 + 3 * u * u * t * C0 + 3 * u * t * t * C1 + t * t * t * P1;
    }
}

/// <summary>ポート 1 個の幾何 — アンカー点 (辺の端点) + ヒット矩形 (world)。</summary>
public readonly record struct PortGeometry(PortId Port, PortDir Dir, Vector2 Anchor, GraphRect HitRect);

/// <summary>ノード内インライン UI の配置枠 (world) — view が <see cref="Key"/> から実 Widget を解決してこの矩形に重ねる
/// ([[Luxel.Editor.WidgetSlot]] 相当)。<see cref="NodeInlineDecoration"/> 由来。</summary>
public readonly record struct WidgetSlot(int NodeId, object Key, GraphRect Rect);

/// <summary>ヒット対象の種別。</summary>
public enum GraphHitKind { Empty, Node, InputPort, OutputPort, Edge }

/// <summary>ヒットテストの結果 — 種別 + 対象 id。</summary>
public readonly record struct GraphHit(GraphHitKind Kind, int NodeId = -1, int PortId = -1, int EdgeId = -1)
{
    /// <summary>何も無い。</summary>
    public static readonly GraphHit None = new(GraphHitKind.Empty);
    /// <summary>ノード本体。</summary>
    public static GraphHit Node(int nodeId) => new(GraphHitKind.Node, NodeId: nodeId);
    /// <summary>ポート (方向で種別が決まる)。</summary>
    public static GraphHit Port(PortDir dir, PortId p)
        => new(dir == PortDir.In ? GraphHitKind.InputPort : GraphHitKind.OutputPort, NodeId: p.Node, PortId: p.Port);
    /// <summary>辺。</summary>
    public static GraphHit Edge(int edgeId) => new(GraphHitKind.Edge, EdgeId: edgeId);
}

/// <summary>ノードの実寸を測る関数 — view が注入する (ラベル幅・ポート数・インライン枠からサイズを決める)。
/// core を Luxel.Typography 非依存に保つための境界 (<c>DiagramLayout.Arrange(spec, measure)</c> と同型)。</summary>
public delegate NodeSize NodeMeasure(GraphNode node);

/// <summary>ジオメトリの設定 — タイトルバー高・ポート行高・角丸・ワイヤ接線長・ヒット許容など。view が注入する。</summary>
public sealed class GraphConfig
{
    /// <summary>タイトルバーの高さ px。</summary>
    public float TitleBarHeight { get; init; } = 22f;
    /// <summary>ポート行の高さ px (アンカーの縦間隔)。</summary>
    public float PortRowHeight { get; init; } = 18f;
    /// <summary>タイトルバー直下からポート行開始までの余白 px。</summary>
    public float PortStartY { get; init; } = 4f;
    /// <summary>ポートのヒット半径 px (world)。</summary>
    public float PortRadius { get; init; } = 5f;
    /// <summary>ノード角丸半径 px (view 用)。</summary>
    public float NodeCornerRadius { get; init; } = 6f;
    /// <summary>ワイヤの水平接線長 px。</summary>
    public float WireTangent { get; init; } = 60f;
    /// <summary>辺のヒット許容距離 px (screen 基準。world では zoom で割る)。</summary>
    public float EdgeHitTolerance { get; init; } = 6f;
    /// <summary>インライン枠の左右パディング px。</summary>
    public float SlotPadding { get; init; } = 6f;
    /// <summary>インライン枠どうしの縦間隔 px。</summary>
    public float SlotGap { get; init; } = 4f;
}

/// <summary>1 ノードの射影結果 — world 矩形 + ポート幾何 + インライン枠。内容 (Pos/サイズ/ポート/レイアウト装飾) が
/// 変わらなければキャッシュから再利用される (Assert.Same/NotSame でテスト)。</summary>
public sealed class NodeLayout
{
    internal NodeLayout(int nodeId, GraphRect rect, PortGeometry[] inputs, PortGeometry[] outputs, WidgetSlot[] slots)
    {
        NodeId = nodeId;
        Rect = rect;
        Inputs = inputs;
        Outputs = outputs;
        Slots = slots;
    }

    /// <summary>ノード id。</summary>
    public int NodeId { get; }
    /// <summary>ノード本体の world 矩形。</summary>
    public GraphRect Rect { get; }
    /// <summary>入力ポート (上から順)。</summary>
    public IReadOnlyList<PortGeometry> Inputs { get; }
    /// <summary>出力ポート (上から順)。</summary>
    public IReadOnlyList<PortGeometry> Outputs { get; }
    /// <summary>ノード内インライン枠。</summary>
    public IReadOnlyList<WidgetSlot> Slots { get; }

    /// <summary>指定 id のポート幾何 (無ければ null)。</summary>
    public PortGeometry? Port(int portId)
    {
        foreach (PortGeometry p in Inputs) if (p.Port.Port == portId) return p;
        foreach (PortGeometry p in Outputs) if (p.Port.Port == portId) return p;
        return null;
    }
}
