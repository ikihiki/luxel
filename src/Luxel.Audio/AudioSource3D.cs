using System.Numerics;
using Luxel.UI;

namespace Luxel.Audio;

/// <summary>
/// 3D 空間に配置される音源。<see cref="Position"/> を持ち、per-frame <see cref="Update"/> で
/// <see cref="AudioListener"/> との距離を計算して volume + pan を voice に流す。
///
/// 距離減衰は linear: distance ≤ MinDistance で 100%、≥ MaxDistance で 0%、間は線形。
/// pan は listener の Right ベクトルとの内積 (-1 = 完全左、+1 = 完全右)。
/// (Doppler shift は M7+ で追加検討。)
/// </summary>
public sealed class AudioSource3D : IDisposable
{
    private readonly IAudioVoice _voice;
    private readonly AudioClip _clip;
    private bool _submitted;
    private bool _disposed;

    public Vector3 Position { get; set; } = Vector3.Zero;
    public float MinDistance { get; set; } = 1f;
    public float MaxDistance { get; set; } = 20f;
    public Signal<float> Volume { get; } = new(1f);   // per-source base volume (attenuation 前)
    public Signal<float> Pitch { get; } = new(1f);
    public AudioBus? Bus { get; set; }

    /// <summary>直近の Update で計算した実効 volume (attenuation + bus 掛け算後)。デバッグ用。</summary>
    public float EffectiveVolume { get; private set; }
    /// <summary>直近の pan 値。</summary>
    public float EffectivePan { get; private set; }

    public bool IsPlaying => _voice.IsPlaying;

    public AudioSource3D(IAudioBackend backend, AudioClip clip)
    {
        _clip = clip;
        _voice = backend.CreateVoice(clip.Format);
    }

    public void Play(bool loop = false)
    {
        if (!_submitted)
        {
            _voice.SubmitBuffer(_clip.PcmData, loop);
            _submitted = true;
        }
        _voice.Play();
    }

    public void Stop()
    {
        _voice.Stop();
        _submitted = false;
    }

    /// <summary>per-frame 呼び出し ─ listener 相対で volume + pan を計算して voice に反映。</summary>
    public void Update(AudioListener listener)
    {
        Vector3 delta = Position - listener.Position;
        float dist = delta.Length();
        float attenuation = MinDistance >= MaxDistance ? (dist <= MinDistance ? 1f : 0f)
                          : Math.Clamp(1f - (dist - MinDistance) / (MaxDistance - MinDistance), 0f, 1f);
        float pan = 0f;
        if (dist > 0.001f)
            pan = Math.Clamp(Vector3.Dot(delta / dist, listener.Right), -1f, 1f);

        float bus = Bus?.EffectiveVolume ?? 1f;
        EffectiveVolume = Volume.Peek() * attenuation * bus;
        EffectivePan = pan;
        _voice.Volume = EffectiveVolume;
        _voice.Pitch = Pitch.Peek();
        _voice.Pan = pan;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _voice.Dispose();
    }
}
