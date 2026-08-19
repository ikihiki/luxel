using System.Text.Json;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed record EditorProjectTemplate(string Id, string Name, string Description);
public sealed record NewProjectRequest(string Name, string Location, string TemplateId);
public sealed record ProjectValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ProjectValidationResult Validate(NewProjectRequest request, IEnumerable<EditorProjectTemplate> templates)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Project name is required.");
        else if (request.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) errors.Add("Project name contains invalid characters.");
        if (string.IsNullOrWhiteSpace(request.Location)) errors.Add("Project location is required.");
        if (!templates.Any(x => x.Id == request.TemplateId)) errors.Add("Select a valid project template.");
        return new(errors.Count == 0, errors);
    }
}

public interface IEditorProjectBackend
{
    IReadOnlyList<EditorProjectTemplate> Templates { get; }
    string Create(NewProjectRequest request);
    string Open(string projectId);
}

public sealed class PassthroughEditorProjectBackend : IEditorProjectBackend
{
    public static PassthroughEditorProjectBackend Instance { get; } = new();
    public IReadOnlyList<EditorProjectTemplate> Templates { get; } = [new("empty", "Empty", "Empty project")];
    public string Create(NewProjectRequest request) => Path.Combine(request.Location, request.Name).Replace('\\', '/');
    public string Open(string projectId) => projectId;
}

public sealed class EditorProjectService
{
    public const string RecentProjectsKey = "editor.recentProjects.v1";
    private readonly IEditorSettingsStore _settings;
    private readonly IEditorProjectBackend _backend;
    private readonly List<string> _recent = [];

    public EditorProjectService(IEditorSettingsStore settings, IEditorProjectBackend? backend = null)
    {
        _settings = settings;
        _backend = backend ?? PassthroughEditorProjectBackend.Instance;
        LoadRecent();
    }

    public IReadOnlyList<EditorProjectTemplate> Templates => _backend.Templates;
    public Signal<int> Version { get; } = new(0);
    public Signal<string?> Error { get; } = new(null);
    public IReadOnlyList<string> RecentProjects { get { _ = Version.Value; return _recent; } }

    public ProjectValidationResult Validate(NewProjectRequest request) => ProjectValidationResult.Validate(request, Templates);

    public bool TryCreate(NewProjectRequest request, out string? projectId)
    {
        ProjectValidationResult validation = Validate(request);
        if (!validation.IsValid) { Error.Value = string.Join(" ", validation.Errors); projectId = null; return false; }
        return Try(() => _backend.Create(request), out projectId);
    }

    public bool TryOpen(string candidate, out string? projectId) => Try(() => _backend.Open(candidate), out projectId);

    public void Remember(string projectId)
    {
        _recent.RemoveAll(x => string.Equals(x, projectId, StringComparison.Ordinal));
        _recent.Insert(0, projectId);
        if (_recent.Count > 12) _recent.RemoveRange(12, _recent.Count - 12);
        Persist();
        Version.Value++;
    }

    public void RemoveRecent(string projectId)
    {
        if (!_recent.Remove(projectId)) return;
        Persist();
        Version.Value++;
    }

    private bool Try(Func<string> action, out string? projectId)
    {
        try
        {
            projectId = action();
            Remember(projectId);
            Error.Value = null;
            return true;
        }
        catch (Exception ex)
        {
            projectId = null;
            Error.Value = ex.Message;
            return false;
        }
    }

    private void LoadRecent()
    {
        try
        {
            string? json = _settings.Read(RecentProjectsKey);
            if (string.IsNullOrWhiteSpace(json)) return;
            foreach (string project in JsonSerializer.Deserialize<string[]>(json) ?? [])
                if (!_recent.Contains(project, StringComparer.Ordinal)) _recent.Add(project);
        }
        catch (Exception ex) { Error.Value = ex.Message; }
    }

    private void Persist() => _settings.Write(RecentProjectsKey, JsonSerializer.Serialize(_recent));
}

public sealed record EditorWelcomeActions(Action? OpenSample = null, Action? OpenGallery = null);

public sealed class WelcomeView : CompositeControl
{
    private readonly Signal<string> _projectName = new("");
    private readonly Signal<string> _projectLocation = new("");
    private readonly Signal<int> _templateIndex = new(0);

    public WelcomeView(EditorApplication application, EditorProjectService projects, EditorWelcomeActions? actions = null)
    {
        Application = application;
        Projects = projects;
        Actions = actions ?? new();
    }

    public EditorApplication Application { get; }
    public EditorProjectService Projects { get; }
    public EditorWelcomeActions Actions { get; }
    public Signal<string?> ActionError { get; } = new(null);
    public Signal<IReadOnlyList<string>> ValidationErrors { get; } = new([]);
    public Signal<string> ProjectName => _projectName;
    public Signal<string> ProjectLocation => _projectLocation;

    public bool CreateProject()
    {
        EditorProjectTemplate? template = Projects.Templates.ElementAtOrDefault(_templateIndex.Peek());
        var request = new NewProjectRequest(_projectName.Peek(), _projectLocation.Peek(), template?.Id ?? "");
        ProjectValidationResult validation = Projects.Validate(request);
        ValidationErrors.Value = validation.Errors;
        if (!validation.IsValid) return false;
        bool opened = Application.CreateProject(request);
        ActionError.Value = opened ? null : Application.WelcomeError.Peek() ?? Projects.Error.Peek();
        return opened;
    }

    public bool OpenProject()
    {
        bool opened = Application.OpenPickedProject();
        ActionError.Value = opened ? null : Application.WelcomeError.Peek() ?? "No project was selected.";
        return opened;
    }

    public bool OpenRecent(string projectId)
    {
        bool opened = Application.OpenProject(projectId);
        ActionError.Value = opened ? null : Application.WelcomeError.Peek();
        return opened;
    }

    public void RemoveRecent(string projectId) => Projects.RemoveRecent(projectId);
    public bool OpenSample() => InvokeAction(Actions.OpenSample, "Sample project navigation is not available on this host.");
    public bool OpenGallery() => InvokeAction(Actions.OpenGallery, "Gallery navigation is not available on this host.");

    protected override Widget Build()
    {
        _ = Projects.Version.Value;
        _ = Application.Version.Value;
        IReadOnlyList<EditorProjectTemplate> templates = Projects.Templates;
        var rows = new List<Widget>
        {
            Text("Welcome to Luxel Editor"),
            Text("New Project"),
            TextField(_projectName, placeholder: "Project name", width: 320),
            TextField(_projectLocation, placeholder: "Project location", width: 320),
        };
        if (templates.Count > 0)
            rows.Add(Select(templates.Select(x => x.Name).ToArray(), _templateIndex, width: 220));
        rows.Add(Button(_ => CreateProject(), "Create Project"));
        rows.Add(HStack(8)[
            Button(_ => OpenProject(), "Open Project"),
            Button(_ => OpenSample(), "Sample Project"),
            Button(_ => OpenGallery(), "Gallery")]);

        IReadOnlyList<string> validation = ValidationErrors.Value;
        if (validation.Count > 0) rows.AddRange(validation.Select(x => (Widget)Text(x)));
        string? error = ActionError.Value ?? Application.WelcomeError.Value ?? Projects.Error.Value;
        if (error is not null) rows.Add(Text(error));

        if (Projects.RecentProjects.Count > 0)
        {
            rows.Add(Text("Recent Projects"));
            foreach (string project in Projects.RecentProjects)
                rows.Add(HStack(6)[
                    Button(_ => OpenRecent(project), project),
                    Button(_ => RemoveRecent(project), "Remove")]);
        }
        return Border(background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(24))[VStack(8)[rows.ToArray()]];
    }

    private bool InvokeAction(Action? action, string unavailable)
    {
        if (action is null) { ActionError.Value = unavailable; return false; }
        try { action(); ActionError.Value = null; return true; }
        catch (Exception ex) { ActionError.Value = ex.Message; return false; }
    }
}
