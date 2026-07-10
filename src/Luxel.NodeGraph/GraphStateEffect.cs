namespace Luxel.NodeGraph;

/// <summary>
/// トランザクションが運ぶ**副作用** — 文書変更以外の状態更新 ([[Luxel.Document.StateEffect]] 相当)。S2 では装飾の
/// 差し替え/削除。effect は変更を適用した**後**の新しい Doc を基準に解釈される (プロバイダは新状態に対して装飾を計算する)。
/// </summary>
public abstract record GraphStateEffect
{
    /// <summary>装飾テーブルへ適用した結果を返す。</summary>
    public abstract GraphDecorationTable ApplyTo(GraphDecorationTable table);
}

/// <summary>owner の装飾集合を差し替える。</summary>
public sealed record SetGraphDecorations(string Owner, GraphDecorationSet Set) : GraphStateEffect
{
    /// <inheritdoc/>
    public override GraphDecorationTable ApplyTo(GraphDecorationTable table) => table.Set(Owner, Set);
}

/// <summary>owner の装飾を外す。</summary>
public sealed record RemoveGraphDecorations(string Owner) : GraphStateEffect
{
    /// <inheritdoc/>
    public override GraphDecorationTable ApplyTo(GraphDecorationTable table) => table.Remove(Owner);
}

/// <summary>
/// 装飾の**供給元** — 状態から装飾集合を導く純関数 (同期) ([[Luxel.Document.IDecorationProvider]] 相当)。選択強調・
/// 配線ヒントなど状態から即座に決まるものはこれで十分。検証のような**非同期**プロバイダは view がワーカーで走らせ、
/// 結果を <see cref="SetGraphDecorations"/> effect にして反映する (安定 id なので古い結果でも対象存在チェックで整合)。
/// </summary>
public interface IGraphDecorationProvider
{
    /// <summary>この供給元の owner キー。</summary>
    string Owner { get; }
    /// <summary>状態から装飾集合を導く。</summary>
    GraphDecorationSet Provide(NodeGraphState state);
}

/// <summary>同期プロバイダ群を effect へまとめるヘルパ (view が状態更新のたびに呼ぶ想定)。</summary>
public static class GraphDecorationProviders
{
    /// <summary>各プロバイダを走らせ <see cref="SetGraphDecorations"/> effect の列にする。</summary>
    public static IReadOnlyList<GraphStateEffect> Collect(NodeGraphState state, IEnumerable<IGraphDecorationProvider> providers)
        => providers.Select(p => (GraphStateEffect)new SetGraphDecorations(p.Owner, p.Provide(state))).ToList();
}
