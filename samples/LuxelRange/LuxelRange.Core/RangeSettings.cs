using Luxel.Settings;
using Luxel.UI;

namespace LuxelRange.Core;

/// <summary>
/// 「Luxel Range」の永続設定 — ハイスコア + 音量。<see cref="SettingsStore"/> の上に載り、値は
/// <see cref="Signal{T}"/>。<see cref="SettingsStore.AutoSave"/> = true なので変更は即
/// <see cref="IFileStore"/> (exe は %APPDATA%、テストはインメモリ) へ書き戻る。capstone ① の SettingsStore を
/// 2 ゲーム目でも再利用できることの確認 (タスク 15)。
/// </summary>
public sealed class RangeSettings
{
    private const string FileName = "range.json";
    private readonly SettingsStore _store;

    /// <summary>ハイスコア (これまでの最高合計スコア)。</summary>
    public Signal<int> HighScore { get; }
    /// <summary>マスター音量。</summary>
    public Signal<float> MasterVolume { get; }
    /// <summary>BGM 音量。</summary>
    public Signal<float> MusicVolume { get; }
    /// <summary>SE 音量。</summary>
    public Signal<float> SfxVolume { get; }

    public RangeSettings(IFileStore files)
    {
        _store = SettingsStore.LoadFrom(files, FileName);
        _store.AutoSave = true;
        HighScore = _store.Get("range.highscore", 0);
        MasterVolume = _store.Get("volume.master", 0.8f);
        MusicVolume = _store.Get("volume.music", 0.7f);
        SfxVolume = _store.Get("volume.sfx", 0.9f);
    }

    /// <summary>スコアがハイスコアを超えていれば更新して保存 (Result 遷移時に呼ぶ)。更新したら true。</summary>
    public bool SubmitScore(int score)
    {
        if (score <= HighScore.Value) return false;
        HighScore.Value = score;   // AutoSave が永続化
        return true;
    }

    public void Save() => _store.Save();
}
