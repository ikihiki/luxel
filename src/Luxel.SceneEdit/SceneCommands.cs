namespace Luxel.SceneEdit;

/// <summary>
/// 状態遷移を組み立てる純関数群 (GraphCommands 相当) — <c>(SceneEditState) → SceneTransaction</c>。
/// **空間非依存のものだけ**をここに置く: 座標を伴う移動 (BuildMove) や複製オフセットは
/// 空間アダプタ (view 層) が <see cref="SetField"/> 列を組み立てる (ADR-0016 原則 3)。
/// view はヒット/幾何を解決してからこれらを呼ぶ。
/// </summary>
public static class SceneCommands
{
    /// <summary>エンティティを 1 個追加し、それを選択する。</summary>
    public static SceneTransaction AddEntity(SceneEditState s, SceneEntity entity)
        => s.Update(new SceneTransactionSpec { Changes = [new AddEntity(entity)], Selection = SceneSelection.Single(entity.Id) });

    /// <summary>選択中のエンティティを削除する。1 トランザクション = 1 undo。</summary>
    public static SceneTransaction DeleteSelection(SceneEditState s)
        => s.Update(new SceneTransactionSpec
        {
            Changes = s.Selection.Entities.Select(id => (SceneChange)new RemoveEntity(id)).ToList(),
            Selection = SceneSelection.Empty,
        });

    /// <summary>選択中のエンティティを複製し、複製を選択する (1 undo)。id は max+1 から連番。
    /// <paramref name="offset"/> で空間アダプタが位置ずらし等を注入できる (null = そのまま複製)。</summary>
    public static SceneTransaction DuplicateSelection(SceneEditState s, Func<SceneEntity, SceneEntity>? offset = null)
    {
        if (s.Selection.IsEmpty) return s.Update(new SceneTransactionSpec());
        int next = NextEntityId(s.Doc);
        var changes = new List<SceneChange>();
        var clones = new List<int>();
        foreach (int id in s.Selection.Entities)   // 昇順 (正規化済み) = 決定的
        {
            SceneEntity src = s.Doc.Entity(id);
            var clone = SceneEntity.Of(next, src.Name, src.Components);
            if (offset is not null) clone = offset(clone);
            changes.Add(new AddEntity(clone));
            clones.Add(next);
            next++;
        }
        return s.Update(new SceneTransactionSpec { Changes = changes, Selection = SceneSelection.Of(clones) });
    }

    /// <summary>選択を差し替える (文書は変えない)。</summary>
    public static SceneTransaction Select(SceneEditState s, SceneSelection selection)
        => s.Update(new SceneTransactionSpec { Selection = selection });

    /// <summary>指定エンティティ群を選択する。</summary>
    public static SceneTransaction SelectEntities(SceneEditState s, IEnumerable<int> ids, int main = -1)
        => Select(s, SceneSelection.Of(ids, main));

    /// <summary>全エンティティを選択する。</summary>
    public static SceneTransaction SelectAll(SceneEditState s)
        => Select(s, SceneSelection.Of(s.Doc.Entities.Select(e => e.Id)));

    /// <summary>選択を解除する。</summary>
    public static SceneTransaction SelectNone(SceneEditState s) => Select(s, SceneSelection.Empty);

    /// <summary>次の空きエンティティ id (max+1)。</summary>
    public static int NextEntityId(SceneDoc doc)
    {
        int max = 0;
        foreach (SceneEntity e in doc.Entities) max = Math.Max(max, e.Id);
        return max + 1;
    }
}
