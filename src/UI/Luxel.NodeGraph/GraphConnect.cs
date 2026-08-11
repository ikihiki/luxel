namespace Luxel.NodeGraph;

/// <summary>
/// ポート間の接続可否を判定する純ヘルパ — view の配線ドラッグと、コマンド/テストで共有する。
/// 可能なら出力→入力に正規化した <c>(outPort, inPort)</c> を返す。規則: 別ノード・一方が出力で他方が入力・
/// 型キー一致・同一辺の重複なし。単入力ポートの付け替え (既存辺の置換) は呼び出し側 (view) が判断する。
/// </summary>
public static class GraphConnect
{
    /// <summary>2 ポートが接続可能か。可能なら <paramref name="outPort"/>/<paramref name="inPort"/> に正規化して返す。</summary>
    public static bool TryResolve(NodeGraphDoc doc, PortId a, PortId b, out PortId outPort, out PortId inPort)
    {
        outPort = inPort = default;
        NodePort? pa = doc.Port(a), pb = doc.Port(b);
        if (pa is null || pb is null) return false;
        if (a.Node == b.Node) return false;              // 自己接続不可
        if (pa.Dir == pb.Dir) return false;              // 一方が出力・他方が入力である必要
        if (pa.TypeKey != pb.TypeKey) return false;      // 型キー一致
        (outPort, inPort) = pa.Dir == PortDir.Out ? (a, b) : (b, a);
        foreach (GraphEdge e in doc.Edges)
            if (e.From == outPort && e.To == inPort) return false;   // 重複辺
        return true;
    }

    /// <summary>接続可能かだけを返す (正規化不要な判定用)。</summary>
    public static bool CanConnect(NodeGraphDoc doc, PortId a, PortId b) => TryResolve(doc, a, b, out _, out _);
}
