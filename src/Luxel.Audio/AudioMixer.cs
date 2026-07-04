namespace Luxel.Audio;

/// <summary>
/// SFX 用のオンショット再生ミキサ。<see cref="PlayOneShot"/> で発火して忘れられる。
/// format ごとに voice pool を持ち、再生完了 (<see cref="IAudioVoice.BuffersQueued"/>=0) で
/// pool に返却して次の PlayOneShot で再利用する。
///
/// per-frame <see cref="Tick"/> で完了 voice を pool へ回収する。
/// </summary>
public sealed class AudioMixer : IDisposable
{
    private readonly IAudioBackend _backend;
    // format ごとの pool (idle voice)
    private readonly Dictionary<AudioFormat, Stack<IAudioVoice>> _pool = new();
    // 現在再生中の voice
    private readonly List<(IAudioVoice Voice, AudioFormat Format)> _active = new();
    private bool _disposed;

    public AudioBus? Bus { get; set; }

    public AudioMixer(IAudioBackend backend) { _backend = backend; }

    /// <summary>fire-and-forget で clip を 1 回鳴らす。 voice は pool から借用。<see cref="Bus"/> があれば掛け算適用。</summary>
    public void PlayOneShot(AudioClip clip, float volume = 1f, float pitch = 1f, float pan = 0f)
    {
        IAudioVoice voice = Borrow(clip.Format);
        float busVol = Bus?.EffectiveVolume ?? 1f;
        voice.Volume = volume * busVol;
        voice.Pitch = pitch;
        voice.Pan = pan;
        voice.SubmitBuffer(clip.PcmData);
        voice.Play();
        _active.Add((voice, clip.Format));
    }

    /// <summary>per-frame: 完了した voice を pool に戻す。</summary>
    public void Tick()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var (v, fmt) = _active[i];
            if (v.BuffersQueued == 0)
            {
                v.Stop();
                Return(v, fmt);
                _active.RemoveAt(i);
            }
        }
    }

    public int ActiveVoiceCount => _active.Count;

    private IAudioVoice Borrow(AudioFormat fmt)
    {
        if (_pool.TryGetValue(fmt, out var stack) && stack.Count > 0)
            return stack.Pop();
        return _backend.CreateVoice(fmt);
    }

    private void Return(IAudioVoice v, AudioFormat fmt)
    {
        if (!_pool.TryGetValue(fmt, out var stack))
            _pool[fmt] = stack = new Stack<IAudioVoice>();
        stack.Push(v);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (v, _) in _active) v.Dispose();
        foreach (var stack in _pool.Values)
            foreach (var v in stack) v.Dispose();
        _active.Clear();
        _pool.Clear();
    }
}
