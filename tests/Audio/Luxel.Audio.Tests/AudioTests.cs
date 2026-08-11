using Luxel.Audio;
using NAudio.Wave;

namespace Luxel.Tests;

public class AudioTests
{
    [Fact]
    public void AudioFormat_ByteCounts_AreCorrect()
    {
        var fmt = AudioFormat.Pcm16Stereo44k;
        Assert.Equal(4, fmt.BytesPerSample);       // 16-bit × 2ch
        Assert.Equal(44100 * 4, fmt.BytesPerSecond);
    }

    [Fact]
    public void AudioClip_Duration_MatchesPcmLength()
    {
        var fmt = AudioFormat.Pcm16Mono22k;
        // 1 秒ぶん (mono 16-bit 22050Hz → 44100 bytes)
        var pcm = new byte[fmt.BytesPerSecond];
        var clip = new AudioClip(fmt, pcm);
        Assert.Equal(TimeSpan.FromSeconds(1), clip.Duration);
        Assert.Equal(22050, clip.SampleCount);
    }

    [Fact]
    public void NullBackend_TracksPlayStopCalls()
    {
        using var backend = new NullAudioBackend();
        backend.Initialize();
        Assert.True(backend.Initialized);
        var voice = backend.CreateVoice(AudioFormat.Pcm16Stereo44k);
        voice.SubmitBuffer(new byte[100]);
        voice.Play();
        Assert.True(voice.IsPlaying);
        voice.Stop();
        Assert.False(voice.IsPlaying);
    }

    [Fact]
    public void AudioClipLoader_ReadsWav_Roundtrip()
    {
        // 440Hz サイン波 0.1秒 の WAV を NAudio で書き出し → Loader で読む
        var format = new WaveFormat(22050, 16, 1);
        int samples = 22050 / 10;   // 0.1s
        var pcm = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(short.MaxValue * 0.3f * MathF.Sin(2f * MathF.PI * 440f * i / 22050f));
            pcm[i * 2] = (byte)(s & 0xff);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), format))
            writer.Write(pcm, 0, pcm.Length);
        ms.Position = 0;

        var clip = AudioClipLoader.Load(ms, ".wav", name: "test");
        Assert.Equal(22050, clip.Format.SampleRate);
        Assert.Equal(1, clip.Format.Channels);
        Assert.Equal(16, clip.Format.BitsPerSample);
        Assert.Equal(pcm.Length, clip.PcmData.Length);
        Assert.Equal("test", clip.Name);
        // NAudio の ToSampleProvider().ToWaveProvider16() で 16-bit → float → 16-bit の丸めが入るため
        // byte 単位の一致ではなく sample 値の許容差で確認
        int maxDiff = 0;
        for (int i = 0; i < pcm.Length; i += 2)
        {
            short expected = (short)(pcm[i] | (pcm[i + 1] << 8));
            short actual = (short)(clip.PcmData[i] | (clip.PcmData[i + 1] << 8));
            maxDiff = Math.Max(maxDiff, Math.Abs(expected - actual));
        }
        Assert.True(maxDiff <= 2, $"max sample diff = {maxDiff} (許容 ±2)");
    }

    // WaveFileWriter が dispose 時に base stream も dispose するのを回避
    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;
        public IgnoreDisposeStream(Stream inner) { _inner = inner; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
