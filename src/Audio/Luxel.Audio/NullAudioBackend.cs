namespace Luxel.Audio;

/// <summary>
/// 何も再生しない実装。単体テスト / audio device 無し環境向け。
/// <see cref="IAudioVoice.Play"/>/<see cref="IAudioVoice.Stop"/> の状態遷移だけ管理し、実際の音声出力は行わない。
/// </summary>
public sealed class NullAudioBackend : IAudioBackend
{
    public float MasterVolume { get; set; } = 1.0f;
    public bool Initialized { get; private set; }
    private readonly List<NullVoice> _voices = new();

    public void Initialize() => Initialized = true;

    public IAudioVoice CreateVoice(AudioFormat format)
    {
        var v = new NullVoice(format);
        _voices.Add(v);
        return v;
    }

    public IReadOnlyList<IAudioVoice> Voices => _voices;

    public void Dispose()
    {
        foreach (var v in _voices) v.Dispose();
        _voices.Clear();
        Initialized = false;
    }

    private sealed class NullVoice : IAudioVoice
    {
        public AudioFormat Format { get; }
        public float Volume { get; set; } = 1f;
        public float Pitch { get; set; } = 1f;
        public float Pan { get; set; } = 0f;
        public bool IsPlaying { get; private set; }
        public int SubmittedBytes { get; private set; }
        public int PlayCount { get; private set; }
        public int StopCount { get; private set; }
        public bool Looping { get; private set; }
        public int BuffersQueued { get; internal set; }

        public NullVoice(AudioFormat format) { Format = format; }

        public void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false)
        {
            SubmittedBytes += pcm.Length;
            Looping = loop;
            BuffersQueued++;
        }
        public void Play() { IsPlaying = true; PlayCount++; }
        public void Stop() { IsPlaying = false; StopCount++; BuffersQueued = 0; }
        public void Pause() { IsPlaying = false; }
        public void Dispose() { IsPlaying = false; }
    }
}
