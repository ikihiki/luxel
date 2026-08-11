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

        // ボイス単位 biquad LPF (transposed direct form II — 状態は z1/z2 の 2 つ)
        public bool HasFilter;
        public float B0, B1, B2, A1, A2;
        public float Z1, Z2;

        // このボイスのウェット送り量 (0 = ディレイなし)
        public float DelayMix;
    }

    private readonly List<ActiveVoice> _active = new();
    private long _rendered;                 // 焼き込み済みの絶対サンプル数 (= 次チャンクの先頭)
    private bool _playing;

    // 全体バスのフィードバックディレイライン (循環バッファ、最大 2 秒)。
    // 送り量はイベント単位 (DelayMix)、長さ/帰還は直近の指定イベントが設定する (last-writer-wins)。
    private readonly float[] _delayL;
    private readonly float[] _delayR;
    private int _delayPos;
    private int _delaySamples;               // 現在のディレイ長 (サンプル、0 = 無効)
    private float _delayFeedback;

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
        int maxDelay = Math.Max(1, sampleRate * 2);   // 2 秒ぶんの循環バッファ
        _delayL = new float[maxDelay];
        _delayR = new float[maxDelay];
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
            var av = new ActiveVoice
            {
                Wave = wave,
                Step = Math.Max(0.01f, e.Controls.Speed ?? 1f),
                GainL = gain * (float)Math.Cos(a),
                GainR = gain * (float)Math.Sin(a),
                StartSample = (long)Math.Round(e.Time * _rate),
            };
            if (e.Controls.Cutoff is float cut)
                SetLowpass(av, cut, e.Controls.Resonance ?? 0.707f);
            if (e.Controls.DelayTime is float dt)
            {
                _delaySamples = Math.Clamp((int)Math.Round(dt * _rate), 0, _delayL.Length - 1);
                _delayFeedback = Math.Clamp(e.Controls.DelayFeedback ?? 0.5f, 0f, 0.99f);
                av.DelayMix = Math.Clamp(e.Controls.DelayMix ?? 0.5f, 0f, 1f);
            }
            _active.Add(av);
        }
        RenderChunk((int)Math.Round((windowEnd - windowStart) * _rate));
    }

    /// <summary>RBJ クックブックの LPF 係数を計算してボイスへ格納する。</summary>
    private void SetLowpass(ActiveVoice v, float cutoffHz, float q)
    {
        float f0 = Math.Clamp(cutoffHz, 20f, _rate * 0.49f);
        q = Math.Max(0.1f, q);
        float w0 = 2f * MathF.PI * f0 / _rate;
        float cosw = MathF.Cos(w0), sinw = MathF.Sin(w0);
        float alpha = sinw / (2f * q);
        float b0 = (1f - cosw) / 2f, b1 = 1f - cosw, b2 = (1f - cosw) / 2f;
        float a0 = 1f + alpha, a1 = -2f * cosw, a2 = 1f - alpha;
        v.HasFilter = true;
        v.B0 = b0 / a0; v.B1 = b1 / a0; v.B2 = b2 / a0;
        v.A1 = a1 / a0; v.A2 = a2 / a0;
        v.Z1 = 0f; v.Z2 = 0f;
    }

    private void RenderChunk(int samples)
    {
        if (samples <= 0) return;
        var mix = new float[samples * 2];
        long chunkStart = _rendered;
        // ディレイ送り (ドライ信号のウェット分をステレオで集める)
        float[]? sendL = null, sendR = null;

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
                if (v.HasFilter)   // transposed direct form II biquad
                {
                    float y = v.B0 * s + v.Z1;
                    v.Z1 = v.B1 * s - v.A1 * y + v.Z2;
                    v.Z2 = v.B2 * s - v.A2 * y;
                    s = y;
                }
                float l = s * v.GainL, r = s * v.GainR;
                mix[i * 2] += l;
                mix[i * 2 + 1] += r;
                if (v.DelayMix > 0f)
                {
                    (sendL ??= new float[samples])[i] += l * v.DelayMix;
                    (sendR ??= new float[samples])[i] += r * v.DelayMix;
                }
                v.Pos += v.Step;
            }
            if (v.Pos >= v.Wave.Length - 1) _active.RemoveAt(vi);   // 鳴り終わった
        }

        // 全体バスのフィードバックディレイ (送りが無くても既存のテールは鳴らし続ける)
        if (_delaySamples > 0)
        {
            int size = _delayL.Length;
            for (int i = 0; i < samples; i++)
            {
                int read = _delayPos - _delaySamples;
                if (read < 0) read += size;
                float dl = _delayL[read], dr = _delayR[read];
                mix[i * 2] += dl;
                mix[i * 2 + 1] += dr;
                _delayL[_delayPos] = (sendL?[i] ?? 0f) + dl * _delayFeedback;
                _delayR[_delayPos] = (sendR?[i] ?? 0f) + dr * _delayFeedback;
                if (++_delayPos >= size) _delayPos = 0;
            }
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
    public void Hush()
    {
        _active.Clear();
        Array.Clear(_delayL);
        Array.Clear(_delayR);
    }

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
