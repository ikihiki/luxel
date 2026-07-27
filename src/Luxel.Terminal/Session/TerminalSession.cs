using System.Threading.Channels;
using Luxel.Terminal.Parsing;
using Luxel.Terminal.Screen;

namespace Luxel.Terminal.Session;

public enum TerminalSessionState { Created, Running, Closing, Exited, Disposed }
public enum TerminalCloseMode { Graceful, TerminateTree, Detach }

public sealed record TerminalLaunchOptions
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();
    public int Columns { get; init; } = 120;
    public int Rows { get; init; } = 30;
    public TerminalCloseMode CloseMode { get; init; } = TerminalCloseMode.TerminateTree;
    public TimeSpan GracefulCloseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public static TerminalLaunchOptions DefaultShell(int columns = 120, int rows = 30)
    {
        string file = OperatingSystem.IsWindows()
            ? System.Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : System.Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
        return new TerminalLaunchOptions { FileName = file, Columns = columns, Rows = rows };
    }
}

public readonly record struct TerminalExitStatus(int ExitCode, bool Terminated, Exception? Error = null);

public interface ITerminalPty : IAsyncDisposable
{
    Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken = default);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);
    Task<TerminalExitStatus> WaitForExitAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(TerminalCloseMode mode, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class TerminalSession : IAsyncDisposable
{
    private interface ICommand;
    private sealed record WriteCommand(byte[] Bytes) : ICommand;
    private sealed record ResizeCommand(int Columns, int Rows) : ICommand;
    private sealed record ResponseCommand(byte[] Bytes) : ICommand;

    private readonly ITerminalPty _pty;
    private readonly TerminalBuffer _buffer;
    private readonly VtParser _parser;
    private readonly Channel<ICommand> _commands = Channel.CreateBounded<ICommand>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private Task? _readTask, _commandTask, _exitTask;
    private TerminalSnapshot? _latest;
    private TerminalLaunchOptions? _options;
    private int _updatePending;

    public TerminalSessionState State { get; private set; } = TerminalSessionState.Created;
    public TerminalExitStatus? ExitStatus { get; private set; }
    public event Action? Updated;
    public event Action<TerminalExitStatus>? Exited;
    public TerminalBuffer Buffer => _buffer;

    public TerminalSession(ITerminalPty pty, int columns = 120, int rows = 30, int scrollbackLimit = 10_000)
    {
        _pty = pty; _buffer = new TerminalBuffer(columns, rows, scrollbackLimit); _parser = new VtParser(_buffer);
        _parser.Response += response => _commands.Writer.TryWrite(new ResponseCommand(response.ToArray()));
        _latest = _buffer.Snapshot();
    }

    public async Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken = default)
    {
        if (State != TerminalSessionState.Created) throw new InvalidOperationException("Terminal session has already been started.");
        _options = options; _buffer.Resize(options.Columns, options.Rows);
        await _pty.StartAsync(WithTerminalEnvironment(options), cancellationToken).ConfigureAwait(false);
        State = TerminalSessionState.Running;
        _readTask = ReadLoopAsync(_cts.Token); _commandTask = CommandLoopAsync(_cts.Token); _exitTask = ObserveExitAsync();
    }

    public TerminalSnapshot Snapshot()
    {
        lock (_sync) return _latest ?? _buffer.Snapshot();
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        => _commands.Writer.WriteAsync(new WriteCommand(bytes.ToArray()), cancellationToken);
    public ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
        => SendAsync(System.Text.Encoding.UTF8.GetBytes(text), cancellationToken);
    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        => _commands.Writer.WriteAsync(new ResizeCommand(columns, rows), cancellationToken);

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[32 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await _pty.ReadAsync(bytes, cancellationToken).ConfigureAwait(false); if (read == 0) break;
                _parser.Parse(bytes.AsSpan(0, read)); PublishSnapshot();
            }
            _parser.Flush(); PublishSnapshot();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { await CompleteAsync(new TerminalExitStatus(-1, false, ex)).ConfigureAwait(false); }
    }

    private async Task CommandLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ICommand command in _commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (command)
                {
                    case WriteCommand write: await _pty.WriteAsync(write.Bytes, cancellationToken).ConfigureAwait(false); break;
                    case ResponseCommand response: await _pty.WriteAsync(response.Bytes, cancellationToken).ConfigureAwait(false); break;
                    case ResizeCommand resize:
                        _buffer.Resize(resize.Columns, resize.Rows); PublishSnapshot();
                        await _pty.ResizeAsync(resize.Columns, resize.Rows, cancellationToken).ConfigureAwait(false); break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { await CompleteAsync(new TerminalExitStatus(-1, false, ex)).ConfigureAwait(false); }
    }

    private async Task ObserveExitAsync()
    {
        try { await CompleteAsync(await _pty.WaitForExitAsync(_cts.Token).ConfigureAwait(false)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception ex) { await CompleteAsync(new TerminalExitStatus(-1, false, ex)).ConfigureAwait(false); }
    }

    private void PublishSnapshot()
    {
        lock (_sync) _latest = _buffer.Snapshot();
        if (Interlocked.Exchange(ref _updatePending, 1) == 0) Updated?.Invoke();
    }
    public bool ConsumeUpdate()
    {
        bool value = Interlocked.Exchange(ref _updatePending, 0) != 0; return value;
    }

    private async Task CompleteAsync(TerminalExitStatus status)
    {
        bool notify;
        lock (_sync) { notify = State is TerminalSessionState.Running or TerminalSessionState.Closing; if (notify) { ExitStatus = status; State = TerminalSessionState.Exited; } }
        if (!notify) return; _commands.Writer.TryComplete(); Exited?.Invoke(status); await Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (State is TerminalSessionState.Disposed or TerminalSessionState.Exited or TerminalSessionState.Created) return;
        State = TerminalSessionState.Closing; _commands.Writer.TryComplete();
        TerminalLaunchOptions o = _options!;
        await _pty.CloseAsync(o.CloseMode, o.GracefulCloseTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (State == TerminalSessionState.Disposed) return;
        try { await CloseAsync().ConfigureAwait(false); } catch { }
        _cts.Cancel(); _commands.Writer.TryComplete();
        Task[] tasks = new[] { _readTask, _commandTask, _exitTask }.Where(t => t is not null).Cast<Task>().ToArray();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        await _pty.DisposeAsync().ConfigureAwait(false); _cts.Dispose(); State = TerminalSessionState.Disposed;
    }

    private static TerminalLaunchOptions WithTerminalEnvironment(TerminalLaunchOptions options)
    {
        var env = new Dictionary<string, string?>(options.Environment, StringComparer.OrdinalIgnoreCase);
        env.TryAdd("TERM", "xterm-256color"); env.TryAdd("COLORTERM", "truecolor"); env.TryAdd("TERM_PROGRAM", "Luxel.Terminal"); env.TryAdd("TERM_PROGRAM_VERSION", typeof(TerminalSession).Assembly.GetName().Version?.ToString() ?? "0");
        return options with { Environment = env };
    }
}
