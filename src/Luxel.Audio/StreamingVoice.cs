namespace Luxel.Audio;

/// <summary>
/// <see cref="IAudioStream"/> を**デコードしながら再生**する声 — BGM 等の長尺用。<see cref="StreamMixerSink"/> と
/// 同じ「毎 Tick <see cref="Pump"/> + キュー深さ &lt; <see cref="QueueDepth"/> まで補充」方式なので専用スレッド不要
/// (フレームレート依存のジッタはキュー深さで吸収)。チャンクは float→16bit へ量子化して submit。
/// ループは <see cref="LoopingStream"/> でラップして渡す (このクラスは終端で止まる)。
/// <para>駆動: フレームループ/AddAnimation から毎 Tick <see cref="Pump"/> を呼ぶ。</para>
/// </summary>
public sealed class StreamingVoice : IDisposable
{
    private readonly IAudioStream _stream;
    private readonly IAudioVoice _voice;
    private readonly float[] _floatChunk;   // chunkFrames * channels
    private readonly byte[][] _ring;         // (QueueDepth+1) 個の再利用 16bit バッファ
    private int _ringIndex;
    private bool _started;
    private bool _ended;
    private bool _disposed;

    /// <summary>先読みするチャンク数 (この数までキューを満たす)。</summary>
    public int QueueDepth { get; }

    public StreamingVoice(IAudioBackend backend, IAudioStream stream, float chunkSeconds = 0.1f, int queueDepth = 3)
    {
        if (queueDepth < 1) throw new ArgumentOutOfRangeException(nameof(queueDepth));
        _stream = stream;
        QueueDepth = queueDepth;
        _voice = backend.CreateVoice(new AudioFormat(stream.SampleRate, stream.Channels, 16));

        int chunkFrames = Math.Max(1, (int)(stream.SampleRate * chunkSeconds));
        int chunkSamples = chunkFrames * stream.Channels;
        _floatChunk = new float[chunkSamples];
        _ring = new byte[queueDepth + 1][];
        for (int i = 0; i < _ring.Length; i++) _ring[i] = new byte[chunkSamples * 2];
    }

    /// <summary>再生中 (開始済み・未 Dispose)。</summary>
    public bool IsPlaying => _voice.IsPlaying;

    /// <summary>ストリーム終端に達し、キューも空になった (再生完了)。</summary>
    public bool Finished => _ended && _voice.BuffersQueued == 0;

    /// <summary>キューに残っているバッファ数。</summary>
    public int BuffersQueued => _voice.BuffersQueued;

    /// <summary>毎 Tick 呼ぶ: キューが <see cref="QueueDepth"/> 未満なら、終端までチャンクをデコードして submit する。</summary>
    public void Pump()
    {
        if (_ended || _disposed) return;
        while (_voice.BuffersQueued < QueueDepth)
        {
            int floats = _stream.Read(_floatChunk);
            if (floats <= 0) { _ended = true; break; }

            byte[] buf = _ring[_ringIndex];
            _ringIndex = (_ringIndex + 1) % _ring.Length;
            int bytes = Quantize(_floatChunk.AsSpan(0, floats), buf);
            _voice.SubmitBuffer(buf.AsMemory(0, bytes));

            if (!_started) { _voice.Play(); _started = true; }
        }
    }

    /// <summary>再生を止めてキューを捨てる (Pump を止めれば以後補充されない)。</summary>
    public void Stop()
    {
        _voice.Stop();
        _ended = true;
    }

    /// <summary>先頭から再生し直す。</summary>
    public void Restart()
    {
        _voice.Stop();
        _stream.Reset();
        _ringIndex = 0;
        _started = false;
        _ended = false;
    }

    private static int Quantize(ReadOnlySpan<float> src, byte[] dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            float f = Math.Clamp(src[i], -1f, 1f);
            short q = (short)MathF.Round(f * 32767f);
            dst[i * 2] = (byte)q;
            dst[i * 2 + 1] = (byte)(q >> 8);
        }
        return src.Length * 2;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _voice.Stop();
        _voice.Dispose();
        _stream.Dispose();
    }
}
