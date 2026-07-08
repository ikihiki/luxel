using System.Runtime.InteropServices;

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

/// <summary>MIDI 出力ポートの抽象 — 実デバイス (<see cref="WinMmMidiOut"/>) と
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

/// <summary>
/// Windows マルチメディア API (winmm midiOut*) を叩く実 MIDI 出力。**実デバイス依存** —
/// 自動テストは張らず、実機スモークで確認する。デバイス無し/非 Windows では
/// <see cref="OpenDefault"/> が <see cref="NullMidiOut"/> を返す。
/// </summary>
public sealed partial class WinMmMidiOut : IMidiOut
{
    private const uint MidiMapper = 0xFFFFFFFF;   // MIDI_MAPPER (-1)
    private nint _handle;

    public WinMmMidiOut(uint deviceId = MidiMapper)
    {
        uint r = midiOutOpen(out _handle, deviceId, nint.Zero, nint.Zero, 0);
        if (r != 0) throw new InvalidOperationException($"midiOutOpen 失敗 (MMRESULT={r})");
    }

    /// <summary>利用可能な MIDI 出力デバイスがあるか (Windows かつ 1 台以上)。</summary>
    public static bool HasDevice => OperatingSystem.IsWindows() && midiOutGetNumDevs() > 0;

    /// <summary>デバイスがあれば実出力、無ければ <see cref="NullMidiOut"/> を返す。</summary>
    public static IMidiOut OpenDefault() => HasDevice ? new WinMmMidiOut() : new NullMidiOut();

    public void Send(MidiMessage msg)
    {
        if (_handle != nint.Zero) midiOutShortMsg(_handle, msg.Packed);
    }

    public void Reset()
    {
        if (_handle != nint.Zero) midiOutReset(_handle);
    }

    public void Dispose()
    {
        if (_handle == nint.Zero) return;
        midiOutReset(_handle);
        midiOutClose(_handle);
        _handle = nint.Zero;
    }

    [LibraryImport("winmm.dll")] private static partial uint midiOutGetNumDevs();
    [LibraryImport("winmm.dll")] private static partial uint midiOutOpen(out nint handle, uint deviceId, nint callback, nint instance, uint flags);
    [LibraryImport("winmm.dll")] private static partial uint midiOutShortMsg(nint handle, uint msg);
    [LibraryImport("winmm.dll")] private static partial uint midiOutReset(nint handle);
    [LibraryImport("winmm.dll")] private static partial uint midiOutClose(nint handle);
}
