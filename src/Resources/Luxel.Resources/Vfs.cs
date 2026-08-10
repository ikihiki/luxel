using Luxel.Platform;

namespace Luxel.Resources;

/// <summary>
/// バーチャルファイルシステム。組込み FileSource はこれ経由で読む (DI で差替: pak/overlay/メモリ等)。
/// </summary>
public interface IVirtualFileSystem
{
    Task<byte[]> ReadAsync(string path, CancellationToken ct);
    bool Exists(string path);
    /// <summary>変更監視 (自動リロード源)。未対応なら null。</summary>
    IReloadToken? Watch(string path, Action onChanged) => null;
}

/// <summary>Platform の読取抽象を既存 VFS API へ接続する adapter。</summary>
public class PlatformFileSystemAdapter(IPlatformFileSystem fileSystem) : IVirtualFileSystem, IDisposable
{
    public Task<byte[]> ReadAsync(string path, CancellationToken ct)
        => fileSystem.ReadAllBytesAsync(path, ct);

    public bool Exists(string path) => fileSystem.Exists(path);

    public IReloadToken? Watch(string path, Action onChanged)
    {
        IDisposable? token = fileSystem.Watch(path, onChanged);
        return token is null ? null : new ReloadToken(token);
    }

    public void Dispose()
    {
        if (fileSystem is IDisposable disposable) disposable.Dispose();
    }

    private sealed class ReloadToken(IDisposable token) : IReloadToken
    {
        private IDisposable? _token = token;
        public void Dispose() => Interlocked.Exchange(ref _token, null)?.Dispose();
    }
}

/// <summary>
/// 従来互換の physical VFS。実際の System.IO 利用は登録済み Platform desktop 実装へ委譲する。
/// Web/WASM では <see cref="PlatformFileSystemAdapter"/> に明示的な実装を渡すこと。
/// </summary>
public sealed class PhysicalFileSystem : PlatformFileSystemAdapter
{
    public PhysicalFileSystem(string root) : base(PlatformFileSystems.CreatePhysical(root)) { }
    public PhysicalFileSystem(IPlatformFileSystem fileSystem) : base(fileSystem) { }
}

/// <summary>メモリ上の VFS (テスト/動的生成用)。Set でファイルを置くと Watch が発火する。</summary>
public sealed class MemoryFileSystem : IVirtualFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly Dictionary<string, List<Action>> _watchers = new();
    private readonly object _lock = new();

    public void Set(string path, byte[] data)
    {
        List<Action>? cbs = null;
        lock (_lock)
        {
            _files[path] = data;
            if (_watchers.TryGetValue(path, out var list)) cbs = new List<Action>(list);
        }
        if (cbs != null) foreach (var cb in cbs) cb();
    }

    public Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        lock (_lock)
            return _files.TryGetValue(path, out var d)
                ? Task.FromResult((byte[])d.Clone())
                : Task.FromException<byte[]>(new FileNotFoundException(path));
    }

    public bool Exists(string path) { lock (_lock) return _files.ContainsKey(path); }

    public IReloadToken? Watch(string path, Action onChanged)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(path, out var list)) _watchers[path] = list = new();
            list.Add(onChanged);
        }
        return new Token(() => { lock (_lock) { if (_watchers.TryGetValue(path, out var l)) l.Remove(onChanged); } });
    }

    private sealed class Token(Action dispose) : IReloadToken
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }
}

/// <summary>A revisioned, mutable in-memory VFS for editor workspaces.</summary>
public sealed class WorkspaceFileSystem : IVirtualFileSystem
{
    private readonly object _gate = new();
    private Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action>> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private long _revision;

    public long Revision { get { lock (_gate) return _revision; } }

    public long Set(string path, byte[] data, long? expectedRevision = null) =>
        ApplyBatch([new WorkspaceSetOperation(path, data)], expectedRevision);

    public long Delete(string path, long? expectedRevision = null) =>
        ApplyBatch([new WorkspaceDeleteOperation(path)], expectedRevision);

    public long Rename(string path, string newPath, long? expectedRevision = null) =>
        ApplyBatch([new WorkspaceRenameOperation(path, newPath)], expectedRevision);

    public long ApplyBatch(IEnumerable<WorkspaceFileOperation> operations, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        WorkspaceFileOperation[] batch = operations.ToArray();
        if (batch.Any(operation => operation is null))
            throw new ArgumentException("Workspace operations cannot contain null values.", nameof(operations));

        List<Action> callbacks;
        long revision;
        lock (_gate)
        {
            EnsureRevision(expectedRevision);
            if (batch.Length == 0) return _revision;

            var next = _files.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorkspaceFileOperation operation in batch)
            {
                switch (operation)
                {
                    case WorkspaceSetOperation set:
                    {
                        string path = WorkspacePath.Normalize(set.Path);
                        ArgumentNullException.ThrowIfNull(set.Data);
                        next[path] = (byte[])set.Data.Clone();
                        changedPaths.Add(path);
                        break;
                    }
                    case WorkspaceDeleteOperation delete:
                    {
                        string path = WorkspacePath.Normalize(delete.Path);
                        if (!next.Remove(path)) throw new FileNotFoundException("Workspace file was not found.", path);
                        changedPaths.Add(path);
                        break;
                    }
                    case WorkspaceRenameOperation rename:
                    {
                        string path = WorkspacePath.Normalize(rename.Path);
                        string newPath = WorkspacePath.Normalize(rename.NewPath);
                        if (path == newPath) break;
                        if (!next.Remove(path, out byte[]? data))
                            throw new FileNotFoundException("Workspace file was not found.", path);
                        if (next.ContainsKey(newPath))
                            throw new IOException($"Workspace file '{newPath}' already exists.");
                        next[newPath] = data;
                        changedPaths.Add(path);
                        changedPaths.Add(newPath);
                        break;
                    }
                    default:
                        throw new ArgumentException($"Unsupported workspace operation '{operation.GetType().Name}'.", nameof(operations));
                }
            }

            _files = next;
            revision = _revision = checked(_revision + 1);
            callbacks = changedPaths
                .SelectMany(path => _watchers.TryGetValue(path, out List<Action>? watchers) ? watchers : [])
                .Distinct()
                .ToList();
        }

        foreach (Action callback in callbacks) callback();
        return revision;
    }

    public WorkspaceFileSystemSnapshot Snapshot()
    {
        lock (_gate)
        {
            var files = _files.ToDictionary(
                pair => pair.Key,
                pair => (ReadOnlyMemory<byte>)(byte[])pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
            return new WorkspaceFileSystemSnapshot(_revision, files);
        }
    }

    public Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string normalized = WorkspacePath.Normalize(path);
        lock (_gate)
            return _files.TryGetValue(normalized, out byte[]? data)
                ? Task.FromResult((byte[])data.Clone())
                : Task.FromException<byte[]>(new FileNotFoundException("Workspace file was not found.", normalized));
    }

    public bool Exists(string path)
    {
        string normalized = WorkspacePath.Normalize(path);
        lock (_gate) return _files.ContainsKey(normalized);
    }

    public IReloadToken Watch(string path, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        string normalized = WorkspacePath.Normalize(path);
        lock (_gate)
        {
            if (!_watchers.TryGetValue(normalized, out List<Action>? callbacks))
                _watchers[normalized] = callbacks = [];
            callbacks.Add(onChanged);
        }
        return new WorkspaceWatchToken(() =>
        {
            lock (_gate)
            {
                if (!_watchers.TryGetValue(normalized, out List<Action>? callbacks)) return;
                callbacks.Remove(onChanged);
                if (callbacks.Count == 0) _watchers.Remove(normalized);
            }
        });
    }

    private void EnsureRevision(long? expectedRevision)
    {
        if (expectedRevision is { } expected && expected != _revision)
            throw new StaleWorkspaceRevisionException(expected, _revision);
    }

    private sealed class WorkspaceWatchToken(Action dispose) : IReloadToken
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed record WorkspaceFileSystemSnapshot(
    long Revision,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Files);

public abstract record WorkspaceFileOperation;
public sealed record WorkspaceSetOperation(string Path, byte[] Data) : WorkspaceFileOperation;
public sealed record WorkspaceDeleteOperation(string Path) : WorkspaceFileOperation;
public sealed record WorkspaceRenameOperation(string Path, string NewPath) : WorkspaceFileOperation;

public sealed class StaleWorkspaceRevisionException(long expectedRevision, long actualRevision)
    : InvalidOperationException($"Workspace revision {expectedRevision} is stale; current revision is {actualRevision}.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}

public static class WorkspaceLimits
{
    public const int MaxFileCount = 128;
    public const int MaxCSharpFileBytes = 128 * 1024;
    public const int MaxTotalSourceBytes = 2 * 1024 * 1024;
}

public static class WorkspacePath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/') || normalized.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Workspace paths must be relative normalized file paths.", nameof(path));
        if (normalized.Contains(':', StringComparison.Ordinal) || normalized.Any(char.IsControl))
            throw new ArgumentException("Workspace paths cannot contain URI, drive, colon, NUL, or control characters.", nameof(path));
        string[] segments = normalized.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Workspace paths cannot contain empty, '.' or '..' segments.", nameof(path));
        return normalized;
    }
}
