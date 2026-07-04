using System.Numerics;

namespace Luxel.Diagram;

/// <summary>配置済みノード (左上座標 + サイズ)。</summary>
public sealed record DiagramNodeLayout(DiagramNode Node, float X, float Y, float W, float H)
{
    /// <summary>ノードの中心座標。</summary>
    public Vector2 Center => new(X + W / 2, Y + H / 2);
}

/// <summary>配置済みエッジ (始点/終点はノード境界上、ラベルは中点)。</summary>
public sealed record DiagramEdgeLayout(DiagramEdge Edge, Vector2 From, Vector2 To, Vector2 LabelCenter);

/// <summary>レイアウト結果 (全体サイズ + 配置)。</summary>
public sealed record DiagramLayoutResult(float Width, float Height,
    IReadOnlyList<DiagramNodeLayout> Nodes, IReadOnlyList<DiagramEdgeLayout> Edges);

/// <summary>
/// 階層 (ランク) 自動レイアウト v1: エッジの最長パスでランクを決め、主軸方向にランクを並べ、
/// ランク内は出現順に積む (交差最小化はしない — 込み入った図は書き手が宣言順で調整する)。
/// 循環は安全 (逆向きエッジはランク付けに使わず、線だけ引く)。純ロジック — 文字計測は関数で注入。
/// </summary>
public static class DiagramLayout
{
    private const float PadX = 14, PadY = 8;       // ノード内側余白
    private const float RankGap = 64, NodeGap = 18;
    private const float DiamondSlack = 1.6f;       // ひし形はラベルより広く取る

    /// <summary>spec を配置して全体サイズと座標を返す。<paramref name="measure"/> はラベル 1 行の (幅, 高さ)。</summary>
    public static DiagramLayoutResult Arrange(DiagramSpec spec, Func<string, (float W, float H)> measure)
    {
        IReadOnlyList<DiagramNode> ns = spec.Nodes;
        if (ns.Count == 0) return new DiagramLayoutResult(0, 0, [], []);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ns.Count; i++) index[ns[i].Id] = i;

        // ランク = 最長パス (反復緩和、循環はパス数で頭打ち = 逆辺を自然に無視)
        int[] rank = new int[ns.Count];
        for (int pass = 0; pass < ns.Count; pass++)
        {
            bool changed = false;
            foreach (DiagramEdge e in spec.Edges)
            {
                int f = index[e.From], t = index[e.To];
                if (f == t) continue;
                if (rank[t] < rank[f] + 1 && rank[f] + 1 <= ns.Count) { rank[t] = rank[f] + 1; changed = true; }
            }
            if (!changed) break;
        }

        // サイズ計測 + ランク別グループ (出現順)
        var size = new (float W, float H)[ns.Count];
        for (int i = 0; i < ns.Count; i++)
        {
            (float tw, float th) = measure(ns[i].Label);
            float w = tw + PadX * 2, h = th + PadY * 2;
            if (ns[i].Shape == DiagramShape.Diamond) { w *= DiamondSlack; h *= DiamondSlack; }
            size[i] = (w, h);
        }
        int maxRank = rank.Length > 0 ? rank.Max() : 0;
        var ranks = new List<int>[maxRank + 1];
        for (int r = 0; r <= maxRank; r++) ranks[r] = new List<int>();
        for (int i = 0; i < ns.Count; i++) ranks[rank[i]].Add(i);

        // 主軸 = ランク方向、交差軸 = ランク内。ランクの主軸幅 = ランク内最大サイズ
        var pos = new (float X, float Y)[ns.Count];
        float main = 0, crossMax = 0;
        foreach (List<int> group in ranks)
        {
            float rankMain = group.Count > 0
                ? group.Max(i => spec.Horizontal ? size[i].W : size[i].H) : 0;
            float crossTotal = group.Sum(i => spec.Horizontal ? size[i].H : size[i].W)
                             + NodeGap * Math.Max(0, group.Count - 1);
            crossMax = MathF.Max(crossMax, crossTotal);
            float cross = 0;
            foreach (int i in group)
            {
                // 主軸はランク中央寄せ
                float m = main + (rankMain - (spec.Horizontal ? size[i].W : size[i].H)) / 2;
                pos[i] = spec.Horizontal ? (m, cross) : (cross, m);
                cross += (spec.Horizontal ? size[i].H : size[i].W) + NodeGap;
            }
            main += rankMain + RankGap;
        }
        main -= RankGap;

        // 各ランクを交差軸で中央寄せ
        foreach (List<int> group in ranks)
        {
            float crossTotal = group.Sum(i => spec.Horizontal ? size[i].H : size[i].W)
                             + NodeGap * Math.Max(0, group.Count - 1);
            float offset = (crossMax - crossTotal) / 2;
            foreach (int i in group)
                pos[i] = spec.Horizontal ? (pos[i].X, pos[i].Y + offset) : (pos[i].X + offset, pos[i].Y);
        }

        var nodeLayouts = new DiagramNodeLayout[ns.Count];
        for (int i = 0; i < ns.Count; i++)
            nodeLayouts[i] = new DiagramNodeLayout(ns[i], pos[i].X, pos[i].Y, size[i].W, size[i].H);

        // エッジ: 中心を結ぶ直線をノード境界 (矩形近似) でクリップ
        var edgeLayouts = new List<DiagramEdgeLayout>(spec.Edges.Count);
        foreach (DiagramEdge e in spec.Edges)
        {
            DiagramNodeLayout a = nodeLayouts[index[e.From]], b = nodeLayouts[index[e.To]];
            Vector2 p = ClipToBox(a, b.Center), q = ClipToBox(b, a.Center);
            edgeLayouts.Add(new DiagramEdgeLayout(e, p, q, (p + q) / 2));
        }

        float width = spec.Horizontal ? main : crossMax;
        float height = spec.Horizontal ? crossMax : main;
        return new DiagramLayoutResult(width, height, nodeLayouts, edgeLayouts);
    }

    /// <summary>ノード中心から <paramref name="toward"/> へ向かう線と矩形境界の交点。</summary>
    private static Vector2 ClipToBox(DiagramNodeLayout n, Vector2 toward)
    {
        Vector2 c = n.Center;
        Vector2 d = toward - c;
        if (d.LengthSquared() < 0.001f) return c;
        float tx = d.X != 0 ? (n.W / 2) / MathF.Abs(d.X) : float.MaxValue;
        float ty = d.Y != 0 ? (n.H / 2) / MathF.Abs(d.Y) : float.MaxValue;
        return c + d * MathF.Min(tx, ty);
    }
}
