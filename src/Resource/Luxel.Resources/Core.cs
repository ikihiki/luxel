using System.Runtime.CompilerServices;

namespace Luxel.Resources;

/// <summary>ステップが宣言する実行器 (内部スケジューラ。利用者は管理しない)。</summary>
public enum Executor { Io, Cpu, External }

/// <summary>キャッシュされた値の破棄責任。</summary>
public enum ResourceOwnership
{
    /// <summary><see cref="ResourceSystem"/> が eviction / replacement 時に <see cref="IDisposable.Dispose"/> を呼ぶ。</summary>
    Owned,
    /// <summary>値の破棄責任は呼び出し側にあり、<see cref="ResourceSystem"/> は破棄しない。</summary>
    Borrowed,
}

/// <summary>リソースの読込状態。</summary>
public enum ResourceStatus { Loading, Ready, Failed }

/// <summary>自動リロードの購読トークン (破棄で監視解除)。</summary>
public interface IReloadToken : IDisposable { }

/// <summary>
/// 実行レーン: スレッドプール上で同時実行数を <c>maxConcurrency</c> に制限して継続を走らせる。
/// `await ctx.Io/Cpu/External` の hop 先。スレッドプールスレッドをブロックせずキューで絞る。
/// </summary>
internal sealed class ResourceLane
{
    private readonly int _max;
    private readonly Queue<Action> _queue = new();
    private readonly object _lock = new();
    private int _running;

    public ResourceLane(int maxConcurrency) => _max = Math.Max(1, maxConcurrency);

    public void Post(Action continuation)
    {
        lock (_lock) _queue.Enqueue(continuation);
        Drain();
    }

    private void Drain()
    {
        while (true)
        {
            Action? cont = null;
            lock (_lock)
            {
                if (_running < _max && _queue.Count > 0) { cont = _queue.Dequeue(); _running++; }
            }
            if (cont == null) return;
            ThreadPool.UnsafeQueueUserWorkItem(static s =>
            {
                var (self, c) = ((ResourceLane, Action))s!;
                try { c(); }
                finally { lock (self._lock) self._running--; self.Drain(); }
            }, (this, cont));
        }
    }
}

/// <summary>レーンへ継続を載せる awaitable (`await ctx.Cpu` 等)。常に hop する。</summary>
public readonly struct StageAwaitable : INotifyCompletion
{
    private readonly ResourceLane _lane;
    internal StageAwaitable(ResourceLane lane) => _lane = lane;

    public StageAwaitable GetAwaiter() => this;
    // Browser WebAssembly has no independently progressing ThreadPool while synchronous story
    // construction is waiting for scope-local GPU creation. Run the lane continuation inline there;
    // genuinely asynchronous source/decoder work still yields on its own Task.
    public bool IsCompleted => OperatingSystem.IsBrowser();
    public void OnCompleted(Action continuation) => _lane.Post(continuation);
    public void GetResult() { }
}

/// <summary>uri の解析結果 (scheme/path/query/ext + fragment + キャッシュキー)。既定 scheme = file。</summary>
public readonly struct ResourceUri
{
    public string Scheme { get; }
    public string Path { get; }
    public string Query { get; }
    public string Extension { get; }
    public string Fragment { get; }

    public ResourceUri(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        raw = raw.Trim();
        int i = raw.IndexOf("://", StringComparison.Ordinal);
        string body;
        if (i > 0) { Scheme = raw[..i].ToLowerInvariant(); body = raw[(i + 3)..]; }
        else
        {
            int c = raw.IndexOf(':');
            if (c > 1) { Scheme = raw[..c].ToLowerInvariant(); body = raw[(c + 1)..]; }
            else { Scheme = "file"; body = raw; }
        }

        int hash = body.IndexOf('#');
        string pathAndQuery;
        if (hash >= 0) { pathAndQuery = body[..hash]; Fragment = body[(hash + 1)..]; }
        else { pathAndQuery = body; Fragment = ""; }

        int question = pathAndQuery.IndexOf('?');
        if (question >= 0) { Path = pathAndQuery[..question]; Query = pathAndQuery[(question + 1)..]; }
        else { Path = pathAndQuery; Query = ""; }

        int dot = Path.LastIndexOf('.');
        int slash = Path.LastIndexOfAny(['/', '\\']);
        Extension = dot > slash && dot >= 0 ? Path[dot..].ToLowerInvariant() : "";
    }

    /// <summary>この URI を基準に相対参照を解決する。file/http(s)/workspace を含め scheme を保持する。</summary>
    public ResourceUri Resolve(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        relative = relative.Trim();
        if (HasExplicitScheme(relative)) return new ResourceUri(relative);

        if (relative[0] == '#')
            return new ResourceUri(BuildUrl(Path, Query, relative[1..]));

        string reference = relative;
        string fragment = "";
        int hash = reference.IndexOf('#');
        if (hash >= 0)
        {
            fragment = reference[(hash + 1)..];
            reference = reference[..hash];
        }

        string query = "";
        int question = reference.IndexOf('?');
        if (question >= 0)
        {
            query = reference[(question + 1)..];
            reference = reference[..question];
        }

        string resolvedPath;
        if (reference.Length == 0)
        {
            resolvedPath = Path;
            if (question < 0) query = Query;
        }
        else
        {
            string normalizedReference = reference.Replace('\\', '/');
            string basePath = Path.Replace('\\', '/');
            int protectedSegments = Scheme == "file" ? 0 : 1;
            if (normalizedReference.StartsWith('/'))
            {
                if (protectedSegments == 1 && TryGetAuthority(basePath, out string authority))
                    resolvedPath = NormalizePath(authority + normalizedReference, protectedSegments);
                else
                    resolvedPath = NormalizePath(normalizedReference, protectedSegments: 0);
            }
            else
            {
                int slash = basePath.LastIndexOf('/');
                string directory = slash >= 0 ? basePath[..(slash + 1)] : "";
                resolvedPath = NormalizePath(directory + normalizedReference, protectedSegments);
            }
        }

        return new ResourceUri(BuildUrl(resolvedPath, query, fragment));
    }

    private static bool HasExplicitScheme(string value)
    {
        int schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator > 0) return true;
        int colon = value.IndexOf(':');
        return colon > 1;
    }

    private static bool TryGetAuthority(string path, out string authority)
    {
        int slash = path.IndexOf('/');
        authority = slash < 0 ? path : path[..slash];
        return authority.Length > 0;
    }

    private static string NormalizePath(string path, int protectedSegments)
    {
        bool rooted = path.StartsWith('/');
        string[] segments = path.Split('/');
        var normalized = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (normalized.Count > protectedSegments && normalized[^1] != "..") normalized.RemoveAt(normalized.Count - 1);
                else if (!rooted && protectedSegments == 0) normalized.Add(segment);
                continue;
            }
            normalized.Add(segment);
        }
        string result = string.Join('/', normalized);
        return rooted ? "/" + result : result;
    }

    private string BuildUrl(string path, string query, string fragment)
    {
        string url = Scheme is "file" or "" ? path : $"{Scheme}://{path}";
        if (query.Length > 0) url += "?" + query;
        return fragment.Length == 0 ? url : url + "#" + fragment;
    }

    public string Key
    {
        get
        {
            string key = Scheme + "|" + Path;
            if (Query.Length > 0) key += "?" + Query;
            return Fragment.Length == 0 ? key : key + "#" + Fragment;
        }
    }

    public string Url
    {
        get
        {
            string baseUrl = Scheme is "file" or "" ? Path : $"{Scheme}://{Path}";
            if (Query.Length > 0) baseUrl += "?" + Query;
            return Fragment.Length == 0 ? baseUrl : baseUrl + "#" + Fragment;
        }
    }

    public ResourceUri WithoutFragment() => Fragment.Length == 0
        ? this
        : new ResourceUri(Url[..^(Fragment.Length + 1)]);
    public override string ToString() => Url;
}
