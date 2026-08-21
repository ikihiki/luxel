using Luxel.UI;
using Luxel.Workbench;
using Luxel.Typography;

namespace Luxel.Controls;

public interface IHostCapabilities
{
    bool PersistentStorage { get; }
    bool ProjectPicker { get; }
    bool NativeDialogs { get; }
    bool FileWatching { get; }
    bool ProcessBuild { get; }
    bool RevealInFileManager { get; }
    bool AssetImport { get; }
    string? AssetImportUnavailableReason { get; }
}

public readonly record struct EditorHostCapabilities(
    bool PersistentStorage,
    bool ProjectPicker = false,
    bool NativeDialogs = false,
    bool FileWatching = false,
    bool ProcessBuild = false,
    bool RevealInFileManager = false,
    bool AssetImport = false,
    string? AssetImportUnavailableReason = null) : IHostCapabilities;

/// <summary>Optional storage status exposed by hosts whose synchronous writes become durable asynchronously.</summary>
public interface IEditorStorageStatus
{
    bool IsDurable { get; }
    bool RequiresUnloadWarning { get; }
    string StatusText { get; }
}

public interface IProjectPicker { string? PickProject(); }
public interface IEditorSettingsStore { string? Read(string key); void Write(string key, string value); }
public interface IBuildService { bool IsAvailable { get; } void Build(); }

public sealed class MemoryEditorSettingsStore : IEditorSettingsStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public string? Read(string key) => _values.GetValueOrDefault(key);
    public void Write(string key, string value) => _values[key] = value;
}

public interface IEditorProjectStorageProvider
{
    /// <summary>Creates isolated storage for a resolved project without mutating the active project.</summary>
    IFileStorage CreateStorage(string projectId);
    /// <summary>Called only after the replacement session has been created and activated successfully.</summary>
    void ProjectActivated(string projectId, IFileStorage storage) { }
}

public interface IEditorHost
{
    IFileStorage Files { get; }
    IProjectPicker Projects { get; }
    IEditorSettingsStore Settings { get; }
    IBuildService Builds { get; }
    IHostCapabilities Capabilities { get; }
    IEditorProjectStorageProvider? ProjectStorage => null;
    IEditorSavePathPicker SavePaths => NullEditorSavePathPicker.Instance;
    IEditorProjectBackend ProjectBackend => PassthroughEditorProjectBackend.Instance;
    IEditorAssetHost AssetHost => NullEditorAssetHost.Instance;
}

/// <summary>Non-serializing tool pane exposed through the shared document/docking model.</summary>
public sealed class EditorToolDocument(string kind, string title, Func<Widget> createView) : IEditorDocument
{
    public string Kind { get; } = kind;
    public string Title { get; } = title;
    public Signal<bool> Dirty { get; } = new(false);
    public bool CanUndo => false;
    public bool CanRedo => false;
    public Widget CreateView() => createView();
    public void Undo() { }
    public void Redo() { }
    public string Serialize() => "";
    public void LoadFrom(string content) { }
}

public sealed record EditorDiagnostic(string Severity, string Source, string Message, string? Path = null, int Line = 0, int Column = 0);

public sealed class EditorSession : IDisposable
{
    private readonly Dictionary<string, IEditorDocument> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DockItem> _standardPanes = new(StringComparer.Ordinal);
    private readonly IEditorSavePathPicker _savePaths;
    private readonly IDisposable _layoutSync;
    private readonly IDisposable _legacySelectionSync;
    private readonly IDisposable _legacyDiagnosticsSync;
    private readonly IDisposable _legacyOutputSync;
    private readonly IDisposable _editorAppearanceSync;
    private readonly EditorAutosaveScheduler _autosaveScheduler;
    private readonly IEditorIntervalPump? _autosavePump;
    private readonly IDisposable? _ownedAutosavePump;
    private readonly List<WeakReference<TextEditorView>> _textViews = [];
    private VectorFont? _editorFont;
    private string? _editorFontName;
    private readonly Dictionary<SceneDocument, SceneHierarchyController> _hierarchyControllers = new();
    private int _diagnosticSequence;

    public EditorSession(IFileStorage files, IEnumerable<KeyValuePair<string, IEditorDocument>> documents, DockTree layout,
        IEditorSettingsStore? settings = null, IEditorSavePathPicker? savePaths = null,
        IEditorAssetHost? assetHost = null, IHostCapabilities? capabilities = null,
        IEditorIntervalScheduler? autosaveScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(documents);
        Files = files;
        AssetHost = assetHost ?? NullEditorAssetHost.Instance;
        Capabilities = capabilities ?? new EditorHostCapabilities(PersistentStorage: false);
        _savePaths = savePaths ?? NullEditorSavePathPicker.Instance;
        SettingsStore = settings ?? new MemoryEditorSettingsStore();
        Layout = new Signal<DockTree>(layout ?? throw new ArgumentNullException(nameof(layout)));
        Documents = new DocumentStore(Workspace, files);

        foreach ((string id, IEditorDocument document) in documents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(document);
            if (!_documents.TryAdd(id, document))
                throw new ArgumentException($"Editor document id '{id}' is registered more than once.", nameof(documents));
            Workspace.Open(document);
        }

        DiagnosticsService = new EditorDiagnosticsService();
        OutputService = new EditorOutputService();
        SelectionService = new EditorSelectionService();
        Settings = new EditorSettingsService(SettingsStore);
#pragma warning disable CS0618
        _legacySelectionSync = Reactive.Effect(() => Selection.Value = SelectionService.Current.Value);
        _legacyDiagnosticsSync = Reactive.Effect(() =>
        {
            _ = DiagnosticsService.Version.Value;
            Diagnostics.Value = DiagnosticsService.Items.Select(ToLegacyDiagnostic).ToArray();
        });
        _legacyOutputSync = Reactive.Effect(() =>
        {
            _ = OutputService.Version.Value;
            Output.Value = string.Join(Environment.NewLine, OutputService.Entries.Select(x => x.Message));
        });
#pragma warning restore CS0618
        _editorAppearanceSync = Reactive.Effect(RefreshEditorAppearance);
        IEditorIntervalScheduler intervalScheduler;
        if (autosaveScheduler is null)
        {
            var pump = new SerializedEditorIntervalPump();
            intervalScheduler = pump;
            _autosavePump = pump;
            _ownedAutosavePump = pump;
        }
        else
        {
            intervalScheduler = autosaveScheduler;
            _autosavePump = autosaveScheduler as IEditorIntervalPump;
        }
        _autosaveScheduler = new EditorAutosaveScheduler(Settings, intervalScheduler, () => Autosave());
        Assets = new AssetOperations(new FileAssetStorage(files),
            Capabilities.RevealInFileManager ? new(EditorCapabilityAvailability.Enabled) : null,
            Capabilities.AssetImport ? new(EditorCapabilityAvailability.Enabled) :
                new(EditorCapabilityAvailability.Disabled,
                    Capabilities.AssetImportUnavailableReason ?? "Import requires a host file picker."));
        Assets.Mutated += ApplyAssetMutation;
        RegisterStandardPanes();
        RegisterCoreCommands();
        Keymap = new EditorKeymap(Commands, SettingsStore);
        CloseCoordinator = new EditorCloseCoordinator(this, savePaths);
        DockTree defaultLayout = layout;
        string[] panes = _documents.Keys.Concat(layout.Groups.SelectMany(x => x.Tabs)).Concat([
            EditorPaneIds.Hierarchy, EditorPaneIds.Scene, EditorPaneIds.Inspector, EditorPaneIds.Assets,
            EditorPaneIds.Documents, EditorPaneIds.Problems, EditorPaneIds.Output, EditorPaneIds.Settings,
            EditorPaneIds.KeyBindings, EditorPaneIds.Play]).Distinct().ToArray();
        LayoutService = new EditorLayoutService(SettingsStore, () => defaultLayout, panes);
        EditorLayoutRestoreResult restored = LayoutService.Restore();
        Layout.Value = restored.Layout;
        if (restored.Reason is { } layoutReason)
        {
            OutputService.Write("Layout", layoutReason, EditorOutputLevel.Warning);
            StatusText.Value = layoutReason;
        }
        LayoutService.Attach(Layout);
        _layoutSync = Reactive.Effect(SynchronizeActiveDocument);
    }

    public EditorSession(IEnumerable<KeyValuePair<string, IEditorDocument>> documents, DockTree layout)
        : this(new MemoryFileStorage(), documents, layout) { }

    public IEditorAssetHost AssetHost { get; }
    public IHostCapabilities Capabilities { get; }
    public IFileStorage Files { get; }
    public IEditorSettingsStore SettingsStore { get; }
    public Workspace Workspace { get; } = new();
    public DocumentStore Documents { get; }
    public CommandRegistry Commands { get; } = new();
    public Signal<DockTree> Layout { get; }
    public EditorSelectionService SelectionService { get; }
    public EditorDiagnosticsService DiagnosticsService { get; }
    public EditorOutputService OutputService { get; }
    /// <summary>Phase 2 compatibility signal. New code should use <see cref="SelectionService"/>.</summary>
    [Obsolete("Use SelectionService.Current.")]
    public Signal<object?> Selection { get; } = new(null);
    /// <summary>Phase 2 compatibility signal mirrored from <see cref="DiagnosticsService"/>.</summary>
    [Obsolete("Use DiagnosticsService.")]
    public Signal<IReadOnlyList<EditorDiagnostic>> Diagnostics { get; } = new([]);
    /// <summary>Phase 2 compatibility output text mirrored from <see cref="OutputService"/>.</summary>
    [Obsolete("Use OutputService.")]
    public Signal<string> Output { get; } = new("");
    public EditorSettingsService Settings { get; }
    public EditorKeymap Keymap { get; }
    public IAssetOperations Assets { get; }
    public EditorLayoutService LayoutService { get; }
    public EditorCloseCoordinator CloseCoordinator { get; }
    public Signal<bool> IsPlaying { get; } = new(false);
    public Signal<string> StatusText { get; } = new("Ready");
    public IReadOnlyDictionary<string, IEditorDocument> OpenDocuments => _documents;
    public IEditorDocument? ActiveDocument => Workspace.Active.Peek();
    public Action? CloseProjectRequested { get; set; }
    public Action? ExitRequested { get; set; }

    public DockItem ResolveDockItem(string id)
    {
        if (_documents.TryGetValue(id, out IEditorDocument? document))
            return new DockItem(document.Title, () => CreateDocumentView(document), document.Dirty);
        if (_standardPanes.TryGetValue(id, out DockItem? pane)) return pane;
        throw new KeyNotFoundException($"Unknown Editor document or pane id '{id}'.");
    }

    public bool IsStandardPane(string id) => _standardPanes.ContainsKey(id);

    public bool CloseTab(string id)
        => _documents.ContainsKey(id) ? CloseDocument(id) : LayoutService.SetPaneVisible(Layout, id, false);

    public string? IdOf(IEditorDocument document) => _documents.FirstOrDefault(x => ReferenceEquals(x.Value, document)).Key;

    public string ResolveDocumentKind(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".scene" or ".luxelscene") return EditorDocumentProviderIds.Scene;
        if (extension is ".graph" or ".nodegraph") return EditorDocumentProviderIds.NodeGraph;
        return Workspace.ProviderFor(EditorDocumentProviderIds.Text) is not null ? EditorDocumentProviderIds.Text :
            Workspace.Providers.FirstOrDefault()?.Kind ?? throw new InvalidOperationException("No document provider is registered.");
    }

    public string AttachDocument(IEditorDocument document, string? preferredId = null)
    {
        string baseId = string.IsNullOrWhiteSpace(preferredId) ? document.Title : preferredId;
        string id = baseId;
        for (int suffix = 2; _documents.ContainsKey(id); suffix++) id = $"{baseId}#{suffix}";
        _documents[id] = document;
        LayoutService.RegisterItemId(id);
        Workspace.Open(document);
        DockGroup target = Layout.Peek().Groups.First();
        Layout.Value = Layout.Peek().AddTab(target.Id, id);
        return id;
    }

    public bool ActivateDocument(string id)
    {
        if (!_documents.ContainsKey(id)) return false;
        DockTree next = Layout.Peek().ActivateTab(id);
        if (!next.Groups.SelectMany(group => group.Tabs).Contains(id, StringComparer.Ordinal)) return false;
        Layout.Value = next;
        return ReferenceEquals(ActiveDocument, _documents[id]);
    }

    public bool ActivateDocument(IEditorDocument document)
        => IdOf(document) is { } id && ActivateDocument(id);

    public bool CloseDocument(string id) => CloseCoordinator.BeginDocument(id);

    internal bool CloseDocumentCore(string id)
    {
        if (!_documents.TryGetValue(id, out IEditorDocument? document)) return false;
        Layout.Value = Layout.Peek().RemoveTab(id);
        _documents.Remove(id);
        LayoutService.UnregisterItemId(id);
        if (document is SceneDocument scene && _hierarchyControllers.Remove(scene, out SceneHierarchyController? hierarchy))
            hierarchy.Dispose();
        return Workspace.Close(document);
    }

    public bool Save(IEditorDocument? document = null)
    {
        document ??= ActiveDocument;
        if (document is null) return false;
        try
        {
            Documents.Save(document);
            if (Files is IEditorStorageStatus { IsDurable: false } storageStatus)
            {
                OutputService.Write("Save", $"Saved {document.Title} in memory; {storageStatus.StatusText}", EditorOutputLevel.Warning);
                StatusText.Value = storageStatus.StatusText;
            }
            else
            {
                OutputService.Write("Save", $"Saved {document.Title}.");
                StatusText.Value = $"Saved {document.Title}";
            }
            return true;
        }
        catch (Exception ex) { ReportFailure("save", ex, document); return false; }
    }

    public bool SaveAsActive()
    {
        IEditorDocument? document = ActiveDocument;
        if (document is null) return false;
        string? path = _savePaths.PickSavePath(document);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Value = "Save As cancelled";
            return false;
        }
        return SaveAs(document, path);
    }

    public bool SaveAs(IEditorDocument document, string path)
    {
        try
        {
            Documents.SaveAs(document, path);
            if (Files is IEditorStorageStatus { IsDurable: false } storageStatus)
            {
                OutputService.Write("Save", $"Saved {document.Title} as {path} in memory; {storageStatus.StatusText}", EditorOutputLevel.Warning);
                StatusText.Value = storageStatus.StatusText;
            }
            else
            {
                OutputService.Write("Save", $"Saved {document.Title} as {path}.");
                StatusText.Value = $"Saved {document.Title} as {path}";
            }
            return true;
        }
        catch (Exception ex) { ReportFailure("save", ex, document); return false; }
    }

    public bool SaveAll()
    {
        foreach (IEditorDocument document in Workspace.Documents.Where(x => x.Dirty.Peek()))
            if (Documents.BindingOf(document) is null || !Save(document)) return false;
        return true;
    }

    /// <summary>
    /// Advances the default autosave scheduler on the caller's thread. Production hosts should call this from
    /// their UI tick; deterministic tests can advance it directly. Injected non-pump schedulers drive themselves.
    /// </summary>
    public void PumpAutosave(TimeSpan elapsed) => _autosavePump?.Pump(elapsed);

    public int Autosave()
    {
        if (!Settings.Current.Peek().AutosaveEnabled) return 0;
        int saved = 0;
        foreach (IEditorDocument document in Workspace.Documents.Where(x => x.Dirty.Peek() && Documents.BindingOf(x) is not null))
        {
            try { Documents.Save(document); saved++; }
            catch (Exception ex) { ReportFailure("autosave", ex, document); }
        }
        return saved;
    }

    public ExternalChangeActionState CompareExternalChange(IEditorDocument document)
        => new(false, "A compare editor is not available in the initial Editor MVP.");

    public ExternalChangeResult ResolveExternalChange(IEditorDocument document, ExternalChangeDecision decision)
    {
        try
        {
            DocumentBinding binding = Documents.BindingOf(document) ?? throw new InvalidOperationException("The document is not bound to a file.");
            switch (decision)
            {
                case ExternalChangeDecision.Reload: Documents.Reload(document); break;
                case ExternalChangeDecision.KeepLocal: binding.ExternalChange.Value = false; break;
                case ExternalChangeDecision.Compare: return new(false, CompareExternalChange(document).DisabledReason);
            }
            return new(true);
        }
        catch (Exception ex) { ReportFailure("external-change", ex, document); return new(false, ex.Message); }
    }

    public void ReportFailure(string source, Exception exception, IEditorDocument? document = null)
    {
        string? id = document is null ? null : IdOf(document);
        string? path = document is null ? null : Documents.BindingOf(document)?.Path;
        DiagnosticsService.Add(new EditorDiagnosticItem($"{source}:{++_diagnosticSequence}", EditorDiagnosticSeverity.Error,
            source, exception.Message, path, DocumentId: id));
        OutputService.Write(source, exception.Message, EditorOutputLevel.Error);
        StatusText.Value = exception.Message;
    }

    public SceneDocument? SceneDocument
        => ActiveDocument as SceneDocument ?? _documents.Values.OfType<SceneDocument>().FirstOrDefault();

    public bool OpenAsset(string path)
    {
        try
        {
            IEditorDocument document = Documents.Open(ResolveDocumentKind(path), path);
            string? id = IdOf(document);
            if (id is null) id = AttachDocument(document, path);
            return ActivateDocument(id);
        }
        catch (Exception ex)
        {
            ReportFailure("asset-open", ex);
            return false;
        }
    }

    private void ApplyAssetMutation(AssetMutationResult result)
    {
        foreach (AssetMutationFailure failure in result.Failures)
            ReportFailure("asset-mutation", new IOException($"{failure.Path}: {failure.Message}"));

        foreach (AssetPathMutation change in result.Changes)
        {
            if (change.OldPath is null || Documents.DocAt(change.OldPath) is not { } document) continue;
            try
            {
                if (change.NewPath is { } newPath)
                    Documents.Rebind(document, newPath);
                else
                {
                    Documents.Unbind(document);
                    document.Dirty.Value = true;
                }
            }
            catch (Exception ex)
            {
                // Storage already changed. Never leave a document bound to a path that no longer represents it.
                Documents.Unbind(document);
                document.Dirty.Value = true;
                ReportFailure("asset-mutation", ex, document);
            }
        }
    }

    private Widget CreateDocumentView(IEditorDocument document)
    {
        Widget view = document.CreateView();
        if (view is TextEditorView editor)
        {
            editor.EditorFont = _editorFont;
            _textViews.Add(new WeakReference<TextEditorView>(editor));
        }
        return view;
    }

    private void RefreshEditorAppearance()
    {
        string fontName = Settings.Current.Value.EditorFont.Trim();
        if (!string.Equals(fontName, _editorFontName, StringComparison.Ordinal))
        {
            VectorFont? replacement = null;
            try { replacement = LoadEditorFont(fontName); }
            catch (Exception ex) { Settings.Error.Value = $"Editor font '{fontName}' could not be loaded: {ex.Message}"; }
            VectorFont? old = _editorFont;
            _editorFont = replacement;
            _editorFontName = fontName;
            old?.Dispose();
        }

        for (int i = _textViews.Count - 1; i >= 0; i--)
        {
            if (!_textViews[i].TryGetTarget(out TextEditorView? editor)) { _textViews.RemoveAt(i); continue; }
            editor.EditorFont = _editorFont;
            editor.MarkNeedsRealize();
        }
    }

    private static VectorFont LoadEditorFont(string name)
    {
        if (File.Exists(name)) return VectorFont.Load(name);
        string file = name.Contains("udev", StringComparison.OrdinalIgnoreCase) ? "UDEVGothic-Regular.ttf"
            : name.Contains("biz", StringComparison.OrdinalIgnoreCase) ? "BIZUDGothic-Regular.ttf"
            : name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
                ? name : name.Replace(" ", "", StringComparison.Ordinal) + ".ttf";
        foreach (string directory in new[]
        {
            Environment.GetEnvironmentVariable("LUXEL_FONT_DIR") ?? "",
            Path.Combine(AppContext.BaseDirectory, "fonts"),
            Path.Combine(Directory.GetCurrentDirectory(), "fonts"),
        })
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string candidate = Path.Combine(directory, file);
            if (File.Exists(candidate)) return VectorFont.Load(candidate);
        }
        return VectorFont.LoadSystem(file);
    }

    private static EditorDiagnostic ToLegacyDiagnostic(EditorDiagnosticItem item)
        => new(item.Severity.ToString(), item.Source, item.Message, item.Path, item.Line, item.Column);

    private void RegisterStandardPanes()
    {
        _standardPanes[EditorPaneIds.Documents] = new DockItem("Documents", () => new EditorDocumentTabs(this));
        _standardPanes[EditorPaneIds.Hierarchy] = new DockItem("Hierarchy", CreateHierarchyView);
        _standardPanes[EditorPaneIds.Inspector] = new DockItem("Inspector", CreateInspectorView);
        _standardPanes[EditorPaneIds.Assets] = new DockItem("Assets", () => EditorKit.AssetBrowser(
            storage: Files, operations: Assets, expanded: new HashSet<string>(StringComparer.Ordinal),
            onOpen: (_, path) => OpenAsset(path),
            onImportRequest: browser =>
            {
                IReadOnlyList<(string Name, string Content)> files = AssetHost.PickImportFiles();
                if (files.Count > 0) browser.Import("", files);
            },
            onRevealRequest: (_, path) => AssetHost.Reveal(path)));
        _standardPanes[EditorPaneIds.Problems] = new DockItem("Problems", () => new ProblemsView(DiagnosticsService, this));
        _standardPanes[EditorPaneIds.Output] = new DockItem("Output", () => new OutputView(OutputService));
        _standardPanes[EditorPaneIds.Scene] = new DockItem("Scene", () => SceneDocument?.CreateView() ?? MissingPane("No scene document is open."));
        _standardPanes[EditorPaneIds.Settings] = new DockItem("Settings", () => new SettingsView(Settings));
        _standardPanes[EditorPaneIds.KeyBindings] = new DockItem("Key Bindings", () => new KeyBindingsView(Keymap));
        _standardPanes[EditorPaneIds.Play] = new DockItem("Play", () => MissingPane("Play view is provided by the host."));
    }

    private Widget CreateHierarchyView()
    {
        SceneDocument? scene = SceneDocument;
        if (scene is null) return MissingPane("Open a scene document to use Hierarchy.");
        if (!_hierarchyControllers.TryGetValue(scene, out SceneHierarchyController? controller))
        {
            controller = new SceneHierarchyController(IdOf(scene) ?? EditorPaneIds.Scene, scene, SelectionService);
            _hierarchyControllers.Add(scene, controller);
        }
        return new SceneHierarchyView(controller);
    }

    private Widget CreateInspectorView()
    {
        SceneDocument? scene = SceneDocument;
        return scene is null ? MissingPane("Open a scene document to use Inspector.")
            : EditorKit.SceneInspector(scene.View);
    }

    private static Widget MissingPane(string message) => Kit.Border(
        background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(12))[Kit.Muted(message)];

    private void RegisterCoreCommands()
    {
        Commands.Register(EditorCommandIds.Save, "Save", () => Save(), () => ActiveDocument?.Dirty.Peek() == true,
            "Ctrl+S", "File/Save", toolbar: true);
        Commands.Register(EditorCommandIds.SaveAs, "Save As", () => SaveAsActive(), () => ActiveDocument is not null,
            "Ctrl+Shift+S", "File/Save As");
        Commands.Register(EditorCommandIds.SaveAll, "Save All", () => SaveAll(), () => Workspace.AnyDirty.Value,
            key: null, menuPath: "File/Save All");
        Commands.Register(EditorCommandIds.Close, "Close", () => { if (IdOf(ActiveDocument!) is { } id) CloseDocument(id); },
            () => ActiveDocument is not null, "Ctrl+W", "File/Close");
        Commands.Register(EditorCommandIds.CloseProject, "Close Project", () =>
            CloseCoordinator.BeginProject(() => CloseProjectRequested?.Invoke()), () => CloseProjectRequested is not null,
            menuPath: "File/Close Project");
        Commands.Register(EditorCommandIds.Exit, "Exit", RequestApplicationExit,
            () => ExitRequested is not null, menuPath: "File/Exit");
        Commands.Register(EditorCommandIds.Undo, "Undo", Workspace.Undo, () => Workspace.CanUndo, "Ctrl+Z", "Edit/Undo");
        Commands.Register(EditorCommandIds.Redo, "Redo", Workspace.Redo, () => Workspace.CanRedo, "Ctrl+Y", "Edit/Redo");
        Commands.Register(EditorCommandIds.ResetLayout, "Reset Layout", () => LayoutService.Reset(Layout), menuPath: "Window/Reset Layout");
        Commands.Register(EditorCommandIds.FocusMode, "Focus Active Pane", ToggleFocusMode,
            () => LayoutService.IsFocusMode || ActivePaneId() is not null, key: "Ctrl+Shift+F", menuPath: "Window/Focus Mode");
        foreach ((string paneId, DockItem pane) in _standardPanes)
        {
            string id = $"window.pane.{paneId}";
            Commands.Register(id, pane.Title, () =>
            {
                bool visible = Layout.Peek().GroupOf(paneId) is not null;
                LayoutService.SetPaneVisible(Layout, paneId, !visible);
            }, menuPath: $"Window/Panes/{pane.Title}");
        }
    }

    private string? ActivePaneId()
    {
        DockGroup? group = Layout.Peek().Groups.FirstOrDefault(x => x is { Active: >= 0 } && x.Active < x.Tabs.Count);
        return group is null ? null : group.Tabs[group.Active];
    }

    private void ToggleFocusMode()
    {
        if (LayoutService.IsFocusMode) LayoutService.ExitFocusMode(Layout);
        else if (ActivePaneId() is { } paneId) LayoutService.EnterFocusMode(Layout, paneId);
    }

    private void RequestApplicationExit()
    {
        bool confirmCleanExit = Settings.Current.Peek().ConfirmExit;
        if (Workspace.AnyDirty.Value || confirmCleanExit)
            CloseCoordinator.BeginApplication(() => ExitRequested?.Invoke(), confirmCleanExit);
        else ExitRequested?.Invoke();
    }

    private void SynchronizeActiveDocument()
    {
        DockGroup? group = Layout.Value.Groups.FirstOrDefault(g => g is { Active: >= 0 } && g.Active < g.Tabs.Count);
        if (group is null) return;
        if (_documents.TryGetValue(group.Tabs[group.Active], out IEditorDocument? document)) Workspace.Activate(document);
    }

    public void Dispose()
    {
        Assets.Mutated -= ApplyAssetMutation;
        foreach (SceneHierarchyController controller in _hierarchyControllers.Values) controller.Dispose();
        _hierarchyControllers.Clear();
        _autosaveScheduler.Dispose();
        _ownedAutosavePump?.Dispose();
        _editorAppearanceSync.Dispose();
        _legacySelectionSync.Dispose();
        _legacyDiagnosticsSync.Dispose();
        _legacyOutputSync.Dispose();
        _layoutSync.Dispose();
        LayoutService.Dispose();
        Documents.Dispose();
        _editorFont?.Dispose();
        _editorFont = null;
        foreach (IEditorDocument document in _documents.Values) (document as IDisposable)?.Dispose();
        _documents.Clear();
    }
}

public sealed class EditorApplication : IDisposable
{
    private const string LastProjectKey = "editor.lastProject";
    private readonly Func<IFileStorage, EditorSession> _createSession;

    public EditorApplication(IEditorHost host, Func<IFileStorage, EditorSession> createSession)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
        Projects = new EditorProjectService(host.Settings, host.ProjectBackend);
    }

    public IEditorHost Host { get; }
    public EditorProjectService Projects { get; }
    public EditorSession? Session { get; private set; }
    public string? ProjectId { get; private set; }
    public bool ExitRequested { get; private set; }
    public Signal<int> Version { get; } = new(0);
    public Signal<string?> WelcomeError { get; } = new(null);

    public bool Restore()
    {
        string? project = Host.Settings.Read(LastProjectKey);
        return project is not null && OpenProject(project);
    }

    public bool OpenProject(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        if (Session?.Workspace.AnyDirty.Value == true)
        {
            WelcomeError.Value = "Close or save dirty documents before opening another project.";
            return false;
        }
        if (!Projects.TryResolveOpen(projectId, out string? resolved) || resolved is null)
        {
            WelcomeError.Value = Projects.Error.Peek();
            return false;
        }
        return ActivateResolvedProject(resolved);
    }

    public bool CreateProject(NewProjectRequest request)
    {
        if (Session?.Workspace.AnyDirty.Value == true)
        {
            WelcomeError.Value = "Close or save dirty documents before creating another project.";
            return false;
        }
        return Projects.TryResolveCreate(request, out string? project) && project is not null && ActivateResolvedProject(project);
    }

    private bool ActivateResolvedProject(string resolved)
    {
        EditorSession? next = null;
        IFileStorage? storage = null;
        try
        {
            storage = Host.ProjectStorage?.CreateStorage(resolved) ?? Host.Files;
            next = _createSession(storage);
            next.CloseProjectRequested = CloseProjectCore;
            next.ExitRequested = ExitCore;
        }
        catch (Exception ex)
        {
            next?.Dispose();
            WelcomeError.Value = ex.Message;
            return false;
        }

        EditorSession? previous = Session;
        string? previousProjectId = ProjectId;
        Session = next;
        ProjectId = resolved;
        try
        {
            Host.ProjectStorage?.ProjectActivated(resolved, storage);
        }
        catch (Exception ex)
        {
            Session = previous;
            ProjectId = previousProjectId;
            next.Dispose();
            WelcomeError.Value = ex.Message;
            return false;
        }

        previous?.Dispose();
        Projects.Remember(resolved);
        Host.Settings.Write(LastProjectKey, resolved);
        WelcomeError.Value = null;
        Version.Value++;
        return true;
    }

    public bool OpenPickedProject() => Host.Projects.PickProject() is { } project && OpenProject(project);

    public void CloseProject()
    {
        if (Session is { } session) session.CloseCoordinator.BeginProject(CloseProjectCore);
        else CloseProjectCore();
    }

    public void RequestExit()
    {
        if (Session is { } session)
        {
            bool confirmCleanExit = session.Settings.Current.Peek().ConfirmExit;
            if (session.Workspace.AnyDirty.Value || confirmCleanExit)
            {
                session.CloseCoordinator.BeginApplication(ExitCore, confirmCleanExit);
                return;
            }
        }
        ExitCore();
    }

    private void CloseProjectCore()
    {
        Session?.Dispose();
        Session = null;
        ProjectId = null;
        Version.Value++;
    }

    private void ExitCore()
    {
        ExitRequested = true;
        CloseProjectCore();
    }

    public void Dispose() => CloseProjectCore();
}
