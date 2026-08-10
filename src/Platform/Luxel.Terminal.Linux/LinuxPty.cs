using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Luxel.Terminal.Session;

namespace Luxel.Terminal.Linux;

/// <summary>A glibc x64 pseudo-terminal backed by <c>forkpty(3)</c>.</summary>
/// <remarks>
/// All child-side work after <c>forkpty</c> stays in the bundled native shim, which immediately calls
/// <c>chdir</c>/<c>execve</c> or <c>_exit</c>. The child never returns to the managed runtime after fork.
/// </remarks>
public sealed class LinuxPty : ITerminalPty
{
    private const int Eintr = 4;
    private const int Eio = 5;
    private const int Eagain = 11;
    private const int Esrch = 3;
    private const int FGetFl = 3;
    private const int FSetFl = 4;
    private const int ONonBlock = 0x800;
    private const int SigHup = 1;
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private const int WifSignaledMask = 0x7f;

    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private Task<TerminalExitStatus>? _exitTask;
    private int _pid;
    private bool _disposed;

    public int ProcessId { get { lock (_sync) return _pid; } }

    public Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("LinuxPty supports Linux x64 only.");
        if (options.Columns <= 0 || options.Columns > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(options), "Columns must fit a positive unsigned 16-bit value.");
        if (options.Rows <= 0 || options.Rows > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(options), "Rows must fit a positive unsigned 16-bit value.");

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pid != 0) throw new InvalidOperationException("The PTY has already been started.");
        }

        string executable = ResolveExecutable(options.FileName, options.Environment);
        string[] argv = [options.FileName, .. options.Arguments];
        Dictionary<string, string> environment = BuildEnvironment(options.Environment);

        using var nativeExecutable = new NativeString(executable);
        using var nativeArgv = new NativeStringArray(argv);
        using var nativeEnv = new NativeStringArray(environment.Select(static pair => $"{pair.Key}={pair.Value}"));
        using var nativeWorkingDirectory = new NativeString(options.WorkingDirectory);
        int master = -1;
        int pid = Native.luxel_forkpty(nativeExecutable.Pointer, nativeArgv.Pointer, nativeEnv.Pointer,
            nativeWorkingDirectory.Pointer, (ushort)options.Rows, (ushort)options.Columns, out master);
        if (pid == -1) throw LastError("forkpty");
        try
        {
            int flags = Native.fcntl(master, FGetFl, 0);
            if (flags == -1 || Native.fcntl(master, FSetFl, flags | ONonBlock) == -1) throw LastError("fcntl(O_NONBLOCK)");
            var handle = new SafeFileHandle((nint)master, ownsHandle: true);
            master = -1; // SafeFileHandle now owns the descriptor.
            lock (_sync)
            {
                if (_disposed)
                {
                    handle.Dispose();
                    KillProcessGroup(pid, SigKill);
                    throw new ObjectDisposedException(nameof(LinuxPty));
                }
                _pid = pid;
                _handle = handle;
                _exitTask = Task.Run(() => WaitForProcess(pid));
            }
            return Task.CompletedTask;
        }
        catch
        {
            if (master >= 0) Native.close(master);
            KillProcessGroup(pid, SigKill);
            ReapSynchronously(pid);
            throw;
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        SafeFileHandle handle = GetHandle();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = ReadNative(handle, buffer);
            if (count >= 0) return count;
            int error = Marshal.GetLastPInvokeError();
            if (error == Eio) return 0; // PTY slave closure is reported as EIO on Linux.
            if (error == Eintr) continue;
            if (error != Eagain) throw new Win32Exception(error, "read");
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        SafeFileHandle handle = GetHandle();
        int written = 0;
        while (written < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = WriteNative(handle, buffer[written..]);
            if (count > 0) { written += count; continue; }
            if (count == 0) throw new IOException("PTY write made no progress.");
            int error = Marshal.GetLastPInvokeError();
            if (error == Eintr) continue;
            if (error != Eagain) throw new Win32Exception(error, "write");
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (columns <= 0 || columns > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0 || rows > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(rows));
        SafeFileHandle handle = GetHandle();
        var size = new WinSize((ushort)rows, (ushort)columns, 0, 0);
        if (Native.ioctl(handle, Native.TiocsWinsz, ref size) == -1) throw LastError("ioctl(TIOCSWINSZ)");
        return ValueTask.CompletedTask;
    }

    public async Task<TerminalExitStatus> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        Task<TerminalExitStatus> task;
        lock (_sync) task = _exitTask ?? throw new InvalidOperationException("The PTY has not been started.");
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(TerminalCloseMode mode, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Task<TerminalExitStatus>? exitTask;
        int pid;
        lock (_sync) { pid = _pid; exitTask = _exitTask; }
        if (pid == 0 || exitTask is null || exitTask.IsCompleted) { CloseStream(); return; }

        if (mode == TerminalCloseMode.Detach) { CloseStream(); return; }

        int signal = mode == TerminalCloseMode.Graceful ? SigHup : SigTerm;
        try
        {
            SignalSessionMembers(pid, signal, includeLeader: false);
            try { await WaitForSessionMembersAsync(pid, TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false); }
            finally { KillProcessGroup(pid, signal); }

            try { await exitTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                SignalSessionMembers(pid, SigKill, includeLeader: false);
                KillProcessGroup(pid, SigKill);
                await exitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally { CloseStream(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        try { await CloseAsync(TerminalCloseMode.TerminateTree, TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        finally { CloseStream(); }
    }

    private static unsafe int ReadNative(SafeFileHandle handle, Memory<byte> buffer)
    {
        using MemoryHandle pin = buffer.Pin();
        return Native.read(handle, (nint)pin.Pointer, (nuint)buffer.Length);
    }

    private static unsafe int WriteNative(SafeFileHandle handle, ReadOnlyMemory<byte> buffer)
    {
        using MemoryHandle pin = buffer.Pin();
        return Native.write(handle, (nint)pin.Pointer, (nuint)buffer.Length);
    }

    private SafeFileHandle GetHandle()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle ?? throw new InvalidOperationException("The PTY has not been started.");
        }
    }

    private void CloseStream()
    {
        SafeFileHandle? handle;
        lock (_sync) { handle = _handle; _handle = null; }
        handle?.Dispose();
    }

    private static TerminalExitStatus WaitForProcess(int pid)
    {
        int result;
        int status;
        do result = Native.waitpid(pid, out status, 0); while (result == -1 && Marshal.GetLastPInvokeError() == Eintr);
        if (result == -1) return new TerminalExitStatus(-1, false, LastError("waitpid"));
        bool signaled = (status & WifSignaledMask) != 0;
        int exitCode = signaled ? 128 + (status & WifSignaledMask) : (status >> 8) & 0xff;
        return new TerminalExitStatus(exitCode, signaled);
    }

    private static void ReapSynchronously(int pid) { while (Native.waitpid(pid, out _, 0) == -1 && Marshal.GetLastPInvokeError() == Eintr) { } }

    private static void KillProcessGroup(int pid, int signal)
    {
        if (pid <= 0) return;
        if (Native.kill(-pid, signal) == -1 && Marshal.GetLastPInvokeError() != Esrch) throw LastError("kill");
    }

    private static void SignalSessionMembers(int sessionId, int signal, bool includeLeader)
    {
        foreach (int processId in GetSessionMembers(sessionId))
        {
            if (!includeLeader && processId == sessionId) continue;
            if (Native.kill(processId, signal) == -1 && Marshal.GetLastPInvokeError() != Esrch) throw LastError("kill");
        }
    }

    private static async Task WaitForSessionMembersAsync(int sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (GetSessionMembers(sessionId).Any(processId => processId != sessionId) && Environment.TickCount64 < deadline)
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<int> GetSessionMembers(int sessionId)
    {
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int processId)) continue;
            string stat;
            try { stat = File.ReadAllText(Path.Combine(directory, "stat")); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            int commandEnd = stat.LastIndexOf(')');
            if (commandEnd < 0 || commandEnd + 2 >= stat.Length) continue;
            string[] fields = stat[(commandEnd + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 3 && int.TryParse(fields[3], out int candidateSession) && candidateSession == sessionId)
                yield return processId;
        }
    }

    private static Dictionary<string, string> BuildEnvironment(IReadOnlyDictionary<string, string?> overrides)
    {
        var result = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(static entry => (string)entry.Key, static entry => (string?)entry.Value ?? string.Empty, StringComparer.Ordinal);
        foreach ((string key, string? value) in overrides)
        {
            if (key.Contains('=') || key.Contains('\0')) throw new ArgumentException("Environment variable names cannot contain '=' or NUL.", nameof(overrides));
            if (value is null) result.Remove(key); else result[key] = value;
        }
        return result;
    }

    private static string ResolveExecutable(string fileName, IReadOnlyDictionary<string, string?> overrides)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('\0')) throw new ArgumentException("An executable is required.", nameof(fileName));
        if (fileName.Contains('/')) return Path.GetFullPath(fileName);
        string path = overrides.TryGetValue("PATH", out string? replacement) ? replacement ?? string.Empty : Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (string directory in path.Split(':'))
        {
            string candidate = Path.Combine(directory.Length == 0 ? Environment.CurrentDirectory : directory, fileName);
            if (File.Exists(candidate) && Native.access(candidate, 1) == 0) return candidate;
        }
        throw new Win32Exception(2, $"Executable '{fileName}' was not found on PATH.");
    }

    private static Win32Exception LastError(string operation) => new(Marshal.GetLastPInvokeError(), operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort XPixels;
        public ushort YPixels;
        public WinSize(ushort rows, ushort columns, ushort xPixels, ushort yPixels)
            => (Rows, Columns, XPixels, YPixels) = (rows, columns, xPixels, yPixels);
    }

    private sealed class NativeString : IDisposable
    {
        public nint Pointer { get; }
        public NativeString(string? value) => Pointer = value is null ? nint.Zero : Marshal.StringToCoTaskMemUTF8(value);
        public void Dispose() { if (Pointer != nint.Zero) Marshal.FreeCoTaskMem(Pointer); }
    }

    private sealed class NativeStringArray : IDisposable
    {
        private readonly nint[] _strings;
        public nint Pointer { get; }
        public NativeStringArray(IEnumerable<string> values)
        {
            _strings = values.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            Pointer = Marshal.AllocCoTaskMem((_strings.Length + 1) * nint.Size);
            for (int i = 0; i < _strings.Length; i++) Marshal.WriteIntPtr(Pointer, i * nint.Size, _strings[i]);
            Marshal.WriteIntPtr(Pointer, _strings.Length * nint.Size, nint.Zero);
        }
        public void Dispose()
        {
            foreach (nint value in _strings) Marshal.FreeCoTaskMem(value);
            Marshal.FreeCoTaskMem(Pointer);
        }
    }

    private static class Native
    {
        internal const uint TiocsWinsz = 0x5414;
        [DllImport("luxelpty", SetLastError = true)]
        internal static extern int luxel_forkpty(nint path, nint argv, nint envp, nint cwd,
            ushort rows, ushort columns, out int master);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int fcntl(int fd, int command, int argument);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int read(SafeFileHandle fd, nint buffer, nuint count);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int write(SafeFileHandle fd, nint buffer, nuint count);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int waitpid(int pid, out int status, int options);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int kill(int pid, int signal);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int close(int fd);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
        [DllImport("libc.so.6", SetLastError = true)] internal static extern int ioctl(SafeFileHandle fd, uint request, ref WinSize size);
    }
}
