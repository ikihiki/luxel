namespace Luxel.SceneEdit;

/// <summary>
/// シーンの選択状態 — 選択エンティティ id 集合 + 主エンティティ (GraphSelection 相当)。
/// id は昇順・重複なしに正規化する (決定性)。編集追従は <see cref="Retain"/> が削除された
/// エンティティへの参照を落とす (安定 id なので写像ではなく存在フィルタ)。
/// タイルレイヤの選択は GE-1 S2 で足す。
/// </summary>
public sealed class SceneSelection
{
    /// <summary>空選択。</summary>
    public static readonly SceneSelection Empty = new([], -1);

    /// <summary>選択エンティティ id (昇順・重複なし)。</summary>
    public IReadOnlyList<int> Entities { get; }

    /// <summary>主エンティティ id (ハンドル/インスペクタの対象。無ければ -1)。</summary>
    public int Main { get; }

    private SceneSelection(int[] entities, int main)
    {
        Entities = entities;
        Main = main;
    }

    /// <summary>id 集合から正規化して作る。<paramref name="main"/> が含まれなければ末尾 (無ければ -1)。</summary>
    public static SceneSelection Of(IEnumerable<int> entities, int main = -1)
    {
        int[] ids = entities.Distinct().OrderBy(x => x).ToArray();
        int m = ids.Contains(main) ? main : ids.Length > 0 ? ids[^1] : -1;
        return new SceneSelection(ids, m);
    }

    /// <summary>エンティティ 1 個を選択 (主に設定)。</summary>
    public static SceneSelection Single(int id) => Of([id], id);

    public bool Contains(int id) => Entities.Contains(id);

    public bool IsEmpty => Entities.Count == 0;

    /// <summary><paramref name="doc"/> に存在しないエンティティへの参照を落とした選択を返す (編集追従)。</summary>
    public SceneSelection Retain(SceneDoc doc)
    {
        int[] ids = Entities.Where(doc.HasEntity).ToArray();
        if (ids.Length == Entities.Count) return this;
        int m = ids.Contains(Main) ? Main : ids.Length > 0 ? ids[^1] : -1;
        return new SceneSelection(ids, m);
    }
}
