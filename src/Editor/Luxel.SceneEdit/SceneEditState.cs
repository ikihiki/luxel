namespace Luxel.SceneEdit;

/// <summary>トランザクションの指定 — 変更 (複数を 1 セットに束ねる) + 結果選択 (NodeGraph の
/// GraphTransactionSpec 相当)。<see cref="Selection"/> 省略時は現在の選択を編集で写す
/// (削除エンティティへの参照は落ちる)。**viewport はここに無い** — カメラは空間アダプタ
/// (2D = pan/zoom、3D = 軌道) の所有で、型が空間ごとに違うためコアの状態に持たせない
/// (ADR-0016、空間非依存原則の帰結。undo 対象外なのは NodeGraph と同じ)。</summary>
public sealed class SceneTransactionSpec
{
    /// <summary>この編集での変更列 (省略 = 変更なし)。</summary>
    public IReadOnlyList<SceneChange>? Changes { get; init; }

    /// <summary>結果の選択 (省略 = 現在の選択を保持し、削除された参照を落とす)。</summary>
    public SceneSelection? Selection { get; init; }
}

/// <summary>
/// シーンエディタの**不変スナップショット** — 文書 + 選択 (NodeGraphState 相当)。
/// 編集は <see cref="Update"/> が <see cref="SceneTransaction"/> を作り、その
/// <see cref="SceneTransaction.State"/> が新しい状態になる (元は変わらない)。
/// 装飾 (gizmo ハイライト等) は必要になった段で足す。
/// </summary>
public sealed class SceneEditState
{
    public SceneDoc Doc { get; }

    public SceneSelection Selection { get; }

    internal SceneEditState(SceneDoc doc, SceneSelection selection)
    {
        Doc = doc;
        Selection = selection.Retain(doc);
    }

    /// <summary>初期状態を作る (省略時は空の 2D シーン・空選択)。</summary>
    public static SceneEditState Create(SceneDoc? doc = null, SceneSelection? selection = null)
        => new(doc ?? SceneDoc.Empty(SceneSpace.TwoD), selection ?? SceneSelection.Empty);

    /// <summary>指定からトランザクションを作る (適用は <see cref="SceneTransaction.State"/>)。</summary>
    public SceneTransaction Update(SceneTransactionSpec spec) => new(this, spec);

    /// <summary>変更列だけのトランザクション (選択は写す) の便宜。</summary>
    public SceneTransaction Apply(params SceneChange[] changes)
        => Update(new SceneTransactionSpec { Changes = changes });

    /// <summary>文書を変えず選択だけ差し替えるトランザクションの便宜。</summary>
    public SceneTransaction WithSelection(SceneSelection selection)
        => Update(new SceneTransactionSpec { Selection = selection });

    // undo/redo で新 Doc に切り替える (選択は Retain される)
    internal SceneEditState With(SceneDoc doc, SceneSelection selection) => new(doc, selection);
}

/// <summary>
/// 1 回の状態遷移 — 開始状態 + 変更セット + 結果選択 (GraphTransaction 相当)。
/// <see cref="State"/> が適用後の新しい <see cref="SceneEditState"/> (遅延計算・キャッシュ)。
/// undo 履歴はこの Transaction から作られる。
/// </summary>
public sealed class SceneTransaction
{
    public SceneEditState StartState { get; }

    public SceneChangeSet Changes { get; }

    public SceneSelection Selection { get; }

    private SceneEditState? _state;

    internal SceneTransaction(SceneEditState start, SceneTransactionSpec spec)
    {
        StartState = start;
        Changes = new SceneChangeSet(spec.Changes ?? []);
        Selection = spec.Selection ?? start.Selection;
    }

    /// <summary>文書が変わるか (変更が空でない)。</summary>
    public bool DocChanged => !Changes.IsEmpty;

    /// <summary>適用後の新しい状態 (遅延生成・キャッシュ)。</summary>
    public SceneEditState State => _state ??= new SceneEditState(Changes.Apply(StartState.Doc), Selection);
}

/// <summary>
/// undo/redo 履歴 — 各エントリが「変更セット + その逆 + 前後の選択」を持つ (GraphHistory 相当)。
/// **1 トランザクション = 1 undo** が基本。<see cref="Record"/> の <c>coalesce</c> で直前エントリと
/// 連結して連続移動を 1 undo に畳める (いつ畳むかは view が決める — コアは wall-clock を持たない)。
/// カメラは履歴に積まない (アダプタ所有)。
/// </summary>
public sealed class SceneHistory
{
    private readonly record struct Entry(SceneChangeSet Changes, SceneChangeSet Inverted, SceneSelection Before, SceneSelection After);

    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>undo スタックの深さ (テスト/観測用)。</summary>
    public int UndoDepth => _undo.Count;

    public void Clear() { _undo.Clear(); _redo.Clear(); }

    /// <summary>トランザクションを履歴に記録する (文書非変更なら無視)。redo スタックは破棄。
    /// <paramref name="coalesce"/>=true かつ直前エントリがあれば連結して 1 undo に畳む。</summary>
    public void Record(SceneTransaction tr, bool coalesce = false)
    {
        if (!tr.DocChanged) return;
        SceneChangeSet inverted = tr.Changes.InvertAgainst(tr.StartState.Doc);
        var entry = new Entry(tr.Changes, inverted, tr.StartState.Selection, tr.Selection);

        if (coalesce && _undo.Count > 0)
        {
            Entry prev = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _undo.Add(new Entry(
                prev.Changes.Concat(tr.Changes),
                inverted.Concat(prev.Inverted),
                prev.Before,
                tr.Selection));
        }
        else _undo.Add(entry);

        _redo.Clear();
    }

    /// <summary>現在状態に対し 1 手 undo した状態を返す (履歴が空なら現状のまま)。</summary>
    public SceneEditState Undo(SceneEditState current)
    {
        if (_undo.Count == 0) return current;
        Entry e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        return current.With(e.Inverted.Apply(current.Doc), e.Before);
    }

    /// <summary>現在状態に対し 1 手 redo した状態を返す (履歴が空なら現状のまま)。</summary>
    public SceneEditState Redo(SceneEditState current)
    {
        if (_redo.Count == 0) return current;
        Entry e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        return current.With(e.Changes.Apply(current.Doc), e.After);
    }
}
