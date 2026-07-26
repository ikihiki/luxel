namespace Luxel.Platform;

/// <summary>Optional Win32-only raw key hook used by TSF before portable key dispatch.</summary>
public interface IWin32RawKeyInput
{
    Func<ushort, nint, bool>? KeyPreFilter { get; set; }
}
