using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Workbench;

namespace Luxel.Editor.Browser;

public enum BrowserStorageFailureKind { None, Quota, Permission, Unavailable, Unknown }
public enum BrowserStorageDurability { Initializing, Durable, Pending, Temporary }

public sealed record BrowserStorageState(BrowserStorageDurability Durability, BrowserStorageFailureKind Failure, string Message)
{
    public static BrowserStorageState Ready { get; } = new(BrowserStorageDurability.Durable, BrowserStorageFailureKind.None, "IndexedDB workspace");
    public bool Persistent => Durability is BrowserStorageDurability.Durable or BrowserStorageDurability.Pending;
    public bool Durable => Durability == BrowserStorageDurability.Durable;
    public bool RequiresUnloadWarning => Durability is BrowserStorageDurability.Pending or BrowserStorageDurability.Temporary;
    public string StatusText => Durability switch
    {
        BrowserStorageDurability.Pending => "Saving project to IndexedDB…",
        BrowserStorageDurability.Temporary => $"Temporary session — {Message}",
        BrowserStorageDurability.Initializing => "Initializing IndexedDB…",
        _ => Message,
    };
}

public interface IBrowserWorkspacePersistence
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(string workspace);
    Task SaveAsync(string workspace, string path, string content);
    Task DeleteAsync(string workspace, string path);
}

/// <summary>Synchronous Editor storage backed by a hydrated IndexedDB mirror with explicit durability state.</summary>
public sealed class BrowserWorkspaceStorage : IFileStorage, IEditorStorageStatus
{
    private readonly IBrowserWorkspacePersistence _persistence;
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action>> _watchers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _persistenceGate = new();
    private Task _persistenceTail = Task.CompletedTask;
    private int _pendingWrites;

    public BrowserWorkspaceStorage(IBrowserWorkspacePersistence persistence, string workspace)
    {
        _persistence = persistence;
        Workspace = NormalizeWorkspace(workspace);
    }

    public BrowserStorageState State { get; private set; } = new(BrowserStorageDurability.Initializing, BrowserStorageFailureKind.None, "Storage has not initialized.");
    public bool IsDurable => State.Durable;
    public bool RequiresUnloadWarning => State.RequiresUnloadWarning;
    public string StatusText => State.StatusText;
    public event Action<BrowserStorageState>? StateChanged;
    public event Action<Exception>? PersistenceError;
    public string Workspace { get; }

    public async Task InitializeAsync()
    {
        try
        {
            IReadOnlyDictionary<string, string> loaded = await _persistence.LoadAsync(Workspace);
            lock (_gate)
            {
                _files.Clear();
                foreach ((string path, string content) in loaded) _files[path] = content;
            }
            SetState(BrowserStorageState.Ready);
        }
        catch (Exception error) { FallBack(error); }
    }

    public bool Exists(string path) { lock (_gate) return _files.ContainsKey(NormalizePath(path)); }
    public string? Read(string path) { lock (_gate) return _files.GetValueOrDefault(NormalizePath(path)); }
    public IEnumerable<string> List() { lock (_gate) return _files.Keys.Order(StringComparer.Ordinal).ToArray(); }

    public void Write(string path, string content)
    {
        path = NormalizePath(path);
        lock (_gate) _files[path] = content;
        Notify(path);
        Persist(() => _persistence.SaveAsync(Workspace, path, content));
    }

    public void Delete(string path)
    {
        path = NormalizePath(path);
        lock (_gate) { if (!_files.Remove(path)) throw new FileNotFoundException(path); }
        Notify(path);
        Persist(() => _persistence.DeleteAsync(Workspace, path));
    }

    public void Move(string sourcePath, string destinationPath)
    {
        sourcePath = NormalizePath(sourcePath); destinationPath = NormalizePath(destinationPath);
        string content;
        lock (_gate)
        {
            if (!_files.TryGetValue(sourcePath, out content!)) throw new FileNotFoundException(sourcePath);
            if (_files.ContainsKey(destinationPath)) throw new IOException($"Destination already exists: {destinationPath}");
            _files.Remove(sourcePath); _files[destinationPath] = content;
        }
        Notify(sourcePath); Notify(destinationPath);
        Persist(async () =>
        {
            await _persistence.SaveAsync(Workspace, destinationPath, content);
            await _persistence.DeleteAsync(Workspace, sourcePath);
        });
    }

    public IDisposable Watch(string path, Action onChanged)
    {
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!_watchers.TryGetValue(path, out List<Action>? list)) _watchers[path] = list = [];
            list.Add(onChanged);
        }
        return new Subscription(() => { lock (_gate) if (_watchers.TryGetValue(path, out List<Action>? list)) list.Remove(onChanged); });
    }

    public Task FlushAsync() { lock (_persistenceGate) return _persistenceTail; }

    private void Persist(Func<Task> operation)
    {
        if (State.Durability == BrowserStorageDurability.Temporary) return;
        lock (_persistenceGate)
        {
            _pendingWrites++;
            SetState(new(BrowserStorageDurability.Pending, BrowserStorageFailureKind.None, "IndexedDB write pending"));
            _persistenceTail = _persistenceTail.ContinueWith(async _ =>
            {
                Exception? failure = null;
                try { await operation(); }
                catch (Exception error) { failure = error; }
                lock (_persistenceGate) _pendingWrites--;
                if (failure is not null)
                {
                    FallBack(failure);
                    PersistenceError?.Invoke(failure);
                }
                else if (Volatile.Read(ref _pendingWrites) == 0 && State.Durability != BrowserStorageDurability.Temporary)
                    SetState(BrowserStorageState.Ready);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        }
    }

    private void FallBack(Exception error)
    {
        BrowserStorageFailureKind kind = Classify(error);
        string reason = kind switch
        {
            BrowserStorageFailureKind.Quota => "browser storage quota was exceeded",
            BrowserStorageFailureKind.Permission => "browser storage permission was denied",
            BrowserStorageFailureKind.Unavailable => "IndexedDB is unavailable",
            _ => $"browser storage failed: {error.Message}",
        };
        SetState(new(BrowserStorageDurability.Temporary, kind, reason));
    }

    public static BrowserStorageFailureKind Classify(Exception error)
    {
        string text = error.ToString();
        if (text.Contains("Quota", StringComparison.OrdinalIgnoreCase)) return BrowserStorageFailureKind.Quota;
        if (text.Contains("NotAllowed", StringComparison.OrdinalIgnoreCase) || text.Contains("Security", StringComparison.OrdinalIgnoreCase)
            || text.Contains("permission", StringComparison.OrdinalIgnoreCase)) return BrowserStorageFailureKind.Permission;
        if (text.Contains("InvalidState", StringComparison.OrdinalIgnoreCase) || text.Contains("NotSupported", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) return BrowserStorageFailureKind.Unavailable;
        return BrowserStorageFailureKind.Unknown;
    }

    private void SetState(BrowserStorageState state) { State = state; StateChanged?.Invoke(state); }
    private void Notify(string path)
    {
        Action[] callbacks;
        lock (_gate) callbacks = _watchers.GetValueOrDefault(path)?.ToArray() ?? [];
        foreach (Action callback in callbacks) callback();
    }
    private static string NormalizeWorkspace(string value) => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
    private static string NormalizePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string path = value.Replace('\\', '/').TrimStart('/');
        if (path.Split('/').Any(x => x == "..")) throw new ArgumentException("Storage path cannot contain '..'.", nameof(value));
        return path;
    }
    private sealed class Subscription(Action dispose) : IDisposable { private Action? _dispose = dispose; public void Dispose() { _dispose?.Invoke(); _dispose = null; } }
}

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
internal sealed partial class IndexedDbWorkspacePersistence : IBrowserWorkspacePersistence
{
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(string workspace)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(await LoadCore(workspace)) ?? [];
    public Task SaveAsync(string workspace, string path, string content) => SaveCore(workspace, path, content);
    public Task DeleteAsync(string workspace, string path) => DeleteCore(workspace, path);
    [JSImport("loadWorkspace", "luxel-editor-storage")] private static partial Task<string> LoadCore(string workspace);
    [JSImport("saveFile", "luxel-editor-storage")] private static partial Task SaveCore(string workspace, string path, string content);
    [JSImport("deleteFile", "luxel-editor-storage")] private static partial Task DeleteCore(string workspace, string path);
}
