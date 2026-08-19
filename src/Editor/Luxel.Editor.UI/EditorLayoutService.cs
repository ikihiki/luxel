using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

public sealed record EditorLayoutRestoreResult(DockTree Layout, bool UsedFallback, string? Reason);

public sealed class EditorLayoutService : IDisposable
{
    public const string SettingsKey = "editor.layout.v1";
    private readonly IEditorSettingsStore _settings;
    private readonly Func<DockTree> _defaultLayout;
    private readonly HashSet<string> _paneIds;
    private IDisposable? _sync;
    private DockTree? _focusRestore;

    public EditorLayoutService(IEditorSettingsStore settings, Func<DockTree> defaultLayout, IEnumerable<string> paneIds)
    {
        _settings = settings;
        _defaultLayout = defaultLayout;
        _paneIds = paneIds.ToHashSet(StringComparer.Ordinal);
    }

    public void RegisterItemId(string itemId) => _paneIds.Add(itemId);
    public void UnregisterItemId(string itemId) => _paneIds.Remove(itemId);

    public Signal<string?> LastStatus { get; } = new(null);
    public bool IsFocusMode => _focusRestore is not null;

    public EditorLayoutRestoreResult Restore()
    {
        string? json = _settings.Read(SettingsKey);
        if (string.IsNullOrWhiteSpace(json)) return new(_defaultLayout(), false, null);
        try
        {
            DockTree layout = DockTree.Deserialize(json);
            string? invalid = Validate(layout);
            if (invalid is not null) throw new InvalidDataException(invalid);
            LastStatus.Value = null;
            return new(layout, false, null);
        }
        catch (Exception ex)
        {
            string reason = $"Layout reset to default: {ex.Message}";
            LastStatus.Value = reason;
            return new(_defaultLayout(), true, reason);
        }
    }

    public void Attach(Signal<DockTree> layout)
    {
        _sync?.Dispose();
        bool initial = true;
        _sync = Reactive.Effect(() =>
        {
            DockTree value = layout.Value;
            if (initial) { initial = false; return; }
            if (_focusRestore is not null) return; // focus mode is temporary and must not replace the durable layout
            string? invalid = Validate(value);
            if (invalid is not null)
            {
                LastStatus.Value = invalid;
                return;
            }
            _settings.Write(SettingsKey, value.Serialize());
        });
    }

    public void Reset(Signal<DockTree> layout)
    {
        _focusRestore = null;
        layout.Value = _defaultLayout();
        LastStatus.Value = "Layout reset.";
    }

    public bool SetPaneVisible(Signal<DockTree> layout, string paneId, bool visible)
    {
        if (!_paneIds.Contains(paneId)) return false;
        DockTree current = layout.Peek();
        bool present = current.GroupOf(paneId) is not null;
        if (present == visible) return false;
        layout.Value = visible
            ? current.AddTab(current.Groups.First().Id, paneId)
            : current.RemoveTab(paneId);
        return true;
    }

    public bool EnterFocusMode(Signal<DockTree> layout, string paneId)
    {
        if (!_paneIds.Contains(paneId) || layout.Peek().GroupOf(paneId) is null || _focusRestore is not null) return false;
        _focusRestore = layout.Peek();
        layout.Value = DockTree.Single(paneId);
        return true;
    }

    public bool ExitFocusMode(Signal<DockTree> layout)
    {
        if (_focusRestore is null) return false;
        DockTree previous = _focusRestore;
        _focusRestore = null;
        layout.Value = previous;
        return true;
    }

    public string? Validate(DockTree layout)
    {
        var tabs = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new HashSet<int>();
        string? Walk(DockNode node)
        {
            if (!nodeIds.Add(node.Id)) return $"Duplicate dock node id: {node.Id}.";
            if (node is DockGroup group)
            {
                if (group.Active < -1 || group.Active >= group.Tabs.Count || (group.Tabs.Count == 0 && group.Active != -1))
                    return $"Invalid active tab index in group {group.Id}.";
                foreach (string tab in group.Tabs)
                {
                    if (!_paneIds.Contains(tab)) return $"Unknown pane id: {tab}.";
                    if (!tabs.Add(tab)) return $"Duplicate pane id: {tab}.";
                }
                return null;
            }
            var split = (DockSplit)node;
            if (split.Children.Count < 2 || split.Sizes.Count != split.Children.Count || split.Sizes.Any(x => x <= 0))
                return $"Malformed split {split.Id}.";
            foreach (DockNode child in split.Children)
                if (Walk(child) is { } error) return error;
            return null;
        }

        string? result = Walk(layout.Root);
        if (result is not null) return result;
        foreach (DockFloat floating in layout.Floats)
        {
            if (floating.W < 120 || floating.H < 80 || !float.IsFinite(floating.X + floating.Y + floating.W + floating.H))
                return $"Malformed floating pane {floating.Group.Id}.";
            if (Walk(floating.Group) is { } error) return error;
        }
        return null;
    }

    public void Dispose() => _sync?.Dispose();
}
