namespace Luxel.UI;

/// <summary>Platform-neutral accessibility roles exposed by retained UI controls.</summary>
public enum SemanticRole
{
    Group,
    List,
    ListItem,
    Tree,
    TreeItem,
    TabList,
    Tab,
    Grid,
    Row,
    GridCell,
    Status,
    Button,
}

/// <summary>An immutable accessibility snapshot. Platform adapters can translate this tree to native semantics.</summary>
public sealed record SemanticNode(
    SemanticRole Role,
    string? Label = null,
    string? Key = null,
    bool Selected = false,
    bool Disabled = false,
    string? Description = null,
    IReadOnlyList<SemanticNode>? Children = null);

/// <summary>Implemented by controls that expose framework-level accessibility semantics.</summary>
public interface ISemanticProvider
{
    SemanticNode GetSemantics();
}
