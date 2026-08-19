using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public interface IEditorStatusContribution
{
    string Key { get; }
    StatusBarRegion Region { get; }
    int Priority { get; }
    bool IsVisible(EditorSession session);
    Widget Create(EditorSession session);
}

public sealed class EditorStatusBar(EditorSession session, IEnumerable<IEditorStatusContribution>? contributions = null) : CompositeControl
{
    public EditorSession Session { get; } = session;
    public IReadOnlyList<IEditorStatusContribution> Contributions { get; } = contributions?.ToArray() ?? [];

    protected override Widget Build()
    {
        _ = Session.Workspace.Active.Value;
        _ = Session.Workspace.AnyDirty.Value;
        bool isPlaying = Session.IsPlaying.Value;
        StatusBarItem[] items = Contributions.Where(x => x.IsVisible(Session))
            .Select(x => new StatusBarItem(x.Key, x.Create(Session), x.Region, x.Priority)).ToArray();
        return StatusBar(
            left: [Muted($"{Session.OpenDocuments.Count} docs")],
            center: [Text((Func<string>)(() => Session.StatusText.Value))],
            right: [Badge(isPlaying ? "Playing" : "Ready", Intent.Primary)],
            items: items);
    }
}
