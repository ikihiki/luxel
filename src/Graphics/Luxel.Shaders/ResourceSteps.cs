using System.Text;
using Luxel.Graphics;
using Luxel.Resources;

namespace Luxel.Shaders;

public sealed class SlangSourceStep : IResourceStep<byte[], SlangSource>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public IEnumerable<string> Extensions => [".slang", ".slangh"];

    public Task<SlangSource> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        try
        {
            return Task.FromResult(new SlangSource(uri.Path, StrictUtf8.GetString(input)));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ShaderCompilationException(
                $"Shader source '{uri.Path}' is not valid UTF-8.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Shader source is not valid UTF-8.", "SLANG_UTF8", uri.Path)],
                innerException: exception);
        }
    }
}

public sealed class SlangCompileStep(ISlangCompiler compiler, GpuBackendKind backend) : IResourceStep<SlangSource, GpuShaderCode>
{
    public IEnumerable<string> Extensions => [".slang"];
    public IEnumerable<string> FragmentPatterns => ["compute", "graphics"];

    public async Task<GpuShaderCode> RunAsync(SlangSource input, ResourceUri uri, LoadContext ctx)
    {
        ShaderProgramKind kind = uri.Fragment switch
        {
            "compute" => ShaderProgramKind.Compute,
            "graphics" => ShaderProgramKind.Graphics,
            _ => throw new ShaderCompilationException(
                $"Unsupported Slang program selector '#{uri.Fragment}'.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Use '#compute' or '#graphics'.", "SLANG_SELECTOR", uri.Path)]),
        };
        SlangCompilation compilation = await compiler.CompileAsync(input, SlangCompileOptions.ForBackend(backend, kind), ctx.Token).ConfigureAwait(false);
        return compilation.ToGpuShaderCode();
    }
}
