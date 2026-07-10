namespace Luxel.NodeGraph;

/// <summary>
/// undo/redo 履歴 — 各エントリが「変更セット + その逆 + 前後の選択」を持つ ([[Luxel.Document.History]] 相当)。
/// **1 トランザクション = 1 undo** が基本で、複数ノード移動やパレット追加も 1 つの Transaction なので自動で
/// 1 undo になる。<see cref="Record"/> の <c>coalesce</c> で直前エントリと <see cref="GraphChangeSet.Concat"/>
/// して連続移動を 1 undo に畳める (いつ畳むかは view が決める — コアは wall-clock を持たない)。viewport (pan/zoom)
/// は履歴に積まない — undo/redo は現在の viewport を保つ。<see cref="NodeGraphState"/> とは別の可変コントローラ。
/// </summary>
public sealed class GraphHistory
{
    private readonly record struct Entry(GraphChangeSet Changes, GraphChangeSet Inverted, GraphSelection Before, GraphSelection After);

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
    /// <paramref name="coalesce"/>=true かつ直前エントリがあれば連結して 1 undo に畳む。</summary>
    public void Record(GraphTransaction tr, bool coalesce = false)
    {
        if (!tr.DocChanged) return;
        GraphChangeSet inverted = tr.Changes.InvertAgainst(tr.StartState.Doc);
        var entry = new Entry(tr.Changes, inverted, tr.StartState.Selection, tr.Selection);

        if (coalesce && _undo.Count > 0)
        {
            Entry prev = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _undo.Add(new Entry(
                prev.Changes.Concat(tr.Changes),        // 旧→新 (prev then tr)
                inverted.Concat(prev.Inverted),         // 新→旧 (invert(tr) then invert(prev))
                prev.Before,
                tr.Selection));
        }
        else _undo.Add(entry);

        _redo.Clear();
    }

    /// <summary>現在状態に対し 1 手 undo した状態を返す (履歴が空なら現状のまま)。viewport は現状維持。</summary>
    public NodeGraphState Undo(NodeGraphState current)
    {
        if (_undo.Count == 0) return current;
        Entry e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        return current.With(e.Inverted.Apply(current.Doc), e.Before);
    }

    /// <summary>現在状態に対し 1 手 redo した状態を返す (履歴が空なら現状のまま)。viewport は現状維持。</summary>
    public NodeGraphState Redo(NodeGraphState current)
    {
        if (_redo.Count == 0) return current;
        Entry e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        return current.With(e.Changes.Apply(current.Doc), e.After);
    }
}
