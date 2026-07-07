using LuxelCavern.Core;
using Luxel.Audio;

namespace Luxel.Tests;

/// <summary>
/// <see cref="CavernAudio"/> の配線スモーク (<see cref="NullAudioBackend"/> で音デバイス非依存):
/// BGM ループ再生の投入 / イベントでワンショット SE が発火 / 音量バス階層。
/// </summary>
public class CavernAudioTests
{
    private static (CavernAudio audio, AudioMixer mixer, NullAudioBackend backend) Make()
    {
        var backend = new NullAudioBackend();
        backend.Initialize();
        var mixer = new AudioMixer(backend);
        return (new CavernAudio(backend, mixer), mixer, backend);
    }

    [Fact]
    public void PlayBgm_SubmitsLoopingBuffer()
    {
        var (audio, _, backend) = Make();
        audio.PlayBgm();
        // BGM voice が 1 本再生中 (loop) になる。
        Assert.Contains(backend.Voices, v => v.IsPlaying);
    }

    [Fact]
    public void React_OnCoin_FiresOneShot()
    {
        var (audio, mixer, _) = Make();
        var sim = CavernLevel.CreateSim();
        audio.ResetForNewGame();
        audio.React(sim);        // baseline (無音)
        Assert.Equal(0, mixer.ActiveVoiceCount);

        sim.Coins++;
        audio.React(sim);        // コイン SE
        Assert.Equal(1, mixer.ActiveVoiceCount);
    }

    [Fact]
    public void BusHierarchy_MusicAndSfxUnderMaster()
    {
        var (audio, _, _) = Make();
        Assert.Same(audio.Master, audio.Music.Parent);
        Assert.Same(audio.Master, audio.Sfx.Parent);

        audio.Master.Volume.Value = 0.5f;
        audio.Sfx.Volume.Value = 0.5f;
        Assert.Equal(0.25f, audio.Sfx.EffectiveVolume, 3);   // 0.5 × 0.5
    }
}
