namespace Luxel.Audio.Sequencing;

/// <summary>
/// サンプル精度のソフトウェアシーケンサミキサ。<see cref="IEventSink"/> として受けたイベントを
/// **PCM チャンクに焼き込み**、1 本のストリーミング voice へ先行投入する。
/// <list type="bullet">
/// <item>voice を都度 Play() するとフレーム精度 (±16ms) のジッタが出る — PCM に焼けば 1 サンプル精度</item>
/// <item>イベント単位の gain/pan (等パワー)/speed (線形補間リサンプル) はここで適用</item>
/// <item>リリースがチャンク境界を跨ぐ音はアクティブボイスとして持ち越す</item>
/// <item>出力はソフトクリップ (tanh 近似) — stack の重ね録りで割れない</item>
/// </list>
/// スレッドは UI (Tick) 専有。窓は <see cref="IEventSink.Schedule"/> の呼び出し順に連続していること
/// (スケジューラが保証する)。テストでは <see cref="LastChunk"/> で出力 PCM を検証できる。
/// </summary>
public sealed class StreamMixerSink : IEventSink, IDisposable
{
    private readonly InstrumentBank _bank;
    private readonly IAudioVoice _voice;
    private readonly int _rate;

    private sealed class ActiveVoice
    {
        public required float[] Wave;      // モノラル素材
        public double Pos;                  // 素材内の再生位置 (サンプル、speed 分進む)
        public required double Step;       // 1 出力サンプルあたりの前進量 (= speed)
        public required float GainL, GainR;
        public long StartSample;            // 絶対サンプル位置 (この時点から鳴る)
    }

    private readonly List<ActiveVoice> _active = new();
    private long _rendered;                 // 焼き込み済みの絶対サンプル数 (= 次チャンクの先頭)
    private bool _playing;

    // 波形タップ (UI 表示用): 128 サンプルごとのピークをリングに書く
    private readonly float[] _peaks = new float[256];
    private int _peakCursor;
    private long _peakBlock;

    /// <summary>直近チャンクの出力 (テスト検証用 — <see cref="KeepLastChunk"/> が true のとき)。</summary>
    internal float[]? LastChunk { get; private set; }
    internal bool KeepLastChunk { get; set; }

    /// <summary>投入済みでまだ再生されていないチャンク数 (先読み制御用)。</summary>
    public int BuffersQueued => _voice.BuffersQueued;

    /// <summary>焼き込み済みの絶対時刻 (秒) — 次に Schedule される窓の先頭。</summary>
    public double RenderedSeconds => (double)_rendered / _rate;

    public StreamMixerSink(InstrumentBank bank, IAudioBackend backend, int sampleRate = 44100)
    {
        _bank = bank;
        _rate = sampleRate;
        _voice = backend.CreateVoice(new AudioFormat(sampleRate, 2, 16));
    }

    /// <summary>窓 [windowStart, windowEnd) を PCM に焼いて voice へ投入する。</summary>
    public void Schedule(ReadOnlySpan<ScheduledEvent> events, double windowStart, double windowEnd)
    {
        foreach (ScheduledEvent e in events)
        {
            IInstrument? inst = _bank.Resolve(e.Controls.Instrument);
            if (inst is null) continue;
            float[] wave = inst.Render(e.Controls, e.Duration, _rate);
            if (wave.Length == 0) continue;
            float gain = e.Controls.Gain ?? 1f;
            float pan = Math.Clamp(e.Controls.Pan ?? 0f, -1f, 1f);
            // 等パワーパン
            double a = (pan + 1) * (Math.PI / 4);
            _active.Add(new ActiveVoice
            {
                Wave = wave,
                Step = Math.Max(0.01f, e.Controls.Speed ?? 1f),
                GainL = gain * (float)Math.Cos(a),
                GainR = gain * (float)Math.Sin(a),
                StartSample = (long)Math.Round(e.Time * _rate),
            });
        }
        RenderChunk((int)Math.Round((windowEnd - windowStart) * _rate));
    }

    private void RenderChunk(int samples)
    {
        if (samples <= 0) return;
        var mix = new float[samples * 2];
        long chunkStart = _rendered;

        for (int vi = _active.Count - 1; vi >= 0; vi--)
        {
            ActiveVoice v = _active[vi];
            int from = (int)Math.Max(0, v.StartSample - chunkStart);
            for (int i = from; i < samples; i++)
            {
                if (chunkStart + i < v.StartSample) continue;
                int i0 = (int)v.Pos;
                if (i0 >= v.Wave.Length - 1) break;
                float frac = (float)(v.Pos - i0);
                float s = v.Wave[i0] + (v.Wave[i0 + 1] - v.Wave[i0]) * frac;   // 線形補間
                mix[i * 2] += s * v.GainL;
                mix[i * 2 + 1] += s * v.GainR;
                v.Pos += v.Step;
            }
            if (v.Pos >= v.Wave.Length - 1) _active.RemoveAt(vi);   // 鳴り終わった
        }

        // ソフトクリップ + 16bit 化 + ピークタップ
        var pcm = new byte[samples * 4];
        float blockPeak = 0;
        for (int i = 0; i < samples; i++)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                float s = mix[i * 2 + ch];
                s = MathF.Tanh(s);   // ソフトリミッタ
                short q = (short)MathF.Round(s * short.MaxValue);
                pcm[(i * 2 + ch) * 2] = (byte)q;
                pcm[(i * 2 + ch) * 2 + 1] = (byte)(q >> 8);
                blockPeak = MathF.Max(blockPeak, MathF.Abs(s));
            }
            if (++_peakBlock >= 128)
            {
                _peaks[_peakCursor] = blockPeak;
                _peakCursor = (_peakCursor + 1) % _peaks.Length;
                _peakBlock = 0;
                blockPeak = 0;
            }
        }

        if (KeepLastChunk) LastChunk = mix;
        _rendered += samples;
        _voice.SubmitBuffer(pcm);
        if (!_playing) { _voice.Play(); _playing = true; }
    }

    /// <summary>全停止 — 鳴っている/予定のイベントを破棄する。投入済みチャンク (先読み分,
    /// 最大数百 ms) は鳴り切る (XAudio2 のキューは取り消さない — v1 の割り切り)。</summary>
    public void Hush() => _active.Clear();

    /// <summary>UI 波形表示用: 直近ピーク列 (128 サンプル ≒ 3ms 毎) を古い順にコピーする。</summary>
    public void CopyPeaks(Span<float> dest)
    {
        int n = Math.Min(dest.Length, _peaks.Length);
        for (int i = 0; i < n; i++)
            dest[dest.Length - 1 - i] = _peaks[(_peakCursor - 1 - i + _peaks.Length * 2) % _peaks.Length];
    }

    public void Dispose()
    {
        _voice.Stop();
        _voice.Dispose();
    }
}
