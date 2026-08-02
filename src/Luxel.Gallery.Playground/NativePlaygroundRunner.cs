using Luxel.Scripting.Roslyn.Web;
using Luxel.Shaders;
using Luxel.UI;

namespace Luxel.Gallery.Playground;

public interface INativePlaygroundRunner
{
    Task<NativePlaygroundRunResult> RunAsync(
        NativePlaygroundSession session,
        CancellationToken cancellationToken = default);

    Task<NativePlaygroundRunResult> RunAsync(
        PlaygroundDraft draft,
        CancellationToken cancellationToken = default);
}

public sealed class NativePlaygroundRunCoordinator : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private bool _disposed;

    public async Task<bool> RunAsync(
        Func<CancellationToken, Task<NativePlaygroundRunResult>> run,
        Action<NativePlaygroundRunResult> publish)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(publish);
        (int generation, CancellationTokenSource cancellation) = BeginRun();
        CancellationToken token = cancellation.Token;
        try
        {
            NativePlaygroundRunResult result = await run(token);
            if (!IsCurrent(generation, token))
            {
                (result.Widget as IDisposable)?.Dispose();
                return false;
            }
            publish(result);
            return true;
        }
        catch (OperationCanceledException) when (!IsCurrent(generation, token))
        {
            return false;
        }
        finally
        {
            CompleteRun(cancellation);
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            _generation++;
            cancellation = _cancellation;
            _cancellation = null;
        }
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Cancel();
    }

    private (int Generation, CancellationTokenSource Cancellation) BeginRun()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource next;
        int generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = ++_generation;
            next = new CancellationTokenSource();
            previous = _cancellation;
            _cancellation = next;
        }
        previous?.Cancel();
        return (generation, next);
    }

    private void CompleteRun(CancellationTokenSource cancellation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        }
        cancellation.Dispose();
    }

    private bool IsCurrent(int generation, CancellationToken token)
    {
        lock (_sync)
            return !_disposed && !token.IsCancellationRequested && generation == _generation;
    }
}

/// <summary>In-process native adapter for the browser-neutral multi-document Roslyn pipeline.</summary>
public sealed class NativePlaygroundRunner : INativePlaygroundRunner
{
    private static readonly Lazy<WebScriptCompiler> DefaultCompiler = new(
        () => new WebScriptCompiler(LoadMetadataReferences()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly WebScriptCompiler _compiler;
    private readonly WebScriptExecutor _executor;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public NativePlaygroundRunner()
        : this(DefaultCompiler.Value, new WebScriptExecutor())
    {
    }

    public NativePlaygroundRunner(WebScriptCompiler compiler, WebScriptExecutor executor)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<NativePlaygroundRunResult> RunAsync(
        NativePlaygroundSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        PlaygroundDraft draft = session.Draft;
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ShaderDiagnostic> shaderDiagnostics = await session.ResourceSession
                .PreloadSlangAsync(draft, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (shaderDiagnostics.Any(diagnostic => diagnostic.Severity == ShaderDiagnosticSeverity.Error))
                return new NativePlaygroundRunResult(
                    false,
                    null,
                    [],
                    new WebScriptFailure("slang", "One or more Playground shaders failed to compile."),
                    shaderDiagnostics);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using IDisposable scope = WebScriptResources.Push(session.ResourceSession);
                NativePlaygroundRunResult result = RunCore(draft, shaderDiagnostics, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    public async Task<NativePlaygroundRunResult> RunAsync(
        PlaygroundDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => RunCore(draft, [], cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private NativePlaygroundRunResult RunCore(
        PlaygroundDraft draft,
        IReadOnlyList<ShaderDiagnostic> shaderDiagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaygroundFile entry = draft.MainFile;
        if (!string.Equals(Path.GetExtension(entry.Path), ".csx", StringComparison.OrdinalIgnoreCase))
            return new NativePlaygroundRunResult(
                false,
                null,
                [],
                new WebScriptFailure("validation", "The native Playground entry file must use the .csx extension.", FileName: entry.Path),
                shaderDiagnostics);

        var support = draft.Files
            .Where(file => file.Id != entry.Id &&
                string.Equals(Path.GetExtension(file.Path), ".cs", StringComparison.OrdinalIgnoreCase))
            .Select(file => new WebScriptDocument(file.Path, file.Source))
            .ToArray();
        var project = new WebScriptProject(new WebScriptDocument(entry.Path, entry.Source), support);
        WebScriptCompilation compilation = _compiler.Compile(project);
        cancellationToken.ThrowIfCancellationRequested();
        if (!compilation.Success || compilation.PeImage is null)
            return new NativePlaygroundRunResult(false, null, compilation.Diagnostics, null, shaderDiagnostics);

        WebScriptExecution execution = _executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
        cancellationToken.ThrowIfCancellationRequested();
        return new NativePlaygroundRunResult(
            execution.Success,
            execution.Widget,
            compilation.Diagnostics,
            execution.Failure,
            shaderDiagnostics);
    }

    private static IReadOnlyList<MetadataReferenceImage> LoadMetadataReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            foreach (string path in trusted.Split(Path.PathSeparator)) paths.Add(path);
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")) paths.Add(path);

        var references = new List<MetadataReferenceImage>(paths.Count);
        foreach (string path in paths.OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                references.Add(new MetadataReferenceImage(Path.GetFileName(path), File.ReadAllBytes(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Optional runtime images can disappear or be unreadable while the host is starting.
            }
        }
        return references;
    }
}

public sealed record NativePlaygroundRunResult(
    bool Success,
    Widget? Widget,
    IReadOnlyList<WebScriptDiagnostic> Diagnostics,
    WebScriptFailure? Failure,
    IReadOnlyList<ShaderDiagnostic>? ShaderDiagnostics = null);
