using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Luxel.Terminal.Windows;

internal static class NativeMethods
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint HandleFlagInherit = 0x00000001;
    internal const int ProcThreadAttributePseudoConsole = 0x00020016;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord { internal short X; internal short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal int cb;
        internal string? lpReserved;
        internal string? lpDesktop;
        internal string? lpTitle;
        internal int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        internal short wShowWindow, cbReserved2;
        internal IntPtr lpReserved2;
        internal SafeFileHandle? hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx { internal StartupInfo StartupInfo; internal IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal SafeKernelObjectHandle hProcess;
        internal SafeKernelObjectHandle hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        internal ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    internal enum JobObjectInfoType { ExtendedLimitInformation = 9 }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out SafePseudoConsoleHandle pseudoConsole);

    [DllImport("kernel32.dll")]
    internal static extern int ResizePseudoConsole(SafePseudoConsoleHandle pseudoConsole, Coord size);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeKernelObjectHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(SafeKernelObjectHandle job, JobObjectInfoType type, ref JobObjectExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(SafeKernelObjectHandle job, SafeKernelObjectHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(SafeKernelObjectHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeKernelObjectHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(SafeKernelObjectHandle thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    internal static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), operation);
    internal static void ThrowIfFailed(bool success, string operation) { if (!success) throw Error(operation); }
    internal static void ThrowIfFailedHResult(int result, string operation) { if (result < 0) throw new Win32Exception(result, operation); }
}

internal sealed class SafePseudoConsoleHandle : SafeHandle
{
    private SafePseudoConsoleHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
    protected override bool ReleaseHandle() { NativeMethods.ClosePseudoConsole(handle); return true; }
}

internal sealed class SafeKernelObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeKernelObjectHandle() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
