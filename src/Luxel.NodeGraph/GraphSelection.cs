namespace Luxel.NodeGraph;

/// <summary>
/// グラフの選択状態 — 選択ノード id 集合 + 選択辺 id 集合 + 主ノード ([[Luxel.Editor.EditorSelection]] 相当)。
/// id は昇順・重複なしに正規化する (決定性)。編集追従は <see cref="Retain"/> が、削除されたノード/辺への
/// 参照を落とす (テキストの Map に相当するが、安定 id なので写像ではなく存在フィルタ)。
/// </summary>
public sealed class GraphSelection
{
    /// <summary>空選択。</summary>
    public static readonly GraphSelection Empty = new([], [], -1);

    /// <summary>選択ノード id (昇順・重複なし)。</summary>
    public IReadOnlyList<int> Nodes { get; }
    /// <summary>選択辺 id (昇順・重複なし)。</summary>
    public IReadOnlyList<int> Edges { get; }
    /// <summary>主ノード id (キャレット相当。無ければ -1)。</summary>
    public int Main { get; }

    private GraphSelection(int[] nodes, int[] edges, int main)
    {
        Nodes = nodes;
        Edges = edges;
        Main = main;
    }

    /// <summary>ノード/辺 id の集合から正規化して作る。<paramref name="main"/> が Nodes に含まれなければ末尾 (無ければ -1)。</summary>
    public static GraphSelection Of(IEnumerable<int> nodes, IEnumerable<int>? edges = null, int main = -1)
    {
        int[] ns = nodes.Distinct().OrderBy(x => x).ToArray();
        int[] es = (edges ?? []).Distinct().OrderBy(x => x).ToArray();
        int m = ns.Contains(main) ? main : ns.Length > 0 ? ns[^1] : -1;
        return new GraphSelection(ns, es, m);
    }

    /// <summary>ノード 1 個を選択 (主に設定)。</summary>
    public static GraphSelection Node(int id) => Of([id], null, id);
    /// <summary>辺 1 本を選択。</summary>
    public static GraphSelection Edge(int id) => Of([], [id]);

    /// <summary>ノードが選択されているか。</summary>
    public bool ContainsNode(int id) => Nodes.Contains(id);
    /// <summary>辺が選択されているか。</summary>
    public bool ContainsEdge(int id) => Edges.Contains(id);
    /// <summary>何も選択していないか。</summary>
    public bool IsEmpty => Nodes.Count == 0 && Edges.Count == 0;

    /// <summary><paramref name="doc"/> に存在しないノード/辺への参照を落とした選択を返す (編集追従)。</summary>
    public GraphSelection Retain(NodeGraphDoc doc)
    {
        int[] ns = Nodes.Where(doc.HasNode).ToArray();
        int[] es = Edges.Where(doc.HasEdge).ToArray();
        if (ns.Length == Nodes.Count && es.Length == Edges.Count) return this;   // 変化なし
        int m = ns.Contains(Main) ? Main : ns.Length > 0 ? ns[^1] : -1;
        return new GraphSelection(ns, es, m);
    }
}
