using Luxel.Workbench;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed class EditorDialogs(EditorSession session) : CompositeControl
{
    private readonly Signal<bool> _open = new(false);
    private object? _activeRequest;

    public EditorSession Session { get; } = session;

    public IEditorDocument? ExternalChangeDocument()
    {
        foreach (IEditorDocument document in Session.Workspace.Documents)
            if (Session.Documents.BindingOf(document)?.ExternalChange.Value == true) return document;
        return null;
    }

    public ExternalChangeResult ResolveExternalChange(ExternalChangeDecision decision)
        => ExternalChangeDocument() is { } document
            ? Session.ResolveExternalChange(document, decision)
            : new(false, "No external change is pending.");

    protected override Widget Build()
    {
        EditorPendingClose? pending = Session.CloseCoordinator.Pending.Value;
        IEditorDocument? external = pending is null ? ExternalChangeDocument() : null;
        object? request = (object?)pending ?? external;
        Widget? panel = pending is not null
            ? BuildCloseDialog(pending)
            : external is not null ? BuildExternalChangeDialog(external) : null;
        if (request is null)
        {
            _activeRequest = null;
            _open.Value = false;
        }
        else if (!ReferenceEquals(request, _activeRequest))
        {
            _activeRequest = request;
            _open.Value = true;
        }
        return Dialog(_open, panel ?? Spacer());
    }

    private Widget BuildCloseDialog(EditorPendingClose pending)
    {
        if (pending.Scope == EditorCloseScope.Application && pending.Documents.Count == 0)
        {
            return DialogPanel([
                Text("Exit application?"),
                Text("Are you sure you want to exit Luxel Editor?"),
                HStack(6)[
                    Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Discard), "Exit"),
                    Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Cancel), "Cancel")],
            ]);
        }
        string title = pending.Scope switch
        {
            EditorCloseScope.Document => "Close document?",
            EditorCloseScope.Project => "Close project?",
            _ => "Exit application?",
        };
        bool cleanExitConfirmation = pending.Scope == EditorCloseScope.Application && pending.Documents.Count == 0;
        string document = pending.Current?.Title ?? "changes";
        var rows = new List<Widget>
        {
            Text(title),
            Text(cleanExitConfirmation ? "Exit Luxel Editor?" : $"Save changes to {document}?")
        };
        if (pending.Error is { } error) rows.Add(Text(error));
        rows.Add(cleanExitConfirmation
            ? HStack(6)[
                Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Discard), "Exit"),
                Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Cancel), "Cancel")]
            : HStack(6)[
                Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.SaveAndContinue), "Save"),
                Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Discard), "Discard"),
                Button(_ => Session.CloseCoordinator.Decide(EditorCloseDecision.Cancel), "Cancel")]);
        return DialogPanel(rows);
    }

    private Widget BuildExternalChangeDialog(IEditorDocument document)
    {
        ExternalChangeActionState compare = Session.CompareExternalChange(document);
        var rows = new List<Widget>
        {
            Text("File changed outside the Editor"),
            Text($"Reload {document.Title} or keep local changes?"),
            HStack(6)[
                Button(_ => ResolveExternalChange(ExternalChangeDecision.Reload), "Reload"),
                Button(_ => ResolveExternalChange(ExternalChangeDecision.KeepLocal), "Keep Local")],
            compare.Enabled
                ? Button(_ => ResolveExternalChange(ExternalChangeDecision.Compare), "Compare")
                : VStack(2)[
                    Text("Compare (disabled)", color: Bind.From(() => UiTheme.T.TextMuted)),
                    Muted(compare.DisabledReason ?? "Compare is unavailable.")],
        };
        return DialogPanel(rows);
    }

    private static Widget DialogPanel(IEnumerable<Widget> rows)
        => Border(background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(16))[VStack(8)[rows.ToArray()]];
}

public sealed class EditorApplicationShell(EditorApplication application, EditorWelcomeActions? welcomeActions = null) : CompositeControl
{
    public EditorApplication Application { get; } = application;
    public EditorShell? Shell { get; private set; }
    public WelcomeView? Welcome { get; private set; }

    public Widget CurrentView()
    {
        if (Application.Session is { } session)
        {
            Welcome = null;
            return Shell = EditorKit.EditorShell(session);
        }
        Shell = null;
        return Welcome = new WelcomeView(Application, Application.Projects, welcomeActions);
    }

    protected override Widget Build()
    {
        _ = Application.Version.Value;
        return CurrentView();
    }
}
