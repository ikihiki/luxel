using System.Diagnostics;
using Luxel.Controls;
using Luxel.Editor.Native;
using Luxel.Workbench;

namespace Luxel.Editor.Native.Tests;

public sealed class NativeHostServiceTests
{
    [Fact]
    public void SettingsAndWindowLayoutPersistAcrossInstances()
    {
        using var temp = new TempDirectory(); string settingsPath = Path.Combine(temp.Path, "settings.json");
        var first = new NativeEditorSettingsStore(settingsPath);
        new NativeWindowLayout(10, 20, 900, 700).Write(first);
        var second = new NativeEditorSettingsStore(settingsPath);
        Assert.Equal(new NativeWindowLayout(10, 20, 900, 700), NativeWindowLayout.Read(second));
    }

    [Fact]
    public void ProjectSwitchCreatesPerProjectStorageAndActivatesOnlyAfterSessionSuccess()
    {
        using var first = new TempDirectory(); using var second = new TempDirectory();
        File.WriteAllText(Path.Combine(first.Path, "id"), "first");
        File.WriteAllText(Path.Combine(second.Path, "id"), "second");
        var context = new NativeProjectContext(first.Path);
        var projects = new NativeProjectService(context, new Dialogs(second.Path));
        var host = new Host(context, projects);
        using var app = new EditorApplication(host, files =>
        {
            string id = files.Read("id") ?? throw new InvalidDataException("missing id");
            if (id == "bad") throw new InvalidOperationException("bad project");
            return new EditorSession(files, new Dictionary<string, IEditorDocument> { ["doc"] = new Document(id) }, DockTree.Single("doc"));
        });
        Assert.True(app.OpenProject(first.Path));
        EditorSession original = app.Session!;
        Assert.True(app.OpenProject(second.Path));
        Assert.NotSame(original, app.Session);
        Assert.Equal(second.Path, context.Root);
        Assert.Equal("second", context.Storage.Read("id"));

        using var bad = new TempDirectory(); File.WriteAllText(Path.Combine(bad.Path, "id"), "bad");
        EditorSession active = app.Session!;
        Assert.False(app.OpenProject(bad.Path));
        Assert.Same(active, app.Session);
        Assert.Equal(second.Path, context.Root);
        Assert.Equal("second", context.Storage.Read("id"));
    }

    [Fact]
    public void SavePickerAndRevealPathsRejectProjectEscape()
    {
        using var project = new TempDirectory(); using var outside = new TempDirectory();
        var context = new NativeProjectContext(project.Path);
        var picker = new NativeSavePathPicker(context, new Dialogs(Path.Combine(outside.Path, "file.txt")));
        Assert.Null(picker.PickSavePath(new Document("file.txt")));
        var insidePicker = new NativeSavePathPicker(context, new Dialogs(Path.Combine(project.Path, "folder", "file.txt")));
        Assert.Equal("folder/file.txt", insidePicker.PickSavePath(new Document("file.txt")));
        Assert.Throws<ArgumentException>(() => context.ResolveWithinRoot("../outside.txt"));
        Assert.Throws<ArgumentException>(() => context.ResolveWithinRoot(Path.Combine(outside.Path, "outside.txt")));
    }

    [Fact]
    public void MissingBuildAndRevealExecutablesAreReportedUnavailable()
    {
        using var project = new TempDirectory();
        string missing = Path.Combine(project.Path, "missing-executable");
        var build = new NativeBuildService(() => project.Path, dotnetExecutable: missing);
        var reveal = new NativeAssetHost(new NativeProjectContext(project.Path), new Dialogs(null), missing);
        Assert.False(build.IsAvailable);
        Assert.False(reveal.IsRevealAvailable);
        Assert.Throws<NotSupportedException>(build.Build);
        Assert.Throws<NotSupportedException>(() => reveal.Reveal("file.txt"));

        if (!OperatingSystem.IsWindows())
        {
            string nonExecutable = Path.Combine(project.Path, "not-executable");
            File.WriteAllText(nonExecutable, "#!/bin/sh\nexit 0\n");
            Assert.False(new NativeBuildService(() => project.Path, dotnetExecutable: nonExecutable).IsAvailable);
        }
    }

    [Fact]
    public async Task BuildDrainsStdoutAndStderrConcurrently()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var project = new TempDirectory();
        string executable = CreateScript(project.Path, "build-output.sh", "i=0; while [ $i -lt 3000 ]; do echo out-$i; echo err-$i >&2; i=$((i+1)); done; exit 0");
        string output = "";
        var build = new NativeBuildService(() => project.Path, value => output = value, executable);
        await build.BuildAsync(CancellationToken.None);
        Assert.Equal(0, build.LastExitCode);
        Assert.Contains("out-2999", output);
        Assert.Contains("err-2999", output);
    }

    [Fact]
    public async Task BuildCancellationTerminatesTheChildProcess()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var project = new TempDirectory();
        string executable = CreateScript(project.Path, "build-sleep.sh", "sleep 30");
        var build = new NativeBuildService(() => project.Path, dotnetExecutable: executable);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build.BuildAsync(cancellation.Token));
    }

    [Fact]
    public void NonExecutableFilesDoNotAdvertiseBuildOrRevealCapabilities()
    {
        if (OperatingSystem.IsWindows()) return;
        using var project = new TempDirectory();
        string file = Path.Combine(project.Path, "not-executable");
        File.WriteAllText(file, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Null(NativeExecutable.Find(file));
        Assert.False(new NativeBuildService(() => project.Path, dotnetExecutable: file).IsAvailable);
        Assert.False(new NativeAssetHost(new NativeProjectContext(project.Path), new Dialogs(null), file).IsRevealAvailable);
    }

    [Fact]
    public void RevealUsesArgumentListAndRejectsSymlinkEscape()
    {
        if (OperatingSystem.IsWindows()) return;
        using var project = new TempDirectory(); using var outside = new TempDirectory();
        string executable = CreateScript(project.Path, "reveal.sh", "exit 0");
        string asset = Path.Combine(project.Path, "folder with spaces", "asset.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        File.WriteAllText(asset, "asset");
        var host = new NativeAssetHost(new NativeProjectContext(project.Path), new Dialogs(null), executable);
        ProcessStartInfo start = host.CreateRevealStartInfo("folder with spaces/asset.txt");
        Assert.NotEmpty(start.ArgumentList);
        Assert.DoesNotContain("folder with spaces/asset.txt", start.Arguments, StringComparison.Ordinal);

        string link = Path.Combine(project.Path, "escape");
        try { Directory.CreateSymbolicLink(link, outside.Path); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        Assert.Throws<ArgumentException>(() => host.CreateRevealStartInfo("escape/outside.txt"));
    }

    private static string CreateScript(string directory, string name, string body)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private sealed class Dialogs(string? result) : INativeDialogService
    {
        public bool IsAvailable => result is not null;
        public string? PickFolder(string title, string? initialDirectory = null) => result;
        public string? PickFile(string title, string? initialDirectory = null) => result;
        public string? PickSaveFile(string title, string suggestedName, string? initialDirectory = null) => result;
    }
    private sealed class Host(NativeProjectContext context, NativeProjectService projects) : IEditorHost, IBuildService
    {
        public IFileStorage Files => context.Storage;
        public IProjectPicker Projects => projects;
        public IEditorSettingsStore Settings { get; } = new MemoryEditorSettingsStore();
        public IBuildService Builds => this;
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(true, FileWatching: true);
        public IEditorProjectStorageProvider ProjectStorage => projects;
        public IEditorProjectBackend ProjectBackend => projects;
        public bool IsAvailable => false;
        public void Build() { }
    }
    private sealed class Document(string title) : IEditorDocument
    {
        public string Kind => "text"; public string Title => title; public Luxel.UI.Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false; public bool CanRedo => false; public Luxel.UI.Widget CreateView() => Kit.Spacer();
        public void Undo() { } public void Redo() { } public string Serialize() => ""; public void LoadFrom(string content) { }
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "luxel-editor-test-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
