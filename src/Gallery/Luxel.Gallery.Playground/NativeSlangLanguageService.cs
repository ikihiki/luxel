using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Shaders;

namespace Luxel.Gallery.Playground;

public sealed record NativeSlangLanguageServiceCapability(
    bool IsAvailable,
    string Message,
    string? ExecutablePath = null);

public sealed class NativeSlangLanguageServiceOptions
{
    public string? ExecutablePath { get; init; }
    public string? ToolRoot { get; init; }
    public string? TemporaryRoot { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan SynchronousWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan DiagnosticWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

public static class NativeSlangLanguageServiceDiscovery
{
    public static NativeSlangLanguageServiceCapability Discover(NativeSlangLanguageServiceOptions? options = null)
    {
        options ??= new NativeSlangLanguageServiceOptions();
        foreach (string candidate in Candidates(options).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return new NativeSlangLanguageServiceCapability(
                    true,
                    $"Pinned Slang {SlangToolchain.Version} language server is available.",
                    fullPath);
        }

        return new NativeSlangLanguageServiceCapability(
            false,
            $"Slang completion, diagnostics, and hover are unavailable: slangd from pinned Slang {SlangToolchain.Version} was not found.");
    }

    private static IEnumerable<string> Candidates(NativeSlangLanguageServiceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ExecutablePath)) yield return options.ExecutablePath;
        string? environment = Environment.GetEnvironmentVariable("SLANGD_PATH");
        if (!string.IsNullOrWhiteSpace(environment)) yield return environment;

        string executable = OperatingSystem.IsWindows() ? "slangd.exe" : "slangd";
        string rid = RuntimeIdentifier();
        foreach (string root in SearchRoots(options.ToolRoot))
        {
            yield return Path.Combine(root, SlangToolchain.Version, rid, "bin", executable);
            yield return Path.Combine(root, SlangToolchain.Version, "bin", executable);
            yield return Path.Combine(root, rid, "bin", executable);
            yield return Path.Combine(root, "bin", executable);
            yield return Path.Combine(root, SlangToolchain.Version, rid, "tools", executable);
        }

        string relative = Path.Combine("tools", "slang", SlangToolchain.Version, rid, "bin", executable);
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                yield return Path.Combine(directory.FullName, relative);
                yield return Path.Combine(directory.FullName, "tools", "slang", SlangToolchain.Version, "bin", executable);
                yield return Path.Combine(directory.FullName, "runtimes", rid, "native", executable);
            }
    }

    private static IEnumerable<string> SearchRoots(string? toolRoot)
    {
        if (string.IsNullOrWhiteSpace(toolRoot)) yield break;
        yield return toolRoot;
        yield return Path.Combine(toolRoot, "slang");
        yield return Path.Combine(toolRoot, "tools", "slang");
    }

    private static string RuntimeIdentifier()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };
        if (OperatingSystem.IsWindows()) return $"win-{architecture}";
        if (OperatingSystem.IsLinux()) return $"linux-{architecture}";
        if (OperatingSystem.IsMacOS()) return $"osx-{architecture}";
        return $"unknown-{architecture}";
    }
}

public sealed record SlangLanguageServiceProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record SlangLanguageServiceProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Short-lived process seam retained for capability probes.</summary>
public interface ISlangLanguageServiceProcess
{
    Task<SlangLanguageServiceProcessResult> RunAsync(
        SlangLanguageServiceProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SupervisedSlangLanguageServiceProcess : ISlangLanguageServiceProcess
{
    public async Task<SlangLanguageServiceProcessResult> RunAsync(
        SlangLanguageServiceProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = CreateStartInfo(request.FileName, request.Arguments, request.WorkingDirectory);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Failed to start '{request.FileName}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileNotFoundException($"Unable to start Slang language service at '{request.FileName}'.", request.FileName, exception);
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new SlangLanguageServiceProcessResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

public sealed class NativeSlangLanguageServiceProbe(ISlangLanguageServiceProcess process)
{
    public async Task<NativeSlangLanguageServiceCapability> ProbeAsync(
        NativeSlangLanguageServiceCapability discovered,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        if (!discovered.IsAvailable || string.IsNullOrWhiteSpace(discovered.ExecutablePath)) return discovered;
        try
        {
            SlangLanguageServiceProcessResult result = await process.RunAsync(
                new SlangLanguageServiceProcessRequest(
                    discovered.ExecutablePath,
                    ["--print-builtin-module", "core"],
                    Environment.CurrentDirectory,
                    timeout),
                cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? discovered
                : new NativeSlangLanguageServiceCapability(
                    false,
                    $"Slang completion and hover are unavailable: slangd exited with code {result.ExitCode}.",
                    discovered.ExecutablePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or OperationCanceledException)
        {
            return new NativeSlangLanguageServiceCapability(
                false,
                $"Slang completion and hover are unavailable: {exception.Message}",
                discovered.ExecutablePath);
        }
    }
}

public interface ISlangLanguageServerConnection : IAsyncDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    void Kill();
}

public interface ISlangLanguageServerConnectionFactory
{
    ISlangLanguageServerConnection Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory);
}

public sealed class SlangLanguageServerProcessFactory : ISlangLanguageServerConnectionFactory
{
    public ISlangLanguageServerConnection Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var process = new Process
        {
            StartInfo = SupervisedSlangLanguageServiceProcess.CreateStartInfo(executablePath, arguments, workingDirectory),
            EnableRaisingEvents = true,
        };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Failed to start '{executablePath}'.");
            _ = Task.Run(async () =>
            {
                try { await process.StandardError.ReadToEndAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
            });
            return new SlangLanguageServerProcessConnection(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private sealed class SlangLanguageServerProcessConnection(Process process) : ISlangLanguageServerConnection
    {
        public Stream StandardInput => process.StandardInput.BaseStream;
        public Stream StandardOutput => process.StandardOutput.BaseStream;
        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => process.WaitForExitAsync(cancellationToken);
        public void Kill() => SupervisedSlangLanguageServiceProcess.TryKill(process);
        public ValueTask DisposeAsync()
        {
            Kill();
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class SlangJsonRpcClient : IAsyncDisposable
{
    private static readonly byte[] HeaderSeparator = "\r\n\r\n"u8.ToArray();
    private readonly ISlangLanguageServerConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _reader;
    private long _nextId;
    private int _disposed;

    public SlangJsonRpcClient(ISlangLanguageServerConnection connection)
    {
        _connection = connection;
        _reader = Task.Run(ReadLoopAsync);
    }

    public event Action<string, JsonElement>? Notification;
    public bool HasExited => _connection.HasExited || _reader.IsCompleted;

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        long id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate JSON-RPC request ID.");
        try
        {
            await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token, _stopping.Token);
            try
            {
                return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                _pending.TryRemove(id, out _);
                try { await NotifyAsync("$/cancelRequest", new { id }, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken = default)
        => WriteAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _connection.StandardInput.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _connection.StandardInput.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _connection.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new SlangLanguageServerConnectionException("Failed to write to slangd.", exception);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                int contentLength = await ReadContentLengthAsync(_connection.StandardOutput, _stopping.Token).ConfigureAwait(false);
                byte[] body = new byte[contentLength];
                await ReadExactlyAsync(_connection.StandardOutput, body, _stopping.Token).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt64(out long id))
                {
                    if (_pending.TryRemove(id, out TaskCompletionSource<JsonElement>? completion))
                    {
                        if (root.TryGetProperty("error", out JsonElement error))
                            completion.TrySetException(new SlangLanguageServerResponseException(FormatError(error)));
                        else
                            completion.TrySetResult(root.TryGetProperty("result", out JsonElement result)
                                ? result.Clone()
                                : default);
                    }
                }
                else if (root.TryGetProperty("method", out JsonElement methodElement))
                {
                    string? method = methodElement.GetString();
                    if (method is not null)
                    {
                        JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value.Clone() : default;
                        try { Notification?.Invoke(method, parameters); }
                        catch (Exception) { }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            var closed = failure as SlangLanguageServerConnectionException
                ?? new SlangLanguageServerConnectionException("slangd closed its JSON-RPC stream.", failure);
            foreach ((long id, TaskCompletionSource<JsonElement> completion) in _pending)
                if (_pending.TryRemove(id, out _)) completion.TrySetException(closed);
        }
    }

    private static async Task<int> ReadContentLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var header = new MemoryStream();
        int matched = 0;
        var one = new byte[1];
        while (header.Length < 16 * 1024)
        {
            int count = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException();
            header.WriteByte(one[0]);
            matched = one[0] == HeaderSeparator[matched] ? matched + 1 : one[0] == HeaderSeparator[0] ? 1 : 0;
            if (matched == HeaderSeparator.Length) break;
        }
        if (matched != HeaderSeparator.Length) throw new InvalidDataException("LSP header exceeded 16 KiB.");
        string text = Encoding.ASCII.GetString(header.GetBuffer(), 0, checked((int)header.Length));
        foreach (string line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out int length)
                && length >= 0)
                return length;
        throw new InvalidDataException("LSP message is missing Content-Length.");
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static string FormatError(JsonElement error)
    {
        string message = error.TryGetProperty("message", out JsonElement value) ? value.GetString() ?? "Unknown error" : "Unknown error";
        return error.TryGetProperty("code", out JsonElement code) ? $"slangd error {code}: {message}" : $"slangd error: {message}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping.Cancel();
        _connection.Kill();
        try { await _reader.ConfigureAwait(false); }
        catch (Exception) { }
        await _connection.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _stopping.Dispose();
    }
}

public sealed class SlangLanguageServerConnectionException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed class SlangLanguageServerResponseException(string message) : InvalidOperationException(message);

/// <summary>
/// File-aware adapter for the official slangd stdio LSP. The adapter projects the current Playground
/// workspace to an owned temporary directory, keeps documents synchronized, caches published
/// diagnostics, and bounds synchronous editor calls so a stalled server cannot block indefinitely.
/// </summary>
public sealed class NativeSlangCodeLanguage : ICodeLanguage, IDisposable, IAsyncDisposable
{
    private readonly NativeSlangLanguageServiceOptions _options;
    private readonly ISlangLanguageServerConnectionFactory _factory;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, ProjectedDocument> _desired = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectedDocument> _opened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _projectedFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CodeDiagnostic>> _diagnostics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _diagnosticWaiters = new(StringComparer.Ordinal);
    private readonly string _workspaceRoot;
    private SlangJsonRpcClient? _client;
    private string? _defaultFilePath;
    private bool _disposed;

    public NativeSlangCodeLanguage(
        NativeSlangLanguageServiceCapability capability,
        NativeSlangLanguageServiceOptions? options = null,
        ISlangLanguageServerConnectionFactory? factory = null)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _options = options ?? new NativeSlangLanguageServiceOptions { ExecutablePath = capability.ExecutablePath };
        _factory = factory ?? new SlangLanguageServerProcessFactory();
        string tempBase = _options.TemporaryRoot ?? Path.GetTempPath();
        _workspaceRoot = Path.Combine(tempBase, "luxel-slangd", Guid.NewGuid().ToString("N"));
        if (Capability.IsAvailable) Directory.CreateDirectory(_workspaceRoot);
    }

    public NativeSlangLanguageServiceCapability Capability { get; }
    public string WorkspaceRoot => _workspaceRoot;

    public ICodeLanguage ForFile(string path)
    {
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(path);
        return new BoundLanguage(this, () => normalized);
    }

    public ICodeLanguage ForFile(Func<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new BoundLanguage(this, path);
    }

    public void SyncWorkspace(PlaygroundDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfDisposed();
        if (!Capability.IsAvailable)
        {
            lock (_gate)
                _defaultFilePath = IsSlang(draft.SelectedFile.Path, draft.SelectedFile.Language)
                    ? draft.SelectedFile.Path
                    : draft.Files.FirstOrDefault(file => IsSlang(file.Path, file.Language))?.Path;
            return;
        }
        lock (_gate)
        {
            var nextProjectedFiles = draft.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
            foreach (string removed in _projectedFiles.Except(nextProjectedFiles).ToArray()) DeleteProjectedFile(removed);
            _projectedFiles.Clear();
            _projectedFiles.UnionWith(nextProjectedFiles);
            _desired.Clear();
            foreach (PlaygroundFile file in draft.Files)
            {
                WriteProjectedFile(file.Path, file.Source);
                if (IsSlang(file.Path, file.Language))
                    _desired[file.Path] = new ProjectedDocument(file.Path, file.Source, checked((int)Math.Min(int.MaxValue, file.Version + 1)));
            }
            _defaultFilePath = IsSlang(draft.SelectedFile.Path, draft.SelectedFile.Language)
                ? draft.SelectedFile.Path
                : _desired.Keys.FirstOrDefault();
        }
        if (Capability.IsAvailable) _ = Task.Run(PrefetchAsync);
    }

    public IReadOnlyList<CodeCompletion> Complete(string code, int position)
        => Complete(_defaultFilePath, code, position);

    public IReadOnlyList<CodeDiagnostic> Diagnose(string code)
        => Diagnose(_defaultFilePath, code);

    public string? Hover(string code, int position)
        => Hover(_defaultFilePath, code, position);

    internal IReadOnlyList<CodeCompletion> Complete(string? path, string code, int position)
    {
        if (!CanQuery(path)) return [];
        try
        {
            return RunBounded(async () =>
            {
                await SetSourceAndSynchronizeAsync(path!, code).ConfigureAwait(false);
                JsonElement result = await RequestWithRestartAsync("textDocument/completion", new
                {
                    textDocument = new { uri = ToUri(path!) },
                    position = ToPosition(code, position),
                    context = new { triggerKind = 1 },
                }).ConfigureAwait(false);
                return ParseCompletions(result);
            }, []);
        }
        catch (Exception exception) when (IsExpectedLanguageServerFailure(exception)) { return []; }
    }

    internal IReadOnlyList<CodeDiagnostic> Diagnose(string? path, string code)
    {
        if (!CanQuery(path)) return [];
        try
        {
            return RunBounded(async () =>
            {
                await SetSourceAndSynchronizeAsync(path!, code).ConfigureAwait(false);
                Task wait;
                lock (_gate)
                    wait = _diagnosticWaiters.TryGetValue(path!, out TaskCompletionSource<bool>? waiter)
                        ? waiter.Task
                        : Task.CompletedTask;
                try { await wait.WaitAsync(_options.DiagnosticWaitTimeout).ConfigureAwait(false); }
                catch (TimeoutException) { }
                lock (_gate)
                    return _diagnostics.TryGetValue(path!, out IReadOnlyList<CodeDiagnostic>? diagnostics)
                        ? diagnostics
                        : [];
            }, []);
        }
        catch (Exception exception) when (IsExpectedLanguageServerFailure(exception))
        {
            lock (_gate)
                return path is not null && _diagnostics.TryGetValue(path, out IReadOnlyList<CodeDiagnostic>? diagnostics)
                    ? diagnostics
                    : [];
        }
    }

    internal string? Hover(string? path, string code, int position)
    {
        if (!CanQuery(path)) return null;
        try
        {
            return RunBounded(async () =>
            {
                await SetSourceAndSynchronizeAsync(path!, code).ConfigureAwait(false);
                JsonElement result = await RequestWithRestartAsync("textDocument/hover", new
                {
                    textDocument = new { uri = ToUri(path!) },
                    position = ToPosition(code, position),
                }).ConfigureAwait(false);
                return ParseHover(result);
            }, null);
        }
        catch (Exception exception) when (IsExpectedLanguageServerFailure(exception)) { return null; }
    }

    private bool CanQuery(string? path) => Capability.IsAvailable && !string.IsNullOrWhiteSpace(Capability.ExecutablePath)
        && !string.IsNullOrWhiteSpace(path) && !_disposed;

    private T RunBounded<T>(Func<Task<T>> action, T fallback)
    {
        try { return action().WaitAsync(_options.SynchronousWaitTimeout).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return fallback; }
        catch (OperationCanceledException) { return fallback; }
    }

    private async Task PrefetchAsync()
    {
        try
        {
            await _operation.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_disposed) await EnsureSynchronizedCoreAsync().ConfigureAwait(false);
            }
            finally { _operation.Release(); }
        }
        catch (Exception exception) when (IsExpectedLanguageServerFailure(exception)) { }
    }

    private async Task SetSourceAndSynchronizeAsync(string path, string source)
    {
        lock (_gate)
        {
            int version = _desired.TryGetValue(path, out ProjectedDocument? current) ? current.Version : 1;
            if (current is null || current.Source != source) version = checked(version + 1);
            _desired[path] = new ProjectedDocument(path, source, version);
            WriteProjectedFile(path, source);
        }
        await _operation.WaitAsync().ConfigureAwait(false);
        try { await EnsureSynchronizedCoreAsync().ConfigureAwait(false); }
        finally { _operation.Release(); }
    }

    private async Task<JsonElement> RequestWithRestartAsync(string method, object parameters)
    {
        for (int attempt = 0; ; attempt++)
        {
            await _operation.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureSynchronizedCoreAsync().ConfigureAwait(false);
                return await _client!.RequestAsync(method, parameters, _options.RequestTimeout).ConfigureAwait(false);
            }
            catch (SlangLanguageServerConnectionException) when (attempt == 0)
            {
                await ResetClientCoreAsync().ConfigureAwait(false);
            }
            finally { _operation.Release(); }
        }
    }

    private async Task EnsureSynchronizedCoreAsync()
    {
        await EnsureClientCoreAsync().ConfigureAwait(false);
        Dictionary<string, ProjectedDocument> desired;
        Dictionary<string, ProjectedDocument> opened;
        lock (_gate)
        {
            desired = new Dictionary<string, ProjectedDocument>(_desired, StringComparer.Ordinal);
            opened = new Dictionary<string, ProjectedDocument>(_opened, StringComparer.Ordinal);
        }

        foreach ((string path, ProjectedDocument document) in opened)
        {
            if (desired.ContainsKey(path)) continue;
            await _client!.NotifyAsync("textDocument/didClose", new { textDocument = new { uri = ToUri(path) } }).ConfigureAwait(false);
            lock (_gate)
            {
                _opened.Remove(path);
                _diagnostics.Remove(path);
            }
        }

        foreach ((string path, ProjectedDocument document) in desired)
        {
            if (!opened.TryGetValue(path, out ProjectedDocument? prior))
            {
                ResetDiagnosticWaiter(path);
                await _client!.NotifyAsync("textDocument/didOpen", new
                {
                    textDocument = new { uri = ToUri(path), languageId = "slang", version = document.Version, text = document.Source },
                }).ConfigureAwait(false);
                lock (_gate) _opened[path] = document;
            }
            else if (prior.Source != document.Source || prior.Version != document.Version)
            {
                ResetDiagnosticWaiter(path);
                await _client!.NotifyAsync("textDocument/didChange", new
                {
                    textDocument = new { uri = ToUri(path), version = document.Version },
                    contentChanges = new[] { new { text = document.Source } },
                }).ConfigureAwait(false);
                lock (_gate) _opened[path] = document;
            }
        }
    }

    private async Task EnsureClientCoreAsync()
    {
        if (_client is not null && !_client.HasExited) return;
        await ResetClientCoreAsync().ConfigureAwait(false);
        string executable = Capability.ExecutablePath!;
        ISlangLanguageServerConnection connection;
        try { connection = _factory.Start(executable, _options.Arguments, _workspaceRoot); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new SlangLanguageServerConnectionException($"Unable to start slangd at '{executable}'.", exception);
        }
        _client = new SlangJsonRpcClient(connection);
        _client.Notification += OnNotification;
        JsonElement initialize = await _client.RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            clientInfo = new { name = "Luxel", version = "1" },
            rootUri = new Uri(_workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri,
            workspaceFolders = new[] { new { uri = new Uri(_workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri, name = "Luxel Playground" } },
            capabilities = new
            {
                general = new { positionEncodings = new[] { "utf-16" } },
                textDocument = new
                {
                    synchronization = new { didSave = false, dynamicRegistration = false },
                    completion = new { completionItem = new { snippetSupport = false } },
                    hover = new { contentFormat = new[] { "markdown", "plaintext" } },
                    publishDiagnostics = new { relatedInformation = true },
                },
                workspace = new { workspaceFolders = true },
            },
        }, _options.StartupTimeout).ConfigureAwait(false);
        _ = initialize;
        await _client.NotifyAsync("initialized", new { }).ConfigureAwait(false);
        lock (_gate) _opened.Clear();
    }

    private async Task ResetClientCoreAsync()
    {
        SlangJsonRpcClient? client = _client;
        _client = null;
        lock (_gate) _opened.Clear();
        if (client is not null)
        {
            client.Notification -= OnNotification;
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ResetDiagnosticWaiter(string path)
    {
        lock (_gate)
            _diagnosticWaiters[path] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void OnNotification(string method, JsonElement parameters)
    {
        if (method != "textDocument/publishDiagnostics" || parameters.ValueKind != JsonValueKind.Object) return;
        if (!parameters.TryGetProperty("uri", out JsonElement uriElement)) return;
        string? uriText = uriElement.GetString();
        if (uriText is null || !Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri) || !uri.IsFile) return;
        string relative;
        try { relative = Path.GetRelativePath(_workspaceRoot, uri.LocalPath).Replace('\\', '/'); }
        catch (ArgumentException) { return; }
        if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..") return;
        IReadOnlyList<CodeDiagnostic> diagnostics = parameters.TryGetProperty("diagnostics", out JsonElement array)
            ? ParseDiagnostics(array)
            : [];
        lock (_gate)
        {
            _diagnostics[relative] = diagnostics;
            if (_diagnosticWaiters.Remove(relative, out TaskCompletionSource<bool>? waiter)) waiter.TrySetResult(true);
        }
    }

    private static IReadOnlyList<CodeCompletion> ParseCompletions(JsonElement result)
    {
        JsonElement items = result;
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out JsonElement value)) items = value;
        if (items.ValueKind != JsonValueKind.Array) return [];
        var output = new List<CodeCompletion>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("label", out JsonElement labelElement)) continue;
            string? label = labelElement.GetString();
            if (string.IsNullOrEmpty(label)) continue;
            string insertText = item.TryGetProperty("insertText", out JsonElement insert) ? insert.GetString() ?? label : label;
            if (item.TryGetProperty("textEdit", out JsonElement edit) && edit.ValueKind == JsonValueKind.Object
                && edit.TryGetProperty("newText", out JsonElement newText))
                insertText = newText.GetString() ?? insertText;
            string kind = item.TryGetProperty("kind", out JsonElement kindElement) && kindElement.TryGetInt32(out int kindValue)
                ? CompletionKind(kindValue)
                : "Text";
            output.Add(new CodeCompletion(label, insertText, kind));
        }
        return output;
    }

    private static IReadOnlyList<CodeDiagnostic> ParseDiagnostics(JsonElement diagnostics)
    {
        if (diagnostics.ValueKind != JsonValueKind.Array) return [];
        var output = new List<CodeDiagnostic>();
        foreach (JsonElement diagnostic in diagnostics.EnumerateArray())
        {
            if (!diagnostic.TryGetProperty("range", out JsonElement range)
                || !range.TryGetProperty("start", out JsonElement start)
                || !start.TryGetProperty("line", out JsonElement lineElement)
                || !start.TryGetProperty("character", out JsonElement characterElement)) continue;
            int line = lineElement.GetInt32();
            int character = characterElement.GetInt32();
            int length = 1;
            if (range.TryGetProperty("end", out JsonElement end)
                && end.TryGetProperty("line", out JsonElement endLine)
                && endLine.GetInt32() == line
                && end.TryGetProperty("character", out JsonElement endCharacter))
                length = Math.Max(1, endCharacter.GetInt32() - character);
            string message = diagnostic.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString() ?? "Slang diagnostic"
                : "Slang diagnostic";
            bool isError = !diagnostic.TryGetProperty("severity", out JsonElement severity) || severity.GetInt32() == 1;
            output.Add(new CodeDiagnostic(line + 1, character + 1, length, message, isError));
        }
        return output;
    }

    private static string? ParseHover(JsonElement result)
    {
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        JsonElement contents = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("contents", out JsonElement value)
            ? value
            : result;
        return HoverText(contents);
    }

    private static string? HoverText(JsonElement contents)
    {
        if (contents.ValueKind == JsonValueKind.String) return contents.GetString();
        if (contents.ValueKind == JsonValueKind.Object && contents.TryGetProperty("value", out JsonElement value)) return value.GetString();
        if (contents.ValueKind == JsonValueKind.Array)
        {
            string[] parts = contents.EnumerateArray().Select(HoverText).Where(text => !string.IsNullOrWhiteSpace(text)).Cast<string>().ToArray();
            return parts.Length == 0 ? null : string.Join("\n\n", parts);
        }
        return null;
    }

    private static object ToPosition(string code, int offset)
    {
        int clamped = Math.Clamp(offset, 0, code.Length);
        int line = 0;
        int lineStart = 0;
        for (int i = 0; i < clamped; i++)
            if (code[i] == '\n') { line++; lineStart = i + 1; }
        return new { line, character = clamped - lineStart };
    }

    private static string CompletionKind(int kind) => kind switch
    {
        2 => "Method", 3 => "Function", 4 => "Constructor", 5 => "Field", 6 => "Variable",
        7 => "Class", 8 => "Interface", 9 => "Module", 10 => "Property", 13 => "Enum",
        14 => "Keyword", 20 => "EnumMember", 21 => "Constant", 22 => "Struct", 25 => "TypeParameter",
        _ => "Text",
    };

    private string ToUri(string path) => new Uri(Path.Combine(_workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;

    private void WriteProjectedFile(string path, string source)
    {
        string fullPath = Path.Combine(_workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, source, new UTF8Encoding(false));
    }

    private void DeleteProjectedFile(string path)
    {
        string fullPath = Path.Combine(_workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
        try { File.Delete(fullPath); }
        catch (DirectoryNotFoundException) { }
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && !string.Equals(directory, _workspaceRoot, StringComparison.Ordinal))
        {
            try { Directory.Delete(directory); }
            catch (IOException) { break; }
            catch (UnauthorizedAccessException) { break; }
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsSlang(string path, string language)
        => string.Equals(language, "slang", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".slang", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".slangh", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedLanguageServerFailure(Exception exception)
        => exception is SlangLanguageServerConnectionException or SlangLanguageServerResponseException
            or IOException or InvalidDataException or TimeoutException or OperationCanceledException
            or ObjectDisposedException;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is not null && !_client.HasExited)
            {
                try { await _client.RequestAsync("shutdown", null, _options.ShutdownTimeout).ConfigureAwait(false); }
                catch (Exception exception) when (IsExpectedLanguageServerFailure(exception)) { }
                try { await _client.NotifyAsync("exit", null).ConfigureAwait(false); }
                catch (Exception exception) when (IsExpectedLanguageServerFailure(exception)) { }
            }
            await ResetClientCoreAsync().ConfigureAwait(false);
        }
        finally { _operation.Release(); }
        try { Directory.Delete(_workspaceRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static NativeSlangCodeLanguage CreateDefault(
        NativeSlangLanguageServiceOptions? options = null,
        ISlangLanguageServerConnectionFactory? factory = null)
    {
        options ??= new NativeSlangLanguageServiceOptions();
        return new NativeSlangCodeLanguage(NativeSlangLanguageServiceDiscovery.Discover(options), options, factory);
    }

    private sealed record ProjectedDocument(string Path, string Source, int Version);

    private sealed class BoundLanguage(NativeSlangCodeLanguage owner, Func<string> path) : ICodeLanguage
    {
        public IReadOnlyList<CodeCompletion> Complete(string code, int position) => owner.Complete(path(), code, position);
        public IReadOnlyList<CodeDiagnostic> Diagnose(string code) => owner.Diagnose(path(), code);
        public string? Hover(string code, int position) => owner.Hover(path(), code, position);
    }
}
