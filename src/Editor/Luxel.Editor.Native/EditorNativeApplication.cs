using Luxel.Controls;
using Luxel.Framework.UI;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Editor.Native;

internal sealed class NativeEditorHost : IEditorHost
{
    public NativeEditorHost(string projectRoot, string settingsPath)
    {
        Context = new NativeProjectContext(projectRoot);
        Dialogs = new NativeDialogService();
        ProjectService = new NativeProjectService(Context, Dialogs);
        Settings = new NativeEditorSettingsStore(settingsPath);
        Builds = new NativeBuildService(() => Context.Root, Console.WriteLine);
        SavePaths = new NativeSavePathPicker(Context, Dialogs);
        AssetHost = new NativeAssetHost(Context, Dialogs);
    }
    public NativeProjectContext Context { get; }
    public INativeDialogService Dialogs { get; }
    public NativeProjectService ProjectService { get; }
    public NativeBuildService Builds { get; }
    public NativeAssetHost AssetHost { get; }
    public IFileStorage Files => Context.Storage;
    public IProjectPicker Projects => ProjectService;
    public IEditorSettingsStore Settings { get; }
    IBuildService IEditorHost.Builds => Builds;
    public IEditorProjectStorageProvider ProjectStorage => ProjectService;
    public IEditorSavePathPicker SavePaths { get; }
    public IEditorProjectBackend ProjectBackend => ProjectService;
    IEditorAssetHost IEditorHost.AssetHost => AssetHost;
    public IHostCapabilities Capabilities => new EditorHostCapabilities(
        PersistentStorage: true, ProjectPicker: Dialogs.IsAvailable, NativeDialogs: Dialogs.IsAvailable,
        FileWatching: true, ProcessBuild: Builds.IsAvailable, RevealInFileManager: AssetHost.IsRevealAvailable,
        AssetImport: Dialogs.IsAvailable,
        AssetImportUnavailableReason: Dialogs.IsAvailable ? null : "Asset import requires a native file dialog executable.");
}

public static class EditorNativeApplication
{
    public static int Run(string[] args)
    {
        string projectRoot = ResolveProjectRoot(args);
        string settingsPath = ResolveSettingsPath();
        var host = new NativeEditorHost(projectRoot, settingsPath);
        var application = new EditorApplication(host, files => EditorProductSessionFactory.Create(
            files, host.Settings, host.SavePaths, host.AssetHost, host.Capabilities, host.Builds));
        if (!application.Restore()) application.OpenProject(projectRoot);

        if (!host.Dialogs.IsAvailable && application.Session is { } session)
            session.StatusText.Value = "Native file and folder dialogs are unavailable; configure zenity or use --project-root.";

        NativeWindowLayout windowLayout = NativeWindowLayout.Read(host.Settings);
        LuxelAppBuilder builder = LuxelApp.CreateBuilder(args);
        builder.Options.Title = "Luxel Editor";
        builder.Options.UiName = "editor";
        builder.Options.Width = Math.Max(640, windowLayout.Width);
        builder.Options.Height = Math.Max(480, windowLayout.Height);
        builder.Options.Theme = Theme.Dark;
        builder.ConfigureRuntime(runtime => runtime.Own(application));
        builder.OnStarted(runtime =>
        {
            runtime.MainWindow.Window.SetBounds(windowLayout.X, windowLayout.Y, windowLayout.Width, windowLayout.Height);
            runtime.MainWindow.Window.Resized += (_, _) => PersistWindow(runtime, host.Settings);
            runtime.MainWindow.Window.Moved += (_, _) => PersistWindow(runtime, host.Settings);
            runtime.MainWindow.Window.Closing = () =>
            {
                if (application.ExitRequested) return true;
                application.RequestExit();
                return application.ExitRequested;
            };
        });
        builder.OnFrame((runtime, _) =>
        {
            if (application.ExitRequested && !runtime.MainWindow.Window.IsClosed) runtime.MainWindow.Window.Close();
        });
        LuxelUiApplication app = builder.Build();
        app.MapScreen("/", () => new EditorApplicationShell(application));
        app.Run();
        return 0;
    }

    private static void PersistWindow(LuxelAppRuntime runtime, IEditorSettingsStore settings)
    {
        var window = runtime.MainWindow.Window;
        new NativeWindowLayout(window.X, window.Y, window.Width, window.Height).Write(settings);
    }

    private static string ResolveProjectRoot(string[] args)
    {
        const string prefix = "--project-root=";
        string? argument = args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (argument is not null) return Ensure(argument[prefix.Length..]);
        string? configured = Environment.GetEnvironmentVariable("LUXEL_EDITOR_PROJECT_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Ensure(configured);
        return Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luxel", "EditorProject"));
    }

    private static string ResolveSettingsPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luxel", "Editor", "settings.json");
    private static string Ensure(string path) { path = Path.GetFullPath(path); Directory.CreateDirectory(path); return path; }
}
