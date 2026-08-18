namespace Luxel.NodeGraph;

/// <summary>
/// Mutable controller for a node-graph document. It owns the canonical graph state and undo/redo history;
/// views only project and edit this document. Multiple views can therefore share one history and state.
/// </summary>
public sealed class GraphDocument
{
    private readonly GraphHistory _history = new();
    private NodeGraphState _state;

    public GraphDocument(NodeGraphDoc? doc = null)
        => _state = NodeGraphState.Create(doc);

    /// <summary>Current immutable editor-state snapshot.</summary>
    public NodeGraphState State => _state;

    /// <summary>Current graph data.</summary>
    public NodeGraphDoc Doc => _state.Doc;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    /// <summary>Raised after any state transition. The boolean indicates whether graph data changed.</summary>
    public event Action<GraphDocument, bool>? Changed;

    /// <summary>Apply a transaction and record graph-data changes in this document's history.</summary>
    public void Apply(GraphTransaction transaction, bool coalesce = false)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.DocChanged) _history.Record(transaction, coalesce);
        _state = transaction.State;
        Changed?.Invoke(this, transaction.DocChanged);
    }

    /// <summary>Apply graph changes as one undoable transaction.</summary>
    public void Apply(params GraphChange[] changes)
        => Apply(_state.Apply(changes));

    public void Undo()
    {
        if (!_history.CanUndo) return;
        _state = _history.Undo(_state);
        Changed?.Invoke(this, true);
    }

    public void Redo()
    {
        if (!_history.CanRedo) return;
        _state = _history.Redo(_state);
        Changed?.Invoke(this, true);
    }

    /// <summary>Replace the graph and clear selection, viewport, decorations, and history.</summary>
    public void Load(NodeGraphDoc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _state = NodeGraphState.Create(doc);
        _history.Clear();
        Changed?.Invoke(this, true);
    }
}
