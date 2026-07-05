namespace Luxel.Audio.Sequencing;

/// <summary>
/// イベントの演奏パラメータ一式 (MIDI のメッセージに相当する汎用表現)。
/// **未指定は null** — 合成 (<see cref="Merge"/>) では右側の指定だけが上書きする。
/// MIDI へ落とすときは Gain→velocity (7bit)、Pan→CC10 等の不可逆変換をアダプタ側で行う。
/// v1 は固定フィールド (open な key-value にしない) — 型付き・アロケーションなしを優先。
/// </summary>
public readonly record struct ControlMap(
    string? Instrument = null,   // 音色名 ("bd", "sd", "saw" …) — InstrumentBank で解決
    float? N = null,             // サンプル/バリエーション番号 ("bd:3" の 3)
    float? Note = null,          // MIDI ノート番号 (c4 = 60、小数可)
    float? Gain = null,          // 振幅倍率 (既定 1)
    float? Pan = null,           // -1 (左) .. 0 .. 1 (右)
    float? Speed = null)         // 再生レート倍率 (既定 1)
{
    /// <summary>右側の指定フィールドで上書きした新しい map を返す。</summary>
    public ControlMap Merge(in ControlMap o) => new(
        o.Instrument ?? Instrument, o.N ?? N, o.Note ?? Note,
        o.Gain ?? Gain, o.Pan ?? Pan, o.Speed ?? Speed);
}

/// <summary>
/// 絶対時刻に解決済みの演奏イベント (シーケンシング層の共通語彙)。
/// パターン言語 (Luxel.Strudel) がこれを生成し、<see cref="IEventSink"/> 実装
/// (PCM ミキサ / UI ハイライト / MIDI out …) が消費する。
/// </summary>
public readonly record struct ScheduledEvent(
    double Time,        // 発音時刻 (秒、シーケンス開始からの絶対値)
    double Duration,    // 持続 (秒)
    ControlMap Controls);

/// <summary>
/// イベント列の配送先。スケジューラは時間窓 [windowStart, windowEnd) 単位で先読みクエリし、
/// その窓に頭 (onset) を持つイベントを**時刻順で 1 回だけ**配送する。
/// </summary>
public interface IEventSink
{
    /// <summary>窓 1 つ分のイベントを受け取る (空窓も呼ばれる — ミキサは無音を刻む)。</summary>
    void Schedule(ReadOnlySpan<ScheduledEvent> events, double windowStart, double windowEnd);

    /// <summary>全停止 (Strudel の hush)。予定済みイベントの破棄と発音中の消音。</summary>
    void Hush();
}
