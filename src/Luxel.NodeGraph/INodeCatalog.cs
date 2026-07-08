using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>パレットに並ぶノード種別 1 つ — 表示ラベルと、id + world 位置から <see cref="GraphNode"/> を作る工場。
/// ドメイン (アニメ/シェーダ/オーディオ…) がそのノード群をここで宣言する。</summary>
public sealed record NodeCatalogEntry(string Kind, string Label, Func<int, Vector2, GraphNode> Create);

/// <summary>
/// ノード種別のカタログ — view のパレット (右クリック追加) が引く。ドメイン非依存の要で、ホストが実装して
/// 種別とその工場を供給する (core は具体ドメインを知らない)。<see cref="NodeCatalog"/> が素朴な実装。
/// </summary>
public interface INodeCatalog
{
    /// <summary>登録された種別 (パレットの並び順)。</summary>
    IReadOnlyList<NodeCatalogEntry> Entries { get; }
}

/// <summary>エントリ列をそのまま保持する素朴なカタログ。</summary>
public sealed class NodeCatalog(params NodeCatalogEntry[] entries) : INodeCatalog
{
    /// <inheritdoc/>
    public IReadOnlyList<NodeCatalogEntry> Entries { get; } = entries;
}
