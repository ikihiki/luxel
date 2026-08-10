namespace Luxel.Document;

/// <summary>
/// undo/redo 履歴 — 各エントリが「変更セット + その逆 + 前後の選択」を持つ。1 トランザクション = 1 undo が基本で、
/// マルチカーソルの 1 打鍵も 1 つの Transaction なので自動で 1 undo になる。<see cref="Record"/> の
/// <c>coalesce</c> で直前エントリと <see cref="ChangeSet.Compose"/> して連続タイプを 1 undo に畳める
/// (いつ畳むかは view が決める — コアは wall-clock を持たない)。EditorState とは別の可変コントローラ。
/// </summary>
public sealed class History
{
    private readonly record struct Entry(ChangeSet Changes, ChangeSet Inverted, EditorSelection Before, EditorSelection After);

    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();

    /// <summary>undo できるか。</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>redo できるか。</summary>
    public bool CanRedo => _redo.Count > 0;
    /// <summary>undo スタックの深さ (テスト/観測用)。</summary>
    public int UndoDepth => _undo.Count;

    /// <summary>全消去。</summary>
    public void Clear() { _undo.Clear(); _redo.Clear(); }

    /// <summary>トランザクションを履歴に記録する (文書非変更なら無視)。redo スタックは破棄。
    /// <paramref name="coalesce"/>=true かつ直前エントリがあれば合成して 1 undo に畳む。</summary>
    public void Record(Transaction tr, bool coalesce = false)
    {
        if (!tr.DocChanged) return;
        ChangeSet inverted = tr.Changes.Invert(tr.StartState.Doc.Text);
        var entry = new Entry(tr.Changes, inverted, tr.StartState.Selection, tr.Selection);

        if (coalesce && _undo.Count > 0)
        {
            Entry prev = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _undo.Add(new Entry(
                prev.Changes.Compose(tr.Changes),        // 旧→新 (prev then tr)
                inverted.Compose(prev.Inverted),         // 新→旧 (invert(tr) then invert(prev))
                prev.Before,
                tr.Selection));
        }
        else _undo.Add(entry);

        _redo.Clear();
    }

    /// <summary>現在状態に対し 1 手 undo した状態を返す (履歴が空なら現状のまま)。装飾は逆変更で写す。</summary>
    public EditorState Undo(EditorState current)
    {
        if (_undo.Count == 0) return current;
        Entry e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        return new EditorState(current.Doc.ApplyChange(e.Inverted), e.Before, current.Decorations.Map(e.Inverted));
    }

    /// <summary>現在状態に対し 1 手 redo した状態を返す (履歴が空なら現状のまま)。装飾は変更で写す。</summary>
    public EditorState Redo(EditorState current)
    {
        if (_redo.Count == 0) return current;
        Entry e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        return new EditorState(current.Doc.ApplyChange(e.Changes), e.After, current.Decorations.Map(e.Changes));
    }
}
