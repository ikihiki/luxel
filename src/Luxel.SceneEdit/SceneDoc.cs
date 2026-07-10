namespace Luxel.SceneEdit;

/// <summary>
/// シーン 1 枚の不変スナップショット — エンティティ + タイルレイヤ + 空間宣言
/// (<see cref="Space"/>)。id→索引の辞書で O(1) 引き。NodeGraphDoc と同じ流儀:
/// 編集は変更モデル (GE-1 の SceneChange) が新しい SceneDoc を作る。
/// </summary>
public sealed class SceneDoc
{
    public SceneSpace Space { get; }

    public IReadOnlyList<SceneEntity> Entities { get; }

    public IReadOnlyList<TileLayer> TileLayers { get; }

    private readonly Dictionary<int, int> _entityIndex;
    private readonly Dictionary<int, int> _layerIndex;

    private SceneDoc(SceneSpace space, IReadOnlyList<SceneEntity> entities, IReadOnlyList<TileLayer> layers)
    {
        Space = space;
        Entities = entities;
        TileLayers = layers;
        _entityIndex = new Dictionary<int, int>(entities.Count);
        for (int i = 0; i < entities.Count; i++)
            if (!_entityIndex.TryAdd(entities[i].Id, i))
                throw new ArgumentException($"エンティティ id の重複: {entities[i].Id}");
        _layerIndex = new Dictionary<int, int>(layers.Count);
        for (int i = 0; i < layers.Count; i++)
            if (!_layerIndex.TryAdd(layers[i].Id, i))
                throw new ArgumentException($"タイルレイヤ id の重複: {layers[i].Id}");
    }

    public static SceneDoc Of(SceneSpace space, IReadOnlyList<SceneEntity>? entities = null, IReadOnlyList<TileLayer>? tileLayers = null)
        => new(space, entities ?? [], tileLayers ?? []);

    public static SceneDoc Empty(SceneSpace space) => Of(space);

    public bool HasEntity(int id) => _entityIndex.ContainsKey(id);

    public SceneEntity Entity(int id) => TryEntity(id) ?? throw new KeyNotFoundException($"エンティティが無い: {id}");

    public SceneEntity? TryEntity(int id) => _entityIndex.TryGetValue(id, out int i) ? Entities[i] : null;

    public TileLayer Layer(int id) => TryLayer(id) ?? throw new KeyNotFoundException($"タイルレイヤが無い: {id}");

    public TileLayer? TryLayer(int id) => _layerIndex.TryGetValue(id, out int i) ? TileLayers[i] : null;

    // ---- 変更モデル (SceneChange) 用の不変操作 ----

    /// <summary>エンティティを追加した新しい Doc (id 重複は例外)。<paramref name="index"/> は
    /// 挿入位置 (-1 = 末尾)。エンティティ順は 2D の描画順 (後 = 上) なので、削除の undo は
    /// 元の位置へ戻す必要がある。</summary>
    public SceneDoc AddEntity(SceneEntity entity, int index = -1)
    {
        if (index < 0 || index >= Entities.Count) return new SceneDoc(Space, [.. Entities, entity], TileLayers);
        var list = new List<SceneEntity>(Entities);
        list.Insert(index, entity);
        return new SceneDoc(Space, list, TileLayers);
    }

    /// <summary>エンティティを外した新しい Doc (無ければ例外)。</summary>
    public SceneDoc RemoveEntity(int id)
    {
        _ = Entity(id);
        return new SceneDoc(Space, Entities.Where(e => e.Id != id).ToList(), TileLayers);
    }

    /// <summary>同 id のエンティティを差し替えた新しい Doc (無ければ例外)。</summary>
    public SceneDoc ReplaceEntity(SceneEntity entity)
    {
        _ = Entity(entity.Id);
        return new SceneDoc(Space, Entities.Select(e => e.Id == entity.Id ? entity : e).ToList(), TileLayers);
    }
}
