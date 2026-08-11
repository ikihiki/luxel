using Luxel.Audio.Sequencing;
using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel MIDI out (Q22-E): ScheduledEvent → note on/off の生成を記録用出力で検証する
/// (実デバイス送出は実機スモーク扱い)。</summary>
public class StrudelMidiTests
{
    private sealed class RecordingMidiOut : IMidiOut
    {
        public readonly List<MidiMessage> Sent = new();
        public int Resets;
        public void Send(MidiMessage m) => Sent.Add(m);
        public void Reset() => Resets++;
        public void Dispose() { }
    }

    [Fact]
    public void NoteEvents_ProduceOnThenOff()
    {
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        // 重ならない持続 (0.4) にして同時刻の並び順ゆらぎを避ける
        sink.Schedule(
        [
            new ScheduledEvent(0.0, 0.4, new ControlMap(Note: 60f, Gain: 1f)),
            new ScheduledEvent(0.5, 0.4, new ControlMap(Note: 64f, Gain: 1f)),
        ], 0.0, 1.0);
        sink.Pump(10.0);

        Assert.Equal(4, rec.Sent.Count);
        Assert.True(rec.Sent[0].IsNoteOn); Assert.Equal(60, rec.Sent[0].Data1); Assert.Equal(127, rec.Sent[0].Data2);
        Assert.True(rec.Sent[1].IsNoteOff); Assert.Equal(60, rec.Sent[1].Data1);
        Assert.True(rec.Sent[2].IsNoteOn); Assert.Equal(64, rec.Sent[2].Data1);
        Assert.True(rec.Sent[3].IsNoteOff); Assert.Equal(64, rec.Sent[3].Data1);
    }

    [Fact]
    public void Velocity_FromGain()
    {
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        sink.Schedule([new ScheduledEvent(0.0, 0.1, new ControlMap(Note: 60f, Gain: 0.5f))], 0.0, 1.0);
        sink.Pump(10.0);
        Assert.Equal(64, rec.Sent[0].Data2);   // round(0.5 * 127)
    }

    [Fact]
    public void Pump_SendsOnlyUpToNow()
    {
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        sink.Schedule([new ScheduledEvent(0.0, 0.4, new ControlMap(Note: 60f))], 0.0, 1.0);
        sink.Pump(0.2);                         // note-on (t=0) だけ
        Assert.Single(rec.Sent);
        Assert.True(rec.Sent[0].IsNoteOn);
        Assert.Equal(1, sink.PendingCount);     // note-off (t=0.4) が残る
        sink.Pump(1.0);
        Assert.Equal(2, rec.Sent.Count);
        Assert.True(rec.Sent[1].IsNoteOff);
    }

    [Fact]
    public void Hush_ClearsAndResets()
    {
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        sink.Schedule([new ScheduledEvent(0.0, 0.4, new ControlMap(Note: 60f))], 0.0, 1.0);
        sink.Hush();
        Assert.Equal(0, sink.PendingCount);
        Assert.Equal(1, rec.Resets);
    }

    [Fact]
    public void InstrumentOnly_IsSkipped_But_NUsedAsNote()
    {
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        sink.Schedule(
        [
            new ScheduledEvent(0.0, 0.1, new ControlMap(Instrument: "bd")),   // 音程なし → スキップ
            new ScheduledEvent(0.1, 0.1, new ControlMap(N: 5f)),             // N を音程に
        ], 0.0, 1.0);
        sink.Pump(10.0);
        Assert.Equal(2, rec.Sent.Count);        // N=5 の on/off だけ
        Assert.Equal(5, rec.Sent[0].Data1);
    }

    [Fact]
    public void ThroughScheduler_NotePattern()
    {
        var sched = new StrudelScheduler(0.1) { Cps = 1.0 };
        var rec = new RecordingMidiOut();
        var sink = new MidiOutSink(rec);
        sched.AddSink(sink);
        sched.SetPattern(1, StrudelEval.Evaluate("""note("c4 e4 g4 c5")""").Pattern);
        for (int i = 0; i < 10; i++) sched.RenderWindow();   // 1 サイクル
        sink.Pump(10.0);
        var ons = rec.Sent.Where(m => m.IsNoteOn).Select(m => (int)m.Data1).ToList();
        Assert.Equal([60, 64, 67, 72], ons);
    }

    [Fact]
    public void NullMidiOut_IsSafeFallback()
    {
        // デバイス無し経路: 例外なく飲み込む
        var sink = new MidiOutSink(new NullMidiOut());
        sink.Schedule([new ScheduledEvent(0.0, 0.1, new ControlMap(Note: 60f))], 0.0, 1.0);
        sink.Pump(10.0);
        sink.Hush();
        Assert.Equal(0, sink.PendingCount);
    }

    [Fact]
    public void MidiMessage_PacksForWinmm()
    {
        // status | data1<<8 | data2<<16
        MidiMessage on = MidiMessage.NoteOn(0, 60, 100);
        Assert.Equal((uint)(0x90 | (60 << 8) | (100 << 16)), on.Packed);
        Assert.Equal(0x90, on.Status);
    }
}
