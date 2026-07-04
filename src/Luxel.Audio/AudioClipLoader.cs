using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Luxel.Audio;

/// <summary>
/// WAV / OGG から <see cref="AudioClip"/> を作るヘルパ。NAudio + NAudio.Vorbis を使用。
/// 出力形式は 16-bit PCM (backend の共通形式) に統一 ─ 入力が float でも自動変換される。
/// </summary>
public static class AudioClipLoader
{
    /// <summary>拡張子 (".wav"/".ogg") で分岐して読み込み、16-bit PCM で AudioClip を返す。</summary>
    public static AudioClip Load(Stream input, string extension, string name = "")
    {
        string ext = extension.ToLowerInvariant();
        WaveStream stream = ext switch
        {
            ".wav" => new WaveFileReader(input),
            ".ogg" => new NAudio.Vorbis.VorbisWaveReader(input),
            _ => throw new NotSupportedException($"AudioClipLoader: unsupported extension '{extension}' (.wav / .ogg)"),
        };
        using (stream)
        {
            // 16-bit PCM に統一 (WAV は元々 16bit のことが多いが、OGG は 32-bit float なので変換必須)
            IWaveProvider pcm16 = stream.ToSampleProvider().ToWaveProvider16();
            var fmt = pcm16.WaveFormat;

            using var ms = new MemoryStream();
            byte[] buf = new byte[8192];
            int read;
            while ((read = pcm16.Read(buf, 0, buf.Length)) > 0)
                ms.Write(buf, 0, read);

            return new AudioClip(
                new AudioFormat(fmt.SampleRate, fmt.Channels, 16),
                ms.ToArray(),
                name);
        }
    }

    public static AudioClip Load(byte[] input, string extension, string name = "")
    {
        using var ms = new MemoryStream(input, writable: false);
        return Load(ms, extension, name);
    }
}
