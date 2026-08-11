using LuxelCavern.Core;
using Luxel.Audio;
using Luxel.Settings;

namespace Luxel.Tests;

/// <summary>
/// ゲーム設定 <see cref="CavernSettings"/> の決定的テスト (<see cref="InMemoryFileStore"/> で file IO 非依存):
/// 既定値 / 変更が AutoSave で永続化され再読込で復元 / 音量バスへの反映は <see cref="CavernAudio"/> 側。
/// </summary>
public class CavernSettingsTests
{
    [Fact]
    public void Defaults_WhenNoFile()
    {
        var settings = new CavernSettings(new InMemoryFileStore());
        Assert.Equal(0.8f, settings.MasterVolume.Value, 3);
        Assert.Equal(0.7f, settings.MusicVolume.Value, 3);
        Assert.Equal(0.9f, settings.SfxVolume.Value, 3);
    }

    [Fact]
    public void Change_AutoSaves_AndReloadRestores()
    {
        var files = new InMemoryFileStore();
        var settings = new CavernSettings(files);
        settings.MasterVolume.Value = 0.35f;
        settings.SfxVolume.Value = 0.1f;

        // AutoSave で書かれているはず → 別インスタンスで読み直して復元される
        var reloaded = new CavernSettings(files);
        Assert.Equal(0.35f, reloaded.MasterVolume.Value, 3);
        Assert.Equal(0.1f, reloaded.SfxVolume.Value, 3);
        Assert.Equal(0.7f, reloaded.MusicVolume.Value, 3);   // 変更していない値は既定のまま
    }

    [Fact]
    public void BindSettings_DrivesAudioBusVolumesOnTick()
    {
        var backend = new NullAudioBackend();
        backend.Initialize();
        var audio = new CavernAudio(backend, new AudioMixer(backend));
        var settings = new CavernSettings(new InMemoryFileStore());
        audio.BindSettings(settings);

        settings.MasterVolume.Value = 0.5f;
        settings.SfxVolume.Value = 0.4f;
        audio.Tick();   // 設定 → バスへ反映

        Assert.Equal(0.5f, audio.Master.Volume.Value, 3);
        Assert.Equal(0.2f, audio.Sfx.EffectiveVolume, 3);   // 0.5 (master) × 0.4 (sfx)
    }
}
