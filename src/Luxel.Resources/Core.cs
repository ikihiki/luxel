using System.Runtime.CompilerServices;

namespace Luxel.Resources;

/// <summary>ステップが宣言する実行器 (内部スケジューラ。利用者は管理しない)。</summary>
public enum Executor { Io, Cpu, Gpu }

/// <summary>リソースの読込状態。</summary>
public enum ResourceStatus { Loading, Ready, Failed }

/// <summary>自動リロードの購読トークン (破棄で監視解除)。</summary>
public interface IReloadToken : IDisposable { }

/// <summary>
/// 実行レーン: スレッドプール上で同時実行数を <c>maxConcurrency</c> に制限して継続を走らせる。
/// `await ctx.Io/Cpu/Gpu` の hop 先。スレッドプールスレッドをブロックせずキューで絞る。
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
    public bool IsCompleted => false;
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
