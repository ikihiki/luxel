namespace Luxel.Document;

/// <summary>
/// 装飾の集合 — 開始オフセット昇順に並んだ不変リスト。編集追従は <see cref="Map"/> が全装飾を写し
/// (無効化されたものは落とす)、供給側 (プロバイダ) はこの set を丸ごと差し替える。重なる装飾は共存する
/// (描画時に重畳)。
/// </summary>
public sealed class DecorationSet
{
    /// <summary>空集合。</summary>
    public static readonly DecorationSet Empty = new([]);

    private readonly Decoration[] _decos;

    /// <summary>装飾列から作る (開始→終了で安定ソート)。</summary>
    public DecorationSet(IEnumerable<Decoration> decorations)
        => _decos = decorations.OrderBy(d => d.SortFrom).ThenBy(d => d.SortTo).ToArray();

    /// <summary>ソート済みの装飾列。</summary>
    public IReadOnlyList<Decoration> Decorations => _decos;
    /// <summary>個数。</summary>
    public int Count => _decos.Length;

    /// <summary>変更セットで全装飾を写した新しい集合 (無効化されたものは除外)。</summary>
    public DecorationSet Map(ChangeSet cs)
    {
        var next = new List<Decoration>(_decos.Length);
        foreach (Decoration d in _decos)
            if (d.Map(cs) is { } m) next.Add(m);
        return new DecorationSet(next);
    }

    /// <summary>指定型の装飾だけ列挙する (geometry が Mark/Widget/Line 別に取り出す用)。</summary>
    public IEnumerable<T> OfKind<T>() where T : Decoration => _decos.OfType<T>();

    /// <summary>[from, to) と交差する装飾を列挙する (点装飾は from&lt;=pos&lt;=to で判定)。</summary>
    public IEnumerable<Decoration> Touching(int from, int to)
    {
        foreach (Decoration d in _decos)
            if (d.SortFrom <= to && d.SortTo >= from) yield return d;
    }
}

/// <summary>
/// owner (供給元) 別に <see cref="DecorationSet"/> を保持する不変テーブル — シンタックス/診断/検索/再生位置が
/// それぞれ独立した set を持ち、独立に差し替える。<see cref="EditorState"/> が 1 つ保持し、編集時は
/// <see cref="Map"/> が全 owner の set を写す (プロバイダが出した新しい set は新座標なので写さない)。
/// </summary>
public sealed class DecorationTable
{
    /// <summary>空テーブル。</summary>
    public static readonly DecorationTable Empty = new(new Dictionary<string, DecorationSet>());

    private readonly Dictionary<string, DecorationSet> _byOwner;

    private DecorationTable(Dictionary<string, DecorationSet> byOwner) => _byOwner = byOwner;

    /// <summary>owner が登録されていないか。</summary>
    public bool IsEmpty => _byOwner.Count == 0;
    /// <summary>登録済み owner。</summary>
    public IReadOnlyCollection<string> Owners => _byOwner.Keys;

    /// <summary>owner の set (無ければ null)。</summary>
    public DecorationSet? Get(string owner) => _byOwner.TryGetValue(owner, out DecorationSet? s) ? s : null;

    /// <summary>owner の set を差し替えた新しいテーブル (空集合を渡すと owner を削除)。</summary>
    public DecorationTable Set(string owner, DecorationSet set)
    {
        if (set.Count == 0) return Remove(owner);
        var next = new Dictionary<string, DecorationSet>(_byOwner) { [owner] = set };
        return new DecorationTable(next);
    }

    /// <summary>owner を外した新しいテーブル。</summary>
    public DecorationTable Remove(string owner)
    {
        if (!_byOwner.ContainsKey(owner)) return this;
        var next = new Dictionary<string, DecorationSet>(_byOwner);
        next.Remove(owner);
        return next.Count == 0 ? Empty : new DecorationTable(next);
    }

    /// <summary>全 owner の set を変更セットで写した新しいテーブル。</summary>
    public DecorationTable Map(ChangeSet cs)
    {
        if (IsEmpty) return this;
        var next = new Dictionary<string, DecorationSet>(_byOwner.Count);
        foreach ((string owner, DecorationSet set) in _byOwner)
        {
            DecorationSet mapped = set.Map(cs);
            if (mapped.Count > 0) next[owner] = mapped;
        }
        return next.Count == 0 ? Empty : new DecorationTable(next);
    }

    /// <summary>全 owner の装飾を平坦に列挙する。</summary>
    public IEnumerable<Decoration> All()
    {
        foreach (DecorationSet set in _byOwner.Values)
            foreach (Decoration d in set.Decorations) yield return d;
    }
}
