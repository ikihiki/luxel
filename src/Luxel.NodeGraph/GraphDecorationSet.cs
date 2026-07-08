namespace Luxel.NodeGraph;

/// <summary>
/// 装飾の集合 — <see cref="GraphDecoration.SortKey"/> 昇順に並んだ不変リスト ([[Luxel.Editor.DecorationSet]] 相当)。
/// 編集追従は <see cref="Map"/> が全装飾を写し (削除対象のものを落とす)、供給側 (プロバイダ) はこの set を丸ごと
/// 差し替える。重なる装飾は共存する (描画時に重畳)。
/// </summary>
public sealed class GraphDecorationSet
{
    /// <summary>空集合。</summary>
    public static readonly GraphDecorationSet Empty = new([]);

    private readonly GraphDecoration[] _decos;

    /// <summary>装飾列から作る (SortKey → 型名で安定ソート)。</summary>
    public GraphDecorationSet(IEnumerable<GraphDecoration> decorations)
        => _decos = decorations.OrderBy(d => d.SortKey).ThenBy(d => d.GetType().Name, StringComparer.Ordinal).ToArray();

    /// <summary>ソート済みの装飾列。</summary>
    public IReadOnlyList<GraphDecoration> Decorations => _decos;
    /// <summary>個数。</summary>
    public int Count => _decos.Length;
    /// <summary>レイアウトに効く装飾を含むか (S3 のノード幾何キャッシュのキーに使う)。</summary>
    public bool AnyAffectsLayout => Array.Exists(_decos, d => d.AffectsLayout);

    /// <summary><paramref name="doc"/> に対して全装飾を写した新しい集合 (削除対象のものを除外)。</summary>
    public GraphDecorationSet Map(NodeGraphDoc doc)
    {
        var next = new List<GraphDecoration>(_decos.Length);
        foreach (GraphDecoration d in _decos)
            if (d.Map(doc) is { } m) next.Add(m);
        return next.Count == 0 ? Empty : new GraphDecorationSet(next);
    }

    /// <summary>指定型の装飾だけ列挙する (view/geometry が種別ごとに取り出す用)。</summary>
    public IEnumerable<T> OfKind<T>() where T : GraphDecoration => _decos.OfType<T>();
}

/// <summary>
/// owner (供給元) 別に <see cref="GraphDecorationSet"/> を保持する不変テーブル ([[Luxel.Editor.DecorationTable]] 相当) —
/// 検証/選択/配線ヒントなどがそれぞれ独立した set を持ち、独立に差し替える。<see cref="NodeGraphState"/> が 1 つ保持し、
/// 編集時は <see cref="Map"/> が全 owner の set を写す (プロバイダが出した新しい set は現状態基準なので写さない)。
/// </summary>
public sealed class GraphDecorationTable
{
    /// <summary>空テーブル。</summary>
    public static readonly GraphDecorationTable Empty = new(new Dictionary<string, GraphDecorationSet>());

    private readonly Dictionary<string, GraphDecorationSet> _byOwner;

    private GraphDecorationTable(Dictionary<string, GraphDecorationSet> byOwner) => _byOwner = byOwner;

    /// <summary>owner が登録されていないか。</summary>
    public bool IsEmpty => _byOwner.Count == 0;
    /// <summary>登録済み owner。</summary>
    public IReadOnlyCollection<string> Owners => _byOwner.Keys;
    /// <summary>いずれかの owner がレイアウトに効く装飾を持つか。</summary>
    public bool AnyAffectsLayout
    {
        get
        {
            foreach (GraphDecorationSet s in _byOwner.Values) if (s.AnyAffectsLayout) return true;
            return false;
        }
    }

    /// <summary>owner の set (無ければ null)。</summary>
    public GraphDecorationSet? Get(string owner) => _byOwner.TryGetValue(owner, out GraphDecorationSet? s) ? s : null;

    /// <summary>owner の set を差し替えた新しいテーブル (空集合を渡すと owner を削除)。</summary>
    public GraphDecorationTable Set(string owner, GraphDecorationSet set)
    {
        if (set.Count == 0) return Remove(owner);
        var next = new Dictionary<string, GraphDecorationSet>(_byOwner) { [owner] = set };
        return new GraphDecorationTable(next);
    }

    /// <summary>owner を外した新しいテーブル。</summary>
    public GraphDecorationTable Remove(string owner)
    {
        if (!_byOwner.ContainsKey(owner)) return this;
        var next = new Dictionary<string, GraphDecorationSet>(_byOwner);
        next.Remove(owner);
        return next.Count == 0 ? Empty : new GraphDecorationTable(next);
    }

    /// <summary>全 owner の set を <paramref name="doc"/> に対して写した新しいテーブル。</summary>
    public GraphDecorationTable Map(NodeGraphDoc doc)
    {
        if (IsEmpty) return this;
        var next = new Dictionary<string, GraphDecorationSet>(_byOwner.Count);
        foreach ((string owner, GraphDecorationSet set) in _byOwner)
        {
            GraphDecorationSet mapped = set.Map(doc);
            if (mapped.Count > 0) next[owner] = mapped;
        }
        return next.Count == 0 ? Empty : new GraphDecorationTable(next);
    }

    /// <summary>全 owner の装飾を平坦に列挙する。</summary>
    public IEnumerable<GraphDecoration> All()
    {
        foreach (GraphDecorationSet set in _byOwner.Values)
            foreach (GraphDecoration d in set.Decorations) yield return d;
    }
}
