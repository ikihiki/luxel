using Luxel.Settings;
using Luxel.UI;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム設定 (現状は音量: Master / Music / Sfx)。<see cref="SettingsStore"/> の上に載り、値は <see cref="Signal{T}"/>
/// で公開する — 設定画面のコントロールに直結でき、<see cref="CavernAudio"/> の <see cref="AudioBus"/> 音量へ流し込める。
/// <see cref="SettingsStore.AutoSave"/> = true なので変更は即 <see cref="IFileStore"/> (exe は %APPDATA%) へ書き戻る。
/// 破損ファイルは既定値で起動 + <c>.bak</c> 退避 (SettingsStore が面倒を見る)。
/// </summary>
public sealed class CavernSettings
{
    /// <summary>設定ファイル名 (<see cref="IFileStore"/> ルート直下)。</summary>
    public const string FileName = "cavern-settings.json";

    private readonly SettingsStore _store;

    /// <summary>音量 (0..1, linear)。</summary>
    public Signal<float> MasterVolume { get; }
    public Signal<float> MusicVolume { get; }
    public Signal<float> SfxVolume { get; }

    public CavernSettings(IFileStore files)
    {
        _store = SettingsStore.LoadFrom(files, FileName);
        _store.AutoSave = true;   // スライダー変更のたび即永続化
        MasterVolume = _store.Get("volume.master", 0.8f);
        MusicVolume = _store.Get("volume.music", 0.7f);
        SfxVolume = _store.Get("volume.sfx", 0.9f);
    }

    /// <summary>明示保存 (AutoSave なので通常不要だが、終了時の念押しに使える)。</summary>
    public void Save() => _store.Save();
}
