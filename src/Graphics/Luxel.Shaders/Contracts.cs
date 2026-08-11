using System.Collections.ObjectModel;
using System.Text;
using Luxel.Graphics;

namespace Luxel.Shaders;

public enum ShaderProgramKind
{
    Compute,
    Graphics,
}

public enum SlangCompileTarget
{
    SpirV,
    Dxil,
    Wgsl,
}

public enum ShaderEntryPointStage
{
    Compute,
    Vertex,
    Pixel,
}

public enum ShaderDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ShaderDiagnostic(
    ShaderDiagnosticSeverity Severity,
    string Message,
    string? Code = null,
    string? Path = null,
    int? Line = null,
    int? Column = null);

public sealed class ShaderCompilationException : Exception
{
    public ShaderCompilationException(
        string message,
        IReadOnlyList<ShaderDiagnostic> diagnostics,
        int? exitCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Diagnostics = diagnostics;
        ExitCode = exitCode;
    }

    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }
    public int? ExitCode { get; }
}

public sealed record SlangEntryPoint(string Name, ShaderEntryPointStage Stage);

public sealed class SlangSource
{
    public SlangSource(string path, string text, IReadOnlyDictionary<string, string>? supportingFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = NormalizeRelativePath(path);
        Text = text ?? throw new ArgumentNullException(nameof(text));

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (supportingFiles is not null)
        {
            foreach ((string filePath, string contents) in supportingFiles)
            {
                string normalized = NormalizeRelativePath(filePath);
                if (string.Equals(normalized, Path, StringComparison.Ordinal))
                    throw new ArgumentException("The root source cannot also be a supporting file.", nameof(supportingFiles));
                files.Add(normalized, contents);
            }
        }
        SupportingFiles = new ReadOnlyDictionary<string, string>(files);
    }

    public string Path { get; }
    public string Text { get; }
    public IReadOnlyDictionary<string, string> SupportingFiles { get; }

    public static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':') || System.IO.Path.IsPathRooted(normalized))
            throw new ArgumentException("Shader paths must be non-empty relative paths.", nameof(path));

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Shader paths cannot contain '.' or '..' segments.", nameof(path));
        return string.Join('/', segments);
    }
}

public sealed class SlangCompileOptions
{
    public required ShaderProgramKind ProgramKind { get; init; }
    public required SlangCompileTarget Target { get; init; }
    public string SpirVProfile { get; init; } = SlangToolchain.SpirVProfile;
    public string DxilProfile { get; init; } = SlangToolchain.DxilProfile;
    public IReadOnlyList<SlangEntryPoint>? EntryPoints { get; init; }
    public IReadOnlyDictionary<string, string?> Defines { get; init; } = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());

    public IReadOnlyList<SlangEntryPoint> EffectiveEntryPoints => EntryPoints ?? (ProgramKind == ShaderProgramKind.Compute
        ? [new SlangEntryPoint("main", ShaderEntryPointStage.Compute)]
        : [new SlangEntryPoint("vsMain", ShaderEntryPointStage.Vertex), new SlangEntryPoint("psMain", ShaderEntryPointStage.Pixel)]);

    public string CanonicalDescriptor
    {
        get
        {
            var builder = new StringBuilder()
                .Append("target=").Append(Target)
                .Append(";program=").Append(ProgramKind)
                .Append(";spirv-profile=").Append(SpirVProfile)
                .Append(";dxil-profile=").Append(DxilProfile);
            foreach (SlangEntryPoint entry in EffectiveEntryPoints.OrderBy(entry => entry.Stage).ThenBy(entry => entry.Name, StringComparer.Ordinal))
                builder.Append(";entry=").Append(entry.Stage).Append(':').Append(entry.Name);
            foreach ((string name, string? value) in Defines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(";define=").Append(name).Append('=').Append(value);
            return builder.ToString();
        }
    }

    public static SlangCompileOptions ForBackend(GpuBackendKind backend, ShaderProgramKind programKind) => new()
    {
        ProgramKind = programKind,
        Target = backend switch
        {
            GpuBackendKind.Vulkan => SlangCompileTarget.SpirV,
            GpuBackendKind.D3D12 => SlangCompileTarget.Dxil,
            GpuBackendKind.WebGpu => SlangCompileTarget.Wgsl,
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        },
    };
}

public static class SlangToolchain
{
    public const string Version = "2026.14";
    public const string SpirVProfile = "spirv_1_6";
    public const string DxilProfile = "sm_6_6";
}

public sealed record SlangArtifact(string EntryPoint, ShaderEntryPointStage Stage, byte[] Code);

public sealed class SlangCompilation
{
    public SlangCompilation(
        SlangCompileTarget target,
        ShaderProgramKind programKind,
        IReadOnlyList<SlangArtifact> artifacts,
        IReadOnlyList<ShaderDiagnostic>? diagnostics = null)
    {
        Target = target;
        ProgramKind = programKind;
        Artifacts = artifacts;
        Diagnostics = diagnostics ?? Array.Empty<ShaderDiagnostic>();
    }

    public SlangCompileTarget Target { get; }
    public ShaderProgramKind ProgramKind { get; }
    public IReadOnlyList<SlangArtifact> Artifacts { get; }
    public IReadOnlyList<ShaderDiagnostic> Diagnostics { get; }

    public GpuShaderCode ToGpuShaderCode()
    {
        byte[] SingleModule() => Artifacts.Count == 1
            ? Artifacts[0].Code
            : throw new InvalidOperationException($"Expected one {Target} module, but received {Artifacts.Count} artifacts.");
        byte[] Stage(ShaderEntryPointStage stage) => Artifacts.Single(artifact => artifact.Stage == stage).Code;

        return Target switch
        {
            SlangCompileTarget.SpirV => new GpuShaderCode { SpirV = SingleModule() },
            SlangCompileTarget.Wgsl => new GpuShaderCode { Wgsl = SingleModule() },
            SlangCompileTarget.Dxil when ProgramKind == ShaderProgramKind.Compute => new GpuShaderCode { Dxil = Stage(ShaderEntryPointStage.Compute) },
            SlangCompileTarget.Dxil => new GpuShaderCode
            {
                DxilVertex = Stage(ShaderEntryPointStage.Vertex),
                DxilPixel = Stage(ShaderEntryPointStage.Pixel),
            },
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

public interface ISlangCompiler : IAsyncDisposable
{
    Task<SlangCompilation> CompileAsync(SlangSource source, SlangCompileOptions options, CancellationToken cancellationToken = default);
}
