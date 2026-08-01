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

public sealed record MetadataReferenceImage(string FileName, ReadOnlyMemory<byte> Image);

public sealed record WebScriptDiagnostic(
    string Id,
    string Message,
    WebScriptDiagnosticSeverity Severity,
    int? Line = null,
    int? Column = null,
    int Length = 1);

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
    int? Line = null);

// Transport-neutral records for a future browser worker/preview boundary.
public abstract record WebScriptWorkerMessage(long Revision);
public sealed record CompileScriptRequest(long Revision, string Source) : WebScriptWorkerMessage(Revision);
public sealed record CompileScriptResponse(long Revision, WebScriptCompilation Compilation) : WebScriptWorkerMessage(Revision);
public sealed record ExecuteScriptRequest(long Revision, byte[] PeImage, byte[]? PdbImage) : WebScriptWorkerMessage(Revision);
public sealed record ExecuteScriptResponse(long Revision, WebScriptExecution Execution) : WebScriptWorkerMessage(Revision);

public interface IWebScriptWorkerController
{
    Task<WebScriptCompilation> CompileAsync(CompileScriptRequest request, CancellationToken cancellationToken = default);
    Task<WebScriptExecution> ExecuteAsync(ExecuteScriptRequest request, CancellationToken cancellationToken = default);
}
