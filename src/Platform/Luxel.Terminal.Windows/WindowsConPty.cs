using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Luxel.Terminal.Session;
using Microsoft.Win32.SafeHandles;

namespace Luxel.Terminal.Windows;

/// <summary>Windows 10 ConPTY implementation of <see cref="ITerminalPty"/>.</summary>
public sealed class WindowsConPty : ITerminalPty
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SafePseudoConsoleHandle? _pseudoConsole;
    private SafeKernelObjectHandle? _job;
    private FileStream? _input;
    private FileStream? _output;
    private Process? _process;
    private bool _terminated;
    private bool _detached;
    private int _started;
    private int _disposed;

    public async Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotWindows();
        ValidateDimensions(options.Columns, options.Rows);
        if (string.IsNullOrWhiteSpace(options.FileName)) throw new ArgumentException("A process file name is required.", nameof(options));
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) throw new InvalidOperationException("The ConPTY instance has already been started.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CreateAndStart(options, cancellationToken);
        }
        catch
        {
            DisposeResources(killProcess: true);
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        FileStream output = _output ?? throw NotStarted();
        return output.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        FileStream input = _input ?? throw NotStarted();
        return input.WriteAsync(buffer, cancellationToken);
    }

    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDimensions(columns, rows);
        SafePseudoConsoleHandle pseudoConsole = _pseudoConsole ?? throw NotStarted();
        NativeMethods.ThrowIfFailedHResult(NativeMethods.ResizePseudoConsole(pseudoConsole, ToCoord(columns, rows)), "ResizePseudoConsole failed.");
        return ValueTask.CompletedTask;
    }

    public async Task<TerminalExitStatus> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        Process process = _process ?? throw NotStarted();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new TerminalExitStatus(process.ExitCode, _terminated);
    }

    public async Task CloseAsync(TerminalCloseMode mode, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0) return;

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Process? process = _process;
            if (process is null || HasExited(process)) return;

            if (mode == TerminalCloseMode.Detach)
            {
                DisableKillOnJobClose();
                _detached = true;
                DisposeTransport();
                return;
            }

            if (mode == TerminalCloseMode.Graceful)
            {
                _input?.Dispose();
                _input = null;
                if (await WaitForExitWithinAsync(process, timeout, cancellationToken).ConfigureAwait(false)) return;
            }

            _terminated = true;
            if (_job is { IsInvalid: false, IsClosed: false })
            {
                NativeMethods.ThrowIfFailed(NativeMethods.TerminateJobObject(_job, 1), "TerminateJobObject failed.");
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Process? process = _process;
            bool running = process is not null && !HasExited(process);
            DisposeResources(killProcess: running && !_detached);
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }

    private void CreateAndStart(TerminalLaunchOptions options, CancellationToken cancellationToken)
    {
        NativeMethods.ThrowIfFailed(NativeMethods.CreatePipe(out SafeFileHandle ptyInput, out SafeFileHandle hostInput, IntPtr.Zero, 0), "Creating the ConPTY input pipe failed.");
        using (ptyInput)
        {
            try
            {
                NativeMethods.ThrowIfFailed(NativeMethods.CreatePipe(out SafeFileHandle hostOutput, out SafeFileHandle ptyOutput, IntPtr.Zero, 0), "Creating the ConPTY output pipe failed.");
                using (ptyOutput)
                {
                    try
                    {
                        NativeMethods.ThrowIfFailed(NativeMethods.SetHandleInformation(hostInput, NativeMethods.HandleFlagInherit, 0), "Clearing input pipe inheritance failed.");
                        NativeMethods.ThrowIfFailed(NativeMethods.SetHandleInformation(hostOutput, NativeMethods.HandleFlagInherit, 0), "Clearing output pipe inheritance failed.");
                        NativeMethods.ThrowIfFailedHResult(NativeMethods.CreatePseudoConsole(ToCoord(options.Columns, options.Rows), ptyInput, ptyOutput, 0, out SafePseudoConsoleHandle pseudoConsole), "CreatePseudoConsole failed.");
                        _pseudoConsole = pseudoConsole;

                        using var attributes = new ProcThreadAttributeList(pseudoConsole);
                        NativeMethods.StartupInfoEx startup = new()
                        {
                            StartupInfo = new NativeMethods.StartupInfo { cb = Marshal.SizeOf<NativeMethods.StartupInfoEx>() },
                            lpAttributeList = attributes.Pointer
                        };
                        using var environment = EnvironmentBlock.Create(options.Environment);
                        var commandLine = new StringBuilder(BuildCommandLine(options.FileName, options.Arguments));
                        uint flags = NativeMethods.ExtendedStartupInfoPresent | NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateSuspended;
                        NativeMethods.ThrowIfFailed(NativeMethods.CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                            environment.Pointer, options.WorkingDirectory, ref startup, out NativeMethods.ProcessInformation processInfo), "CreateProcessW failed.");

                        using (processInfo.hThread)
                        using (processInfo.hProcess)
                        {
                            try
                            {
                                _job = CreateKillOnCloseJob();
                                NativeMethods.ThrowIfFailed(NativeMethods.AssignProcessToJobObject(_job, processInfo.hProcess), "AssignProcessToJobObject failed.");
                                _process = Process.GetProcessById(checked((int)processInfo.dwProcessId));
                                cancellationToken.ThrowIfCancellationRequested();
                                if (NativeMethods.ResumeThread(processInfo.hThread) == uint.MaxValue) throw NativeMethods.Error("ResumeThread failed.");
                            }
                            catch
                            {
                                NativeMethods.TerminateProcess(processInfo.hProcess, 1);
                                throw;
                            }
                        }

                        _input = new FileStream(hostInput, FileAccess.Write, 4096, isAsync: false);
                        hostInput = null!; // FileStream owns the handle from here.
                        _output = new FileStream(hostOutput, FileAccess.Read, 4096, isAsync: false);
                        hostOutput = null!; // FileStream owns the handle from here.
                    }
                    finally { hostOutput?.Dispose(); }
                }
            }
            finally { hostInput?.Dispose(); }
        }
    }

    private static SafeKernelObjectHandle CreateKillOnCloseJob()
    {
        SafeKernelObjectHandle job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw NativeMethods.Error("CreateJobObjectW failed.");
        var info = new NativeMethods.JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;
        try
        {
            NativeMethods.ThrowIfFailed(NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
                ref info, checked((uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>())), "SetInformationJobObject failed.");
            return job;
        }
        catch { job.Dispose(); throw; }
    }

    private void DisableKillOnJobClose()
    {
        if (_job is not { IsInvalid: false, IsClosed: false } job) return;
        var info = new NativeMethods.JobObjectExtendedLimitInformation();
        NativeMethods.ThrowIfFailed(NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
            ref info, checked((uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>())), "Disabling job kill-on-close failed.");
        job.Dispose();
        _job = null;
    }

    private void DisposeResources(bool killProcess)
    {
        if (killProcess && _job is { IsInvalid: false, IsClosed: false }) _terminated = NativeMethods.TerminateJobObject(_job, 1);
        else if (killProcess && _process is { } process && !HasExited(process))
        {
            try { process.Kill(entireProcessTree: true); _terminated = true; } catch { }
        }
        DisposeTransport();
        _job?.Dispose(); _job = null;
        _process?.Dispose(); _process = null;
    }

    private void DisposeTransport()
    {
        _input?.Dispose(); _input = null;
        _output?.Dispose(); _output = null;
        _pseudoConsole?.Dispose(); _pseudoConsole = null;
    }

    private static async Task<bool> WaitForExitWithinAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout == Timeout.InfiniteTimeSpan) { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); return true; }
        try { await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
    }

    private static bool HasExited(Process process) { try { return process.HasExited; } catch (InvalidOperationException) { return true; } }
    private static InvalidOperationException NotStarted() => new("The ConPTY process has not been started.");
    private static void ThrowIfNotWindows() { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows ConPTY is only available on Windows."); }
    private static void ValidateDimensions(int columns, int rows)
    {
        if (columns is <= 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows is <= 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(rows));
    }
    private static NativeMethods.Coord ToCoord(int columns, int rows) => new() { X = checked((short)columns), Y = checked((short)rows) };

    internal static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments)
        => string.Join(' ', new[] { QuoteArgument(fileName) }.Concat(arguments.Select(QuoteArgument)));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length != 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    private sealed class ProcThreadAttributeList : IDisposable
    {
        private IntPtr _pointer;
        internal IntPtr Pointer => _pointer;
        internal ProcThreadAttributeList(SafePseudoConsoleHandle pseudoConsole)
        {
            nuint size = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            _pointer = Marshal.AllocHGlobal(checked((nint)size));
            try
            {
                NativeMethods.ThrowIfFailed(NativeMethods.InitializeProcThreadAttributeList(_pointer, 1, 0, ref size), "InitializeProcThreadAttributeList failed.");
                IntPtr value = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(value, pseudoConsole.DangerousGetHandle());
                    NativeMethods.ThrowIfFailed(NativeMethods.UpdateProcThreadAttribute(_pointer, 0,
                        new IntPtr(NativeMethods.ProcThreadAttributePseudoConsole), value, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute failed.");
                }
                finally { Marshal.FreeHGlobal(value); }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_pointer == IntPtr.Zero) return;
            NativeMethods.DeleteProcThreadAttributeList(_pointer);
            Marshal.FreeHGlobal(_pointer);
            _pointer = IntPtr.Zero;
        }
    }

    private sealed class EnvironmentBlock : IDisposable
    {
        private IntPtr _pointer;
        internal IntPtr Pointer => _pointer;
        private EnvironmentBlock(IntPtr pointer) => _pointer = pointer;
        internal static EnvironmentBlock Create(IReadOnlyDictionary<string, string?> changes)
        {
            var values = System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string? value) in changes)
            {
                if (key.Contains('=') || key.Contains('\0')) throw new ArgumentException($"Invalid environment variable name: {key}", nameof(changes));
                if (value is null) values.Remove(key); else if (value.Contains('\0')) throw new ArgumentException($"Environment variable '{key}' contains a null character.", nameof(changes)); else values[key] = value;
            }
            string block = string.Join('\0', values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
            return new EnvironmentBlock(Marshal.StringToHGlobalUni(block));
        }
        public void Dispose() { if (_pointer != IntPtr.Zero) { Marshal.FreeHGlobal(_pointer); _pointer = IntPtr.Zero; } }
    }
}
