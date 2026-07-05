namespace Luxel.Audio.Sequencing;

/// <summary>
/// 音色 — <see cref="ControlMap"/> をモノラル波形へ解決する。
/// **純関数であること** (同じ入力 → 同じ波形): ミキサの出力がテストで検証でき、
/// 内部で乱数を使う場合も固定シードで決定的にする。
/// gain/pan/speed はミキサ側で適用されるため、ここでは素の音を返す。
/// </summary>
public interface IInstrument
{
    /// <summary>1 イベント分のモノラル波形を返す (float、±1 目安)。
    /// <paramref name="duration"/> は発音の長さ (秒) — ドラム等の固定長音色は無視してよい。
    /// 返した配列はミキサが読み取り専用で保持する (使い回し可 — キャッシュ推奨)。</summary>
    float[] Render(in ControlMap controls, double duration, int sampleRate);
}

/// <summary>
/// 音色名 → <see cref="IInstrument"/> の登録簿。シーケンサ層は名前の意味を知らない —
/// アプリ (Strudel kit / ゲーム) が起動時に登録する。
/// </summary>
public sealed class InstrumentBank
{
    private readonly Dictionary<string, IInstrument> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Instrument 未指定 (Note のみ) や未登録名のときに使う音色 (null = そのイベントは無音)。</summary>
    public IInstrument? Fallback { get; set; }

    public void Register(string name, IInstrument instrument) => _map[name] = instrument;

    public IInstrument? Resolve(string? name)
        => name is not null && _map.TryGetValue(name, out IInstrument? inst) ? inst : Fallback;
}
