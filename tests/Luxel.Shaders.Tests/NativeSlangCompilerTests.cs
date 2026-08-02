using System.Text;
using Luxel.Shaders;
using Luxel.Shaders.Slang.Native;

namespace Luxel.Shaders.Tests;

public sealed class NativeSlangCompilerTests
{
    [Fact]
    public async Task WgslInvocationWritesSnapshotAndCleansTemporaryWorkspace()
    {
        using var temporaryRoot = new TemporaryDirectory();
        var runner = new FakeRunner(async (request, token) =>
        {
            Assert.True(File.Exists(request.Arguments[0]));
            Assert.True(File.Exists(Path.Combine(request.WorkingDirectory, "lib", "common.slang")));
            Assert.Contains("wgsl", request.Arguments);
            string output = OutputPath(request);
            await File.WriteAllTextAsync(output, "compiled-wgsl", token);
            return new SlangProcessResult(0, "", "");
        });
        await using var compiler = CreateCompiler(temporaryRoot.Path, runner);

        SlangCompilation result = await compiler.CompileAsync(
            new SlangSource("shaders/main.slang", "import common;", new Dictionary<string, string> { ["lib/common.slang"] = "struct Common {}" }),
            SlangCompileOptions.ForBackend(Luxel.Graphics.GpuBackendKind.WebGpu, ShaderProgramKind.Graphics));

        Assert.Equal("compiled-wgsl", Encoding.UTF8.GetString(result.Artifacts.Single().Code));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryRoot.Path));
    }

    [Fact]
    public async Task GraphicsDxilRunsVertexAndPixelCommands()
    {
        using var temporaryRoot = new TemporaryDirectory();
        var runner = new FakeRunner(async (request, token) =>
        {
            string stage = request.Arguments[request.Arguments.IndexOf("-stage") + 1];
            await File.WriteAllBytesAsync(OutputPath(request), Encoding.UTF8.GetBytes(stage), token);
            return new SlangProcessResult(0, "", "");
        });
        await using var compiler = CreateCompiler(temporaryRoot.Path, runner);

        SlangCompilation result = await compiler.CompileAsync(
            new SlangSource("main.slang", ""),
            SlangCompileOptions.ForBackend(Luxel.Graphics.GpuBackendKind.D3D12, ShaderProgramKind.Graphics));
        Luxel.Graphics.GpuShaderCode code = result.ToGpuShaderCode();

        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal("vertex", Encoding.UTF8.GetString(code.DxilVertex!));
        Assert.Equal("pixel", Encoding.UTF8.GetString(code.DxilPixel!));
        Assert.All(runner.Requests, request => Assert.Contains("sm_6_6", request.Arguments));
    }

    [Fact]
    public async Task FailedProcessProducesStructuredDiagnostics()
    {
        using var temporaryRoot = new TemporaryDirectory();
        var runner = new FakeRunner((request, _) =>
        {
            string input = request.Arguments[0];
            return Task.FromResult(new SlangProcessResult(1, "", $"{input}(12,7): error 30001: expected expression"));
        });
        await using var compiler = CreateCompiler(temporaryRoot.Path, runner);

        ShaderCompilationException exception = await Assert.ThrowsAsync<ShaderCompilationException>(() => compiler.CompileAsync(
            new SlangSource("shaders/main.slang", "broken"),
            SlangCompileOptions.ForBackend(Luxel.Graphics.GpuBackendKind.Vulkan, ShaderProgramKind.Compute)));

        ShaderDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(ShaderDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("30001", diagnostic.Code);
        Assert.Equal("shaders/main.slang", diagnostic.Path);
        Assert.Equal(12, diagnostic.Line);
        Assert.Equal(7, diagnostic.Column);
        Assert.Equal(1, exception.ExitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryRoot.Path));
    }

    [Fact]
    public async Task TimeoutCancelsRunnerAndCleansTemporaryWorkspace()
    {
        using var temporaryRoot = new TemporaryDirectory();
        var runner = new FakeRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        await using var compiler = CreateCompiler(temporaryRoot.Path, runner, TimeSpan.FromMilliseconds(30));

        ShaderCompilationException exception = await Assert.ThrowsAsync<ShaderCompilationException>(() => compiler.CompileAsync(
            new SlangSource("main.slang", ""),
            SlangCompileOptions.ForBackend(Luxel.Graphics.GpuBackendKind.WebGpu, ShaderProgramKind.Compute)));

        Assert.Equal("SLANG_TIMEOUT", Assert.Single(exception.Diagnostics).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryRoot.Path));
    }

    [Fact]
    public async Task CallerCancellationRemainsOperationCanceled()
    {
        using var temporaryRoot = new TemporaryDirectory();
        var runner = new FakeRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        await using var compiler = CreateCompiler(temporaryRoot.Path, runner);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(
            new SlangSource("main.slang", ""),
            SlangCompileOptions.ForBackend(Luxel.Graphics.GpuBackendKind.WebGpu, ShaderProgramKind.Compute), cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryRoot.Path));
    }

    private static NativeSlangCompiler CreateCompiler(string temporaryRoot, ISlangProcessRunner runner, TimeSpan? timeout = null)
        => new(new SlangNativeOptions { TemporaryRoot = temporaryRoot, Timeout = timeout ?? TimeSpan.FromSeconds(2) }, "fake-slangc", runner);

    private static string OutputPath(SlangProcessRequest request) => request.Arguments[request.Arguments.IndexOf("-o") + 1];

    private sealed class FakeRunner(Func<SlangProcessRequest, CancellationToken, Task<SlangProcessResult>> implementation) : ISlangProcessRunner
    {
        public List<SlangProcessRequest> Requests { get; } = [];

        public Task<SlangProcessResult> RunAsync(SlangProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return implementation(request, cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"luxel-shader-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (int index = 0; index < values.Count; index++)
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        return -1;
    }
}
