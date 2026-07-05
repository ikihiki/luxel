using Luxel.UI;

namespace Luxel.Audio;

/// <summary>
/// ミキサーの階層ノード。Master / Music / SFX / Voice のような 音量グループ。
/// 親 bus を持つと、実効音量は自身 × 親のチェーンで計算される。UI の master slider を Signal で bind すれば
/// 全 sound source に同時に効く。
///
/// XAudio2 の SubmixVoice を使わず、AudioSource / AudioSource3D が Tick 時に <see cref="EffectiveVolume"/>
/// を掛け算するシンプル実装。
/// </summary>
public sealed class AudioBus
{
    public string Name { get; }
    public Signal<float> Volume { get; } = new(1f);
    public AudioBus? Parent { get; }

    public AudioBus(string name, AudioBus? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    /// <summary>親 chain を辿って掛け算した実効音量。</summary>
    public float EffectiveVolume
    {
        get
        {
            float v = Volume.Peek();
            AudioBus? p = Parent;
            while (p != null) { v *= p.Volume.Peek(); p = p.Parent; }
            return v;
        }
    }
}
