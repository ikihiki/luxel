using System.Text;
using Luxel.AssetsGpu;
using Luxel.Graphics;
using Luxel.Imaging;
using Luxel.Resources;
using Luxel.Shaders;
using Luxel.Shaders.Slang.Native;
using Luxel.Scripting.Roslyn.Web;

namespace Luxel.Gallery.Playground;


public sealed class NativePlaygroundResourceOptions
{
    public HttpClient? HttpClient { get; init; }
    public GpuDevice? GpuDevice { get; init; }
    public GpuBackendKind? BackendKind { get; init; }
    public ISlangCompiler? SlangCompiler { get; init; }
    public bool OwnsSlangCompiler { get; init; }
    public static NativePlaygroundResourceOptions ForGpu(
        GpuDevice device,
        HttpClient? httpClient = null,
        SlangNativeOptions? slangOptions = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        try
        {
            return new NativePlaygroundResourceOptions
            {
                HttpClient = httpClient,
                GpuDevice = device,
                BackendKind = device.BackendKind,
                SlangCompiler = new NativeSlangCompiler(slangOptions),
                OwnsSlangCompiler = true,
            };
        }
        catch (FileNotFoundException)
        {
            return new NativePlaygroundResourceOptions
            {
                HttpClient = httpClient,
                GpuDevice = device,
                BackendKind = device.BackendKind,
            };
        }
    }
}

/// <summary>
/// Session-owned native workspace resources. It deliberately installs WorkspaceSource and HttpSource,
/// never FileSource, so scripts cannot escape the editable workspace through the resource facade.
/// </summary>
public sealed class NativePlaygroundResourceSession : IWebScriptResourceProvider, IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ISlangCompiler? _compiler;
    private readonly bool _ownsCompiler;
    private readonly GpuBackendKind? _backendKind;
    private readonly List<IDisposable> _preloadedShaders = [];
    private readonly Dictionary<(Type Type, string Name), object> _scriptResources = [];
    private bool _disposed;

    public NativePlaygroundResourceSession(PlaygroundDraft draft, NativePlaygroundResourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        options ??= new NativePlaygroundResourceOptions();
        Workspace = new WorkspaceFileSystem();
        SyncWorkspace(draft);

        _http = options.HttpClient ?? new HttpClient();
        _ownsHttp = options.HttpClient is null;
        _compiler = options.SlangCompiler;
        _ownsCompiler = options.OwnsSlangCompiler;
        GpuBackendKind? backendKind = options.BackendKind ?? options.GpuDevice?.BackendKind;
        _backendKind = backendKind;

        var steps = new List<IResourceStep>
        {
            new TexDecoder(),
            new ImageSharpDecoder(),
            new WorkspaceSlangSourceStep(Workspace),
        };
        if (_compiler is not null && backendKind is not null)
            steps.Add(new SlangCompileStep(_compiler, backendKind.Value));

        Resources = new ResourceSystem(
            sources: [new WorkspaceSource(Workspace), new HttpSource(_http)],
            steps: steps);
        if (options.GpuDevice is not null)
            Resources.InstallAssetGpu(options.GpuDevice);
        Resources.Watch();

        SlangCompilationAvailable = _compiler is not null && backendKind is not null;
        SlangStatus = SlangCompilationAvailable
            ? $"Pinned Slang {SlangToolchain.Version} compilation is available for {backendKind}."
            : backendKind is null
                ? "Slang compilation is unavailable because this Playground session has no GPU backend context."
                : $"Pinned slangc {SlangToolchain.Version} is unavailable.";
    }

    public WorkspaceFileSystem Workspace { get; }
    public ResourceSystem Resources { get; }
    public bool SlangCompilationAvailable { get; }
    public string SlangStatus { get; }

    public ResourceHandle<T> Load<T>(string uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Resources.Load<T>(uri);
    }

    public bool TryGet<T>(string name, out WebScriptResource<T>? resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scriptResources.TryGetValue((typeof(T), name), out object? value))
        {
            resource = (WebScriptResource<T>)value;
            return true;
        }
        resource = null;
        return false;
    }

    public void SyncWorkspace(PlaygroundDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IDisposable handle in _preloadedShaders) handle.Dispose();
        _preloadedShaders.Clear();
        _scriptResources.Clear();

        WorkspaceFileSystemSnapshot previous = Workspace.Snapshot();
        var operations = new List<WorkspaceFileOperation>(previous.Files.Count + draft.Files.Count);
        operations.AddRange(previous.Files.Keys.Select(path => (WorkspaceFileOperation)new WorkspaceDeleteOperation(path)));
        operations.AddRange(draft.Files.Select(file => (WorkspaceFileOperation)new WorkspaceSetOperation(
            file.Path, Encoding.UTF8.GetBytes(file.Source))));
        if (operations.Count > 0) Workspace.ApplyBatch(operations);
        if (Resources is not null) Resources.Pump();
    }

    internal async Task<IReadOnlyList<ShaderDiagnostic>> PreloadSlangAsync(
        PlaygroundDraft draft,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IDisposable handle in _preloadedShaders) handle.Dispose();
        _preloadedShaders.Clear();
        _scriptResources.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        if (!draft.Files.Any(IsSlangRoot)) return [];
        if (!SlangCompilationAvailable)
            return [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, SlangStatus, "SLANG_TOOL_UNAVAILABLE")];

        var diagnostics = new List<ShaderDiagnostic>();
        foreach (PlaygroundFile file in draft.Files.Where(IsSlangRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string selector = IsGraphicsShader(file.Source) ? "graphics" : "compute";
            ResourceHandle<GpuShaderCode> handle = Resources.Load<GpuShaderCode>($"workspace://{file.Path}#{selector}");
            _preloadedShaders.Add(handle);
            try
            {
                await handle.Ready.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = new WebScriptResourceMetadata(
                    handle.Uri.ToString(),
                    file.Path,
                    selector,
                    typeof(GpuShaderCode).FullName ?? nameof(GpuShaderCode),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["workspaceRevision"] = draft.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["fileVersion"] = file.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["programKind"] = selector,
                        ["backend"] = _backendKind?.ToString() ?? "unknown",
                    });
                var resource = new WebScriptResource<GpuShaderCode>(handle.Value, metadata);
                _scriptResources[(typeof(GpuShaderCode), file.Path)] = resource;
                _scriptResources[(typeof(GpuShaderCode), metadata.Uri)] = resource;
            }
            catch (ShaderCompilationException exception)
            {
                diagnostics.AddRange(exception.Diagnostics.Count > 0
                    ? exception.Diagnostics
                    : [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, exception.Message, "SLANG", file.Path)]);
            }
        }
        Resources.Pump();
        cancellationToken.ThrowIfCancellationRequested();
        return diagnostics;
    }

    private static bool IsSlangRoot(PlaygroundFile file)
        => string.Equals(file.Language, "slang", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(file.Path).Equals(".slang", StringComparison.OrdinalIgnoreCase);

    private static bool IsGraphicsShader(string source)
        => (source.Contains("[shader(\"vertex\")]", StringComparison.Ordinal)
                && source.Contains("[shader(\"fragment\")]", StringComparison.Ordinal))
            || (source.Contains("vsMain", StringComparison.Ordinal) && source.Contains("psMain", StringComparison.Ordinal));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (IDisposable handle in _preloadedShaders) handle.Dispose();
        _preloadedShaders.Clear();
        Resources.Dispose();
        if (_ownsCompiler && _compiler is not null) _ = DisposeCompilerAsync(_compiler);
        if (_ownsHttp) _http.Dispose();
    }

    private static async Task DisposeCompilerAsync(ISlangCompiler compiler)
    {
        try
        {
            await compiler.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to dispose the native Playground Slang compiler asynchronously: {exception}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (IDisposable handle in _preloadedShaders) handle.Dispose();
        _preloadedShaders.Clear();
        Resources.Dispose();
        if (_ownsCompiler && _compiler is not null) await _compiler.DisposeAsync().ConfigureAwait(false);
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>Builds a Slang source graph from the complete current workspace snapshot.</summary>
internal sealed class WorkspaceSlangSourceStep(WorkspaceFileSystem workspace) : IResourceStep<byte[], SlangSource>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public IEnumerable<string> Extensions => [".slang", ".slangh"];

    public Task<SlangSource> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        try
        {
            string rootPath = WorkspacePath.Normalize(uri.Path);
            var supporting = workspace.Snapshot().Files
                .Where(pair => !string.Equals(pair.Key, rootPath, StringComparison.Ordinal) && IsSlangPath(pair.Key))
                .ToDictionary(pair => pair.Key, pair => StrictUtf8.GetString(pair.Value.Span), StringComparer.Ordinal);
            return Task.FromResult(new SlangSource(rootPath, StrictUtf8.GetString(input), supporting));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ShaderCompilationException(
                $"Shader source '{uri.Path}' is not valid UTF-8.",
                [new ShaderDiagnostic(ShaderDiagnosticSeverity.Error, "Shader source is not valid UTF-8.", "SLANG_UTF8", uri.Path)],
                innerException: exception);
        }
    }

    private static bool IsSlangPath(string path)
        => Path.GetExtension(path).Equals(".slang", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".slangh", StringComparison.OrdinalIgnoreCase);
}
