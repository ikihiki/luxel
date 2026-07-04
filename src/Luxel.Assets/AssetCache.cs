namespace Luxel.Assets;

/// <summary>
/// path → <see cref="AssetDocument"/> のキャッシュ (重複 Io を防ぐ)。並行 load 要求は同じ Task に join。
/// </summary>
public sealed class AssetCache
{
    private readonly Dictionary<string, AssetDocument> _cache = new();
    private readonly Dictionary<string, Task<AssetDocument>> _inflight = new();
    private readonly object _lock = new();

    public Task<AssetDocument> GetOrLoadAsync(string path)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var doc)) return Task.FromResult(doc);
            if (_inflight.TryGetValue(path, out var pending)) return pending;
            var task = LoadAndCache(path);
            _inflight[path] = task;
            return task;
        }
    }

    private async Task<AssetDocument> LoadAndCache(string path)
    {
        try
        {
            var doc = await AssetLoaders.LoadAsync(path).ConfigureAwait(false);
            lock (_lock) { _cache[path] = doc; _inflight.Remove(path); }
            return doc;
        }
        catch
        {
            lock (_lock) { _inflight.Remove(path); }
            throw;
        }
    }

    public void Clear()
    {
        lock (_lock) { _cache.Clear(); _inflight.Clear(); }
    }

    public int CacheCount { get { lock (_lock) return _cache.Count; } }
}
