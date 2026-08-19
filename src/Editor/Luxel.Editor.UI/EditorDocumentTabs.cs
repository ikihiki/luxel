using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed class EditorDocumentTabs(EditorSession session) : CompositeControl
{
    public EditorSession Session { get; } = session;
    public string? MruDocumentId { get; private set; }

    public bool CloseActive()
    {
        IEditorDocument? active = Session.ActiveDocument;
        if (active is null) return false;
        string? id = Session.IdOf(active);
        return id is not null && Session.CloseDocument(id);
    }

    protected override Widget Build()
    {
        IEditorDocument? active = Session.Workspace.Active.Value;
        string? activeId = active is null ? null : Session.IdOf(active);
        var items = Session.OpenDocuments.Select(x => new TabStripItem(x.Key, x.Value.Title, x.Value.Dirty)).ToArray();
        return TabStrip(items: items, selectedKey: activeId,
            onSelect: (_, id) => { MruDocumentId = activeId; Session.ActivateDocument(id); },
            onCloseRequest: (_, id) => Session.CloseDocument(id),
            onDropRequest: (_, request) =>
            {
                DockGroup? group = Session.Layout.Peek().GroupOf(request.Key);
                if (group is not null) Session.Layout.Value = Session.Layout.Peek().MoveTab(request.Key, group.Id, request.Index);
            });
    }
}
