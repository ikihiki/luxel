using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

public interface IEditorSavePathPicker
{
    string? PickSavePath(IEditorDocument document);
}

public sealed class NullEditorSavePathPicker : IEditorSavePathPicker
{
    public static NullEditorSavePathPicker Instance { get; } = new();
    public string? PickSavePath(IEditorDocument document) => null;
}

public enum EditorCloseScope { Document, Project, Application }
public enum EditorCloseDecision { Cancel, Discard, SaveAndContinue }

public sealed record EditorPendingClose(EditorCloseScope Scope, IReadOnlyList<IEditorDocument> Documents,
    int Index, string? DocumentId, string? Error)
{
    public IEditorDocument? Current => Index >= 0 && Index < Documents.Count ? Documents[Index] : null;
}

public sealed class EditorCloseCoordinator
{
    private readonly EditorSession _session;
    private readonly IEditorSavePathPicker _savePaths;
    private Action? _complete;

    public EditorCloseCoordinator(EditorSession session, IEditorSavePathPicker? savePaths = null)
    {
        _session = session;
        _savePaths = savePaths ?? NullEditorSavePathPicker.Instance;
    }

    public Signal<EditorPendingClose?> Pending { get; } = new(null);

    public bool BeginDocument(string documentId)
    {
        if (!_session.OpenDocuments.TryGetValue(documentId, out IEditorDocument? document)) return false;
        return Begin(EditorCloseScope.Document, [document], () => _session.CloseDocumentCore(documentId), documentId);
    }

    public bool BeginProject(Action closeProject)
        => Begin(EditorCloseScope.Project, _session.Workspace.Documents.Where(x => x.Dirty.Peek()).ToArray(), closeProject, null);

    public bool BeginApplication(Action exit, bool confirmCleanExit = false)
    {
        IEditorDocument[] dirty = _session.Workspace.Documents.Where(x => x.Dirty.Peek()).ToArray();
        if (dirty.Length == 0 && confirmCleanExit) return BeginCleanApplication(exit);
        return Begin(EditorCloseScope.Application, dirty, exit, null);
    }

    public void Decide(EditorCloseDecision decision)
    {
        EditorPendingClose? pending = Pending.Peek();
        if (pending is null) return;
        if (decision == EditorCloseDecision.Cancel) { Pending.Value = null; _complete = null; return; }

        IEditorDocument? current = pending.Current;
        if (current is not null && decision == EditorCloseDecision.SaveAndContinue)
        {
            try
            {
                if (_session.Documents.BindingOf(current) is null)
                {
                    string? path = _savePaths.PickSavePath(current);
                    if (string.IsNullOrWhiteSpace(path)) { Pending.Value = pending with { Error = "Save As was cancelled." }; return; }
                    _session.Documents.SaveAs(current, path);
                }
                else _session.Documents.Save(current);
                _session.OutputService.Write("Save", $"Saved {current.Title}.");
            }
            catch (Exception ex)
            {
                _session.ReportFailure("save", ex, current);
                Pending.Value = pending with { Error = ex.Message };
                return;
            }
        }

        int next = pending.Index + 1;
        if (next < pending.Documents.Count) { Pending.Value = pending with { Index = next, Error = null }; return; }
        Action? complete = _complete;
        Pending.Value = null;
        _complete = null;
        complete?.Invoke();
    }

    private bool BeginCleanApplication(Action exit)
    {
        if (Pending.Peek() is not null) return false;
        _complete = exit;
        Pending.Value = new(EditorCloseScope.Application, [], 0, null, null);
        return true;
    }

    private bool Begin(EditorCloseScope scope, IReadOnlyList<IEditorDocument> documents, Action complete, string? documentId)
    {
        if (Pending.Peek() is not null) return false;
        IEditorDocument[] dirty = documents.Where(x => x.Dirty.Peek()).ToArray();
        if (dirty.Length == 0) { complete(); return true; }
        _complete = complete;
        Pending.Value = new(scope, dirty, 0, documentId, null);
        return true;
    }
}

public enum ExternalChangeDecision { Reload, KeepLocal, Compare }
public sealed record ExternalChangeActionState(bool Enabled, string? DisabledReason = null);
public sealed record ExternalChangeResult(bool Applied, string? Error = null);
