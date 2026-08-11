using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Shaders;

namespace Luxel.Shaders.Slang.Browser;

/// <summary>Browser implementation of <see cref="ISlangCompiler"/> backed by the official Slang WASM module.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserSlangCompiler : ISlangCompiler
{
    private const int MaxFiles = 128;
    private const int MaxSourceBytes = 2 * 1024 * 1024;
    private const int CompileTimeoutMilliseconds = 15_000;
    private static int _nextRequestId;
    private bool _disposed;

    public async Task<SlangCompilation> CompileAsync(
        SlangSource source,
        SlangCompileOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Target != SlangCompileTarget.Wgsl)
            throw new ArgumentException("The browser Slang compiler only supports the WGSL target.", nameof(options));
        if (source.SupportingFiles.Count + 1 > MaxFiles)
            throw LimitExceeded(source.Path, $"The Slang workspace contains {source.SupportingFiles.Count + 1} files; the limit is {MaxFiles}.", "SLANG_FILE_LIMIT");
        int sourceBytes = Encoding.UTF8.GetByteCount(source.Text);
        foreach (string text in source.SupportingFiles.Values)
        {
            sourceBytes = checked(sourceBytes + Encoding.UTF8.GetByteCount(text));
            if (sourceBytes > MaxSourceBytes)
                throw LimitExceeded(source.Path, $"The Slang workspace source exceeds the {MaxSourceBytes} byte limit.", "SLANG_SOURCE_LIMIT");
        }

        int requestId = Interlocked.Increment(ref _nextRequestId);
        var request = new BrowserCompileRequest(
            requestId,
            CompileTimeoutMilliseconds,
            source.Path,
            source.Text,
            source.SupportingFiles,
            options.ProgramKind.ToString().ToLowerInvariant(),
            options.EffectiveEntryPoints.Select(entry => new BrowserEntryPoint(
                entry.Name,
                entry.Stage.ToString().ToLowerInvariant())).ToArray(),
            options.Defines);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state => CancelCompile((int)state!), requestId);
        string responseJson;
        try
        {
            responseJson = await CompileJsonAsync(JsonSerializer.Serialize(
                request, BrowserSlangJsonContext.Default.BrowserCompileRequest));
        }
        catch (JSException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        BrowserCompileResponse response = JsonSerializer.Deserialize(
            responseJson, BrowserSlangJsonContext.Default.BrowserCompileResponse)
            ?? throw new ShaderCompilationException(
                "Slang/WASM returned an invalid response.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "The browser compiler response was empty.", "SLANG_WASM_RESPONSE", source.Path)]);

        ShaderDiagnostic[] diagnostics = response.Diagnostics.Select(diagnostic => new ShaderDiagnostic(
            ParseSeverity(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.Code,
            diagnostic.Path,
            diagnostic.Line,
            diagnostic.Column)).ToArray();
        if (!response.Success || string.IsNullOrEmpty(response.Wgsl))
            throw new ShaderCompilationException(response.Error ?? "Slang/WASM compilation failed.", diagnostics);

        string wgsl = WgslNormalizer.Normalize(response.Wgsl, options.ProgramKind, source.Path);
        SlangEntryPoint representative = options.EffectiveEntryPoints[0];
        return new SlangCompilation(
            SlangCompileTarget.Wgsl,
            options.ProgramKind,
            [new SlangArtifact(representative.Name, representative.Stage, Encoding.UTF8.GetBytes(wgsl))],
            diagnostics);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private static ShaderCompilationException LimitExceeded(string path, string message, string code)
        => new(message, [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, message, code, path)]);

    private static ShaderDiagnosticSeverity ParseSeverity(string? severity) => severity?.ToLowerInvariant() switch
    {
        "warning" => ShaderDiagnosticSeverity.Warning,
        "info" or "information" => ShaderDiagnosticSeverity.Info,
        _ => ShaderDiagnosticSeverity.Error,
    };

    internal sealed record BrowserCompileRequest(
        int RequestId,
        int TimeoutMs,
        string Path,
        string Source,
        IReadOnlyDictionary<string, string> SupportingFiles,
        string ProgramKind,
        IReadOnlyList<BrowserEntryPoint> EntryPoints,
        IReadOnlyDictionary<string, string?> Defines);
    internal sealed record BrowserEntryPoint(string Name, string Stage);
    internal sealed record BrowserCompileResponse(bool Success, string? Wgsl, BrowserDiagnostic[] Diagnostics, string? Error);
    internal sealed record BrowserDiagnostic(string? Severity, string Message, string? Code, string? Path, int? Line, int? Column);

    [JSImport("compile", "luxel-slang")]
    private static partial Task<string> CompileJsonAsync(string requestJson);

    [JSImport("cancel", "luxel-slang")]
    private static partial void CancelCompile(int requestId);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserSlangCompiler.BrowserCompileRequest))]
[JsonSerializable(typeof(BrowserSlangCompiler.BrowserCompileResponse))]
internal sealed partial class BrowserSlangJsonContext : JsonSerializerContext;
