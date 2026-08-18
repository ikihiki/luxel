using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>Gallery/test composition root that owns a portable Editor session.</summary>
public sealed class EditorTestFixture : CompositeControl
{
    public required EditorSession Session { get; init; }
    public string ProductName { get; init; } = "Luxel Editor";
    public EditorShell? Shell { get; private set; }

    protected override void OnRealize(UiBuildContext ctx) => ctx.Own(Session);

    protected override Widget Build()
    {
        Shell = EditorKit.EditorShell(Session, productName: ProductName);
        Shell.HAlign.SetBase(Align.Stretch);
        Shell.VAlign.SetBase(Align.Stretch);
        return Shell;
    }
}

/// <summary>Portable Editor chrome shared by Browser, Native, and Gallery fixtures.</summary>
[UiComponent]
public sealed partial class EditorShell : CompositeControl
{
    [UiParam] private readonly Bindable<EditorSession> _session = new();
    [UiParam] private readonly Bindable<string> _productName = "Luxel Editor";

    private Toolbar? _toolbar;
    public DockHost? DocumentsHost { get; private set; }

    protected override void OnRealize(UiBuildContext ctx)
    {
        EditorSession session = Session.Get();
        if (ctx.Host is { } host) ctx.Own(session.Commands.BindShortcuts(host));
        bool initialized = false;
        ctx.Own(Reactive.Effect(() =>
        {
            _ = session.Workspace.AnyDirty.Value;
            _ = session.Workspace.Active.Value;
            if (initialized) _toolbar?.Refresh();
            else initialized = true;
        }));
    }

    protected override Widget Build()
    {
        EditorSession session = Session.Get();
        Func<IReadOnlyList<CommandContribution>> contributions =
            () => session.Workspace.Active.Value?.Contributions ?? [];

        MenuBar menuBar = EditorKit.MenuBar(session.Commands, contributions: contributions);
        _toolbar = EditorKit.Toolbar(session.Commands, contributions: contributions);
        Widget toolbarChrome = Border(
            background: Bind.From(() => UiTheme.T.Surface),
            padding: new Thickness(4, 2),
            hAlign: Align.Stretch)[_toolbar];
        DocumentsHost = EditorKit.DockHost(session.Layout, session.ResolveDockItem, closeRemoves: false,
            onCloseTab: (_, id) => session.CloseDocument(id));
        StatusBar status = StatusBar(
            left: [Muted(ProductName.Get()), Muted($"{session.OpenDocuments.Count} docs")],
            right: [Badge(session.StatusText.Peek(), Intent.Primary)]);

        return Grid(rows:
        [
            GridLength.Px(MenuBar.BarH),
            GridLength.Px(34),
            GridLength.Star(),
            GridLength.Px(StatusBar.BarH),
        ])
        [
            menuBar.GridRow(0),
            toolbarChrome.GridRow(1),
            DocumentsHost.GridRow(2),
            status.GridRow(3)
        ];
    }
}
