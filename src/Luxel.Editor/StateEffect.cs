namespace Luxel.Editor;

/// <summary>
/// トランザクションが運ぶ**副作用** — 文書変更 (<see cref="ChangeSet"/>) 以外の状態更新。S2 では装飾の
/// 差し替え/削除。将来 folding 等もここに乗る。effect は変更を適用した**後**の新座標系で解釈される
/// (プロバイダは新しい状態に対して装飾を計算するため)。
/// </summary>
public abstract record StateEffect
{
    /// <summary>装飾テーブルへ適用した結果を返す (装飾に無関係な effect はそのまま返す)。</summary>
    public abstract DecorationTable ApplyTo(DecorationTable table);
}

/// <summary>owner の装飾集合を差し替える。</summary>
public sealed record SetDecorations(string Owner, DecorationSet Set) : StateEffect
{
    /// <inheritdoc/>
    public override DecorationTable ApplyTo(DecorationTable table) => table.Set(Owner, Set);
}

/// <summary>owner の装飾を外す。</summary>
public sealed record RemoveDecorations(string Owner) : StateEffect
{
    /// <inheritdoc/>
    public override DecorationTable ApplyTo(DecorationTable table) => table.Remove(Owner);
}

/// <summary>
/// 装飾の**供給元** — 状態から装飾集合を導く純関数 (同期)。シンタックス色・行番号など、文書から即座に
/// 決まるものはこれで十分。診断のような**非同期**プロバイダは view がワーカーで走らせ、結果を発行時から
/// 現在までの <see cref="ChangeSet"/> で写して (<see cref="DecorationSet.Map"/>) から
/// <see cref="SetDecorations"/> effect にする — コアは同期契約のみ持つ。
/// </summary>
public interface IDecorationProvider
{
    /// <summary>この供給元の owner キー。</summary>
    string Owner { get; }
    /// <summary>状態から装飾集合を導く。</summary>
    DecorationSet Provide(EditorState state);
}

/// <summary>同期プロバイダ群を effect へまとめるヘルパ (view が状態更新のたびに呼ぶ想定)。</summary>
public static class DecorationProviders
{
    /// <summary>各プロバイダを走らせ <see cref="SetDecorations"/> effect の列にする。</summary>
    public static IReadOnlyList<StateEffect> Collect(EditorState state, IEnumerable<IDecorationProvider> providers)
        => providers.Select(p => (StateEffect)new SetDecorations(p.Owner, p.Provide(state))).ToList();
}
