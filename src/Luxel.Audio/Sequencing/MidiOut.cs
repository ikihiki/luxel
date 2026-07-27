namespace Luxel.Audio.Sequencing;

/// <summary>3 バイトの MIDI チャンネルメッセージ (note on/off 等)。</summary>
public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public const byte NoteOnStatus = 0x90;
    public const byte NoteOffStatus = 0x80;

    public static MidiMessage NoteOn(int channel, int note, int velocity)
        => new((byte)(NoteOnStatus | (channel & 0x0F)), (byte)(note & 0x7F), (byte)(velocity & 0x7F));

    public static MidiMessage NoteOff(int channel, int note)
        => new((byte)(NoteOffStatus | (channel & 0x0F)), (byte)(note & 0x7F), 0);

    public bool IsNoteOn => (Status & 0xF0) == NoteOnStatus && Data2 > 0;
    public bool IsNoteOff => (Status & 0xF0) == NoteOffStatus || ((Status & 0xF0) == NoteOnStatus && Data2 == 0);
    public int Channel => Status & 0x0F;

    /// <summary>winmm midiOutShortMsg 用のパック値 (status | data1&lt;&lt;8 | data2&lt;&lt;16)。</summary>
    public uint Packed => (uint)(Status | (Data1 << 8) | (Data2 << 16));
}

/// <summary>MIDI 出力ポートの抽象 — OS固有の実デバイス実装と
/// headless フォールバック (<see cref="NullMidiOut"/>) を差し替えられる。</summary>
public interface IMidiOut : IDisposable
{
    /// <summary>短メッセージを 1 つ送出する。</summary>
    void Send(MidiMessage msg);

    /// <summary>全ノートオフ (パニック)。</summary>
    void Reset();
}

/// <summary>何もしない MIDI 出力 (デバイスなし/テスト用)。</summary>
public sealed class NullMidiOut : IMidiOut
{
    public void Send(MidiMessage msg) { }
    public void Reset() { }
    public void Dispose() { }
}
