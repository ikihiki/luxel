using Luxel.Audio;

namespace LuxelRange.Core;

/// <summary>
/// SE / BGM を CPU 合成する (外部アセット不要 = publish が軽く決定的)。全て 16-bit mono PCM。
/// </summary>
public static class RangeSfxBank
{
    private static readonly AudioFormat Fmt = AudioFormat.Pcm16Mono44k;

    public static Dictionary<RangeSfx, AudioClip> Build() => new()
    {
        [RangeSfx.Fire]   = Tone(300f, 0.08f, 0.26f, glideTo: 150f),    // 発射 (下降)
        [RangeSfx.Hit]    = Tone(720f, 0.08f, 0.30f, glideTo: 1000f),   // 命中ブリップ
        [RangeSfx.FoxHit] = Tone(500f, 0.16f, 0.30f, glideTo: 780f),    // 動く的
        [RangeSfx.Bonus]  = Tone(880f, 0.32f, 0.30f, glideTo: 1320f),   // ボーナス
    };

    /// <summary>ループ BGM (低音サイン、継ぎ目クリック無し = 整数周期)。</summary>
    public static AudioClip BuildBgm()
    {
        const float seconds = 2f;
        int n = (int)(Fmt.SampleRate * seconds);
        byte[] pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Fmt.SampleRate;
            float v = MathF.Sin(MathF.Tau * 98f * t) * 0.6f + MathF.Sin(MathF.Tau * 147f * t) * 0.4f;
            Write(pcm, i, (short)(v * short.MaxValue * 0.16f));
        }
        return new AudioClip(Fmt, pcm, "range-bgm");
    }

    private static AudioClip Tone(float freq, float seconds, float amp, float glideTo = 0f)
    {
        int n = (int)(Fmt.SampleRate * seconds);
        byte[] pcm = new byte[n * 2];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            float f = glideTo > 0f ? freq + (glideTo - freq) * u : freq;
            phase += MathF.Tau * f / Fmt.SampleRate;
            float env = MathF.Min(1f, i / (Fmt.SampleRate * 0.005f)) * (1f - u);
            Write(pcm, i, (short)(MathF.Sin(phase) * env * amp * short.MaxValue));
        }
        return new AudioClip(Fmt, pcm, $"sfx{freq:0}");
    }

    private static void Write(byte[] pcm, int sample, short s)
    {
        pcm[sample * 2] = (byte)s;
        pcm[sample * 2 + 1] = (byte)(s >> 8);
    }
}

/// <summary>
/// ゲームのオーディオ配線: ループ BGM (<see cref="AudioSource"/>) + イベント発火の SE
/// (<see cref="AudioMixer"/> のワンショット、<see cref="RangeSfxDetector"/> でイベント→cue)。
/// バックエンドは exe が XAudio2、ヘッドレスは Null。capstone ① の Audio 層を 2 ゲーム目でも再利用 (10)。
/// </summary>
public sealed class RangeAudio : IDisposable
{
    private readonly AudioMixer _sfx;
    private readonly AudioSource _bgm;
    private readonly Dictionary<RangeSfx, AudioClip> _clips = RangeSfxBank.Build();
    private readonly List<RangeSfx> _scratch = new();

    public AudioBus Master { get; }
    public AudioBus Music { get; }
    public AudioBus Sfx { get; }

    public RangeAudio(IAudioBackend backend, AudioMixer mixer)
    {
        Master = new AudioBus("Master");
        Music = new AudioBus("Music", Master);
        Sfx = new AudioBus("Sfx", Master);
        _sfx = mixer;
        _sfx.Bus = Sfx;
        _bgm = new AudioSource(backend, RangeSfxBank.BuildBgm()) { Bus = Music };
        _bgm.Volume.Value = 0.6f;
    }

    /// <summary>設定の音量をバスに反映 (呼んだ時点の値。継続追従は <c>Tick</c>)。</summary>
    public void BindSettings(RangeSettings s)
    {
        Master.Volume.Value = s.MasterVolume.Value;
        Music.Volume.Value = s.MusicVolume.Value;
        Sfx.Volume.Value = s.SfxVolume.Value;
    }

    public void PlayBgm() => _bgm.Play(loop: true);
    public void StopBgm() => _bgm.Stop();

    /// <summary>このフレームのイベントを SE として鳴らす (毎固定更新)。</summary>
    public void React(IReadOnlyList<RangeEvent> events)
    {
        _scratch.Clear();
        RangeSfxDetector.Detect(events, _scratch);
        foreach (RangeSfx cue in _scratch)
            if (_clips.TryGetValue(cue, out AudioClip? clip))
                _sfx.PlayOneShot(clip);
    }

    public void Dispose() => _bgm.Dispose();
}
