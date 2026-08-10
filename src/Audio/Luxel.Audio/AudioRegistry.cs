namespace Luxel.Audio;

/// <summary>
/// ゲーム内で使用中の <see cref="AudioBus"/> / <see cref="AudioSource"/> を DevTools 等が可視化できるよう
/// 集約する登録簿。<see cref="AudioMixer"/> と対で使う。
///
/// <para>Bus と Source は user が明示的に <see cref="RegisterBus"/> / <see cref="RegisterSource"/> で登録する
/// (自動 discovery はしない)。登録は idempotent、Unregister も安全。</para>
///
/// <c>Luxel.Framework.Game.SceneLoopServices</c> 経由で Scene に注入され、Scene が per-N-frame で
/// <c>Luxel.Audio</c> Diagnostics イベントとして snapshot を emit する。
/// </summary>
public sealed class AudioRegistry
{
    private readonly List<AudioBus> _buses = new();
    private readonly List<(string Name, AudioSource Source)> _sources = new();

    public AudioMixer? Mixer { get; set; }

    public IReadOnlyList<AudioBus> Buses => _buses;
    public IReadOnlyList<(string Name, AudioSource Source)> Sources => _sources;

    public void RegisterBus(AudioBus bus)
    {
        if (bus is null) return;
        if (!_buses.Contains(bus)) _buses.Add(bus);
    }

    public void RegisterSource(string name, AudioSource source)
    {
        if (source is null) return;
        _sources.Add((name, source));
    }

    public void UnregisterBus(AudioBus bus) => _buses.Remove(bus);
    public void UnregisterSource(AudioSource source) => _sources.RemoveAll(t => ReferenceEquals(t.Source, source));
}
