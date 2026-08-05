using Luxel.Graphics;
using Luxel.Resources;
using Luxel.Shaders;
using Luxel.Shaders.Slang.Native;

namespace Luxel.Gallery;

/// <summary>
/// Installs native Slang compilation without resolving the external tool until a story actually compiles a shader.
/// This keeps non-shader Gallery stories usable when the pinned toolchain has not been acquired yet.
/// </summary>
internal sealed class GallerySlangCompilation : ISlangCompiler, IDisposable
{
    private readonly object _gate = new();
    private NativeSlangCompiler? _compiler;
    private bool _disposed;

    public void Install(ResourceSystem resources, GpuBackendKind backend)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ObjectDisposedException.ThrowIf(_disposed, this);
        resources.AddStep<SlangSource, GpuShaderCode>(new SlangCompileStep(this, backend));
    }

    public Task<SlangCompilation> CompileAsync(
        SlangSource source,
        SlangCompileOptions options,
        CancellationToken cancellationToken = default)
        => Compiler.CompileAsync(source, options, cancellationToken);

    private NativeSlangCompiler Compiler
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_gate)
                return _compiler ??= new NativeSlangCompiler();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        NativeSlangCompiler? compiler;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            compiler = _compiler;
            _compiler = null;
        }
        if (compiler is not null)
            compiler.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
