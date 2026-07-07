using Luxel.Audio;

namespace LuxelCavern.Core;

/// <summary>
/// ゲームのオーディオ配線: ループ BGM (<see cref="AudioSource"/>) + イベント発火の SE (<see cref="AudioMixer"/> の
/// ワンショット)。<see cref="CavernSfxDetector"/> で sim の出来事を SE に変換し、<see cref="AudioBus"/> 階層
/// (Master → Music / Sfx) で音量グループを分ける (設定 UI から Master/Music/Sfx の Volume に bind 可能 — Q06 B)。
/// バックエンドは exe が XAudio2、テスト/ヘッドレスは <see cref="NullAudioBackend"/> を渡す。
/// </summary>
public sealed class CavernAudio : IDisposable
{
    private readonly AudioMixer _sfx;
    private readonly AudioSource _bgm;
    private readonly Dictionary<CavernSfxCue, AudioClip> _clips;
    private readonly CavernSfxDetector _detector = new();
    private readonly List<CavernSfxCue> _scratch = new();

    /// <summary>音量グループ (設定から bind する)。</summary>
    public AudioBus Master { get; }
    public AudioBus Music { get; }
    public AudioBus Sfx { get; }

    /// <param name="backend">出力バックエンド (BGM voice を確保するため)。</param>
    /// <param name="mixer">SE のワンショット用ミキサ (Framework の <c>UseAudio</c> が用意する共有インスタンス)。</param>
    public CavernAudio(IAudioBackend backend, AudioMixer mixer)
    {
        Master = new AudioBus("Master");
        Music = new AudioBus("Music", Master);
        Sfx = new AudioBus("Sfx", Master);

        _sfx = mixer;
        _sfx.Bus = Sfx;
        _clips = CavernSfxBank.Build();
        _bgm = new AudioSource(backend, CavernSfxBank.BuildBgm()) { Bus = Music };
        _bgm.Volume.Value = 0.7f;
    }

    /// <summary>新規/再開でシーンを差し替えたら SE 検出の基準を取り直す (ロード直後の誤発火防止)。</summary>
    public void ResetForNewGame() => _detector.Reset();

    /// <summary>BGM ループ再生を開始。</summary>
    public void PlayBgm() => _bgm.Play(loop: true);

    /// <summary>BGM を止める (タイトルへ戻る等)。</summary>
    public void StopBgm() => _bgm.Stop();

    /// <summary>この sim ステップの出来事を SE として鳴らす (毎固定更新で呼ぶ)。</summary>
    public void React(CavernSim sim)
    {
        _scratch.Clear();
        _detector.Detect(sim, _scratch);
        foreach (CavernSfxCue cue in _scratch)
            if (_clips.TryGetValue(cue, out AudioClip? clip))
                _sfx.PlayOneShot(clip);
    }

    /// <summary>BGM の Signal → voice 反映 (音量変更を効かせる)。SE 側の <see cref="AudioMixer.Tick"/> は Framework が行う。</summary>
    public void Tick() => _bgm.Tick();

    public void Dispose() => _bgm.Dispose();
}
