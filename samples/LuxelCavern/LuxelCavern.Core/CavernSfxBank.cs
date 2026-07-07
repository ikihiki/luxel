using Luxel.Audio;

namespace LuxelCavern.Core;

/// <summary>
/// ゲームの効果音 / BGM を CPU で合成する (外部アセット不要 = publish が軽く決定的)。
/// SE は cue ごとに周波数と長さを変えたエンベロープ付きサイン波、BGM は継ぎ目でクリックが出ないよう
/// バッファ長を整数周期に合わせた低音サインのループ。全て 16-bit mono PCM (<see cref="AudioFormat.Pcm16Mono44k"/>)。
/// </summary>
public static class CavernSfxBank
{
    private static readonly AudioFormat Fmt = AudioFormat.Pcm16Mono44k;

    /// <summary>cue → 効果音クリップの辞書を作る。</summary>
    public static Dictionary<CavernSfxCue, AudioClip> Build() => new()
    {
        [CavernSfxCue.Jump]       = Tone(520f, 0.12f, 0.30f, glideTo: 780f),   // 踏み切りの上昇ブリップ
        [CavernSfxCue.Land]       = Tone(150f, 0.09f, 0.28f),                   // 低い着地音
        [CavernSfxCue.Coin]       = Tone(940f, 0.09f, 0.25f, glideTo: 1180f),   // 明るいコイン
        [CavernSfxCue.Key]        = Tone(1050f, 0.16f, 0.28f, glideTo: 1400f),  // 鍵はより高く長め
        [CavernSfxCue.Defeat]     = Tone(360f, 0.14f, 0.30f, glideTo: 180f),    // 撃破は下降
        [CavernSfxCue.Hurt]       = Tone(210f, 0.18f, 0.34f, glideTo: 140f),    // 被弾は濁った低音
        [CavernSfxCue.Checkpoint] = Tone(680f, 0.20f, 0.28f, glideTo: 900f),    // 到達のチャイム
        [CavernSfxCue.Clear]      = Tone(523f, 0.45f, 0.30f, glideTo: 784f),    // クリアの明るい長音
    };

    /// <summary>ループ BGM (低音の柔らかいサイン、継ぎ目クリック無し)。</summary>
    public static AudioClip BuildBgm()
    {
        const float seconds = 2f;
        int n = (int)(Fmt.SampleRate * seconds);
        byte[] pcm = new byte[n * 2];
        // 110Hz + 165Hz とも seconds=2 で整数周期 → 先頭/末尾が連続しループ時に無クリック。
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Fmt.SampleRate;
            float v = MathF.Sin(MathF.Tau * 110f * t) * 0.6f + MathF.Sin(MathF.Tau * 165f * t) * 0.4f;
            Write(pcm, i, (short)(v * short.MaxValue * 0.18f));
        }
        return new AudioClip(Fmt, pcm, "cavern-bgm");
    }

    /// <summary>エンベロープ付きサイン波 (任意で終端へ周波数グライド)。</summary>
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
            // アタック 5ms / リリースは末尾へ向けて減衰 (クリック防止 + 短い打撃感)
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
