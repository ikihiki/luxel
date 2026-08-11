using Luxel.Audio.Sequencing;

namespace Luxel.Strudel;

/// <summary>
/// 標準音色キット — **すべてプロシージャル生成** (バイナリアセットなし・決定的)。
/// ドラム: bd sd hh oh cp rim lt ht / シンセ: sine tri saw square (Note + ADSR)。
/// ノイズは固定シードの xorshift — 同じ (音色, n) は常に同じ波形 (テスト/snap の決定性)。
/// <c>:n</c> はバリエーション (ピッチ/減衰を少しずらす)。
/// </summary>
public static class StrudelKit
{
    /// <summary>標準キットを登録した InstrumentBank を作る。Fallback = tri (note だけの行が鳴る)。</summary>
    public static InstrumentBank CreateBank()
    {
        var bank = new InstrumentBank();
        bank.Register("bd", new Drum(DrumKind.Bd));
        bank.Register("sd", new Drum(DrumKind.Sd));
        bank.Register("hh", new Drum(DrumKind.Hh));
        bank.Register("oh", new Drum(DrumKind.Oh));
        bank.Register("cp", new Drum(DrumKind.Cp));
        bank.Register("rim", new Drum(DrumKind.Rim));
        bank.Register("lt", new Drum(DrumKind.Lt));
        bank.Register("ht", new Drum(DrumKind.Ht));
        var tri = new Synth(Wave.Tri);
        bank.Register("sine", new Synth(Wave.Sine));
        bank.Register("tri", tri);
        bank.Register("saw", new Synth(Wave.Saw));
        bank.Register("square", new Synth(Wave.Square));
        bank.Fallback = tri;
        return bank;
    }

    private enum DrumKind { Bd, Sd, Hh, Oh, Cp, Rim, Lt, Ht }
    private enum Wave { Sine, Tri, Saw, Square }

    /// <summary>固定シードの決定的ノイズ (xorshift32)。</summary>
    private static float NextNoise(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state >> 8) * (2.0f / (1 << 24)) - 1f;
    }

    private sealed class Drum(DrumKind kind) : IInstrument
    {
        private readonly Dictionary<int, float[]> _cache = new();   // n → 波形 (決定的なので共有可)

        public float[] Render(in ControlMap c, double duration, int rate)
        {
            int n = (int)(c.N ?? 0);
            if (_cache.TryGetValue(n, out float[]? w)) return w;
            w = Generate(kind, n, rate);
            _cache[n] = w;
            return w;
        }

        private static float[] Generate(DrumKind kind, int n, int rate)
        {
            // n バリエーション: ピッチ ±、減衰を僅かにずらす (決定的)
            float det = 1f + 0.06f * (n % 4);
            uint seed = (uint)(0x9E3779B9 ^ (int)kind * 7919 ^ n * 104729) | 1;
            return kind switch
            {
                DrumKind.Bd => Kick(rate, 150f * det, 50f, 0.16f),
                DrumKind.Sd => Snare(rate, seed, 185f * det, 0.16f),
                DrumKind.Hh => NoiseHit(rate, seed, 0.035f, hp: true),
                DrumKind.Oh => NoiseHit(rate, seed, 0.28f, hp: true),
                DrumKind.Cp => Clap(rate, seed),
                DrumKind.Rim => Rim(rate, 800f * det),
                DrumKind.Lt => Kick(rate, 200f * det, 90f, 0.22f),
                DrumKind.Ht => Kick(rate, 300f * det, 150f, 0.18f),
                _ => [],
            };
        }

        private static float[] Kick(int rate, float f0, float f1, float dur)
        {
            var w = new float[(int)(rate * dur)];
            double phase = 0;
            for (int i = 0; i < w.Length; i++)
            {
                float t = (float)i / w.Length;
                float freq = f0 + (f1 - f0) * MathF.Min(1, t * 4);   // 速いピッチスイープ
                phase += 2 * Math.PI * freq / rate;
                float env = MathF.Exp(-5.5f * t);
                w[i] = MathF.Sin((float)phase) * env;
            }
            if (w.Length > 8) for (int i = 0; i < 8; i++) w[i] += (1 - i / 8f) * 0.5f;   // クリック
            return w;
        }

        private static float[] Snare(int rate, uint seed, float tone, float dur)
        {
            var w = new float[(int)(rate * dur)];
            double phase = 0;
            for (int i = 0; i < w.Length; i++)
            {
                float t = (float)i / w.Length;
                phase += 2 * Math.PI * tone / rate;
                float body = MathF.Sin((float)phase) * MathF.Exp(-9f * t) * 0.5f;
                float noise = NextNoise(ref seed) * MathF.Exp(-6f * t) * 0.6f;
                w[i] = body + noise;
            }
            return w;
        }

        private static float[] NoiseHit(int rate, uint seed, float dur, bool hp)
        {
            var w = new float[(int)(rate * dur)];
            float prev = 0;
            for (int i = 0; i < w.Length; i++)
            {
                float t = (float)i / w.Length;
                float x = NextNoise(ref seed);
                if (hp) { float y = x - prev; prev = x; x = y; }   // 1 次差分 ≒ ハイパス
                w[i] = x * MathF.Exp(-7f * t) * 0.7f;
            }
            return w;
        }

        private static float[] Clap(int rate, uint seed)
        {
            var w = new float[(int)(rate * 0.22f)];
            int burst = (int)(rate * 0.011f);
            for (int b = 0; b < 3; b++)
            {
                int off = b * burst;
                for (int i = off; i < w.Length; i++)
                {
                    float t = (float)(i - off) / (w.Length - off);
                    w[i] += NextNoise(ref seed) * MathF.Exp(-11f * t) * 0.45f;
                }
            }
            return w;
        }

        private static float[] Rim(int rate, float freq)
        {
            var w = new float[(int)(rate * 0.05f)];
            double phase = 0;
            for (int i = 0; i < w.Length; i++)
            {
                float t = (float)i / w.Length;
                phase += 2 * Math.PI * freq / rate;
                w[i] = MathF.Sign(MathF.Sin((float)phase)) * MathF.Exp(-18f * t) * 0.6f;
            }
            return w;
        }
    }

    private sealed class Synth(Wave wave) : IInstrument
    {
        public float[] Render(in ControlMap c, double duration, int rate)
        {
            float midi = c.Note ?? 60f;
            float freq = 440f * MathF.Pow(2f, (midi - 69f) / 12f);
            const float attack = 0.005f, release = 0.05f;
            float hold = MathF.Max(0.02f, (float)duration - release);
            var w = new float[(int)(rate * (hold + release))];
            double phase = 0;
            for (int i = 0; i < w.Length; i++)
            {
                float t = (float)i / rate;
                phase += freq / rate;
                double p = phase - Math.Floor(phase);   // [0,1)
                float s = wave switch
                {
                    Wave.Sine => MathF.Sin((float)(2 * Math.PI * p)),
                    Wave.Tri => 1f - 4f * MathF.Abs((float)p - 0.5f),
                    Wave.Saw => (float)(2 * p - 1),
                    _ => p < 0.5 ? 1f : -1f,
                };
                float env = t < attack ? t / attack
                          : t < hold ? 1f
                          : MathF.Max(0, 1f - (t - hold) / release);
                w[i] = s * env * 0.35f;   // シンセは控えめの基準音量
            }
            return w;
        }
    }
}
