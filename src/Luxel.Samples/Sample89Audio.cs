using Luxel;
using Luxel.Audio;
using Luxel.Platform;

namespace Luxel.Samples;

/// <summary>
/// Sample 89 (AUDIO-M1..M4): XAudio2Backend + AudioClip + AudioMixer + AudioSource の実演。
/// 短い sine wave の PCM を生成し、
///   (1) AudioMixer.PlayOneShot で SFX 3 発 (別 pitch)
///   (2) AudioSource で BGM (loop) 相当を 1 秒間再生
/// を Console ログ + 実 audio 出力で確認する。
///
/// Audio device が無い環境ではXAudio2 初期化が失敗し得るので try/catch で fallback (NullBackend)。
/// </summary>
public static class Sample89Audio
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 89: Audio (XAudio2 + AudioMixer + AudioSource) ===");

        // === Backend 選択: 実音 or NullBackend fallback ===
        IAudioBackend backend;
        try
        {
            var xa2 = new XAudio2Backend();
            xa2.Initialize();
            backend = xa2;
            Console.WriteLine("  backend: XAudio2 (実音出力)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  XAudio2 init failed ({ex.Message}) → NullBackend fallback");
            var nb = new NullAudioBackend();
            nb.Initialize();
            backend = nb;
        }

        // === Sine wave clip 生成 (440Hz mono 44.1kHz 0.3秒) ===
        AudioClip beep = MakeSine(freq: 440f, durationSec: 0.3f, sampleRate: 44100, channels: 1);
        Console.WriteLine($"  clip: {beep.Duration.TotalMilliseconds:F0}ms, {beep.Format.SampleRate}Hz, {beep.Format.Channels}ch, {beep.PcmData.Length} bytes");

        // === (1) AudioMixer で 3 発、pitch を変えて音階を作る ===
        using var mixer = new AudioMixer(backend);
        Console.WriteLine("  [Mixer] PlayOneShot × 3 (do-mi-so)");
        mixer.PlayOneShot(beep, volume: 0.3f, pitch: 1.0f);   // ド
        Thread.Sleep(300);
        mixer.Tick();
        Console.WriteLine($"    active voices: {mixer.ActiveVoiceCount}");
        mixer.PlayOneShot(beep, volume: 0.3f, pitch: 1.26f);   // ミ (semi ratio ^4)
        Thread.Sleep(300);
        mixer.Tick();
        mixer.PlayOneShot(beep, volume: 0.3f, pitch: 1.5f);    // ソ
        Thread.Sleep(400);
        mixer.Tick();
        Console.WriteLine($"    active voices after cleanup: {mixer.ActiveVoiceCount}");

        // === (2) AudioSource で loop + Signal volume fade ===
        Console.WriteLine("  [AudioSource] loop 1 秒 + Volume fade");
        AudioClip loopClip = MakeSine(freq: 220f, durationSec: 0.1f, sampleRate: 44100, channels: 1);
        using var source = new AudioSource(backend, loopClip);
        source.Volume.Value = 0.1f;
        source.Play(loop: true);
        for (int i = 0; i < 20; i++)   // 1 秒 (50ms × 20)
        {
            // fade in-out (0.05 → 0.4 → 0.05)
            source.Volume.Value = 0.05f + 0.35f * MathF.Sin(i * MathF.PI / 19);
            source.Tick();
            Thread.Sleep(50);
        }
        source.Stop();
        Console.WriteLine("  BGM stopped");

        backend.Dispose();
        Console.WriteLine("OK: Audio 再生 + Mixer/Source が動作");
        return 0;
    }

    /// <summary>16-bit PCM の sine wave を生成。</summary>
    private static AudioClip MakeSine(float freq, float durationSec, int sampleRate, int channels)
    {
        int samples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2 * channels;
        var pcm = new byte[samples * bytesPerSample];
        for (int i = 0; i < samples; i++)
        {
            // フェード (最初と最後の 20ms でクリック回避)
            float t = i / (float)sampleRate;
            float env = MathF.Min(1f, MathF.Min(t / 0.02f, (durationSec - t) / 0.02f));
            short s = (short)(short.MaxValue * 0.6f * env * MathF.Sin(2f * MathF.PI * freq * t));
            for (int c = 0; c < channels; c++)
            {
                int idx = (i * channels + c) * 2;
                pcm[idx] = (byte)(s & 0xff);
                pcm[idx + 1] = (byte)((s >> 8) & 0xff);
            }
        }
        return new AudioClip(new AudioFormat(sampleRate, channels, 16), pcm, "sine");
    }
}
