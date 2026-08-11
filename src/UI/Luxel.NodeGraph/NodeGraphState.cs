namespace Luxel.NodeGraph;

/// <summary>トランザクションの指定 — 変更 (複数を 1 セットに束ねる) + 結果選択 + viewport + 装飾の副作用
/// ([[Luxel.Document.TransactionSpec]] 相当)。<see cref="Selection"/> 省略時は現在の選択を編集で写す
/// (削除ノード/辺への参照は落ちる)。<see cref="Effects"/> は変更適用後の新しい Doc を基準に解釈される。</summary>
public sealed class GraphTransactionSpec
{
    /// <summary>この編集での変更列 (省略 = 変更なし)。</summary>
    public IReadOnlyList<GraphChange>? Changes { get; init; }
    /// <summary>結果の選択 (省略 = 現在の選択を保持し、削除された参照を落とす)。</summary>
    public GraphSelection? Selection { get; init; }
    /// <summary>結果の viewport (省略 = 現在のまま)。</summary>
    public GraphViewport? Viewport { get; init; }
    /// <summary>装飾等の副作用 (省略 = なし)。変更適用後の新しい Doc を基準に解釈される。</summary>
    public IReadOnlyList<GraphStateEffect>? Effects { get; init; }
}

/// <summary>
/// ノードエディタの**不変スナップショット** — 文書 + 選択 + viewport ([[Luxel.Document.EditorState]] 相当)。
/// 編集は <see cref="Update"/> が <see cref="GraphTransaction"/> を作り、その <see cref="GraphTransaction.State"/>
/// が新しい状態になる (元は変わらない)。装飾フィールドは S2 でここに足す。
/// </summary>
public sealed class NodeGraphState
{
    /// <summary>文書 (ノード + 辺)。</summary>
    public NodeGraphDoc Doc { get; }
    /// <summary>選択。</summary>
    public GraphSelection Selection { get; }
    /// <summary>pan/zoom。</summary>
    public GraphViewport Viewport { get; }
    /// <summary>装飾テーブル (owner 別)。</summary>
    public GraphDecorationTable Decorations { get; }

    // 装飾は呼び出し側 (Transaction.Build / With) が Doc と整合させてから渡す契約 (テキストスタックと同じ)。
    internal NodeGraphState(NodeGraphDoc doc, GraphSelection selection, GraphViewport viewport, GraphDecorationTable? decorations = null)
    {
        Doc = doc;
        Selection = selection.Retain(doc);
        Viewport = viewport;
        Decorations = decorations ?? GraphDecorationTable.Empty;
    }

    /// <summary>初期状態を作る (省略時は空グラフ・空選択・既定 viewport・装飾なし)。</summary>
    public static NodeGraphState Create(NodeGraphDoc? doc = null, GraphSelection? selection = null, GraphViewport? viewport = null)
        => new(doc ?? NodeGraphDoc.Empty, selection ?? GraphSelection.Empty, viewport ?? GraphViewport.Default);

    /// <summary>指定からトランザクションを作る (適用は <see cref="GraphTransaction.State"/>)。</summary>
    public GraphTransaction Update(GraphTransactionSpec spec) => new(this, spec);

    /// <summary>変更列だけのトランザクション (選択/viewport は写す・保持) の便宜。</summary>
    public GraphTransaction Apply(params GraphChange[] changes)
        => Update(new GraphTransactionSpec { Changes = changes });

    /// <summary>文書を変えず選択だけ差し替えるトランザクションの便宜。</summary>
    public GraphTransaction WithSelection(GraphSelection selection)
        => Update(new GraphTransactionSpec { Selection = selection });

    /// <summary>文書を変えず viewport だけ差し替えるトランザクションの便宜 (pan/zoom)。</summary>
    public GraphTransaction WithViewport(GraphViewport viewport)
        => Update(new GraphTransactionSpec { Viewport = viewport });

    /// <summary>文書を変えず owner の装飾を差し替えるトランザクションの便宜 (プロバイダ更新)。</summary>
    public GraphTransaction WithDecorations(string owner, GraphDecorationSet set)
        => Update(new GraphTransactionSpec { Effects = [new SetGraphDecorations(owner, set)] });

    // undo/redo で新 Doc に切り替える際、装飾は削除対象を落として retain する (provider が後で再供給する契約)。
    internal NodeGraphState With(NodeGraphDoc doc, GraphSelection selection) => new(doc, selection, Viewport, Decorations.Map(doc));
}

/// <summary>
/// 1 回の状態遷移 — 開始状態 + 変更セット + 結果選択/viewport ([[Luxel.Document.Transaction]] 相当)。
/// <see cref="State"/> が適用後の新しい <see cref="NodeGraphState"/> (遅延計算・キャッシュ)。undo 履歴は
/// この Transaction から作られる。
/// </summary>
public sealed class GraphTransaction
{
    /// <summary>この遷移の開始状態。</summary>
    public NodeGraphState StartState { get; }
    /// <summary>変更セット。</summary>
    public GraphChangeSet Changes { get; }
    /// <summary>結果の選択 (State 生成時に新 Doc へ Retain される)。</summary>
    public GraphSelection Selection { get; }
    /// <summary>結果の viewport。</summary>
    public GraphViewport Viewport { get; }
    /// <summary>副作用 (装飾更新など)。</summary>
    public IReadOnlyList<GraphStateEffect> Effects { get; }

    private NodeGraphState? _state;

    internal GraphTransaction(NodeGraphState start, GraphTransactionSpec spec)
    {
        StartState = start;
        Changes = new GraphChangeSet(spec.Changes ?? []);
        Selection = spec.Selection ?? start.Selection;
        Viewport = spec.Viewport ?? start.Viewport;
        Effects = spec.Effects ?? [];
    }

    /// <summary>文書が変わるか (変更が空でない)。</summary>
    public bool DocChanged => !Changes.IsEmpty;

    /// <summary>適用後の新しい状態 (遅延生成・キャッシュ)。既存装飾を新 Doc へ写した後、effect を適用する。</summary>
    public NodeGraphState State => _state ??= Build();

    private NodeGraphState Build()
    {
        NodeGraphDoc doc = Changes.Apply(StartState.Doc);
        GraphDecorationTable table = StartState.Decorations.Map(doc);   // 既存装飾を新 Doc へ (削除対象を落とす)
        foreach (GraphStateEffect eff in Effects) table = eff.ApplyTo(table);
        return new NodeGraphState(doc, Selection, Viewport, table);
    }
}
