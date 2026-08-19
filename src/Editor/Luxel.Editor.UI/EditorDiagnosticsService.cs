using Luxel.Workbench;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public interface IEditorLocationReveal
{
    void Reveal(int line, int column);
}

public enum EditorDiagnosticSeverity { Info, Warning, Error }

public sealed record EditorDiagnosticItem(
    string Id,
    EditorDiagnosticSeverity Severity,
    string Source,
    string Message,
    string? Path = null,
    int Line = 0,
    int Column = 0,
    string? DocumentId = null);

public sealed record EditorDiagnosticFilter(
    EditorDiagnosticSeverity? MinimumSeverity = null,
    string? Source = null,
    string? Text = null);

public enum EditorDiagnosticGroup { None, Severity, Source, Path }

public sealed record EditorDiagnosticBucket(string Key, IReadOnlyList<EditorDiagnosticItem> Items);

public sealed class EditorDiagnosticsService
{
    private readonly List<EditorDiagnosticItem> _items = [];
    public Signal<int> Version { get; } = new(0);
    public IReadOnlyList<EditorDiagnosticItem> Items { get { _ = Version.Value; return _items; } }
    public string? NavigationError { get; private set; }

    public void Add(EditorDiagnosticItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        int index = _items.FindIndex(x => x.Id == item.Id);
        if (index >= 0) _items[index] = item; else _items.Add(item);
        Version.Value++;
    }

    public void ReplaceSource(string source, IEnumerable<EditorDiagnosticItem> items)
    {
        _items.RemoveAll(x => string.Equals(x.Source, source, StringComparison.Ordinal));
        foreach (EditorDiagnosticItem item in items)
            _items.Add(item.Source == source ? item : item with { Source = source });
        Version.Value++;
    }

    public void Clear(string? source = null)
    {
        if (source is null) _items.Clear();
        else _items.RemoveAll(x => string.Equals(x.Source, source, StringComparison.Ordinal));
        Version.Value++;
    }

    public IReadOnlyList<EditorDiagnosticItem> Query(EditorDiagnosticFilter? filter = null)
    {
        filter ??= new EditorDiagnosticFilter();
        IEnumerable<EditorDiagnosticItem> query = _items;
        if (filter.MinimumSeverity is { } severity) query = query.Where(x => x.Severity >= severity);
        if (!string.IsNullOrWhiteSpace(filter.Source))
            query = query.Where(x => string.Equals(x.Source, filter.Source.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string text = filter.Text.Trim();
            query = query.Where(x => x.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Source.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (x.Path?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return query.OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Line)
            .ThenBy(x => x.Column)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<EditorDiagnosticBucket> Grouped(
        EditorDiagnosticGroup grouping, EditorDiagnosticFilter? filter = null)
    {
        string Key(EditorDiagnosticItem item) => grouping switch
        {
            EditorDiagnosticGroup.Severity => item.Severity.ToString(),
            EditorDiagnosticGroup.Source => item.Source,
            EditorDiagnosticGroup.Path => item.Path ?? "(no path)",
            _ => "All",
        };
        return Query(filter).GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => grouping == EditorDiagnosticGroup.Severity
                ? group.Max(item => (int)item.Severity) * -1
                : 0)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EditorDiagnosticBucket(group.Key, group.ToArray()))
            .ToArray();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<EditorDiagnosticItem>> Group(
        EditorDiagnosticGroup grouping, EditorDiagnosticFilter? filter = null)
        => Grouped(grouping, filter).ToDictionary(x => x.Key, x => x.Items, StringComparer.OrdinalIgnoreCase);

    public bool Navigate(EditorDiagnosticItem item, EditorSession session, Action<IEditorDocument, int, int>? reveal = null)
    {
        try
        {
            IEditorDocument? document = item.DocumentId is { } id && session.OpenDocuments.TryGetValue(id, out var known)
                ? known
                : item.Path is { } path ? session.Documents.DocAt(path) : null;
            if (document is null && item.Path is { } openPath)
            {
                string kind = session.ResolveDocumentKind(openPath);
                document = session.Documents.Open(kind, openPath);
                session.AttachDocument(document, openPath);
            }
            if (document is null) throw new InvalidOperationException("The diagnostic has no navigable document.");
            session.ActivateDocument(document);
            reveal?.Invoke(document, item.Line, item.Column);
            NavigationError = null;
            return true;
        }
        catch (Exception ex)
        {
            NavigationError = ex.Message;
            return false;
        }
    }
}

public sealed class ProblemsView : CompositeControl
{
    private readonly Signal<int> _severity = new(0);
    private readonly Signal<int> _grouping = new(0);
    private readonly Signal<string> _source = new("");
    private readonly Signal<string> _text = new("");

    public ProblemsView(EditorDiagnosticsService diagnostics, EditorSession? session = null)
    {
        Diagnostics = diagnostics;
        Session = session;
    }

    public EditorDiagnosticsService Diagnostics { get; }
    public EditorSession? Session { get; }
    public EditorDiagnosticFilter Filter { get; private set; } = new();
    public EditorDiagnosticGroup Grouping { get; private set; }
    public Signal<string?> ActionError { get; } = new(null);

    public void SetGrouping(EditorDiagnosticGroup grouping) => _grouping.Value = (int)grouping;
    public void SetFilter(EditorDiagnosticFilter filter)
    {
        _severity.Value = filter.MinimumSeverity is null ? 0 : (int)filter.MinimumSeverity.Value + 1;
        _source.Value = filter.Source ?? "";
        _text.Value = filter.Text ?? "";
    }

    public bool Navigate(EditorDiagnosticItem item)
    {
        if (Session is null) { ActionError.Value = "Problem navigation is not connected to an Editor session."; return false; }
        bool result = Diagnostics.Navigate(item, Session,
            (document, line, column) => (document as IEditorLocationReveal)?.Reveal(line, column));
        ActionError.Value = result ? null : Diagnostics.NavigationError;
        return result;
    }

    protected override Widget Build()
    {
        _ = Diagnostics.Version.Value;
        EditorDiagnosticSeverity? severity = _severity.Value switch
        {
            1 => EditorDiagnosticSeverity.Info,
            2 => EditorDiagnosticSeverity.Warning,
            3 => EditorDiagnosticSeverity.Error,
            _ => null,
        };
        Filter = new(severity, string.IsNullOrWhiteSpace(_source.Value) ? null : _source.Value,
            string.IsNullOrWhiteSpace(_text.Value) ? null : _text.Value);
        Grouping = (EditorDiagnosticGroup)_grouping.Value;
        IReadOnlyList<EditorDiagnosticBucket> groups = Diagnostics.Grouped(Grouping, Filter);
        int count = groups.Sum(group => group.Items.Count);
        var rows = new List<Widget>
        {
            Text($"Problems ({count})"),
            HStack(6)[
                Select(["All", "Info", "Warning", "Error"], _severity, width: 110),
                Select(["Ungrouped", "Severity", "Source", "Path"], _grouping, width: 120),
                TextField(_source, placeholder: "Source", width: 120),
                TextField(_text, placeholder: "Filter", width: 160),
                Button(_ => Diagnostics.Clear(), "Clear")],
        };
        if (count == 0) rows.Add(Muted("No problems"));
        else foreach (EditorDiagnosticBucket group in groups)
        {
            if (Grouping != EditorDiagnosticGroup.None) rows.Add(Muted($"{group.Key} ({group.Items.Count})"));
            foreach (EditorDiagnosticItem item in group.Items)
            {
                string location = item.Path is null ? "" : $" — {item.Path}:{Math.Max(1, item.Line)}:{Math.Max(1, item.Column)}";
                rows.Add(Button(_ => Navigate(item), $"{item.Severity}: {item.Message}{location}"));
            }
        }
        if (ActionError.Value is { } error) rows.Add(Text(error));
        return VStack(4)[rows.ToArray()];
    }
}
