using Luxel.NodeGraph;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// Resolver input for a node's inline widget. The context already identifies the node and parameter represented by
/// the slot; <see cref="Signal{T}"/> returns the matching document-backed Signal without requiring the widget factory
/// to inspect <see cref="GraphNode.Data"/> or create external state.
/// </summary>
public sealed class NodeWidgetContext : IDisposable
{
    private readonly GraphDocument _document;
    private readonly bool _readOnly;
    private IDisposable? _binding;

    internal NodeWidgetContext(GraphDocument document, WidgetSlot slot, NodeParameter? parameter, bool readOnly)
    {
        _document = document;
        _readOnly = readOnly;
        NodeId = slot.NodeId;
        Key = slot.Key;
        Parameter = parameter;
    }

    public int NodeId { get; }
    public object Key { get; }
    public NodeParameter? Parameter { get; }

    public Signal<T> Signal<T>()
    {
        if (Parameter is not NodeParameter<T> parameter)
        {
            string actual = Parameter is null ? "no parameter" : Parameter.ValueType.Name;
            throw new InvalidOperationException($"Node widget '{Key}' has {actual}; {typeof(T).Name} was requested.");
        }

        if (_binding is null) _binding = new NodeParameterSignal<T>(_document, NodeId, parameter, _readOnly);
        if (_binding is NodeParameterSignal<T> typed) return typed.Value;
        throw new InvalidOperationException($"Node widget '{Key}' already requested a different Signal type.");
    }

    public void Dispose()
    {
        _binding?.Dispose();
        _binding = null;
    }
}

/// <summary>
/// Two-way bridge between a typed <see cref="NodeParameter{T}"/> and a <see cref="GraphDocument"/>. Signal writes
/// become undoable graph changes; undo, redo, load, and edits from another view flow back into the same Signal.
/// </summary>
public sealed class NodeParameterSignal<T> : IDisposable
{
    private readonly GraphDocument _document;
    private readonly int _nodeId;
    private readonly NodeParameter<T> _parameter;
    private readonly bool _readOnly;
    private bool _synchronizing;
    private bool _disposed;

    public NodeParameterSignal(GraphDocument document, int nodeId, NodeParameter<T> parameter, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parameter);
        _document = document;
        _nodeId = nodeId;
        _readOnly = readOnly;
        _parameter = parameter;
        Value = new Signal<T>(parameter.Read(document.Doc.Node(nodeId)));
        Value.Changed += OnSignalChanged;
        _document.Changed += OnDocumentChanged;
    }

    public Signal<T> Value { get; }

    private void OnSignalChanged(T value)
    {
        if (_synchronizing || _disposed) return;
        if (_readOnly)
        {
            Synchronize();
            return;
        }

        GraphNode? node = _document.Doc.TryNode(_nodeId);
        if (node is null || EqualityComparer<T>.Default.Equals(_parameter.Read(node), value)) return;
        _document.Apply(_parameter.Set(_nodeId, value));
    }

    private void OnDocumentChanged(GraphDocument _, bool __) => Synchronize();

    private void Synchronize()
    {
        GraphNode? node = _document.Doc.TryNode(_nodeId);
        if (node is null) return;
        T value = _parameter.Read(node);
        if (EqualityComparer<T>.Default.Equals(Value.Peek(), value)) return;
        _synchronizing = true;
        try { Value.Value = value; }
        finally { _synchronizing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Value.Changed -= OnSignalChanged;
        _document.Changed -= OnDocumentChanged;
    }
}
