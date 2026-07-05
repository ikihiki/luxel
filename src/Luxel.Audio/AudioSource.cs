using Luxel.UI;

namespace Luxel.Audio;

/// <summary>
/// 1 個の <see cref="AudioClip"/> を保持する再生ハンドル (BGM / 3D positional 音源向け)。
/// Volume/Pitch/Pan は <see cref="Signal{T}"/> で公開され、UI slider / Animation Transition と自然に bind できる。
/// <see cref="Tick"/> を per-frame 呼ぶことで Signal の変化を voice に反映する。
///
/// SFX 一発 (fire-and-forget) は <see cref="AudioMixer.PlayOneShot"/> を使う方が軽量。
/// </summary>
public sealed class AudioSource : IDisposable
{
    private readonly IAudioVoice _voice;
    private readonly AudioClip _clip;
    private bool _submitted;
    private bool _disposed;

    public Signal<float> Volume { get; } = new(1f);
    public Signal<float> Pitch { get; } = new(1f);
    public Signal<float> Pan { get; } = new(0f);
    public AudioBus? Bus { get; set; }

    public AudioClip Clip => _clip;
    public bool IsPlaying => _voice.IsPlaying;

    public AudioSource(IAudioBackend backend, AudioClip clip)
    {
        _clip = clip;
        _voice = backend.CreateVoice(clip.Format);
    }

    /// <summary>再生開始。<paramref name="loop"/> = true で backend の loop 再生を使う。</summary>
    public void Play(bool loop = false)
    {
        if (!_submitted)
        {
            _voice.SubmitBuffer(_clip.PcmData, loop);
            _submitted = true;
        }
        SyncFromSignals();
        _voice.Play();
    }

    public void Stop()
    {
        _voice.Stop();
        _submitted = false;
    }

    public void Pause() => _voice.Pause();

    /// <summary>Signal の値を voice に転写する。per-frame 呼び出し想定。</summary>
    public void Tick() => SyncFromSignals();

    private void SyncFromSignals()
    {
        float bus = Bus?.EffectiveVolume ?? 1f;
        _voice.Volume = Volume.Peek() * bus;
        _voice.Pitch = Pitch.Peek();
        _voice.Pan = Pan.Peek();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _voice.Dispose();
    }
}
