using Luxel.UI;

namespace Luxel.Scripting.Roslyn.Web;

/// <summary>The single entry point implemented by every generated playground assembly.</summary>
public interface ILuxelWebScriptProgram
{
    Widget Build();
}

public static class WebScriptOutput
{
    private static Action<string>? _sink;

    public static void SetSink(Action<string>? sink) => _sink = sink;

    public static void Write(string message)
        => _sink?.Invoke(message ?? string.Empty);
}

/// <summary>Host-neutral metadata describing a resource made available to an executing script.</summary>
public sealed record WebScriptResourceMetadata(
    string Uri,
    string Path,
    string? Fragment,
    string ResourceType,
    IReadOnlyDictionary<string, string> Properties);

/// <summary>A typed host resource and the metadata describing how it was produced.</summary>
public sealed record WebScriptResource<T>(T Value, WebScriptResourceMetadata Metadata);

/// <summary>Implemented by native or browser hosts that expose resources to executing scripts.</summary>
public interface IWebScriptResourceProvider
{
    bool TryGet<T>(string name, out WebScriptResource<T>? resource);
}

/// <summary>
/// Public execution-context facade for resources supplied by the current script host. The host should use
/// <see cref="Push"/> immediately around execution; scripts can use <see cref="Get{T}"/> or <see cref="TryGet{T}"/>
/// without depending on a browser, native UI, or resource-system implementation type.
/// </summary>
public static class WebScriptResources
{
    private static readonly AsyncLocal<IWebScriptResourceProvider?> CurrentProvider = new();

    public static WebScriptResource<T> Get<T>(string name)
    {
        if (TryGet<T>(name, out WebScriptResource<T>? resource)) return resource!;
        throw new KeyNotFoundException($"Script resource '{name}' of type {typeof(T).Name} is not available.");
    }

    public static bool TryGet<T>(string name, out WebScriptResource<T>? resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        IWebScriptResourceProvider? provider = CurrentProvider.Value;
        if (provider is not null && provider.TryGet(name, out resource)) return true;
        resource = null;
        return false;
    }

    /// <summary>Installs a provider for the current execution flow and restores the previous provider on disposal.</summary>
    public static IDisposable Push(IWebScriptResourceProvider? provider)
    {
        IWebScriptResourceProvider? previous = CurrentProvider.Value;
        CurrentProvider.Value = provider;
        return new Scope(previous);
    }

    private sealed class Scope(IWebScriptResourceProvider? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            CurrentProvider.Value = previous;
            _disposed = true;
        }
    }
}

public sealed record MetadataReferenceImage(string FileName, ReadOnlyMemory<byte> Image);

/// <summary>A source document supplied to the browser-compatible Roslyn pipeline.</summary>
public sealed record WebScriptDocument(string FileName, string Source);

/// <summary>An entry script body and its optional supporting source documents.</summary>
public sealed record WebScriptProject(
    WebScriptDocument EntryDocument,
    IReadOnlyList<WebScriptDocument> Documents)
{
    public static WebScriptProject FromSource(string source, string fileName = WebScriptCompiler.ScriptFileName)
        => new(new WebScriptDocument(fileName, source), []);
}

public sealed record WebScriptDiagnostic(
    string Id,
    string Message,
    WebScriptDiagnosticSeverity Severity,
    int? Line = null,
    int? Column = null,
    int Length = 1,
    string? FileName = null);

public enum WebScriptDiagnosticSeverity { Info, Warning, Error }

public sealed record WebScriptPolicy(int MaxSourceBytes = 128 * 1024)
{
    public IReadOnlyList<WebScriptDiagnostic> Validate(string source)
    {
        var diagnostics = new List<WebScriptDiagnostic>();
        int bytes = System.Text.Encoding.UTF8.GetByteCount(source);
        if (bytes > MaxSourceBytes)
            diagnostics.Add(new("LUXWEB001", $"Source is {bytes} bytes; the limit is {MaxSourceBytes} bytes.", WebScriptDiagnosticSeverity.Error));

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#r", StringComparison.Ordinal) || trimmed.StartsWith("#load", StringComparison.Ordinal))
            {
                diagnostics.Add(new(
                    "LUXWEB002",
                    "Reference and load directives are not supported; packages and references are supplied by the host.",
                    WebScriptDiagnosticSeverity.Error,
                    i + 1,
                    lines[i].Length - trimmed.Length + 1,
                    Math.Max(2, trimmed.TakeWhile(c => !char.IsWhiteSpace(c)).Count())));
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string compact = string.Concat(lines[i].Where(c => !char.IsWhiteSpace(c)));
            if (compact.Contains("while(true)", StringComparison.Ordinal)
                || compact.Contains("for(;;)", StringComparison.Ordinal))
            {
                diagnostics.Add(new(
                    "LUXWEB003",
                    "Statically unbounded loops are not supported in the browser playground.",
                    WebScriptDiagnosticSeverity.Error,
                    i + 1,
                    1,
                    Math.Max(1, lines[i].Length)));
            }
        }
        return diagnostics;
    }
}

public sealed record WebScriptCompilation(
    bool Success,
    byte[]? PeImage,
    byte[]? PdbImage,
    IReadOnlyList<WebScriptDiagnostic> Diagnostics,
    string GeneratedSource);

public sealed record WebScriptExecution(
    bool Success,
    Widget? Widget = null,
    WebScriptFailure? Failure = null);

public sealed record WebScriptFailure(
    string Kind,
    string Message,
    string? ExceptionType = null,
    int? Line = null,
    string? FileName = null);

public sealed record WebCompletionItem(
    string Label,
    string InsertText,
    string Kind,
    string? Detail = null,
    string? Documentation = null);

public sealed record WebCompletionResult(
    int Revision,
    int ReplacementStart,
    int ReplacementLength,
    IReadOnlyList<WebCompletionItem> Items);

public sealed record WebHoverResult(
    int Revision,
    string Markdown,
    int Start,
    int Length);

public sealed record WebFormatResult(
    int Revision,
    string Source);

public sealed record WebAnalysisResult(
    int Revision,
    IReadOnlyList<WebScriptDiagnostic> Diagnostics);

// Transport-neutral records for a future browser worker/preview boundary.
public abstract record WebScriptWorkerMessage(long Revision);
public sealed record CompileScriptRequest(
    long Revision,
    string Source,
    WebScriptProject? Project = null) : WebScriptWorkerMessage(Revision);
public sealed record CompileScriptResponse(long Revision, WebScriptCompilation Compilation) : WebScriptWorkerMessage(Revision);
public sealed record ExecuteScriptRequest(long Revision, byte[] PeImage, byte[]? PdbImage) : WebScriptWorkerMessage(Revision);
public sealed record ExecuteScriptResponse(long Revision, WebScriptExecution Execution) : WebScriptWorkerMessage(Revision);

public interface IWebScriptWorkerController
{
    Task<WebScriptCompilation> CompileAsync(CompileScriptRequest request, CancellationToken cancellationToken = default);
    Task<WebScriptExecution> ExecuteAsync(ExecuteScriptRequest request, CancellationToken cancellationToken = default);
}
