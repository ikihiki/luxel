namespace Luxel.Audio;

/// <summary>
/// PCM 音声の形式 (サンプルレート・チャンネル数・ビット深度)。
/// 現状 16-bit signed integer PCM のみサポート (BitsPerSample=16 前提でバックエンドが動作)。
/// </summary>
public readonly record struct AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>1 サンプル (全チャンネル分) のバイト数。</summary>
    public int BytesPerSample => (BitsPerSample / 8) * Channels;

    /// <summary>1 秒あたりのバイト数 (Duration 計算用)。</summary>
    public int BytesPerSecond => SampleRate * BytesPerSample;

    // === 良く使う既定形式 ===
    public static readonly AudioFormat Pcm16Mono22k = new(22050, 1, 16);
    public static readonly AudioFormat Pcm16Stereo22k = new(22050, 2, 16);
    public static readonly AudioFormat Pcm16Mono44k = new(44100, 1, 16);
    public static readonly AudioFormat Pcm16Stereo44k = new(44100, 2, 16);
    public static readonly AudioFormat Pcm16Mono48k = new(48000, 1, 16);
    public static readonly AudioFormat Pcm16Stereo48k = new(48000, 2, 16);
}
