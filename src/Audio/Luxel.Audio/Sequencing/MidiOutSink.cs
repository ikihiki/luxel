namespace Luxel.Audio.Sequencing;

/// <summary>
/// <see cref="ScheduledEvent"/> を MIDI note on/off に変換して <see cref="IMidiOut"/> へ流す
/// <see cref="IEventSink"/>。音声と違い PCM に焼けないため、メッセージは「絶対秒」で溜め、
/// ホストが実時間クロックで <see cref="Pump"/> を呼んで送出する (StreamMixerSink が窓ループで
/// 駆動されるのと同じ構図 — ライブラリは wall-clock を持たず決定的)。
/// <list type="bullet">
/// <item>Note (無ければ N) → ノート番号、Gain → ベロシティ (既定 0.8 → ~102)</item>
/// <item>各イベントで note-on (Time) と note-off (Time + Duration) を予約</item>
/// <item>音色名だけ (Note/N なし) のイベントは音程が無いのでスキップ</item>
/// </list>
/// メッセージ生成は純関数なので、記録用 <see cref="IMidiOut"/> を挿せばデバイス無しで検証できる。
/// </summary>
public sealed class MidiOutSink : IEventSink, IDisposable
{
    private readonly IMidiOut _out;
    private readonly int _channel;
    private readonly List<(double Time, MidiMessage Msg)> _pending = new();

    public MidiOutSink(IMidiOut? midiOut = null, int channel = 0)
    {
        _out = midiOut ?? new NullMidiOut();
        _channel = channel & 0x0F;
    }

    /// <summary>まだ送出していない予約数 (テスト/デバッグ用)。</summary>
    public int PendingCount => _pending.Count;

    public void Schedule(ReadOnlySpan<ScheduledEvent> events, double windowStart, double windowEnd)
    {
        foreach (ScheduledEvent e in events)
        {
            if (ToNote(e.Controls) is not int note) continue;
            int vel = Velocity(e.Controls);
            _pending.Add((e.Time, MidiMessage.NoteOn(_channel, note, vel)));
            _pending.Add((e.Time + Math.Max(0.01, e.Duration), MidiMessage.NoteOff(_channel, note)));
        }
        _pending.Sort(static (a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary><paramref name="nowSeconds"/> 以前に予約されたメッセージを時刻順に送出する。</summary>
    public void Pump(double nowSeconds)
    {
        int i = 0;
        while (i < _pending.Count && _pending[i].Time <= nowSeconds)
        {
            _out.Send(_pending[i].Msg);
            i++;
        }
        if (i > 0) _pending.RemoveRange(0, i);
    }

    /// <summary>全停止 — 予約破棄 + デバイスの全ノートオフ。</summary>
    public void Hush()
    {
        _pending.Clear();
        _out.Reset();
    }

    public void Dispose() => _out.Dispose();

    /// <summary>Note を優先、無ければ N を音程として使う (どちらも無ければ null = スキップ)。</summary>
    private static int? ToNote(in ControlMap c)
        => c.Note is float n ? (int)MathF.Round(n)
         : c.N is float nn ? (int)MathF.Round(nn)
         : null;

    private static int Velocity(in ControlMap c)
        => Math.Clamp((int)MathF.Round((c.Gain ?? 0.8f) * 127f), 1, 127);
}
