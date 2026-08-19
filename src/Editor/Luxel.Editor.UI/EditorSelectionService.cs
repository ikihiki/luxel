using Luxel.UI;

namespace Luxel.Controls;

public sealed record EditorSelectionState(string? DocumentId, IReadOnlyList<int> EntityIds, int MainEntityId = -1)
{
    public static EditorSelectionState Empty { get; } = new(null, []);
    public bool IsEmpty => EntityIds.Count == 0;
}

public sealed class EditorSelectionService
{
    public Signal<EditorSelectionState> Current { get; } = new(EditorSelectionState.Empty);

    public void SelectEntities(string documentId, IEnumerable<int> entityIds, int mainEntityId = -1)
    {
        int[] ids = entityIds.Distinct().Order().ToArray();
        if (ids.Length == 0) { Clear(); return; }
        int main = ids.Contains(mainEntityId) ? mainEntityId : ids[0];
        EditorSelectionState current = Current.Peek();
        if (current.DocumentId == documentId && current.MainEntityId == main && current.EntityIds.SequenceEqual(ids)) return;
        Current.Value = new(documentId, ids, main);
    }

    public void RemoveInvalid(string documentId, Func<int, bool> exists)
    {
        EditorSelectionState selection = Current.Peek();
        if (selection.DocumentId != documentId) return;
        int[] valid = selection.EntityIds.Where(exists).ToArray();
        if (valid.Length == 0) Clear();
        else SelectEntities(documentId, valid, selection.MainEntityId);
    }

    public void Clear() => Current.Value = EditorSelectionState.Empty;
}
