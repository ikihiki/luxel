using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>ポートの向き — 入力 (左) か出力 (右)。辺は <see cref="PortDir.Out"/> 側から
/// <see cref="PortDir.In"/> 側へ張る。</summary>
public enum PortDir { In, Out }

/// <summary>ポートの安定 id — (ノード id, ノード内ポート id)。座標に依存しないので編集を生き延びる。</summary>
public readonly record struct PortId(int Node, int Port);

/// <summary>
/// ノードのポート 1 個 — 不変。<see cref="TypeKey"/> は接続互換の判定に使うドメイン定義の型キー
/// (例 "float"/"color")。<see cref="Label"/> はノード内のポート横へ表示する任意ラベル。
/// <see cref="Multi"/> は複数の辺を受け付けるか (既定は 1 本、In ポートは通常単数)。
/// </summary>
public sealed record NodePort(int Id, PortDir Dir, string TypeKey, string Label = "", bool Multi = false);

/// <summary>
/// グラフのノード 1 個 — **不変**。安定 <see cref="Id"/> を持ち、位置 <see cref="Pos"/> (world) と
/// ポート列 <see cref="Ports"/> を備える。<see cref="Data"/> はドメインが載せる不透明ペイロード
/// (view はインライン UI として描く)。編集は <c>with</c> 式で新インスタンスを作る。
/// </summary>
public sealed record GraphNode(
    int Id,
    string Kind,
    string Title,
    Vector2 Pos,
    IReadOnlyList<NodePort> Ports,
    object? Data = null,
    bool Collapsed = false)
{
    /// <summary>ポートを id で引く (無ければ null)。</summary>
    public NodePort? Port(int portId)
    {
        foreach (NodePort p in Ports) if (p.Id == portId) return p;
        return null;
    }
}

/// <summary>グラフの辺 1 本 — 安定 <see cref="Id"/> + 出力ポート <see cref="From"/> → 入力ポート <see cref="To"/>。不変。</summary>
public sealed record GraphEdge(int Id, PortId From, PortId To);
