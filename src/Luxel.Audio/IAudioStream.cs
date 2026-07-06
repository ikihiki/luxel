namespace Luxel.Audio;

/// <summary>
/// ファイル等から**逐次デコード**して PCM を供給する音源 (BGM のような長尺用 — 全展開しない)。
/// 出力はインターリーブ float サンプル ([-1,1])。全展開クリップ (<see cref="AudioClip"/>) と対をなす。
/// </summary>
public interface IAudioStream : IDisposable
{
    /// <summary>ソースのサンプルレート (Hz)。</summary>
    int SampleRate { get; }
    /// <summary>チャンネル数 (1=モノ, 2=ステレオ)。</summary>
    int Channels { get; }

    /// <summary><paramref name="dst"/> をインターリーブ float サンプルで埋める。書き込んだ float 数を返す
    /// (0 = 終端)。<paramref name="dst"/> の長さは <see cref="Channels"/> の倍数が望ましい。</summary>
    int Read(Span<float> dst);

    /// <summary>先頭へ巻き戻す (ループ/再生し直し用)。</summary>
    void Reset();
}

/// <summary>内側のストリームを終端で巻き戻して繋ぐループ再生ラッパ。
/// <see cref="Read"/> は (内側が空でない限り) 常に <paramref name="dst"/> を満たす — 継ぎ目のない BGM ループ。</summary>
public sealed class LoopingStream(IAudioStream inner) : IAudioStream
{
    public int SampleRate => inner.SampleRate;
    public int Channels => inner.Channels;

    public int Read(Span<float> dst)
    {
        int total = 0;
        int emptyResets = 0;
        while (total < dst.Length)
        {
            int n = inner.Read(dst[total..]);
            if (n <= 0)
            {
                inner.Reset();
                if (++emptyResets > 1) break;   // 巻き戻しても空 = 真に空 → 無限ループ回避
                continue;
            }
            emptyResets = 0;
            total += n;
        }
        return total;
    }

    public void Reset() => inner.Reset();
    public void Dispose() => inner.Dispose();
}
