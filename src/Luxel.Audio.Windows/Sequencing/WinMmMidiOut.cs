using System.Runtime.InteropServices;
using Luxel.Audio.Sequencing;

namespace Luxel.Audio.Windows.Sequencing;

/// <summary>
/// Windows Multimedia API (winmm midiOut*) を使うMIDI出力バックエンド。
/// 自動テストでは実デバイスを要求せず、デバイスが無い場合は <see cref="OpenDefault"/> が
/// <see cref="NullMidiOut"/> を返す。
/// </summary>
public sealed partial class WinMmMidiOut : IMidiOut
{
    private const uint MidiMapper = 0xFFFFFFFF;
    private nint _handle;

    public WinMmMidiOut(uint deviceId = MidiMapper)
    {
        uint result = midiOutOpen(out _handle, deviceId, nint.Zero, nint.Zero, 0);
        if (result != 0) throw new InvalidOperationException($"midiOutOpen failed (MMRESULT={result}).");
    }

    public static bool HasDevice => OperatingSystem.IsWindows() && midiOutGetNumDevs() > 0;

    public static IMidiOut OpenDefault() => HasDevice ? new WinMmMidiOut() : new NullMidiOut();

    public void Send(MidiMessage message)
    {
        if (_handle != nint.Zero) midiOutShortMsg(_handle, message.Packed);
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
    [LibraryImport("winmm.dll")] private static partial uint midiOutShortMsg(nint handle, uint message);
    [LibraryImport("winmm.dll")] private static partial uint midiOutReset(nint handle);
    [LibraryImport("winmm.dll")] private static partial uint midiOutClose(nint handle);
}
