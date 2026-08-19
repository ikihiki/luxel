namespace Luxel.ValueDocument;

/// <summary>Immutable structural edits over a <see cref="ValueDocument"/> tree.</summary>
public sealed partial class ValueDocument
{
    public ValueApplyResult ReplaceScalar(NodeId nodeId, ValueScalarNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryGetNode(nodeId, out ValueNode? existing) || existing is not ValueScalarNode)
            return Failure(ValueApplyStatus.NodeNotFound, "The scalar node was not found.");

        ValueScalarNode replacement = value.ScalarKind switch
        {
            ValueScalarKind.Null => ValueScalarNode.Null(nodeId),
            ValueScalarKind.Boolean => ValueScalarNode.FromBoolean(nodeId, value.Boolean),
            ValueScalarKind.String => ValueScalarNode.FromString(nodeId, value.Text!),
            ValueScalarKind.Number => ValueScalarNode.FromNumber(nodeId, value.NumberLexeme),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        return CommitStructural(nodeId, _ => replacement, new ValueSelection(nodeId, PointerOf(nodeId)));
    }

    public ValueApplyResult AddObjectProperty(NodeId objectId, string name, ValueNode value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!TryGetNode(objectId, out ValueNode? node) || node is not ValueObjectNode obj)
            return Failure(ValueApplyStatus.NodeNotFound, "The object node was not found.");
        if (TryGetNode(value.Id, out _))
            return Failure(ValueApplyStatus.InvalidOperation, "The inserted value already belongs to this document.");
        if (obj.Properties.Any(property => string.Equals(property.Name, name, StringComparison.Ordinal)))
            return Failure(ValueApplyStatus.InvalidOperation, $"Property '{name}' already exists.");

        string parentPointer = PointerOf(objectId)!;
        return CommitStructural(objectId,
            current => new ValueObjectNode(current.Id, ((ValueObjectNode)current).Properties.Append(new ValueProperty(name, value))),
            new ValueSelection(value.Id, AppendPointer(parentPointer, name)));
    }

    public ValueApplyResult RemoveObjectProperty(NodeId objectId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryGetNode(objectId, out ValueNode? node) || node is not ValueObjectNode obj)
            return Failure(ValueApplyStatus.NodeNotFound, "The object node was not found.");
        int index = IndexOfProperty(obj, name);
        if (index < 0) return Failure(ValueApplyStatus.NodeNotFound, $"Property '{name}' was not found.");

        ValueSelection fallback = ObjectDeletionFallback(obj, objectId, index);
        return CommitStructural(objectId,
            current => new ValueObjectNode(current.Id, ((ValueObjectNode)current).Properties.Where((_, i) => i != index)),
            fallback);
    }

    public ValueApplyResult RenameObjectProperty(NodeId objectId, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        if (!TryGetNode(objectId, out ValueNode? node) || node is not ValueObjectNode obj)
            return Failure(ValueApplyStatus.NodeNotFound, "The object node was not found.");
        int index = IndexOfProperty(obj, oldName);
        if (index < 0) return Failure(ValueApplyStatus.NodeNotFound, $"Property '{oldName}' was not found.");
        if (!string.Equals(oldName, newName, StringComparison.Ordinal)
            && obj.Properties.Any(property => string.Equals(property.Name, newName, StringComparison.Ordinal)))
            return Failure(ValueApplyStatus.InvalidOperation, $"Property '{newName}' already exists.");

        ValueNode child = obj.Properties[index].Value;
        string pointer = AppendPointer(PointerOf(objectId)!, newName);
        return CommitStructural(objectId, current => new ValueObjectNode(current.Id,
            ((ValueObjectNode)current).Properties.Select((property, i) =>
                i == index ? new ValueProperty(newName, property.Value) : property)),
            new ValueSelection(child.Id, pointer));
    }

    public ValueApplyResult InsertArrayItem(NodeId arrayId, int index, ValueNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryGetNode(arrayId, out ValueNode? node) || node is not ValueArrayNode array)
            return Failure(ValueApplyStatus.NodeNotFound, "The array node was not found.");
        if (TryGetNode(value.Id, out _))
            return Failure(ValueApplyStatus.InvalidOperation, "The inserted value already belongs to this document.");
        if ((uint)index > (uint)array.Items.Count)
            return Failure(ValueApplyStatus.InvalidOperation, "The array insertion index is out of range.");

        string pointer = AppendPointer(PointerOf(arrayId)!, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return CommitStructural(arrayId, current =>
        {
            var items = ((ValueArrayNode)current).Items.ToList();
            items.Insert(index, value);
            return new ValueArrayNode(current.Id, items);
        }, new ValueSelection(value.Id, pointer));
    }

    public ValueApplyResult RemoveArrayItem(NodeId arrayId, int index)
    {
        if (!TryGetNode(arrayId, out ValueNode? node) || node is not ValueArrayNode array)
            return Failure(ValueApplyStatus.NodeNotFound, "The array node was not found.");
        if ((uint)index >= (uint)array.Items.Count)
            return Failure(ValueApplyStatus.InvalidOperation, "The array removal index is out of range.");

        ValueSelection fallback = ArrayDeletionFallback(array, arrayId, index);
        return CommitStructural(arrayId, current =>
            new ValueArrayNode(current.Id, ((ValueArrayNode)current).Items.Where((_, i) => i != index)), fallback);
    }

    public ValueApplyResult MoveArrayItem(NodeId arrayId, int fromIndex, int toIndex)
    {
        if (!TryGetNode(arrayId, out ValueNode? node) || node is not ValueArrayNode array)
            return Failure(ValueApplyStatus.NodeNotFound, "The array node was not found.");
        if ((uint)fromIndex >= (uint)array.Items.Count || (uint)toIndex >= (uint)array.Items.Count)
            return Failure(ValueApplyStatus.InvalidOperation, "The array move index is out of range.");
        if (fromIndex == toIndex) return new ValueApplyResult(ValueApplyStatus.Accepted);

        ValueNode moved = array.Items[fromIndex];
        string pointer = AppendPointer(PointerOf(arrayId)!, toIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return CommitStructural(arrayId, current =>
        {
            var items = ((ValueArrayNode)current).Items.ToList();
            ValueNode item = items[fromIndex];
            items.RemoveAt(fromIndex);
            items.Insert(toIndex, item);
            return new ValueArrayNode(current.Id, items);
        }, new ValueSelection(moved.Id, pointer));
    }

    public bool TryGetNode(NodeId nodeId, out ValueNode? node) => TryFind(AcceptedRoot, nodeId, out node);

    public string? PointerOf(NodeId nodeId)
        => TryFindPointer(AcceptedRoot, nodeId, string.Empty, out string? pointer) ? pointer : null;

    private ValueApplyResult CommitStructural(NodeId targetId, Func<ValueNode, ValueNode> replace, ValueSelection selection)
    {
        if (HasInvalidDirtyRawDraft)
        {
            ValidateRawDraft();
            return new ValueApplyResult(ValueApplyStatus.InvalidRawDraft);
        }
        if (!TryReplace(AcceptedRoot, targetId, replace, out ValueNode? root))
            return Failure(ValueApplyStatus.NodeNotFound, "The target node was not found.");
        return ReplaceRoot(root!, ValueTransactionOrigin.Tree, selection);
    }

    private ValueApplyResult Failure(ValueApplyStatus status, string message)
    {
        _diagnostics = [CreateStateDiagnostic(message, "tree")];
        return new ValueApplyResult(status);
    }

    private ValueSelection ObjectDeletionFallback(ValueObjectNode obj, NodeId objectId, int deletedIndex)
    {
        if (obj.Properties.Count > 1)
        {
            int next = deletedIndex < obj.Properties.Count - 1 ? deletedIndex + 1 : deletedIndex - 1;
            ValueProperty property = obj.Properties[next];
            return new ValueSelection(property.Value.Id, AppendPointer(PointerOf(objectId)!, property.Name));
        }
        return new ValueSelection(objectId, PointerOf(objectId));
    }

    private ValueSelection ArrayDeletionFallback(ValueArrayNode array, NodeId arrayId, int deletedIndex)
    {
        if (array.Items.Count > 1)
        {
            int oldIndex = deletedIndex < array.Items.Count - 1 ? deletedIndex + 1 : deletedIndex - 1;
            int newIndex = deletedIndex < array.Items.Count - 1 ? deletedIndex : deletedIndex - 1;
            return new ValueSelection(array.Items[oldIndex].Id,
                AppendPointer(PointerOf(arrayId)!, newIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return new ValueSelection(arrayId, PointerOf(arrayId));
    }

    private static int IndexOfProperty(ValueObjectNode obj, string name)
    {
        for (int i = 0; i < obj.Properties.Count; i++)
            if (string.Equals(obj.Properties[i].Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static string AppendPointer(string parent, string segment) => parent + "/" + JsonPointer.Escape(segment);

    private static bool TryFind(ValueNode current, NodeId id, out ValueNode? found)
    {
        if (current.Id == id) { found = current; return true; }
        IEnumerable<ValueNode> children = current switch
        {
            ValueObjectNode obj => obj.Properties.Select(property => property.Value),
            ValueArrayNode array => array.Items,
            _ => [],
        };
        foreach (ValueNode child in children)
            if (TryFind(child, id, out found)) return true;
        found = null;
        return false;
    }

    private static bool TryFindPointer(ValueNode current, NodeId id, string pointer, out string? found)
    {
        if (current.Id == id) { found = pointer; return true; }
        if (current is ValueObjectNode obj)
        {
            foreach (ValueProperty property in obj.Properties)
                if (TryFindPointer(property.Value, id, AppendPointer(pointer, property.Name), out found)) return true;
        }
        else if (current is ValueArrayNode array)
        {
            for (int i = 0; i < array.Items.Count; i++)
                if (TryFindPointer(array.Items[i], id, AppendPointer(pointer, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), out found)) return true;
        }
        found = null;
        return false;
    }

    private static bool TryReplace(ValueNode current, NodeId id, Func<ValueNode, ValueNode> replace, out ValueNode? result)
    {
        if (current.Id == id) { result = replace(current); return true; }
        if (current is ValueObjectNode obj)
        {
            for (int i = 0; i < obj.Properties.Count; i++)
            {
                ValueProperty property = obj.Properties[i];
                if (!TryReplace(property.Value, id, replace, out ValueNode? child)) continue;
                result = new ValueObjectNode(obj.Id, obj.Properties.Select((item, index) =>
                    index == i ? new ValueProperty(item.Name, child!) : item));
                return true;
            }
        }
        else if (current is ValueArrayNode array)
        {
            for (int i = 0; i < array.Items.Count; i++)
            {
                if (!TryReplace(array.Items[i], id, replace, out ValueNode? child)) continue;
                result = new ValueArrayNode(array.Id, array.Items.Select((item, index) => index == i ? child! : item));
                return true;
            }
        }
        result = null;
        return false;
    }
}
