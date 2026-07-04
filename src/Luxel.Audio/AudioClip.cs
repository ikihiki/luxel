namespace Luxel.Audio;

/// <summary>
/// メモリ上のデコード済み PCM 音声。<see cref="Format"/> に従って <see cref="PcmData"/> を解釈する。
/// Resources 経由でロード (<c>resources.Load&lt;AudioClip&gt;("bgm.ogg")</c>) or 直接 new で生成する。
/// SFX は通常この形式 (全展開)、BGM は AUDIO-M7 で streaming source を別途用意する。
/// </summary>
public sealed class AudioClip
{
    public AudioFormat Format { get; }
    public byte[] PcmData { get; }
    public string Name { get; }

    /// <summary>Clip の総再生時間。</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(PcmData.Length / (double)Format.BytesPerSecond);

    /// <summary>サンプル数 (per-channel フレーム数)。</summary>
    public int SampleCount => PcmData.Length / Format.BytesPerSample;

    public AudioClip(AudioFormat format, byte[] pcm, string name = "")
    {
        Format = format;
        PcmData = pcm;
        Name = name;
    }
}
