using System.Text.RegularExpressions;

namespace Luxel.Diagram;

/// <summary>ノードの形 (mermaid: <c>A[矩形]</c> / <c>A(角丸)</c> / <c>A{ひし形}</c>)。</summary>
public enum DiagramShape { Rect, Rounded, Diamond }

/// <summary>ダイアグラムのノード 1 つ。初出の記法で形とラベルが決まる (以後は id 参照)。</summary>
public sealed record DiagramNode(string Id, string Label, DiagramShape Shape);

/// <summary>有向エッジ (<c>A --> B</c> / <c>A -->|ラベル| B</c>)。</summary>
public sealed record DiagramEdge(string From, string To, string? Label);

/// <summary>パース済みダイアグラム。<see cref="Horizontal"/> = LR/RL (false = TB/TD/BT)。</summary>
public sealed record DiagramSpec(bool Horizontal, IReadOnlyList<DiagramNode> Nodes, IReadOnlyList<DiagramEdge> Edges);

/// <summary>
/// mermaid flowchart の**サブセット**パーサ。対応: 先頭行 <c>flowchart|graph LR|RL|TB|TD|BT</c>、
/// ノード <c>id[ラベル]</c> / <c>id(ラベル)</c> / <c>id{ラベル}</c> (裸 id はラベル = id の矩形)、
/// エッジ <c>A --> B</c> / <c>A -->|ラベル| B</c>、コメント <c>%%</c>。
/// 対応外の行は無視する (subgraph/style/click 等 — 全記法は追わない)。
/// </summary>
public static class MermaidParser
{
    private static readonly Regex NodeRe = new(
        @"^(?<id>[A-Za-z0-9_]+)\s*(?:\[(?<rect>[^\]]*)\]|\((?<round>[^)]*)\)|\{(?<dia>[^}]*)\})?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex EdgeRe = new(
        @"^(?<from>.+?)\s*-{2,}>\s*(?:\|(?<label>[^|]*)\|\s*)?(?<to>.+)$",
        RegexOptions.Compiled);

    public static DiagramSpec Parse(string src)
    {
        bool horizontal = true;
        var nodes = new List<DiagramNode>();
        var byId = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
        var edges = new List<DiagramEdge>();
        bool headerSeen = false;

        foreach (string raw in (src ?? "").Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            if (!headerSeen && Regex.Match(line, @"^(flowchart|graph)\s+(?<dir>LR|RL|TB|TD|BT)\s*$",
                    RegexOptions.IgnoreCase) is { Success: true } h)
            {
                headerSeen = true;
                horizontal = h.Groups["dir"].Value.ToUpperInvariant() is "LR" or "RL";
                continue;
            }

            Match e = EdgeRe.Match(line);
            if (e.Success)
            {
                DiagramNode? from = ParseNode(e.Groups["from"].Value.Trim(), byId, nodes);
                DiagramNode? to = ParseNode(e.Groups["to"].Value.Trim(), byId, nodes);
                if (from is null || to is null) continue;   // 対応外の端点は行ごと無視
                string label = e.Groups["label"].Value.Trim();
                edges.Add(new DiagramEdge(from.Id, to.Id, label.Length > 0 ? label : null));
                continue;
            }

            ParseNode(line, byId, nodes);   // 単独のノード宣言 (対応外の行はここで null → 無視)
        }
        return new DiagramSpec(horizontal, nodes, edges);
    }

    private static DiagramNode? ParseNode(string token, Dictionary<string, DiagramNode> byId, List<DiagramNode> nodes)
    {
        Match m = NodeRe.Match(token);
        if (!m.Success) return null;
        string id = m.Groups["id"].Value;
        if (byId.TryGetValue(id, out DiagramNode? existing))
        {
            // 既知 id: ラベル付き再宣言なら上書き (mermaid と同じく後勝ちでラベルを確定)
            if (m.Groups["rect"].Success || m.Groups["round"].Success || m.Groups["dia"].Success)
            {
                DiagramNode updated = Make(id, m);
                byId[id] = updated;
                nodes[nodes.IndexOf(existing)] = updated;
                return updated;
            }
            return existing;
        }
        DiagramNode n = Make(id, m);
        byId[id] = n;
        nodes.Add(n);
        return n;
    }

    private static DiagramNode Make(string id, Match m) =>
        m.Groups["rect"].Success ? new DiagramNode(id, Or(m.Groups["rect"].Value, id), DiagramShape.Rect)
        : m.Groups["round"].Success ? new DiagramNode(id, Or(m.Groups["round"].Value, id), DiagramShape.Rounded)
        : m.Groups["dia"].Success ? new DiagramNode(id, Or(m.Groups["dia"].Value, id), DiagramShape.Diamond)
        : new DiagramNode(id, id, DiagramShape.Rect);

    private static string Or(string s, string fallback) => s.Trim().Length > 0 ? s.Trim() : fallback;
}
