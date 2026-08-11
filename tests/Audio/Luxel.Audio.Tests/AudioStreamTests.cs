using System.IO;
using Luxel.Audio;

namespace Luxel.Tests;

/// <summary>
/// Audio ストリーミング (タスク 10) の実デバイス不要・決定的テスト:
/// WAV デコード (16bit/float32・チャンク境界跨ぎ・終端) / LoopingStream の巻き戻し /
/// StreamingVoice のポンプ (キュー深さ維持・終端で停止) / headless で例外なし。
/// </summary>
public class AudioStreamTests
{
    private static byte[] MakeWav(int rate, int channels, short[] interleaved)
    {
        int dataLen = interleaved.Length * 2;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(dataLen);
        foreach (short s in interleaved) w.Write(s);
        return ms.ToArray();
    }

    private static byte[] MakeWavFloat(int rate, int channels, float[] interleaved)
    {
        int dataLen = interleaved.Length * 4;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 4); w.Write((short)(channels * 4)); w.Write((short)32);
        w.Write("data"u8); w.Write(dataLen);
        foreach (float f in interleaved) w.Write(f);
        return ms.ToArray();
    }

    // ---- WavStream ----

    [Fact]
    public void Wav_ParsesFormat()
    {
        using var s = new WavStream(new MemoryStream(MakeWav(22050, 2, new short[8])));
        Assert.Equal(22050, s.SampleRate);
        Assert.Equal(2, s.Channels);
    }

    [Fact]
    public void Wav_DecodesInt16ToFloat()
    {
        short[] src = [0, 16384, -16384, 32767, -32768, 8192];
        using var s = new WavStream(new MemoryStream(MakeWav(8000, 1, src)));
        var dst = new float[src.Length];
        int n = s.Read(dst);
        Assert.Equal(src.Length, n);
        Assert.Equal(0f, dst[0], 4);
        Assert.Equal(0.5f, dst[1], 4);
        Assert.Equal(-0.5f, dst[2], 4);
        Assert.Equal(1f, dst[3], 3);
        Assert.Equal(-1f, dst[4], 4);
    }

    [Fact]
    public void Wav_ReadAcrossChunkBoundaries_MatchesWhole()
    {
        short[] src = new short[100];
        for (int i = 0; i < src.Length; i++) src[i] = (short)(i * 300 - 15000);
        byte[] wav = MakeWav(8000, 1, src);

        var whole = new float[src.Length];
        using (var s1 = new WavStream(new MemoryStream(wav))) Assert.Equal(src.Length, s1.Read(whole));

        // 3 サンプルずつ細切れに読んでも同じ内容
        using var s2 = new WavStream(new MemoryStream(wav));
        var acc = new List<float>();
        var small = new float[3];
        int r;
        while ((r = s2.Read(small)) > 0)
            for (int i = 0; i < r; i++) acc.Add(small[i]);
        Assert.Equal(whole.Length, acc.Count);
        for (int i = 0; i < whole.Length; i++) Assert.Equal(whole[i], acc[i], 5);
    }

    [Fact]
    public void Wav_ReturnsZeroAtEnd()
    {
        using var s = new WavStream(new MemoryStream(MakeWav(8000, 1, new short[4])));
        var dst = new float[4];
        Assert.Equal(4, s.Read(dst));
        Assert.Equal(0, s.Read(dst));   // 終端
    }

    [Fact]
    public void Wav_Float32()
    {
        float[] src = [0.25f, -0.75f, 1f, -1f];
        using var s = new WavStream(new MemoryStream(MakeWavFloat(48000, 2, src)));
        Assert.Equal(48000, s.SampleRate);
        Assert.Equal(2, s.Channels);
        var dst = new float[4];
        Assert.Equal(4, s.Read(dst));
        Assert.Equal(0.25f, dst[0], 5);
        Assert.Equal(-0.75f, dst[1], 5);
    }

    [Fact]
    public void Wav_RejectsBadHeader()
        => Assert.Throws<FormatException>(() => new WavStream(new MemoryStream(new byte[12])));

    // ---- LoopingStream ----

    [Fact]
    public void Looping_WrapsSeamlessly()
    {
        short[] src = [3277, 6554, 9830, 13107];   // ≈ 0.1,0.2,0.3,0.4
        var loop = new LoopingStream(new WavStream(new MemoryStream(MakeWav(8000, 1, src))));
        var dst = new float[10];
        int n = loop.Read(dst);
        Assert.Equal(10, n);   // ループなので満杯まで埋まる
        // パターンが 4 周期で繰り返す
        for (int i = 0; i < 10; i++)
            Assert.Equal(src[i % 4] / 32768f, dst[i], 4);
    }

    // ---- StreamingVoice pump ----

    private sealed class DrainVoice : IAudioVoice
    {
        public int BuffersQueued { get; private set; }
        public bool IsPlaying { get; private set; }
        public int Submits { get; private set; }
        public float Volume { get; set; } = 1f;
        public float Pitch { get; set; } = 1f;
        public float Pan { get; set; }
        public void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false) { BuffersQueued++; Submits++; }
        public void Play() => IsPlaying = true;
        public void Stop() { IsPlaying = false; BuffersQueued = 0; }
        public void Pause() => IsPlaying = false;
        public void Drain(int n) => BuffersQueued = Math.Max(0, BuffersQueued - n);
        public void Dispose() { }
    }

    private sealed class DrainBackend : IAudioBackend
    {
        public DrainVoice Voice = null!;
        public float MasterVolume { get; set; } = 1f;
        public void Initialize() { }
        public IAudioVoice CreateVoice(AudioFormat format) => Voice = new DrainVoice();
        public void Dispose() { }
    }

    [Fact]
    public void Pump_FillsToQueueDepth_AndStarts()
    {
        var stream = new WavStream(new MemoryStream(MakeWav(8000, 1, new short[8000])));   // 1s
        var backend = new DrainBackend();
        using var sv = new StreamingVoice(backend, stream, chunkSeconds: 0.1f, queueDepth: 3);

        sv.Pump();
        Assert.Equal(3, backend.Voice.BuffersQueued);   // 深さまで補充
        Assert.True(sv.IsPlaying);

        backend.Voice.Drain(2);                          // 2 チャンク再生完了を模擬
        sv.Pump();
        Assert.Equal(3, backend.Voice.BuffersQueued);   // 再補充
    }

    [Fact]
    public void Pump_StopsAtStreamEnd()
    {
        var stream = new WavStream(new MemoryStream(MakeWav(8000, 1, new short[2400])));   // 0.3s = 3 チャンク
        var backend = new DrainBackend();
        using var sv = new StreamingVoice(backend, stream, chunkSeconds: 0.1f, queueDepth: 3);

        int guard = 0;
        while (!sv.Finished && guard++ < 1000)
        {
            sv.Pump();
            backend.Voice.Drain(3);   // 全部再生完了させる
        }
        Assert.True(sv.Finished);
        Assert.Equal(0, backend.Voice.BuffersQueued);
    }

    [Fact]
    public void Pump_Headless_DoesNotThrow()
    {
        var stream = new WavStream(new MemoryStream(MakeWav(8000, 1, new short[1600])));
        using var backend = new NullAudioBackend();
        backend.Initialize();
        using var sv = new StreamingVoice(backend, stream);
        sv.Pump();   // NullAudioBackend 経路で例外を吐かない
        Assert.True(sv.BuffersQueued > 0);
    }
}
