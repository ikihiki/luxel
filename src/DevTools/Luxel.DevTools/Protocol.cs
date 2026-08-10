using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luxel.DevTools;

/// <summary>ログ 1 件 (離散イベント; 入力/IME/info)。seq 順で欠落なくページングできる。</summary>
public sealed record LogEntry(long Seq, long Ts, string Kind, string Text);

/// <summary>描画フラッシュ統計 (最新のみ)。</summary>
public sealed record StatDto(int TransformWrites, int StyleWrites, long SegBytes, bool FullRebuild, int W, int H);

/// <summary>/poll の軽量応答。大物 (frame/tree/perf/ecs/custom) は含めず rev だけ返す → 変化時のみ取得させる。</summary>
public sealed record PollResponse(
    long FrameRev, long TreeRev, long PerfRev, long EcsRev, long EcsSummaryRev, long CustomRev,
    long SurfacesRev, long InputRev, bool Paused, StatDto? Stat, LogEntry[] Logs, long LogCursor);

/// <summary>JSON 設定 (camelCase, null 省略)。</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>
/// ログのリングバッファ。直近 <c>capacity</c> 件を保持し、<see cref="Since"/> で seq カーソル以降を取得。
/// 新規接続は cursor=0 から呼べば「保持している範囲の先頭」から受け取れる。
/// </summary>
public sealed class LogRing
{
    private readonly LogEntry[] _buf;
    private readonly object _lock = new();
    private readonly long _start = Environment.TickCount64;
    private long _seq;   // 次に割り当てる seq (= これまでの総数)

    public LogRing(int capacity = 512) => _buf = new LogEntry[capacity];

    public void Add(string kind, string text)
    {
        lock (_lock)
        {
            _buf[(int)(_seq % _buf.Length)] = new LogEntry(_seq, Environment.TickCount64 - _start, kind, text);
            _seq++;
        }
    }

    /// <summary>seq が <paramref name="since"/> 以降のログ + 次カーソルを返す。</summary>
    public (LogEntry[] items, long cursor) Since(long since)
    {
        lock (_lock)
        {
            long from = Math.Max(0, Math.Max(since, _seq - _buf.Length));
            var list = new List<LogEntry>();
            for (long s = from; s < _seq; s++) list.Add(_buf[(int)(s % _buf.Length)]);
            return (list.ToArray(), _seq);
        }
    }
}
