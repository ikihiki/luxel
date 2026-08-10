namespace Luxel.DevTools;

/// <summary>
/// 「最新のみ保持」スロット (coalesce)。高頻度イベント (frame/tree/stat) で上書きし、
/// リビジョン番号を進める。HTTP 読み取りはロックフリー (volatile 参照 + Interlocked rev)。
/// Web はこの rev をポーリングし、進んだ時だけ本体を取得する → 負荷をクライアント cadence に従属させる。
/// </summary>
public sealed class LatestSlot<T> where T : class
{
    private volatile T? _value;
    private long _rev;

    /// <summary>最新値を発行 (上書き) し rev を進める。</summary>
    public void Publish(T value)
    {
        _value = value;
        Interlocked.Increment(ref _rev);
    }

    public long Rev => Interlocked.Read(ref _rev);

    /// <summary>現在値と rev。</summary>
    public (T? value, long rev) Read()
    {
        long r = Interlocked.Read(ref _rev);
        return (_value, r);
    }
}
