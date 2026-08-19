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
    private Theme? _systemTheme;
    public EditorDocumentTabs? DocumentTabs { get; private set; }
    public DockHost? DocumentsHost { get; private set; }

    protected override void OnRealize(UiBuildContext ctx)
    {
        EditorSession session = Session.Get();
        if (ctx.Host is { } host) ctx.Own(session.Commands.BindShortcuts(host));
        ctx.AddAnimation(dt =>
        {
            session.PumpAutosave(TimeSpan.FromSeconds(MathF.Max(0, dt)));
            return false;
        }, () => session.Settings.Current.Peek().AutosaveEnabled);
        _systemTheme ??= ctx.Theme.Peek();
        bool initialized = false;
        ctx.Own(Reactive.Effect(() =>
        {
            EditorSettings settings = session.Settings.Current.Value;
            Theme resolved = settings.ResolveTheme(_systemTheme!);
            if (ctx.Theme.Peek() != resolved)
            {
                ctx.Theme.Value = resolved;
                MarkNeedsRealize();
            }
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
        DocumentTabs = new EditorDocumentTabs(session);
        DocumentsHost = EditorKit.DockHost(session.Layout, session.ResolveDockItem, closeRemoves: false,
            onCloseTab: (_, id) => session.CloseTab(id));
        EditorStatusBar status = new(session);
        EditorDialogs dialogs = new(session);

        return Grid(rows:
        [
            GridLength.Px(MenuBar.BarH),
            GridLength.Px(34),
            GridLength.Px(Luxel.Controls.DocumentTabs.StripH),
            GridLength.Star(),
            GridLength.Px(StatusBar.BarH),
        ])
        [
            menuBar.GridRow(0),
            toolbarChrome.GridRow(1),
            DocumentTabs.GridRow(2),
            DocumentsHost.GridRow(3),
            dialogs.GridRow(3),
            status.GridRow(4)
        ];
    }
}
