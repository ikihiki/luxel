using Luxel.Audio;
using Luxel.Platform;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>実ウィンドウ専用ストーリー ([Story(RealWindowOnly = true)] — snap 回帰は SKIP)。
/// 音声再生など、offscreen の決定的描画にならない機能のデモを置く。</summary>
public static class RealWindowStories
{
    private static AudioMixer? _mixer;   // プロセスで 1 個 (XAudio2 マスタリングボイス)

    [Story("Audio/Tone", Height = 220, RealWindowOnly = true)]
    public static Widget AudioTone(StoryContext ctx)
    {
        void Play(float freq, string name)
        {
            if (_mixer is null)
            {
                var backend = new XAudio2Backend();
                backend.Initialize();
                _mixer = new AudioMixer(backend);
            }
            _mixer.Tick();   // 完了 voice を pool へ回収 (デモはフレームループを持たないのでここで)
            _mixer.PlayOneShot(Tone(freq));
            ctx.Log($"play {name} ({freq:0.##}Hz)");
        }
        return Frame(VStack(10)[
            Label("XAudio2 のワンショット再生 — 実窓専用 (snap では SKIP)"),
            Muted("AudioClip は 0.4 秒のサイン波を CPU で合成 (Pcm16Mono44k)"),
            HStack(8)[
                Button(_ => Play(440f, "A4"), "A4 (440Hz)"),
                Button(_ => Play(523.25f, "C5"), "C5"),
                Button(_ => Play(659.26f, "E5"), "E5")]]);
    }

    /// <summary>0.4 秒のサイン波 (44.1kHz mono 16bit)。立ち上がり/終端フェードでクリックノイズを防ぐ。</summary>
    private static AudioClip Tone(float freq)
    {
        AudioFormat fmt = AudioFormat.Pcm16Mono44k;
        int n = (int)(fmt.SampleRate * 0.4f);
        byte[] pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float env = MathF.Min(1f, MathF.Min(i / 800f, (n - i) / 3000f));
            short s = (short)(MathF.Sin(MathF.Tau * freq * i / fmt.SampleRate) * env * short.MaxValue * 0.35f);
            pcm[i * 2] = (byte)s;
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioClip(fmt, pcm, $"tone{freq:0}");
    }
}
