using Luxel.Audio;
using Luxel.Audio.Sequencing;
using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel 実行系 (Phase 4-5): シーケンシング層のイベント→PCM とスケジューラ。</summary>
public class StrudelAudioTests
{
    /// <summary>単位インパルス音色 — サンプル精度の配置検証用。</summary>
    private sealed class Impulse : IInstrument
    {
        public float[] Render(in ControlMap c, double duration, int rate) => [1f, 0f];
    }

    private static (StreamMixerSink Mixer, NullAudioBackend Backend) NewMixer(int rate = 1000)
    {
        var bank = new InstrumentBank();
        bank.Register("imp", new Impulse());
        var backend = new NullAudioBackend();
        var mixer = new StreamMixerSink(bank, backend, rate) { KeepLastChunk = true };
        return (mixer, backend);
    }

    [Fact]
    public void Mixer_PlacesEventAtExactSample()
    {
        (StreamMixerSink mixer, _) = NewMixer();   // rate=1000 → チャンク 0.1s = 100 サンプル
        var ev = new ScheduledEvent(0.042, 0.1, new ControlMap(Instrument: "imp"));
        mixer.Schedule([ev], 0.0, 0.1);
        float[] chunk = mixer.LastChunk!;
        Assert.Equal(200, chunk.Length);               // 100 サンプル × 2ch
        Assert.True(chunk[42 * 2] > 0.5f);             // サンプル 42 に頭
        Assert.Equal(0f, chunk[41 * 2]);               // 直前は無音
    }

    [Fact]
    public void Mixer_GainAndPan()
    {
        (StreamMixerSink mixer, _) = NewMixer();
        var right = new ScheduledEvent(0.0, 0.1, new ControlMap(Instrument: "imp", Gain: 0.5f, Pan: 1f));
        mixer.Schedule([right], 0.0, 0.1);
        float[] c = mixer.LastChunk!;
        Assert.Equal(0f, c[0], 3);                     // L ≈ 0 (完全右)
        Assert.InRange(c[1], 0.4f, 0.6f);              // R ≈ 0.5
    }

    [Fact]
    public void Mixer_EventSpansChunkBoundary()
    {
        var bank = new InstrumentBank();
        bank.Register("long", new LongTone());
        var mixer = new StreamMixerSink(bank, new NullAudioBackend(), 1000) { KeepLastChunk = true };
        // 0.05s から 0.3s 鳴る音 — 2 チャンク目にも続きが出る
        mixer.Schedule([new ScheduledEvent(0.05, 0.3, new ControlMap(Instrument: "long"))], 0.0, 0.1);
        mixer.Schedule([], 0.1, 0.2);
        float[] second = mixer.LastChunk!;
        Assert.True(second[0] != 0f);                  // 持ち越して鳴っている
    }

    private sealed class LongTone : IInstrument
    {
        public float[] Render(in ControlMap c, double duration, int rate)
        {
            var w = new float[(int)(rate * 0.3)];
            Array.Fill(w, 0.4f);
            return w;
        }
    }

    [Fact]
    public void Mixer_SoftClip_Bounded()
    {
        (StreamMixerSink mixer, _) = NewMixer();
        var evs = Enumerable.Range(0, 8)
            .Select(_ => new ScheduledEvent(0.0, 0.1, new ControlMap(Instrument: "imp", Gain: 3f)))
            .ToArray();
        mixer.Schedule(evs, 0.0, 0.1);
        Assert.All(mixer.LastChunk!, s => Assert.InRange(s, -24f, 24f));   // mix 生値 (クリップ前) — 検証は PCM 側
    }

    [Fact]
    public void Delay_ProducesDecayingEchoes()
    {
        (StreamMixerSink mixer, _) = NewMixer();   // rate=1000
        // 全部左チャンネルへ (pan=-1 → GainL=1, GainR=0)、0.25s (=250 サンプル) ディレイ・帰還 0.5・ウェット 1.0
        var c = new ControlMap(Instrument: "imp", Gain: 1f, Pan: -1f,
            DelayTime: 0.25f, DelayFeedback: 0.5f, DelayMix: 1f);
        mixer.Schedule([new ScheduledEvent(0.0, 0.1, c)], 0.0, 1.0);   // 1 チャンク = 1000 サンプル
        float[] ch = mixer.LastChunk!;
        Assert.True(ch[0] > 0.9f);                       // ドライ (頭)
        Assert.True(ch[250 * 2] > 0.9f);                 // 1 反射目 (0.25s)
        Assert.InRange(ch[500 * 2], 0.4f, 0.6f);         // 2 反射目 (帰還 0.5)
        Assert.InRange(ch[750 * 2], 0.2f, 0.3f);         // 3 反射目 (0.5^2)
        Assert.Equal(0f, ch[125 * 2]);                   // 反射の隙間は無音
    }

    /// <summary>固定周波数の正弦波音色 (LPF の減衰検証用)。</summary>
    private sealed class SineTone(double freq) : IInstrument
    {
        public float[] Render(in ControlMap c, double duration, int rate)
        {
            var w = new float[(int)(rate * duration)];
            for (int i = 0; i < w.Length; i++)
                w[i] = MathF.Sin((float)(2 * Math.PI * freq * i / rate));
            return w;
        }
    }

    [Fact]
    public void Lowpass_AttenuatesHighFrequency()
    {
        static float Peak(bool filtered)
        {
            var bank = new InstrumentBank();
            bank.Register("hi", new SineTone(5000));   // カットオフの 10 倍
            var mixer = new StreamMixerSink(bank, new NullAudioBackend(), 44100) { KeepLastChunk = true };
            var c = filtered
                ? new ControlMap(Instrument: "hi", Cutoff: 500f)
                : new ControlMap(Instrument: "hi");
            mixer.Schedule([new ScheduledEvent(0.0, 0.1, c)], 0.0, 0.1);
            float[] mix = mixer.LastChunk!;
            float p = 0;
            for (int i = 2000; i < mix.Length / 2; i++)   // ウォームアップを飛ばす
                p = MathF.Max(p, MathF.Abs(mix[i * 2]));
            return p;
        }
        float filtered = Peak(true), raw = Peak(false);
        Assert.True(raw > 0.5f, $"素の 5kHz は大振幅のはず: {raw}");
        Assert.True(filtered < 0.1f, $"500Hz LPF で 5kHz は大幅減衰のはず: {filtered}");
    }

    [Fact]
    public void Eval_LpfAndDelay_SetControls()
    {
        var haps = StrudelEval.Evaluate("""s("bd").lpf(800).delay(0.25).delayfb(0.6)""").Pattern!.QueryCycles();
        Assert.Equal(800f, haps[0].Value.Cutoff);
        Assert.Equal(0.25f, haps[0].Value.DelayTime);
        Assert.Equal(0.6f, haps[0].Value.DelayFeedback);
    }

    /// <summary>16bit PCM の RIFF/WAVE バイト列をメモリ上で組む (実ファイルに依存しない)。</summary>
    private static byte[] MakeWav16(float[] interleaved, int rate, int channels)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = interleaved.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((ushort)(channels * 2)); w.Write((ushort)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (float s in interleaved)
            w.Write((short)Math.Clamp((int)MathF.Round(s * 32767f), short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Sample_FromWav_DecodesAndPlaysAtPosition()
    {
        var mono = new float[16];
        mono[5] = 1f;                                  // フレーム 5 にスパイク
        byte[] wav = MakeWav16(mono, 1000, 1);
        var bank = new InstrumentBank();
        bank.Register("smp", SampleInstrument.FromWav(new MemoryStream(wav)));
        var mixer = new StreamMixerSink(bank, new NullAudioBackend(), 1000) { KeepLastChunk = true };
        mixer.Schedule([new ScheduledEvent(0.0, 0.1, new ControlMap(Instrument: "smp", Pan: -1f))], 0.0, 0.1);
        float[] ch = mixer.LastChunk!;
        Assert.True(ch[5 * 2] > 0.9f);                 // 素材レート == 出力レート → 1:1、フレーム 5 に頭
        Assert.Equal(0f, ch[4 * 2]);
    }

    [Fact]
    public void Sample_PitchUpByOctave_HalvesLength()
    {
        var inst = new SampleInstrument(new float[100], 1000, baseNote: 60f);
        float[] natural = inst.Render(new ControlMap(Note: 60f), 0, 1000);   // 原音 (base と同じ)
        float[] octaveUp = inst.Render(new ControlMap(Note: 72f), 0, 1000);  // 1 オクターブ上 = 2 倍速
        Assert.InRange(natural.Length, 98, 100);
        Assert.InRange(octaveUp.Length, 49, 51);
    }

    [Fact]
    public void Sample_DownmixesStereoToMono()
    {
        // L=1, R=0 の定常ステレオ → モノは 0.5
        var stereo = new float[8 * 2];
        for (int i = 0; i < 8; i++) { stereo[i * 2] = 1f; stereo[(i * 2) + 1] = 0f; }
        var inst = SampleInstrument.FromWav(new MemoryStream(MakeWav16(stereo, 1000, 2)));
        float[] w = inst.Render(new ControlMap(), 0, 1000);
        Assert.InRange(w[0], 0.49f, 0.51f);
        Assert.InRange(w[3], 0.49f, 0.51f);
    }

    [Fact]
    public void Sample_ResamplesToOutputRate()
    {
        // 素材 500Hz を 1000Hz へ → 2 倍長に伸びる
        var inst = new SampleInstrument(new float[50], 500);
        float[] w = inst.Render(new ControlMap(), 0, 1000);
        Assert.InRange(w.Length, 98, 100);
    }

    [Fact]
    public void Scheduler_FiresOnsetsOnce_AcrossWindows()
    {
        var sched = new StrudelScheduler(chunkSeconds: 0.1) { Cps = 1.0 };   // 1 サイクル = 1 秒
        var sink = new CollectSink();
        sched.AddSink(sink);
        sched.SetPattern(1, StrudelEval.Evaluate("""s("bd sd hh cp")""").Pattern);
        for (int i = 0; i < 10; i++) sched.RenderWindow();   // ちょうど 1 サイクル

        Assert.Equal(4, sink.Events.Count);
        Assert.Equal([0.0, 0.25, 0.5, 0.75], sink.Events.Select(e => e.Time));
        Assert.Equal("bd", sink.Events[0].Controls.Instrument);
        Assert.Equal("cp", sink.Events[3].Controls.Instrument);
    }

    [Fact]
    public void Scheduler_HotSwap_TakesEffectNextWindow()
    {
        var sched = new StrudelScheduler(0.1) { Cps = 1.0 };
        var sink = new CollectSink();
        sched.AddSink(sink);
        sched.SetPattern(1, StrudelEval.Evaluate("""s("bd*4")""").Pattern);
        for (int i = 0; i < 5; i++) sched.RenderWindow();    // 前半 0.5s
        sched.SetPattern(1, StrudelEval.Evaluate("""s("hh*4")""").Pattern);
        for (int i = 0; i < 5; i++) sched.RenderWindow();    // 後半

        Assert.Equal(["bd", "bd", "hh", "hh"],
            sink.Events.Select(e => e.Controls.Instrument!).ToArray());
    }

    [Fact]
    public void Scheduler_Hush_StopsAndPropagates()
    {
        var sched = new StrudelScheduler(0.1) { Cps = 1.0 };
        var sink = new CollectSink();
        sched.AddSink(sink);
        sched.SetPattern(1, StrudelEval.Evaluate("""s("bd*4")""").Pattern);
        sched.RenderWindow();
        sched.Hush();
        for (int i = 0; i < 9; i++) sched.RenderWindow();
        Assert.Single(sink.Events);
        Assert.True(sink.Hushed);
    }

    [Fact]
    public void Scheduler_CpsChangesTempo()
    {
        var sched = new StrudelScheduler(0.1) { Cps = 2.0 };   // 1 サイクル = 0.5 秒
        var sink = new CollectSink();
        sched.AddSink(sink);
        sched.SetPattern(1, StrudelEval.Evaluate("""s("bd")""").Pattern);
        for (int i = 0; i < 10; i++) sched.RenderWindow();     // 1 秒 = 2 サイクル
        Assert.Equal([0.0, 0.5], sink.Events.Select(e => e.Time));
    }

    private sealed class CollectSink : IEventSink
    {
        public readonly List<ScheduledEvent> Events = new();
        public bool Hushed;
        public void Schedule(ReadOnlySpan<ScheduledEvent> events, double ws, double we) => Events.AddRange(events);
        public void Hush() => Hushed = true;
    }

    [Fact]
    public void Kit_IsDeterministic()
    {
        InstrumentBank a = StrudelKit.CreateBank();
        InstrumentBank b = StrudelKit.CreateBank();
        var c = new ControlMap(Instrument: "sd", N: 1);
        Assert.Equal(
            a.Resolve("sd")!.Render(c, 0.25, 44100),
            b.Resolve("sd")!.Render(c, 0.25, 44100));
    }

    [Fact]
    public void Kit_SynthRendersNote()
    {
        InstrumentBank bank = StrudelKit.CreateBank();
        float[] w = bank.Resolve("saw")!.Render(new ControlMap(Note: 69f), 0.25, 44100);
        Assert.True(w.Length > 44100 / 8);
        Assert.Contains(w, s => MathF.Abs(s) > 0.1f);
    }

    [Fact]
    public void Kit_FallbackPlaysNoteOnlyEvents()
    {
        InstrumentBank bank = StrudelKit.CreateBank();
        Assert.NotNull(bank.Resolve(null));            // note("c3") だけの行も鳴る
        Assert.NotNull(bank.Resolve("unknown-name"));  // 未知名は fallback
    }

    [Fact]
    public void EndToEnd_PatternToPcm()
    {
        // s("bd") 1 サイクル → ミキサ 10 チャンク: 先頭チャンクの頭に bd の波形が出る
        var sched = new StrudelScheduler(0.1) { Cps = 1.0 };
        var mixer = new StreamMixerSink(StrudelKit.CreateBank(), new NullAudioBackend(), 4410)
        { KeepLastChunk = true };
        sched.AddSink(mixer);
        sched.SetPattern(1, StrudelEval.Evaluate("""s("bd").gain(1)""").Pattern);
        sched.RenderWindow();
        float[] first = mixer.LastChunk!;
        Assert.Contains(first, s => MathF.Abs(s) > 0.05f);
        // 決定性: 同じ手順は同じ PCM
        var sched2 = new StrudelScheduler(0.1) { Cps = 1.0 };
        var mixer2 = new StreamMixerSink(StrudelKit.CreateBank(), new NullAudioBackend(), 4410)
        { KeepLastChunk = true };
        sched2.AddSink(mixer2);
        sched2.SetPattern(1, StrudelEval.Evaluate("""s("bd").gain(1)""").Pattern);
        sched2.RenderWindow();
        Assert.Equal(first, mixer2.LastChunk!);
    }
}
