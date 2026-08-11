namespace Luxel.Audio.Sequencing;

/// <summary>
/// PCM サンプル (wav) を再生する <see cref="IInstrument"/>。素材をモノラル float 配列で保持し、
/// <see cref="Render"/> で出力レートへ**線形補間リサンプル**する (ミキサの Speed とは別に、素材レート差と
/// Note によるピッチをここで畳む)。素材は決定的なので出力 PCM もユニットテストで検証できる。
/// <list type="bullet">
/// <item>ステレオ以上の素材はチャンネル平均でモノラル化 (ミキサが等パワー pan を掛けるため)</item>
/// <item><see cref="BaseNote"/> を与えると Note で 2^((note-base)/12) のピッチ変更 (無指定は原音)</item>
/// <item>Speed はミキサ側で更にリサンプルされる (二段掛け)</item>
/// </list>
/// </summary>
public sealed class SampleInstrument : IInstrument
{
    private readonly float[] _mono;
    private readonly int _srcRate;

    /// <summary>ピッチ基準の MIDI ノート (null = ピッチ変更なし、原音のまま鳴らす)。</summary>
    public float? BaseNote { get; }

    public SampleInstrument(float[] monoSamples, int sourceSampleRate, float? baseNote = null)
    {
        _mono = monoSamples;
        _srcRate = sourceSampleRate > 0 ? sourceSampleRate : 44100;
        BaseNote = baseNote;
    }

    /// <summary>WAV ストリームを読み込んでサンプル音色を作る (16bit PCM / 32bit float、ステレオはモノ化)。</summary>
    public static SampleInstrument FromWav(Stream wav, float? baseNote = null, bool leaveOpen = false)
    {
        using var s = new WavStream(wav, leaveOpen);
        var interleaved = new List<float>();
        var tmp = new float[4096];
        int n;
        while ((n = s.Read(tmp)) > 0) interleaved.AddRange(tmp.AsSpan(0, n).ToArray());

        int ch = Math.Max(1, s.Channels);
        int frames = interleaved.Count / ch;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float acc = 0;
            for (int c = 0; c < ch; c++) acc += interleaved[(f * ch) + c];
            mono[f] = acc / ch;
        }
        return new SampleInstrument(mono, s.SampleRate, baseNote);
    }

    /// <summary>WAV ファイルパスから読み込む。</summary>
    public static SampleInstrument FromWavFile(string path, float? baseNote = null)
        => FromWav(File.OpenRead(path), baseNote);

    public float[] Render(in ControlMap controls, double duration, int sampleRate)
    {
        float pitch = 1f;
        if (controls.Note is float note && BaseNote is float bn)
            pitch = MathF.Pow(2f, (note - bn) / 12f);

        // 出力 1 サンプルあたり進む素材サンプル数 (素材レート差 × ピッチ)
        double step = (double)_srcRate / sampleRate * pitch;
        if (step <= 0) return [];

        int outLen = (int)(_mono.Length / step);
        if (outLen <= 1 || _mono.Length < 2) return [];

        var w = new float[outLen];
        for (int j = 0; j < outLen; j++)
        {
            double sp = j * step;
            int i0 = (int)sp;
            if (i0 >= _mono.Length - 1) { Array.Resize(ref w, j); break; }
            float frac = (float)(sp - i0);
            w[j] = _mono[i0] + ((_mono[i0 + 1] - _mono[i0]) * frac);
        }
        return w;
    }
}
