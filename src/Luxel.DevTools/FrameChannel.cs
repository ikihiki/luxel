using System.Buffers.Binary;

namespace Luxel.DevTools;

/// <summary>
/// ライブフレーム配信用の「最新のみ保持」チャネル (Q05 F1: 割り当て除去 + 二読者所有権)。
///
/// <para><see cref="LatestSlot{T}"/> は発行のたびに新しい <c>byte[]</c> を確保して swap するため、
/// 720p/60fps では約 220MB/s の GC 割り当てをゲームの main スレッドに載せてしまう。本チャネルは
/// 代わりに <paramref name="slots"/> 枚のリングバッファを使い回し、書き手側の定常割り当てをゼロにする。</para>
///
/// <list type="bullet">
/// <item><b>書き手</b> (ゲーム main スレッド、<c>EmitFrame</c> 経由): <see cref="Publish"/> が次スロットへ
///   tight RGBA をコピーするだけ。バッファはサイズ変化時のみ再確保。</item>
/// <item><b>読み手</b> (DebugServer の HTTP スレッド + 内蔵版 <c>DevToolsApp</c> の島スレッド、2 系統):
///   <see cref="Read"/> が seqlock (世代カウンタ) で整合を検証しつつ**自前の body へコピー**して返す。
///   書き手が読み取り中のスロットを踏まないよう、リングは <c>slots ≥ 3</c> の周回で守る (seqlock は保険)。</item>
/// </list>
///
/// 読み手が確保する body は各自の cadence (HTTP はクライアント要求時、島は毎島フレーム) なので、
/// ゲーム main スレッドの予算 (&lt; 0.5ms/フレーム・割り当てゼロ) には影響しない。
/// </summary>
public sealed class FrameChannel
{
    private readonly int _slots;
    private readonly byte[][] _buf;    // 各スロットの tight RGBA (サイズ変化時のみ再確保)
    private readonly int[] _w;
    private readonly int[] _h;
    private readonly long[] _gen;      // スロットごとの世代 (奇数 = 書き込み中、seqlock)
    private readonly long[] _slotRev;  // スロットが保持するフレームの rev
    private long _rev;                 // 全体リビジョン (発行回数)
    private int _cur = -1;             // 最新スロット index (volatile 読み)
    private readonly object _writeLock = new();

    public FrameChannel(int slots = 3)
    {
        _slots = Math.Max(2, slots);
        _buf = new byte[_slots][];
        _w = new int[_slots];
        _h = new int[_slots];
        _gen = new long[_slots];
        _slotRev = new long[_slots];
    }

    /// <summary>発行済みフレーム数 (rev)。まだ 1 枚も無ければ 0。</summary>
    public long Rev => Interlocked.Read(ref _rev);

    /// <summary>tight RGBA (長さ <c>w*h*4</c>) を最新フレームとして発行する。ゲーム main スレッドから呼ぶ。
    /// バッファはサイズ変化時のみ再確保するので、定常状態では割り当てゼロ。</summary>
    public void Publish(int w, int h, ReadOnlySpan<byte> rgba)
    {
        int len = w * h * 4;
        if (w <= 0 || h <= 0 || rgba.Length < len) return;
        lock (_writeLock)
        {
            long newRev = Interlocked.Read(ref _rev) + 1;
            int slot = (int)(newRev % _slots);

            // seqlock: 書き込み開始で世代を奇数に (読み手はこの間 retry)
            long g = _gen[slot];
            Volatile.Write(ref _gen[slot], g + 1);

            byte[]? dst = _buf[slot];
            if (dst is null || dst.Length < len) { dst = new byte[len]; _buf[slot] = dst; }
            rgba.Slice(0, len).CopyTo(dst);
            _w[slot] = w; _h[slot] = h; _slotRev[slot] = newRev;

            Volatile.Write(ref _gen[slot], g + 2);   // 書き込み完了で偶数へ
            Volatile.Write(ref _cur, slot);
            Interlocked.Exchange(ref _rev, newRev);
        }
    }

    /// <summary>最新フレームを HTTP body (8B ヘッダ w,h LE + tight RGBA) として返す。
    /// <paramref name="sinceRev"/> と同一 rev なら <c>(null, rev)</c> (= 304 相当)。まだ無ければ <c>(null, 0)</c>。
    /// 返す配列は呼び出し側専有 (書き手のリングとは独立) — 非同期送信/アップロードに渡して安全。</summary>
    public (byte[]? body, long rev) Read(long? sinceRev)
    {
        long rev = Interlocked.Read(ref _rev);
        if (rev == 0) return (null, 0);
        if (sinceRev is long s && s == rev) return (null, rev);

        // seqlock 読み: 世代が偶数のまま前後一致すれば整合フレーム。数回 retry して諦めたら null。
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int slot = Volatile.Read(ref _cur);
            if (slot < 0) return (null, rev);
            long g1 = Volatile.Read(ref _gen[slot]);
            if ((g1 & 1) != 0) continue;                 // 書き込み中 → retry

            int w = _w[slot], h = _h[slot];
            long slotRev = _slotRev[slot];
            byte[]? src = _buf[slot];
            int len = w * h * 4;
            if (src is null || w <= 0 || h <= 0 || src.Length < len) continue;

            var body = new byte[8 + len];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0), w);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), h);
            src.AsSpan(0, len).CopyTo(body.AsSpan(8));

            long g2 = Volatile.Read(ref _gen[slot]);
            if (g1 == g2) return (body, slotRev);        // 破れなし → 確定
            // torn: 書き手が同スロットを踏んだ (稀) → retry
        }
        return (null, rev);
    }
}
