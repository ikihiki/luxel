using System.Numerics;
using System.Text.Json;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.SceneEdit;
using Luxel.Workbench;

namespace Luxel.Editor.Browser;

public static class BrowserAutomationContract
{
    public const string StateObject = "luxelEditorState";
    public const string InvokeFunction = "luxelEditorAutomation.invoke";
    public static IReadOnlyList<string> Actions { get; } =
    [
        "open-demo", "select-entity", "edit-transform", "undo", "redo", "open-path",
        "edit-active", "edit-material", "save-active", "change-layout", "reset-demo"
    ];
}

public static class BrowserDemoCommandIds
{
    public const string SelectEntity = "browser.demo.selectEntity";
    public const string OpenPath = "browser.demo.openPath";
    public const string EditActiveText = "browser.demo.editActiveText";
    public const string EditTransform = "browser.demo.editTransform";
    public const string EditMaterial = "browser.demo.editMaterial";
    public const string ChangeLayout = "browser.demo.changeLayout";
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserSelectEntityArguments
{
    public required int EntityId { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserOpenPathArguments
{
    public required string? Path { get; init; }
}

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserEditActiveTextArguments
{
    public required string? Text { get; init; }
}

public sealed class BrowserDemoSeed
{
    private readonly IReadOnlyDictionary<string, string> _files;

    public BrowserDemoSeed(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!files.ContainsKey(EditorProductSessionFactory.ProjectFile))
            throw new ArgumentException($"Demo seed must include {EditorProductSessionFactory.ProjectFile}.", nameof(files));
        _files = new Dictionary<string, string>(files, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> Files => _files;

    public bool EnsureSeeded(IFileStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (storage.Exists(EditorProductSessionFactory.ProjectFile)) return false;
        Write(storage);
        return true;
    }

    public void Reset(IFileStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        foreach (string path in storage.List().ToArray()) storage.Delete(path);
        Write(storage);
    }

    private void Write(IFileStorage storage)
    {
        foreach ((string path, string content) in _files.OrderBy(x => x.Key, StringComparer.Ordinal))
            storage.Write(path, content);
    }
}

public interface IBrowserDemoProjectProvider
{
    string ProjectId => BrowserProjectPicker.BuiltInDemo;
    string GalleryUrl => "../../gallery/";
    string StorageDescription => "Temporary built-in demo";
    Task InitializeAsync();
    IFileStorage Storage { get; }
    Task ResetAsync();
    void ConfigureSession(EditorSession session) { }
}

internal sealed class DefaultBrowserDemoProjectProvider : IBrowserDemoProjectProvider
{
    private readonly MemoryFileStorage _storage = new();
    public IFileStorage Storage => _storage;
    public Task InitializeAsync() { EditorProductSessionFactory.SeedIfEmpty(_storage); return Task.CompletedTask; }
    public Task ResetAsync()
    {
        foreach (string path in _storage.List().ToArray()) _storage.Delete(path);
        EditorProductSessionFactory.SeedIfEmpty(_storage);
        return Task.CompletedTask;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed class BrowserDemoAutomation(
    BrowserProjectStorageProvider projects,
    IBrowserDemoProjectProvider demo)
{
    private static readonly JsonSerializerOptions ArgumentJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    private static readonly JsonElement SelectEntitySchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "entityId" },
        properties = new { entityId = new { type = "integer", minimum = 0 } }
    }, ArgumentJson);
    private static readonly JsonElement OpenPathSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "path" },
        properties = new { path = new { type = "string", minLength = 1 } }
    }, ArgumentJson);
    private static readonly JsonElement EditActiveTextSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "text" },
        properties = new { text = new { type = "string", maxLength = 65_536 } }
    }, ArgumentJson);

    private EditorApplication? _application;
    private EditorSession? _commandsSession;
    private int _resetRevision;

    public void Attach(EditorApplication application) => _application = application;

    public void EnsureCommandsRegistered()
    {
        EditorSession? session = _application?.Session;
        if (session is null || ReferenceEquals(session, _commandsSession)) return;
        session.Commands.Register(BrowserDemoCommandIds.SelectEntity, "Demo: Select Entity",
            context =>
            {
                BrowserSelectEntityArguments value = ParseArguments<BrowserSelectEntityArguments>(context.Arguments);
                Invoke("select-entity", value.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            },
            new CommandArgumentSchema(
                Help: "Args: { entityId: non-negative integer }",
                Required: true,
                DefaultValue: JsonSerializer.SerializeToElement(new { entityId = 2 }, ArgumentJson),
                Validator: ValidateSelectEntityArguments,
                Schema: SelectEntitySchema),
            enabled: () => session.SceneDocument is not null);
        session.Commands.Register(BrowserDemoCommandIds.OpenPath, "Demo: Open Project Path",
            context => Invoke("open-path", NormalizeProjectPath(
                ParseArguments<BrowserOpenPathArguments>(context.Arguments).Path!)),
            new CommandArgumentSchema(
                Help: "Args: { path: non-empty normalized project-relative string }",
                Required: true,
                DefaultValue: JsonSerializer.SerializeToElement(new { path = EditorProductSessionFactory.ScriptFile }, ArgumentJson),
                Validator: ValidateOpenPathArguments,
                Schema: OpenPathSchema));
        session.Commands.Register(BrowserDemoCommandIds.EditActiveText, "Demo: Append Active Text",
            context => Invoke("edit-active", ParseArguments<BrowserEditActiveTextArguments>(context.Arguments).Text),
            new CommandArgumentSchema(
                Help: "Args: { text: string, maximum 65536 characters }",
                Required: true,
                DefaultValue: JsonSerializer.SerializeToElement(new { text = "\n// edited from command palette\n" }, ArgumentJson),
                Validator: ValidateEditActiveTextArguments,
                Schema: EditActiveTextSchema),
            enabled: () => session.ActiveDocument is TextDocument);
        session.Commands.Register(BrowserDemoCommandIds.EditTransform, "Demo: Nudge Selected Entity",
            () => Invoke("edit-transform", null), () => session.SceneDocument is not null);
        session.Commands.Register(BrowserDemoCommandIds.EditMaterial, "Demo: Nudge Material Node",
            () => Invoke("edit-material", null), () => session.ActiveDocument is NodeGraphDocument);
        session.Commands.Register(BrowserDemoCommandIds.ChangeLayout, "Demo: Arrange Workspace",
            () => Invoke("change-layout", null), () => session.Layout.Peek().GroupOf("scene") is not null);
        _commandsSession = session;
    }

    private static T ParseArguments<T>(JsonElement? args) where T : class
    {
        if (args is not { } value)
            throw new InvalidOperationException("Validated command arguments are unavailable.");
        return value.Deserialize<T>(ArgumentJson)
            ?? throw new InvalidOperationException("Validated command arguments are unavailable.");
    }

    private static string? ValidateSelectEntityArguments(JsonElement? args)
    {
        if (args is not { ValueKind: JsonValueKind.Object }) return "Command args must be a JSON object.";
        if (!args.Value.TryGetProperty("entityId", out JsonElement entityId)) return "entityId is required.";
        if (entityId.ValueKind != JsonValueKind.Number || !entityId.TryGetInt32(out _))
            return "entityId must be an integer.";
        return ValidateArguments<BrowserSelectEntityArguments>(args, value => value.EntityId < 0
            ? "entityId must be a non-negative integer." : null);
    }

    private static string? ValidateOpenPathArguments(JsonElement? args)
        => ValidateArguments<BrowserOpenPathArguments>(args, value => ValidateProjectPath(value.Path));

    private static string? ValidateEditActiveTextArguments(JsonElement? args)
        => ValidateArguments<BrowserEditActiveTextArguments>(args, value => value.Text switch
        {
            null => "text must be a string.",
            { Length: > 65_536 } => "text must not exceed 65536 characters.",
            _ => null
        });

    private static string? ValidateArguments<T>(JsonElement? args, Func<T, string?> validate) where T : class
    {
        if (args is not { ValueKind: JsonValueKind.Object }) return "Command args must be a JSON object.";
        try
        {
            T? value = args.Value.Deserialize<T>(ArgumentJson);
            return value is null ? "Command args must be a JSON object." : validate(value);
        }
        catch (JsonException error)
        {
            return error.Message;
        }
    }

    private static string? ValidateProjectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "path must be a non-empty project-relative string.";
        string normalized = path.Replace('\\', '/').Trim();
        bool rooted = Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':');
        if (rooted || normalized.Split('/').Any(segment => segment is ".." or "." or ""))
            return "path must be a normalized project-relative path without '.' or '..' segments.";
        return normalized.Contains('\0') ? "path contains an invalid null character." : null;
    }

    private static string NormalizeProjectPath(string path) => path.Replace('\\', '/').Trim();

    public async Task ResetAsync()
    {
        EditorApplication application = _application ?? throw new InvalidOperationException("Editor application is not ready.");
        application.CloseProjectDiscardingChanges();
        await demo.ResetAsync();
        if (!application.OpenProject(demo.ProjectId))
            throw new InvalidOperationException(application.WelcomeError.Peek() ?? "The demo project could not be reopened after reset.");
        EnsureCommandsRegistered();
        Interlocked.Increment(ref _resetRevision);
    }

    public async Task<string> InvokeAsync(string action, string? value)
    {
        if (action == "reset-demo")
        {
            await ResetAsync();
            return Snapshot();
        }

        string snapshot = Invoke(action, value);
        if (action == "save-active" && projects.ActiveStorage is BrowserWorkspaceStorage activeStorage)
        {
            await activeStorage.FlushAsync();
            snapshot = Snapshot();
        }
        return snapshot;
    }

    public string Invoke(string action, string? value)
    {
        EditorSession session = _application?.Session ?? throw new InvalidOperationException("Editor session is not ready.");
        switch (action)
        {
            case "open-demo":
                _application?.OpenProject(demo.ProjectId);
                EnsureCommandsRegistered();
                break;
            case "select-entity":
            {
                int id = int.Parse(value ?? "2", System.Globalization.CultureInfo.InvariantCulture);
                session.SelectHierarchyEntity(id);
                break;
            }
            case "edit-transform":
            {
                SceneDocument scene = session.SceneDocument ?? throw new InvalidOperationException("Scene document is unavailable.");
                session.ActivateDocument(scene);
                session.Workspace.Activate(scene);
                int id = session.SelectionService.Current.Peek().MainEntityId;
                if (id < 0) id = 2;
                Vector2 old = scene.Doc.Entity(id).Component("transform2d")?.Get("pos")?.AsVec2() ?? Vector2.Zero;
                scene.View.ApplyEdit(new SetField(id, "transform2d", "pos", SceneValue.Of(old + new Vector2(16, 8))));
                break;
            }
            case "undo": session.Commands.Run(EditorCommandIds.Undo); break;
            case "redo": session.Commands.Run(EditorCommandIds.Redo); break;
            case "open-path":
            {
                string path = value ?? EditorProductSessionFactory.ScriptFile;
                if (!session.OpenAsset(path) && session.Documents.DocAt(path) is null)
                    throw new InvalidOperationException($"The demo asset could not be opened: {path}");
                if (session.Documents.DocAt(path) is { } opened) session.Workspace.Activate(opened);
                break;
            }
            case "edit-active":
                if (session.ActiveDocument is TextDocument text)
                {
                    text.Text.Value += value ?? "\n// edited by acceptance test\n";
                    text.Dirty.Value = true;
                }
                else throw new InvalidOperationException("The active document is not text-editable.");
                break;
            case "edit-material":
                if (session.ActiveDocument is NodeGraphDocument graph)
                {
                    int nodeId = graph.Doc.Nodes.First().Id;
                    graph.ApplyEdit(new MoveNode(nodeId, new Vector2(24, 12)));
                }
                else throw new InvalidOperationException("The active document is not a node graph.");
                break;
            case "save-active": session.Save(); break;
            case "change-layout":
            {
                DockTree layout = session.Layout.Peek();
                DockGroup sceneGroup = layout.GroupOf("scene") ?? throw new InvalidOperationException("Scene dock group is unavailable.");
                layout = layout.Dock("script", sceneGroup.Id, DockSide.Bottom);
                DockGroup scriptGroup = layout.GroupOf("script") ?? throw new InvalidOperationException("Script dock group is unavailable.");
                layout = layout.MoveTab("readme", scriptGroup.Id, 0);
                DockSplit split = layout.Root as DockSplit
                    ?? throw new InvalidOperationException("Root dock split is unavailable.");
                float[] sizes = split.Sizes.Select((_, index) => index == 0 ? 3f : 1f).ToArray();
                layout = layout.WithSizes(split.Id, sizes);
                session.Layout.Value = layout;
                session.SettingsStore.Write(EditorLayoutService.SettingsKey, layout.Serialize());
                break;
            }
            case "reset-demo": throw new InvalidOperationException("Reset Demo must be invoked through InvokeAsync.");
            default: throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown Editor browser automation action.");
        }
        return Snapshot();
    }

    public string Snapshot()
    {
        EditorSession? session = _application?.Session;
        var documents = session?.OpenDocuments.Select(pair => new
        {
            id = pair.Key,
            title = pair.Value.Title,
            kind = pair.Value.Kind,
            dirty = pair.Value.Dirty.Peek(),
            active = ReferenceEquals(pair.Value, session.ActiveDocument),
            path = session.Documents.BindingOf(pair.Value)?.Path
        }).OrderBy(x => x.id, StringComparer.Ordinal).ToArray() ?? [];
        SceneDocument? scene = session?.SceneDocument;
        int selected = session?.SelectionService.Current.Peek().MainEntityId ?? -1;
        Vector2? position = selected >= 0
            ? scene?.Doc.Entity(selected).Component("transform2d")?.Get("pos")?.AsVec2()
            : null;
        NodeGraphDocument? material = session?.OpenDocuments.Values.OfType<NodeGraphDocument>().FirstOrDefault();
        GraphNode? firstMaterialNode = material?.Doc.Nodes.FirstOrDefault();
        DockTree? dock = session?.Layout.Peek();
        return JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            projectId = _application?.ProjectId ?? "",
            status = session?.StatusText.Peek() ?? _application?.WelcomeError.Peek() ?? "",
            storage = demo.StorageDescription,
            storagePersistent = projects.ActiveStorage is BrowserWorkspaceStorage { State.Persistent: true },
            documents,
            activeText = session?.ActiveDocument is TextDocument activeText ? activeText.Text.Peek() : null,
            selection = new { entityId = selected, sceneSelected = scene?.View.IsSelected(selected) == true },
            inspector = new { entityId = selected, position = position is { } p ? new[] { p.X, p.Y } : null },
            material = material is null ? null : new
            {
                nodeCount = material.Doc.Nodes.Count,
                edgeCount = material.Doc.Edges.Count,
                firstNodePosition = firstMaterialNode is null ? null : new[] { firstMaterialNode.Pos.X, firstMaterialNode.Pos.Y }
            },
            layout = dock?.Serialize() ?? "",
            dock = dock is null ? null : new
            {
                groupCount = dock.Groups.Count(),
                splitCount = CountSplits(dock.Root),
                groups = dock.Groups.Select(group => new { id = group.Id, tabs = group.Tabs }).ToArray(),
                rootSizes = dock.Root is DockSplit root ? root.Sizes : []
            },
            files = projects.ActiveStorage?.List().Order(StringComparer.Ordinal).ToArray() ?? [],
            warningCount = session?.DiagnosticsService.Items.Count(x => x.Severity == EditorDiagnosticSeverity.Warning) ?? 0,
            resetRevision = Volatile.Read(ref _resetRevision)
        });
    }

    private static int CountSplits(DockNode node)
        => node is DockSplit split ? 1 + split.Children.Sum(CountSplits) : 0;
}
