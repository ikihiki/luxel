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

/// <summary>実ディスク VFS。assetRoot 相対。FileSystemWatcher で変更監視。</summary>
public sealed class PhysicalFileSystem : IVirtualFileSystem, IDisposable
{
    private readonly string _root;
    private readonly List<FileSystemWatcher> _watchers = new();

    public PhysicalFileSystem(string root) => _root = System.IO.Path.GetFullPath(root);

    private string Full(string path) => System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, path));

    public Task<byte[]> ReadAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(Full(path), ct);
    public bool Exists(string path) => File.Exists(Full(path));

    public IReloadToken? Watch(string path, Action onChanged)
    {
        string full = Full(path);
        string? dir = System.IO.Path.GetDirectoryName(full);
        string name = System.IO.Path.GetFileName(full);
        if (dir == null || !Directory.Exists(dir)) return null;
        var w = new FileSystemWatcher(dir, name) { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        FileSystemEventHandler h = (_, _) => onChanged();
        w.Changed += h; w.Created += h; w.Renamed += (_, _) => onChanged();
        lock (_watchers) _watchers.Add(w);
        return new Token(() => { try { w.Dispose(); } catch { } lock (_watchers) _watchers.Remove(w); });
    }

    public void Dispose() { lock (_watchers) { foreach (var w in _watchers) w.Dispose(); _watchers.Clear(); } }

    private sealed class Token(Action dispose) : IReloadToken
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }
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
