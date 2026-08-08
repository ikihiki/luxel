using System.Runtime.CompilerServices;

namespace Luxel.Platform.Windows;

/// <summary>System.IO を使う Windows desktop 用 rooted file system。</summary>
public sealed class WindowsPlatformFileSystem : IPlatformFileSystem, IDisposable
{
    private readonly string _root;
    private readonly List<FileSystemWatcher> _watchers = [];

    public WindowsPlatformFileSystem(string root) => _root = Path.GetFullPath(root);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(Full(path), cancellationToken);

    public bool Exists(string path) => File.Exists(Full(path));

    public IDisposable? Watch(string path, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        string full = Full(path);
        string? directory = Path.GetDirectoryName(full);
        string name = Path.GetFileName(full);
        if (directory is null || !Directory.Exists(directory)) return null;

        var watcher = new FileSystemWatcher(directory, name)
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        FileSystemEventHandler changed = (_, _) => onChanged();
        RenamedEventHandler renamed = (_, _) => onChanged();
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Renamed += renamed;
        lock (_watchers) _watchers.Add(watcher);
        return new WatchToken(this, watcher);
    }

    private string Full(string path) => Path.GetFullPath(Path.Combine(_root, path));

    public void Dispose()
    {
        lock (_watchers)
        {
            foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }

    private sealed class WatchToken(WindowsPlatformFileSystem owner, FileSystemWatcher watcher) : IDisposable
    {
        private FileSystemWatcher? _watcher = watcher;

        public void Dispose()
        {
            FileSystemWatcher? current = Interlocked.Exchange(ref _watcher, null);
            if (current is null) return;
            current.Dispose();
            lock (owner._watchers) owner._watchers.Remove(current);
        }
    }
}

internal static class WindowsPlatformFileSystemRegistration
{
#pragma warning disable CA2255 // Desktop integration assembly intentionally registers its platform factory at load time.
    [ModuleInitializer]
    internal static void Register() =>
        PlatformFileSystems.RegisterPhysicalFactory(static root => new WindowsPlatformFileSystem(root));
#pragma warning restore CA2255
}
