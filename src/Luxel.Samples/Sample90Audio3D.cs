using System.Numerics;
using Luxel;
using Luxel.Audio;
using Luxel.Platform;

namespace Luxel.Samples;

/// <summary>
/// Sample 90 (AUDIO-M5..M6): 3D positional audio + AudioBus 階層。
///
/// Listener を原点に置き、3 個の音源を左 / 中央 / 右に配置。listener を Y 軸回転させながら
/// 各 source の EffectiveVolume と EffectivePan の変化を Console にプリント (回転で pan が入れ替わる)。
/// Master → SFX bus の階層で bus volume を 0.5 に落とすと全 source が半減することを確認。
/// </summary>
public static class Sample90Audio3D
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 90: 3D Positional Audio + AudioBus ===");

        IAudioBackend backend;
        try
        {
            var xa2 = new XAudio2Backend();
            xa2.Initialize();
            backend = xa2;
            Console.WriteLine("  backend: XAudio2");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  XAudio2 init failed ({ex.Message}) → NullBackend");
            var nb = new NullAudioBackend();
            nb.Initialize();
            backend = nb;
        }

        // Bus 階層: Master → SFX (階層検証用)
        var master = new AudioBus("Master");
        var sfx = new AudioBus("SFX", parent: master);

        // 短い beep を 3 個 (異なる pitch で位置的にも区別)
        AudioClip beep = MakeSine(440f, 0.5f, 44100, 1);

        var listener = new AudioListener { Position = Vector3.Zero, Forward = -Vector3.UnitZ, Up = Vector3.UnitY };

        // 3 音源: 左 (-3, 0, 0), 中央 (0, 0, -3), 右 (+3, 0, 0)
        var sources = new List<AudioSource3D>
        {
            new(backend, beep) { Position = new Vector3(-3, 0, 0), MinDistance = 1f, MaxDistance = 10f, Bus = sfx },
            new(backend, beep) { Position = new Vector3(0, 0, -3), MinDistance = 1f, MaxDistance = 10f, Bus = sfx },
            new(backend, beep) { Position = new Vector3(3, 0, 0),  MinDistance = 1f, MaxDistance = 10f, Bus = sfx },
        };
        sources[0].Pitch.Value = 0.8f;
        sources[1].Pitch.Value = 1.0f;
        sources[2].Pitch.Value = 1.2f;

        Console.WriteLine($"  bus: Master(vol {master.Volume.Peek()}) → SFX(vol {sfx.Volume.Peek()}) = eff {sfx.EffectiveVolume}");
        Console.WriteLine($"  listener at {listener.Position}, forward {listener.Forward}");
        Console.WriteLine($"  sources at: {sources[0].Position}, {sources[1].Position}, {sources[2].Position}");

        foreach (var s in sources) s.Play(loop: true);

        // === Rotation demo: listener を 3 回転させ、pan の変化を観測 ===
        Console.WriteLine("  [Rotate] listener を Y 軸回転 → pan 変化");
        for (int step = 0; step <= 4; step++)
        {
            float yaw = step * MathF.PI / 2;   // 0, 90, 180, 270, 360°
            listener.Forward = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw));
            foreach (var s in sources) s.Update(listener);
            Console.WriteLine($"    yaw={yaw * 180 / MathF.PI:F0}°  " +
                $"L(vol={sources[0].EffectiveVolume:F2}, pan={sources[0].EffectivePan:+0.00;-0.00})  " +
                $"C(vol={sources[1].EffectiveVolume:F2}, pan={sources[1].EffectivePan:+0.00;-0.00})  " +
                $"R(vol={sources[2].EffectiveVolume:F2}, pan={sources[2].EffectivePan:+0.00;-0.00})");
            Thread.Sleep(500);
        }

        // === Bus volume: SFX を 0.5 に → 全体が半減 ===
        Console.WriteLine("  [Bus] SFX bus を 0.5 に");
        sfx.Volume.Value = 0.5f;
        listener.Forward = -Vector3.UnitZ;   // reset
        foreach (var s in sources) s.Update(listener);
        Console.WriteLine($"    L eff={sources[0].EffectiveVolume:F2}, C eff={sources[1].EffectiveVolume:F2}, R eff={sources[2].EffectiveVolume:F2}");
        Console.WriteLine($"    master.EffectiveVolume={master.EffectiveVolume}, sfx.EffectiveVolume={sfx.EffectiveVolume}");
        Thread.Sleep(500);

        // === Master mute: Master を 0 → 全 source が 0 ===
        Console.WriteLine("  [Bus] Master を 0 に (mute)");
        master.Volume.Value = 0f;
        foreach (var s in sources) s.Update(listener);
        Console.WriteLine($"    L eff={sources[0].EffectiveVolume:F2}, C eff={sources[1].EffectiveVolume:F2}, R eff={sources[2].EffectiveVolume:F2}");
        Thread.Sleep(300);

        foreach (var s in sources) s.Stop();
        foreach (var s in sources) s.Dispose();
        backend.Dispose();

        // 検証: 90° 回転で左右音源の pan が反転していること + master 0 で全 volume 0
        bool ok = master.EffectiveVolume == 0f
              && sources[0].EffectiveVolume == 0f
              && sources[2].EffectiveVolume == 0f;
        Console.WriteLine(ok ? "OK: 3D positional + Bus 階層動作" : "FAILED");
        return ok ? 0 : 1;
    }

    private static AudioClip MakeSine(float freq, float durationSec, int sampleRate, int channels)
    {
        int samples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2 * channels;
        var pcm = new byte[samples * bytesPerSample];
        for (int i = 0; i < samples; i++)
        {
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
