using System.Runtime.InteropServices.JavaScript;
using Luxel.Controls;
using Luxel.Workbench;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Editor.Browser;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
internal sealed class BrowserEditorHost(
    BrowserProjectStorageProvider projects,
    BrowserProjectPicker picker,
    BrowserSettingsStore settings,
    UnsupportedBrowserBuildService builds) : IEditorHost
{
    private readonly MemoryFileStorage _fallback = new();
    public IFileStorage Files => _fallback;
    public IProjectPicker Projects => picker;
    public IEditorSettingsStore Settings => settings;
    public IBuildService Builds => builds;
    public IEditorProjectStorageProvider ProjectStorage => projects;
    public IEditorProjectBackend ProjectBackend => projects;
    public IEditorAssetHost AssetHost => NullEditorAssetHost.Instance;
    public IHostCapabilities Capabilities => new EditorHostCapabilities(
        PersistentStorage: projects.ActiveStorage is BrowserWorkspaceStorage { State.Persistent: true },
        ProjectPicker: true,
        NativeDialogs: false,
        FileWatching: false,
        ProcessBuild: false,
        RevealInFileManager: false,
        AssetImport: false,
        AssetImportUnavailableReason: "Browser asset import is not wired yet; add files through a project folder or import a project archive.");
}

public static class BrowserStartupDiagnostics
{
    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Exception reason = error.GetBaseException();
        return $"Luxel Editor failed to start.\nReason: {reason.GetType().Name}: {reason.Message}\n"
            + "Fallback: use a browser/device with WebGPU enabled, or open this project in the native Editor.";
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class EditorBrowserApplication
{
    private static EditorApplication? _application;
    private static BrowserProjectCoordinator? _coordinator;
    private static BrowserJsServices? _js;

    public static IServiceCollection AddLuxelEditorBrowser(this IServiceCollection services)
    {
        services.AddSingleton<BrowserJsServices>();
        services.AddSingleton<IBrowserWorkspacePersistence, IndexedDbWorkspacePersistence>();
        services.AddSingleton(provider => new BrowserWorkspaceStorage(
            provider.GetRequiredService<IBrowserWorkspacePersistence>(), "default"));
        services.AddSingleton<BrowserProjectStorageProvider>();
        services.AddSingleton<BrowserProjectPicker>(provider => new(provider.GetRequiredService<BrowserJsServices>().PickProject));
        services.AddSingleton<BrowserSettingsStore>(provider =>
        {
            BrowserJsServices js = provider.GetRequiredService<BrowserJsServices>();
            return new(js.ReadSetting, js.WriteSetting);
        });
        services.AddSingleton<UnsupportedBrowserBuildService>();
        services.AddSingleton<BrowserProjectCoordinator>();
        services.AddSingleton<IEditorHost, BrowserEditorHost>();
        services.AddSingleton(provider =>
        {
            IEditorHost host = provider.GetRequiredService<IEditorHost>();
            BrowserProjectCoordinator coordinator = provider.GetRequiredService<BrowserProjectCoordinator>();
            var application = new EditorApplication(host, files =>
            {
                IHostCapabilities capabilities = new EditorHostCapabilities(
                    PersistentStorage: files is BrowserWorkspaceStorage { State.Persistent: true },
                    ProjectPicker: true, NativeDialogs: false, FileWatching: false,
                    ProcessBuild: false, RevealInFileManager: false, AssetImport: false,
                    AssetImportUnavailableReason: "Browser asset import is not wired yet; add files through a project folder or import a project archive.");
                EditorSession session = EditorProductSessionFactory.Create(
                    files, host.Settings, host.SavePaths, host.AssetHost, capabilities, host.Builds);
                coordinator.ConfigureSession(session);
                return session;
            });
            coordinator.Attach(application);
            return application;
        });
        return services;
    }

    public static async Task RunAsync(IServiceProvider services)
    {
        _js = services.GetRequiredService<BrowserJsServices>();
        try
        {
            BrowserWorkspaceStorage indexedDb = services.GetRequiredService<BrowserWorkspaceStorage>();
            await indexedDb.InitializeAsync();
            BrowserProjectStorageProvider projects = services.GetRequiredService<BrowserProjectStorageProvider>();
            projects.Register(BrowserProjectPicker.BuiltInDemo, static () => new MemoryFileStorage());
            projects.Register(BrowserProjectPicker.IndexedDbWorkspace, () => indexedDb);

            _coordinator = services.GetRequiredService<BrowserProjectCoordinator>();
            _coordinator.AttachIndexedDb(indexedDb);
            projects.Activated += _coordinator.ProjectActivated;
            _application = services.GetRequiredService<EditorApplication>();
            if (!_application.Restore()) _application.OpenProject(BrowserProjectPicker.BuiltInDemo);

            bool fileSystemAccess = _js.FileSystemAccessAvailable;
            var welcome = new EditorWelcomeActions(
                OpenSample: _coordinator.OpenDemo,
                ProjectActions:
                [
                    new("Open IndexedDB Workspace", _coordinator.OpenIndexedDb),
                    new("Import Project Archive…", _coordinator.ImportArchive),
                    new("Open Browser Folder…", _coordinator.OpenFolder, fileSystemAccess,
                        fileSystemAccess ? null : "File System Access API is unavailable; use Import Project Archive."),
                ]);
            await BrowserEditorRuntime.RunAsync(new EditorApplicationShell(_application, welcome), _coordinator, _js);
        }
        catch (Exception error)
        {
            _js.SetFailure(BrowserStartupDiagnostics.Describe(error));
            Console.Error.WriteLine(error);
        }
    }

    [JSExport]
    public static bool RunCommand(string id) => _application?.Session?.Commands.Run(id) == true;

    [JSExport]
    public static string ActiveDocumentId()
    {
        EditorSession? session = _application?.Session;
        return session?.ActiveDocument is { } active ? session.IdOf(active) ?? "" : "";
    }

    [JSExport]
    public static string ProjectId() => _application?.ProjectId ?? "";

    [JSExport]
    public static string StatusText() => _application?.Session?.StatusText.Peek() ?? _application?.WelcomeError.Peek() ?? "";

    [JSExport]
    public static bool RequestExit()
    {
        _application?.RequestExit();
        return _application?.ExitRequested == true;
    }
}
