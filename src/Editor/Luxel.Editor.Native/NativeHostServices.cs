using System.Diagnostics;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Workbench;

namespace Luxel.Editor.Native;

public static class NativeExecutable
{
    public static string? Find(string command)
    {
        if (Path.IsPathFullyQualified(command)) return IsExecutable(command) ? command : null;
        string executable = OperatingSystem.IsWindows() && !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? command + ".exe" : command;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable)).FirstOrDefault(IsExecutable);
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch { return false; }
    }
}

public interface INativeDialogService
{
    bool IsAvailable { get; }
    string? PickFolder(string title, string? initialDirectory = null);
    string? PickFile(string title, string? initialDirectory = null);
    string? PickSaveFile(string title, string suggestedName, string? initialDirectory = null);
}

public sealed class NativeDialogService : INativeDialogService
{
    private readonly string? _dialogExecutable = OperatingSystem.IsWindows() ? NativeExecutable.Find("powershell")
        : OperatingSystem.IsMacOS() ? NativeExecutable.Find("osascript") : NativeExecutable.Find("zenity");
    public bool IsAvailable => _dialogExecutable is not null;

    public string? PickFolder(string title, string? initialDirectory = null) => _dialogExecutable is null ? null : OperatingSystem.IsWindows()
        ? PowerShellFolder(title, initialDirectory)
        : OperatingSystem.IsMacOS() ? Run(_dialogExecutable, ["-e", $"POSIX path of (choose folder with prompt {QuoteApple(title)})"])
        : Run(_dialogExecutable, ["--file-selection", "--directory", $"--title={title}", .. Initial(initialDirectory)]);

    public string? PickFile(string title, string? initialDirectory = null) => _dialogExecutable is null ? null : OperatingSystem.IsWindows()
        ? PowerShellFile(title, initialDirectory, save: false, null)
        : OperatingSystem.IsMacOS() ? Run(_dialogExecutable, ["-e", $"POSIX path of (choose file with prompt {QuoteApple(title)})"])
        : Run(_dialogExecutable, ["--file-selection", $"--title={title}", .. Initial(initialDirectory)]);

    public string? PickSaveFile(string title, string suggestedName, string? initialDirectory = null) => _dialogExecutable is null ? null : OperatingSystem.IsWindows()
        ? PowerShellFile(title, initialDirectory, save: true, suggestedName)
        : OperatingSystem.IsMacOS() ? Run(_dialogExecutable, ["-e", $"POSIX path of (choose file name with prompt {QuoteApple(title)} default name {QuoteApple(suggestedName)})"])
        : Run(_dialogExecutable, ["--file-selection", "--save", "--confirm-overwrite", $"--filename={Path.Combine(initialDirectory ?? "", suggestedName)}", $"--title={title}"]);

    private static IEnumerable<string> Initial(string? path) => string.IsNullOrWhiteSpace(path) ? [] : [$"--filename={Path.TrimEndingDirectorySeparator(path)}/"];
    private static string QuoteApple(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    private string? PowerShellFolder(string title, string? initial)
    {
        string script = "Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.FolderBrowserDialog;"
            + $"$d.Description='{Ps(title)}';" + (initial is null ? "" : $"$d.SelectedPath='{Ps(initial)}';")
            + "if($d.ShowDialog() -eq 'OK'){[Console]::Write($d.SelectedPath)}";
        return Run(_dialogExecutable!, ["-NoProfile", "-STA", "-Command", script]);
    }
    private string? PowerShellFile(string title, string? initial, bool save, string? suggested)
    {
        string type = save ? "SaveFileDialog" : "OpenFileDialog";
        string script = $"Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.{type};$d.Title='{Ps(title)}';"
            + (initial is null ? "" : $"$d.InitialDirectory='{Ps(initial)}';")
            + (suggested is null ? "" : $"$d.FileName='{Ps(suggested)}';")
            + "if($d.ShowDialog() -eq 'OK'){[Console]::Write($d.FileName)}";
        return Run(_dialogExecutable!, ["-NoProfile", "-STA", "-Command", script]);
    }
    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string? Run(string file, IEnumerable<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            string output = stdout.Result.Trim();
            return process.ExitCode == 0 && output.Length > 0 ? Path.GetFullPath(output) : null;
        }
        catch { return null; }
    }
}

public sealed class NativeEditorSettingsStore : IEditorSettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;
    private readonly object _gate = new();
    public NativeEditorSettingsStore(string path)
    {
        _path = Path.GetFullPath(path);
        try { _values = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [] : []; }
        catch { _values = []; }
    }
    public string? Read(string key) { lock (_gate) return _values.GetValueOrDefault(key); }
    public void Write(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
    }
}

public sealed class NativeProjectContext(string initialRoot)
{
    public string Root { get; private set; } = NormalizeRoot(initialRoot);
    public IFileStorage Storage { get; private set; } = new PhysicalFileStorage(NormalizeRoot(initialRoot));
    public void Activate(string root, IFileStorage storage) { Root = NormalizeRoot(root); Storage = storage; }
    public string ResolveWithinRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Split('/').Any(x => x == ".."))
            throw new ArgumentException("Path must remain relative to the active project root.", nameof(path));
        string full = Path.GetFullPath(Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(full)) throw new ArgumentException("Path escapes the active project root.", nameof(path));
        string physical = ResolvePhysicalPath(full);
        if (!IsWithinRoot(physical)) throw new ArgumentException("Path resolves outside the active project root.", nameof(path));
        return physical;
    }
    public bool TryGetRelativePath(string path, out string relative)
    {
        string full = Path.GetFullPath(path);
        if (!IsWithinRoot(full)) { relative = ""; return false; }
        string physical = ResolvePhysicalPath(full);
        if (!IsWithinRoot(physical)) { relative = ""; return false; }
        relative = Path.GetRelativePath(Root, physical).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.Split('/').Any(x => x == "..")) { relative = ""; return false; }
        return true;
    }
    private string ResolvePhysicalPath(string full)
    {
        string relative = Path.GetRelativePath(Root, full);
        string current = Root;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(current, segment);
            FileSystemInfo? info = Directory.Exists(candidate) ? new DirectoryInfo(candidate)
                : File.Exists(candidate) ? new FileInfo(candidate) : null;
            current = info?.LinkTarget is not null
                ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate
                : candidate;
            current = Path.GetFullPath(current);
            if (!IsWithinRoot(current)) return current;
        }
        return current;
    }
    private bool IsWithinRoot(string full)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(prefix, comparison);
    }
    private static string NormalizeRoot(string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var directory = new DirectoryInfo(full);
        return Path.TrimEndingDirectorySeparator(directory.LinkTarget is not null
            ? directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full
            : full);
    }
}

public sealed class NativeProjectService(NativeProjectContext context, INativeDialogService dialogs)
    : IProjectPicker, IEditorProjectBackend, IEditorProjectStorageProvider
{
    public IReadOnlyList<EditorProjectTemplate> Templates { get; } = [new("empty", "Empty", "A local Luxel project")];
    public string? PickProject() => dialogs.PickFolder("Open Luxel project", context.Root);
    public string Open(string projectId)
    {
        string root = Path.GetFullPath(projectId);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }
    public string Create(NewProjectRequest request)
    {
        string path = Path.GetFullPath(Path.Combine(request.Location, request.Name));
        Directory.CreateDirectory(path);
        return path;
    }
    public IFileStorage CreateStorage(string projectId) => new PhysicalFileStorage(projectId);
    public void ProjectActivated(string projectId, IFileStorage storage) => context.Activate(projectId, storage);
}

public sealed class NativeSavePathPicker(NativeProjectContext context, INativeDialogService dialogs) : IEditorSavePathPicker
{
    public string? PickSavePath(IEditorDocument document)
    {
        string? full = dialogs.PickSaveFile("Save document", document.Title, context.Root);
        return full is not null && context.TryGetRelativePath(full, out string relative) ? relative : null;
    }
}

public sealed class NativeBuildService : IBuildService
{
    private readonly Func<string> _projectRoot;
    private readonly Action<string>? _output;
    private readonly string? _dotnet;
    public NativeBuildService(Func<string> projectRoot, Action<string>? output = null, string? dotnetExecutable = null)
    {
        _projectRoot = projectRoot;
        _output = output;
        _dotnet = dotnetExecutable is null ? NativeExecutable.Find("dotnet") : NativeExecutable.Find(dotnetExecutable);
    }
    public bool IsAvailable => _dotnet is not null;
    public int LastExitCode { get; private set; }
    public void Build() => BuildAsync(CancellationToken.None).GetAwaiter().GetResult();
    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        if (_dotnet is null) throw new NotSupportedException("The dotnet executable was not found on PATH.");
        var start = new ProcessStartInfo(_dotnet) { WorkingDirectory = _projectRoot(), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("build");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start dotnet build.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            try { await Task.WhenAll(stdout, stderr); } catch { }
            throw;
        }
        string[] output = await Task.WhenAll(stdout, stderr);
        LastExitCode = process.ExitCode;
        _output?.Invoke(output[0] + output[1]);
        if (LastExitCode != 0) throw new InvalidOperationException($"Build failed with exit code {LastExitCode}.\n{output[1]}");
    }
}

public sealed class NativeAssetHost : IEditorAssetHost
{
    private readonly NativeProjectContext _context;
    private readonly INativeDialogService _dialogs;
    private readonly string? _revealExecutable;
    public NativeAssetHost(NativeProjectContext context, INativeDialogService dialogs, string? revealExecutable = null)
    {
        _context = context;
        _dialogs = dialogs;
        _revealExecutable = revealExecutable is not null ? NativeExecutable.Find(revealExecutable)
            : OperatingSystem.IsWindows() ? NativeExecutable.Find("explorer")
            : OperatingSystem.IsMacOS() ? NativeExecutable.Find("open") : NativeExecutable.Find("xdg-open");
    }
    public bool IsRevealAvailable => _revealExecutable is not null;
    public IReadOnlyList<(string Name, string Content)> PickImportFiles()
    {
        string? file = _dialogs.PickFile("Import asset", _context.Root);
        return file is null ? [] : [(Path.GetFileName(file), File.ReadAllText(file))];
    }
    public ProcessStartInfo CreateRevealStartInfo(string path)
    {
        if (_revealExecutable is null) throw new NotSupportedException("No file-manager reveal executable is available.");
        string full = _context.ResolveWithinRoot(path);
        var start = new ProcessStartInfo(_revealExecutable) { UseShellExecute = false };
        if (OperatingSystem.IsWindows()) start.ArgumentList.Add($"/select,{full}");
        else if (OperatingSystem.IsMacOS()) { start.ArgumentList.Add("-R"); start.ArgumentList.Add(full); }
        else start.ArgumentList.Add(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!);
        return start;
    }

    public void Reveal(string path) => Process.Start(CreateRevealStartInfo(path))?.Dispose();
}

public sealed record NativeWindowLayout(int X, int Y, int Width, int Height)
{
    public const string SettingsKey = "editor.native.window.v1";
    public static NativeWindowLayout Read(IEditorSettingsStore settings)
    {
        try { return JsonSerializer.Deserialize<NativeWindowLayout>(settings.Read(SettingsKey) ?? "") ?? new(80, 60, 1280, 800); }
        catch { return new(80, 60, 1280, 800); }
    }
    public void Write(IEditorSettingsStore settings) => settings.Write(SettingsKey, JsonSerializer.Serialize(this));
}
