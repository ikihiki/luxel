namespace Luxel.Audio;

/// <summary>
/// オーディオ出力バックエンドの抽象。Windows は <c>Luxel.Audio.Windows.XAudio2Backend</c>、
/// headless テストは <see cref="NullAudioBackend"/> を差替える。
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>バックエンド固有のリソース (デバイス / mastering voice) を初期化する。</summary>
    void Initialize();

    /// <summary>音源を再生するための voice を確保する。format ごとに別インスタンス。</summary>
    IAudioVoice CreateVoice(AudioFormat format);

    /// <summary>マスター音量 (0..1, linear)。全 voice に適用される。</summary>
    float MasterVolume { get; set; }
}

/// <summary>
/// 単一 voice (音源スロット) の抽象。バッファを受け付けて Play/Stop する。
/// SourceVoice 相当。<see cref="Volume"/>/<see cref="Pitch"/>/<see cref="Pan"/> は per-voice。
/// </summary>
public interface IAudioVoice : IDisposable
{
    /// <summary>PCM バッファを再生キューに投入する。<paramref name="loop"/> = true なら backend 側でループ。</summary>
    void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false);

    void Play();
    void Stop();
    void Pause();

    /// <summary>0..1 (linear)。</summary>
    float Volume { get; set; }

    /// <summary>0.5..2.0 相当 (backend 依存の再生速度倍率)。1.0 で原音、2.0 で 1 オクターブ上。</summary>
    float Pitch { get; set; }

    /// <summary>-1 (完全左) .. 0 (中央) .. 1 (完全右)。ステレオ voice でのみ有効。</summary>
    float Pan { get; set; }

    /// <summary>Play() 後 Stop() されるまで true。バッファ枯渇時は backend 依存 (通常 false になる)。</summary>
    bool IsPlaying { get; }

    /// <summary>提出済みでまだ再生し切っていないバッファ数 (SFX pool の完了判定に使う)。</summary>
    int BuffersQueued { get; }
}
