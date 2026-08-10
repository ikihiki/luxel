using System.Text.RegularExpressions;
using Luxel.Shaders;

namespace Luxel.Shaders.Slang.Native;

public sealed class NativeSlangCompiler : ISlangCompiler
{
    private static readonly Regex DiagnosticPattern = new(
        @"^(?<path>.+?)(?:\((?<line>\d+)(?:,(?<column>\d+))?\)|:(?<line2>\d+):(?<column2>\d+)):\s*(?<severity>note|info|warning|error|fatal error)(?:\s+(?<code>\w+))?:\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly SlangNativeOptions _options;
    private readonly ISlangProcessRunner _runner;
    private readonly string _slangcPath;
    private bool _disposed;

    public NativeSlangCompiler(SlangNativeOptions? options = null)
    {
        _options = options ?? new SlangNativeOptions();
        ValidateOptions(_options);
        _slangcPath = SlangToolDiscovery.Resolve(_options);
        _runner = new SlangProcessRunner();
    }

    internal NativeSlangCompiler(SlangNativeOptions options, string slangcPath, ISlangProcessRunner runner)
    {
        _options = options;
        ValidateOptions(options);
        _slangcPath = slangcPath;
        _runner = runner;
    }

    public async Task<SlangCompilation> CompileAsync(
        SlangSource source,
        SlangCompileOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ValidateCompileOptions(options);

        string temporaryRoot = _options.TemporaryRoot ?? Path.GetTempPath();
        Directory.CreateDirectory(temporaryRoot);
        string workspace = Path.Combine(temporaryRoot, $"luxel-slang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            await WriteSourcesAsync(workspace, source, cancellationToken).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(_options.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                return await CompileWorkspaceAsync(workspace, source, options, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new ShaderCompilationException(
                    $"slangc exceeded the {_options.Timeout.TotalSeconds:0.###} second timeout.",
                    [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Shader compilation timed out.", "SLANG_TIMEOUT", source.Path)]);
            }
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async Task<SlangCompilation> CompileWorkspaceAsync(
        string workspace,
        SlangSource source,
        SlangCompileOptions options,
        CancellationToken cancellationToken)
    {
        string inputPath = Path.Combine(workspace, source.Path.Replace('/', Path.DirectorySeparatorChar));
        if (options.Target == SlangCompileTarget.Dxil)
        {
            var artifacts = new List<SlangArtifact>();
            var diagnostics = new List<ShaderDiagnostic>();
            foreach (SlangEntryPoint entryPoint in options.EffectiveEntryPoints)
            {
                string outputPath = Path.Combine(workspace, $"output-{entryPoint.Stage}.dxil");
                List<string> arguments = BuildArguments(inputPath, outputPath, options, entryPoint);
                SlangProcessResult result = await RunAsync(workspace, arguments, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(ParseDiagnostics(result, workspace));
                EnsureSuccess(result, diagnostics, source.Path);
                artifacts.Add(new SlangArtifact(entryPoint.Name, entryPoint.Stage,
                    await ReadArtifactAsync(outputPath, source.Path, cancellationToken).ConfigureAwait(false)));
            }
            return new SlangCompilation(options.Target, options.ProgramKind, artifacts, diagnostics);
        }
        else
        {
            string extension = options.Target == SlangCompileTarget.SpirV ? ".spv" : ".wgsl";
            string outputPath = Path.Combine(workspace, "output" + extension);
            List<string> arguments = BuildArguments(inputPath, outputPath, options, entryPoint: null);
            SlangProcessResult result = await RunAsync(workspace, arguments, cancellationToken).ConfigureAwait(false);
            List<ShaderDiagnostic> diagnostics = ParseDiagnostics(result, workspace);
            EnsureSuccess(result, diagnostics, source.Path);
            byte[] code = await ReadArtifactAsync(outputPath, source.Path, cancellationToken).ConfigureAwait(false);
            SlangEntryPoint representative = options.EffectiveEntryPoints[0];
            return new SlangCompilation(options.Target, options.ProgramKind,
                [new SlangArtifact(representative.Name, representative.Stage, code)], diagnostics);
        }
    }

    private async Task<SlangProcessResult> RunAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(
                new SlangProcessRequest(_slangcPath, arguments, workspace, _options.EnvironmentVariables),
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException exception)
        {
            throw new ShaderCompilationException(
                $"Pinned slangc {SlangToolchain.Version} is unavailable.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, exception.Message, "SLANG_TOOL_UNAVAILABLE")],
                innerException: exception);
        }
    }

    private static async Task<byte[]> ReadArtifactAsync(string outputPath, string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
            throw new ShaderCompilationException(
                "slangc exited successfully without producing the requested output.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Compiler output is missing.", "SLANG_OUTPUT_MISSING", sourcePath)]);
        return await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
    }

    private static List<string> BuildArguments(
        string inputPath,
        string outputPath,
        SlangCompileOptions options,
        SlangEntryPoint? entryPoint)
    {
        var arguments = new List<string> { inputPath, "-target" };
        switch (options.Target)
        {
            case SlangCompileTarget.SpirV:
                arguments.AddRange(["spirv", "-emit-spirv-directly", "-profile", options.SpirVProfile,
                    "-force-glsl-scalar-layout", "-fvk-use-entrypoint-name"]);
                break;
            case SlangCompileTarget.Dxil:
                arguments.AddRange(["dxil", "-profile", options.DxilProfile, "-fvk-use-entrypoint-name"]);
                break;
            case SlangCompileTarget.Wgsl:
                arguments.Add("wgsl");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        foreach ((string name, string? value) in options.Defines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            arguments.Add(value is null ? $"-D{name}" : $"-D{name}={value}");

        if (entryPoint is not null)
        {
            arguments.AddRange(["-entry", entryPoint.Name, "-stage", StageArgument(entryPoint.Stage)]);
            if (options.Target == SlangCompileTarget.Dxil)
                arguments.AddRange(["-Xdxc", "-validator-version", "-Xdxc", "1.7"]);
        }
        arguments.AddRange(["-o", outputPath]);
        return arguments;
    }

    private static string StageArgument(ShaderEntryPointStage stage) => stage switch
    {
        ShaderEntryPointStage.Compute => "compute",
        ShaderEntryPointStage.Vertex => "vertex",
        ShaderEntryPointStage.Pixel => "pixel",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static async Task WriteSourcesAsync(string workspace, SlangSource source, CancellationToken cancellationToken)
    {
        await WriteSourceAsync(workspace, source.Path, source.Text, cancellationToken).ConfigureAwait(false);
        foreach ((string path, string text) in source.SupportingFiles)
            await WriteSourceAsync(workspace, path, text, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSourceAsync(string workspace, string relativePath, string text, CancellationToken cancellationToken)
    {
        string path = Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
    }

    private static List<ShaderDiagnostic> ParseDiagnostics(SlangProcessResult result, string workspace)
    {
        var diagnostics = new List<ShaderDiagnostic>();
        ParseText(result.StandardError, workspace, diagnostics);
        ParseText(result.StandardOutput, workspace, diagnostics);
        return diagnostics;
    }

    private static void ParseText(string text, string workspace, List<ShaderDiagnostic> diagnostics)
    {
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = DiagnosticPattern.Match(rawLine);
            if (!match.Success) continue;
            string severityText = match.Groups["severity"].Value;
            ShaderDiagnosticSeverity severity = severityText.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? ShaderDiagnosticSeverity.Warning
                : severityText.Contains("error", StringComparison.OrdinalIgnoreCase)
                    ? ShaderDiagnosticSeverity.Error
                    : ShaderDiagnosticSeverity.Info;
            string path = NormalizeDiagnosticPath(match.Groups["path"].Value, workspace);
            diagnostics.Add(new ShaderDiagnostic(
                severity,
                match.Groups["message"].Value,
                EmptyToNull(match.Groups["code"].Value),
                path,
                ParsePositiveInt(match.Groups["line"].Success ? match.Groups["line"].Value : match.Groups["line2"].Value),
                ParsePositiveInt(match.Groups["column"].Success ? match.Groups["column"].Value : match.Groups["column2"].Value)));
        }
    }

    private static void EnsureSuccess(SlangProcessResult result, List<ShaderDiagnostic> diagnostics, string sourcePath)
    {
        if (result.ExitCode == 0) return;
        if (diagnostics.Count == 0)
        {
            string message = FirstNonEmptyLine(result.StandardError) ?? FirstNonEmptyLine(result.StandardOutput) ?? "slangc failed without diagnostics.";
            diagnostics.Add(new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, message, "SLANGC_EXIT", sourcePath));
        }
        throw new ShaderCompilationException($"slangc failed with exit code {result.ExitCode}.", diagnostics, result.ExitCode);
    }

    private static string NormalizeDiagnosticPath(string path, string workspace)
    {
        string fullWorkspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate;
        try { candidate = Path.GetFullPath(path); }
        catch { return path.Replace('\\', '/'); }
        return candidate.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(workspace, candidate).Replace('\\', '/')
            : path.Replace('\\', '/');
    }

    private static int? ParsePositiveInt(string value) => int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? FirstNonEmptyLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static void ValidateOptions(SlangNativeOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero && options.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive or infinite.");
    }

    private static void ValidateCompileOptions(SlangCompileOptions options)
    {
        IReadOnlyList<SlangEntryPoint> entries = options.EffectiveEntryPoints;
        if (entries.Count == 0) throw new ArgumentException("At least one entry point is required.", nameof(options));
        if (options.Target == SlangCompileTarget.Dxil)
        {
            if (options.ProgramKind == ShaderProgramKind.Compute && (entries.Count != 1 || entries[0].Stage != ShaderEntryPointStage.Compute))
                throw new ArgumentException("DXIL compute compilation requires one compute entry point.", nameof(options));
            if (options.ProgramKind == ShaderProgramKind.Graphics &&
                (entries.Count != 2 || entries.Count(entry => entry.Stage == ShaderEntryPointStage.Vertex) != 1 || entries.Count(entry => entry.Stage == ShaderEntryPointStage.Pixel) != 1))
                throw new ArgumentException("DXIL graphics compilation requires one vertex and one pixel entry point.", nameof(options));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
