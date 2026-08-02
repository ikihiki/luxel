using System.Text;
using Luxel.Resources;
using Luxel.Shaders;

namespace LuxelPlaygroundBrowser;

/// <summary>Builds a Slang source graph from the current browser workspace snapshot.</summary>
internal sealed class WorkspaceSlangSourceStep(WorkspaceFileSystem workspace) : IResourceStep<byte[], SlangSource>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".slang", ".slangh"];

    private static bool IsSlangPath(string path)
        => Path.GetExtension(path).Equals(".slang", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".slangh", StringComparison.OrdinalIgnoreCase);

    public async Task<SlangSource> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        try
        {
            string rootPath = WorkspacePath.Normalize(uri.Path);
            WorkspaceFileSystemSnapshot snapshot = workspace.Snapshot();
            var supporting = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in snapshot.Files.Keys.Where(path =>
                         !string.Equals(path, rootPath, StringComparison.Ordinal) && IsSlangPath(path)))
            {
                using ResourceHandle<byte[]> dependency = ctx.Load<byte[]>($"workspace://{path}");
                await dependency.Ready.ConfigureAwait(false);
                supporting.Add(path, StrictUtf8.GetString(dependency.Value));
            }
            return new SlangSource(rootPath, StrictUtf8.GetString(input), supporting);
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
