using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Luxel.Controls;
using Luxel.Gallery.Playground;
using Luxel.Settings;

namespace Luxel.Gallery.Playground.Tests;

public sealed class NativeSlangLanguageServiceTests
{
    private static readonly PlaygroundDraft Draft = new PlaygroundTemplate(
        "slang", "Slang", "", "shader.slang",
        [new PlaygroundFile("shader", "shader.slang", "slang", "float4 main() { return 0; }", 0)]).CreateDraft();

    [Fact]
    public async Task Fake_server_initializes_syncs_and_maps_diagnostics_completion_and_hover()
    {
        var server = new FakeSlangServer(message =>
        {
            string method = Method(message);
            return method switch
            {
                "initialize" => [Response(message, new { capabilities = new { hoverProvider = true, completionProvider = new { } } })],
                "textDocument/didOpen" => [Notification("textDocument/publishDiagnostics", new
                {
                    uri = message.RootElement.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString(),
                    diagnostics = new[] { new
                    {
                        range = new { start = new { line = 1, character = 2 }, end = new { line = 1, character = 5 } },
                        severity = 1,
                        message = "expected expression",
                    } },
                })],
                "textDocument/completion" => [Response(message, new
                {
                    isIncomplete = false,
                    items = new[] { new { label = "float4", insertText = "float4", kind = 7 } },
                })],
                "textDocument/hover" => [Response(message, new { contents = new { kind = "markdown", value = "`float4`" } })],
                "shutdown" => [Response(message, (object?)null)],
                _ => [],
            };
        });
        var factory = new FakeConnectionFactory(server);
        var options = Options();
        await using var language = new NativeSlangCodeLanguage(
            new NativeSlangLanguageServiceCapability(true, "found", "/fake/slangd"), options, factory);
        language.SyncWorkspace(Draft);
        ICodeLanguage file = language.ForFile("shader.slang");

        IReadOnlyList<CodeDiagnostic> diagnostics = file.Diagnose(Draft.MainFile.Source);
        IReadOnlyList<CodeCompletion> completions = file.Complete(Draft.MainFile.Source, 3);
        string? hover = file.Hover(Draft.MainFile.Source, 3);

        Assert.Contains("initialize", server.Methods);
        Assert.Contains("initialized", server.Methods);
        Assert.Contains("textDocument/didOpen", server.Methods);
        Assert.Single(diagnostics);
        Assert.Equal(new CodeDiagnostic(2, 3, 3, "expected expression", true), diagnostics[0]);
        Assert.Equal(new CodeCompletion("float4", "float4", "Class"), Assert.Single(completions));
        Assert.Equal("`float4`", hover);
        Assert.True(File.Exists(Path.Combine(language.WorkspaceRoot, "shader.slang")));
    }

    [Fact]
    public async Task Workspace_changes_emit_didChange_and_didClose()
    {
        var server = new FakeSlangServer(message => Method(message) switch
        {
            "initialize" => [Response(message, new { capabilities = new { } })],
            "textDocument/completion" => [Response(message, Array.Empty<object>())],
            "shutdown" => [Response(message, (object?)null)],
            _ => [],
        });
        await using var language = new NativeSlangCodeLanguage(
            new NativeSlangLanguageServiceCapability(true, "found", "/fake/slangd"), Options(), new FakeConnectionFactory(server));
        language.SyncWorkspace(Draft);
        ICodeLanguage file = language.ForFile("shader.slang");
        _ = file.Complete(Draft.MainFile.Source, 2);
        _ = file.Complete("float4 main() { return 1; }", 2);
        PlaygroundDraft withoutSlang = new PlaygroundTemplate(
            "plain", "Plain", "", "Main.csx", [new PlaygroundFile("main", "Main.csx", "csharp", "return null;")]).CreateDraft();
        language.SyncWorkspace(withoutSlang);

        await WaitUntilAsync(() => server.Methods.Contains("textDocument/didClose"));

        Assert.Contains("textDocument/didChange", server.Methods);
        Assert.Contains("textDocument/didClose", server.Methods);
    }

    [Fact]
    public async Task Request_timeout_is_bounded_and_sends_cancel_without_fabricating_results()
    {
        var server = new FakeSlangServer(message => Method(message) switch
        {
            "initialize" => [Response(message, new { capabilities = new { } })],
            "shutdown" => [Response(message, (object?)null)],
            _ => [],
        });
        var options = Options(TimeSpan.FromMilliseconds(60));
        await using var language = new NativeSlangCodeLanguage(
            new NativeSlangLanguageServiceCapability(true, "found", "/fake/slangd"), options, new FakeConnectionFactory(server));
        language.SyncWorkspace(Draft);

        IReadOnlyList<CodeCompletion> completion = language.ForFile("shader.slang").Complete(Draft.MainFile.Source, 1);

        Assert.Empty(completion);
        await WaitUntilAsync(() => server.Methods.Contains("$/cancelRequest"));
    }

    [Fact]
    public async Task Crashed_server_is_restarted_once_and_request_is_replayed()
    {
        FakeSlangServer? first = null;
        first = new FakeSlangServer(message => Method(message) switch
        {
            "initialize" => [Response(message, new { capabilities = new { } })],
            "textDocument/completion" => Close(first!),
            _ => [],
        });
        var second = new FakeSlangServer(message => Method(message) switch
        {
            "initialize" => [Response(message, new { capabilities = new { } })],
            "textDocument/completion" => [Response(message, new[] { new { label = "restarted", kind = 3 } })],
            "shutdown" => [Response(message, (object?)null)],
            _ => [],
        });
        var factory = new FakeConnectionFactory(first, second);
        await using var language = new NativeSlangCodeLanguage(
            new NativeSlangLanguageServiceCapability(true, "found", "/fake/slangd"), Options(), factory);
        language.SyncWorkspace(Draft);

        CodeCompletion completion = Assert.Single(language.ForFile("shader.slang").Complete(Draft.MainFile.Source, 1));

        Assert.Equal("restarted", completion.Label);
        Assert.Equal(2, factory.StartCount);
    }

    [Fact]
    public async Task Native_session_owns_synchronizes_and_disposes_language_workspace()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "luxel-slangd-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string executable = Path.Combine(temporaryRoot, OperatingSystem.IsWindows() ? "slangd.exe" : "slangd");
        await File.WriteAllTextAsync(executable, "fake");
        var server = new FakeSlangServer(message => Method(message) switch
        {
            "initialize" => [Response(message, new { capabilities = new { } })],
            "shutdown" => [Response(message, (object?)null)],
            _ => [],
        });
        var options = new NativeSlangLanguageServiceOptions
        {
            ExecutablePath = executable,
            TemporaryRoot = temporaryRoot,
            StartupTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromMilliseconds(100),
            SynchronousWaitTimeout = TimeSpan.FromMilliseconds(250),
            ShutdownTimeout = TimeSpan.FromMilliseconds(100),
        };
        string workspaceRoot;
        await using (var session = new NativePlaygroundSession(
            new InMemoryFileStore(),
            new PlaygroundTemplate("owned", "Owned", "", "shader.slang", [new PlaygroundFile("shader.slang", "float x;")]),
            languageServiceOptions: options,
            languageServerFactory: new FakeConnectionFactory(server)))
        {
            workspaceRoot = session.SlangLanguage.WorkspaceRoot;
            session.UpdateFile("shader.slang", "float y;");
            Assert.Equal("float y;", await File.ReadAllTextAsync(Path.Combine(workspaceRoot, "shader.slang")));
        }

        Assert.False(Directory.Exists(workspaceRoot));
        Directory.Delete(temporaryRoot, recursive: true);
    }

    [Fact]
    public async Task Real_slangd_smoke_test_when_explicitly_enabled_and_available()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LUXEL_TEST_REAL_SLANGD"), "1", StringComparison.Ordinal)) return;
        NativeSlangLanguageServiceCapability capability = NativeSlangLanguageServiceDiscovery.Discover();
        if (!capability.IsAvailable) return;
        await using var language = new NativeSlangCodeLanguage(capability);
        language.SyncWorkspace(Draft);
        _ = language.ForFile("shader.slang").Diagnose(Draft.MainFile.Source);
    }

    private static NativeSlangLanguageServiceOptions Options(TimeSpan? requestTimeout = null) => new()
    {
        ExecutablePath = "/fake/slangd",
        StartupTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = requestTimeout ?? TimeSpan.FromMilliseconds(250),
        DiagnosticWaitTimeout = requestTimeout is null ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(30),
        ShutdownTimeout = requestTimeout is null ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(50),
    };

    private static string Method(JsonDocument message) => message.RootElement.GetProperty("method").GetString()!;

    private static byte[] Response(JsonDocument request, object? result)
        => Frame(new { jsonrpc = "2.0", id = request.RootElement.GetProperty("id").GetInt64(), result });

    private static byte[] Notification(string method, object parameters)
        => Frame(new { jsonrpc = "2.0", method, @params = parameters });

    private static IReadOnlyList<byte[]> Close(FakeSlangServer server)
    {
        server.CloseOutput();
        return [];
    }

    private static byte[] Frame(object message)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeConnectionFactory(params FakeSlangServer[] servers) : ISlangLanguageServerConnectionFactory
    {
        private int _index;
        public int StartCount { get; private set; }

        public ISlangLanguageServerConnection Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
        {
            StartCount++;
            return servers[Math.Min(_index++, servers.Length - 1)].Connect();
        }
    }

    private sealed class FakeSlangServer(Func<JsonDocument, IReadOnlyList<byte[]>> handler)
    {
        private FakeConnection? _connection;
        public ConcurrentQueue<string> Methods { get; } = new();

        public ISlangLanguageServerConnection Connect()
        {
            _connection = new FakeConnection(message =>
            {
                using JsonDocument json = JsonDocument.Parse(message);
                Methods.Enqueue(Method(json));
                return handler(json);
            });
            return _connection;
        }

        public void CloseOutput() => _connection?.CloseOutput();
    }

    private sealed class FakeConnection : ISlangLanguageServerConnection
    {
        private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
        private readonly ParsingWriteStream _input;
        private bool _exited;

        public FakeConnection(Func<byte[], IReadOnlyList<byte[]>> handler)
        {
            _input = new ParsingWriteStream(message =>
            {
                foreach (byte[] response in handler(message)) _output.Writer.TryWrite(response);
            });
            StandardOutput = new ChannelReadStream(_output.Reader);
        }

        public Stream StandardInput => _input;
        public Stream StandardOutput { get; }
        public bool HasExited => _exited;
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CloseOutput() { _exited = true; _output.Writer.TryComplete(); }
        public void Kill() => CloseOutput();
        public ValueTask DisposeAsync() { CloseOutput(); return ValueTask.CompletedTask; }
    }

    private sealed class ParsingWriteStream(Action<byte[]> message) : Stream
    {
        private readonly MemoryStream _buffer = new();
        private readonly object _gate = new();

        public override void Write(byte[] buffer, int offset, int count) => Accept(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => Accept(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Accept(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void Accept(ReadOnlySpan<byte> bytes)
        {
            lock (_gate)
            {
                _buffer.Position = _buffer.Length;
                _buffer.Write(bytes);
                while (TryTakeMessage(out byte[]? body)) message(body);
            }
        }

        private bool TryTakeMessage(out byte[] body)
        {
            byte[] data = _buffer.ToArray();
            int separator = Find(data, "\r\n\r\n"u8);
            if (separator < 0) { body = []; return false; }
            string header = Encoding.ASCII.GetString(data, 0, separator);
            string lengthLine = header.Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            int length = int.Parse(lengthLine.AsSpan("Content-Length:".Length).Trim());
            int bodyStart = separator + 4;
            if (data.Length < bodyStart + length) { body = []; return false; }
            body = data.AsSpan(bodyStart, length).ToArray();
            byte[] remaining = data.AsSpan(bodyStart + length).ToArray();
            _buffer.SetLength(0);
            _buffer.Write(remaining);
            return true;
        }

        private static int Find(byte[] data, ReadOnlySpan<byte> value)
        {
            for (int i = 0; i <= data.Length - value.Length; i++)
                if (data.AsSpan(i, value.Length).SequenceEqual(value)) return i;
            return -1;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class ChannelReadStream(ChannelReader<byte[]> reader) : Stream
    {
        private byte[]? _current;
        private int _offset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current is null || _offset == _current.Length)
            {
                if (!await reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!reader.TryRead(out _current)) continue;
                _offset = 0;
            }
            int count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
