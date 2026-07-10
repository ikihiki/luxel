namespace Luxel.SceneEdit;

/// <summary>
/// シーンへの 1 個の変更 — NodeGraph の GraphChange と同じアトム (ADR-0016)。<see cref="Apply"/> が
/// 新 Doc を返し、<see cref="InvertAgainst"/> が**適用直前**の Doc に対する逆変更列を返す。
/// 安定 id 前提なので座標写像は不要。空間非依存 — 座標を持つ変更は <see cref="SetField"/> が
/// <see cref="SceneValue"/> (形ベース) で運ぶため、2D/3D どちらの Transform にも同じ形で効く。
/// </summary>
public abstract record SceneChange
{
    /// <summary>この変更を適用した新しい Doc。</summary>
    public abstract SceneDoc Apply(SceneDoc doc);

    /// <summary>この変更を打ち消す逆変更列 (<paramref name="doc"/> = 適用直前の Doc)。</summary>
    public abstract IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc);
}

/// <summary>エンティティを 1 個追加する。<paramref name="Index"/> は挿入位置 (-1 = 末尾) —
/// エンティティ順は 2D の描画順なので、削除の逆はこの Index で元の位置へ戻す。</summary>
public sealed record AddEntity(SceneEntity Entity, int Index = -1) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc) => doc.AddEntity(Entity, Index);
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc) => [new RemoveEntity(Entity.Id)];
}

/// <summary>エンティティを 1 個削除する (コンポーネントごと)。逆 = 元の位置 (描画順) への復活。</summary>
public sealed record RemoveEntity(int EntityId) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc) => doc.RemoveEntity(EntityId);
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc)
    {
        int index = 0;
        for (; index < doc.Entities.Count; index++) if (doc.Entities[index].Id == EntityId) break;
        return [new AddEntity(doc.Entity(EntityId), index)];
    }
}

/// <summary>エンティティ名を変更する。</summary>
public sealed record RenameEntity(int EntityId, string Name) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc)
    {
        SceneEntity e = doc.Entity(EntityId);
        return doc.ReplaceEntity(SceneEntity.Of(e.Id, Name, e.Components));
    }
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc) => [new RenameEntity(EntityId, doc.Entity(EntityId).Name)];
}

/// <summary>コンポーネントを置換する (同型が無ければ追加)。インスペクタの「コンポーネント追加」もこれ。</summary>
public sealed record SetComponent(int EntityId, SceneComponent Component) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc) => doc.ReplaceEntity(doc.Entity(EntityId).WithComponent(Component));
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc)
        => doc.Entity(EntityId).Component(Component.Type) is { } old
            ? [new SetComponent(EntityId, old)]
            : [new RemoveComponent(EntityId, Component.Type)];
}

/// <summary>コンポーネントを外す (無ければ例外 — 呼び側の判定ミスを隠さない)。</summary>
public sealed record RemoveComponent(int EntityId, string Type) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc)
    {
        SceneEntity e = doc.Entity(EntityId);
        if (e.Component(Type) is null) throw new KeyNotFoundException($"コンポーネントが無い: entity {EntityId} の {Type}");
        return doc.ReplaceEntity(e.WithoutComponent(Type));
    }
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc)
        => [new SetComponent(EntityId, doc.Entity(EntityId).Component(Type)!)];
}

/// <summary>
/// コンポーネントの 1 フィールドを差し替える — 移動 (transform の pos) とインスペクタ編集の実体。
/// **コンポーネントとフィールドは存在していること** (スキーマ既定値で作られたコンポーネントは
/// 全フィールドを持つ)。無ければ例外。
/// </summary>
public sealed record SetField(int EntityId, string Type, string Field, SceneValue Value) : SceneChange
{
    public override SceneDoc Apply(SceneDoc doc)
    {
        SceneEntity e = doc.Entity(EntityId);
        SceneComponent c = e.Component(Type) ?? throw new KeyNotFoundException($"コンポーネントが無い: entity {EntityId} の {Type}");
        if (c.Get(Field) is null) throw new KeyNotFoundException($"フィールドが無い: {Type}.{Field} (entity {EntityId})");
        return doc.ReplaceEntity(e.WithComponent(c.With(Field, Value)));
    }
    public override IReadOnlyList<SceneChange> InvertAgainst(SceneDoc doc)
    {
        SceneComponent c = doc.Entity(EntityId).Component(Type)
            ?? throw new KeyNotFoundException($"コンポーネントが無い: entity {EntityId} の {Type}");
        SceneValue old = c.Get(Field) ?? throw new KeyNotFoundException($"フィールドが無い: {Type}.{Field}");
        return [new SetField(EntityId, Type, Field, old)];
    }
}

/// <summary>
/// 変更の列 — GraphChangeSet 相当。1 トランザクションが束ねる変更の単位で、まとめて適用/反転する。
/// <see cref="InvertAgainst"/> は各変更を**適用直前**の中間 Doc に対して反転し、逆順に並べて返す。
/// </summary>
public sealed class SceneChangeSet
{
    /// <summary>空の変更セット。</summary>
    public static readonly SceneChangeSet Empty = new([]);

    /// <summary>変更列 (適用順)。</summary>
    public IReadOnlyList<SceneChange> Changes { get; }

    public SceneChangeSet(IReadOnlyList<SceneChange> changes) => Changes = changes;

    public bool IsEmpty => Changes.Count == 0;

    public int Count => Changes.Count;

    /// <summary>全変更を順に適用した新しい Doc。</summary>
    public SceneDoc Apply(SceneDoc doc)
    {
        foreach (SceneChange c in Changes) doc = c.Apply(doc);
        return doc;
    }

    /// <summary>この変更セットの逆 (<paramref name="startDoc"/> に対して)。適用すると startDoc に戻る。</summary>
    public SceneChangeSet InvertAgainst(SceneDoc startDoc)
    {
        var before = new SceneDoc[Changes.Count];
        SceneDoc d = startDoc;
        for (int i = 0; i < Changes.Count; i++) { before[i] = d; d = Changes[i].Apply(d); }

        var inv = new List<SceneChange>();
        for (int i = Changes.Count - 1; i >= 0; i--) inv.AddRange(Changes[i].InvertAgainst(before[i]));
        return new SceneChangeSet(inv);
    }

    /// <summary>連結 (this の後に <paramref name="other"/>)。連続移動などの undo 畳み込みに使う。</summary>
    public SceneChangeSet Concat(SceneChangeSet other)
        => other.IsEmpty ? this : IsEmpty ? other : new SceneChangeSet([.. Changes, .. other.Changes]);
}
