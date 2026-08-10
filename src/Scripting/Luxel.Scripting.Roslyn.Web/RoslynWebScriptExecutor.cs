using Luxel.Scripting;

namespace Luxel.Scripting.Roslyn.Web;

/// <summary>
/// Compiles and executes a fixed-contract Luxel UI script. Browser hosts can replace the
/// controller with a Web Worker/preview-iframe implementation without changing Gallery code.
/// </summary>
public sealed class RoslynWebScriptExecutor(IWebScriptWorkerController controller) : IScriptExecutor
{
    private long _revision;

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        long revision = Interlocked.Increment(ref _revision);
        WebScriptCompilation compilation;
        try
        {
            var project = new WebScriptProject(
                new WebScriptDocument(request.FileName, request.Source),
                request.Files
                    .Where(file => string.Equals(Path.GetExtension(file.FileName), ".cs", StringComparison.OrdinalIgnoreCase))
                    .Select(file => new WebScriptDocument(file.FileName, file.Source))
                    .ToArray());
            compilation = await controller.CompileAsync(
                new CompileScriptRequest(revision, request.Source, project), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return InfrastructureFailure(exception);
        }

        IReadOnlyList<ScriptExecutionDiagnostic> diagnostics = compilation.Diagnostics.Select(Map).ToArray();
        if (!compilation.Success || compilation.PeImage is null)
        {
            return new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.CompilationFailed,
                Diagnostics = diagnostics,
                Failure = new ScriptExecutionFailure
                {
                    Kind = ScriptFailureKind.Compilation,
                    Message = "The script contains compilation errors.",
                },
            };
        }

        WebScriptExecution execution;
        try
        {
            execution = await controller.ExecuteAsync(
                new ExecuteScriptRequest(revision, compilation.PeImage, compilation.PdbImage), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return InfrastructureFailure(exception, diagnostics);
        }

        if (!execution.Success)
        {
            WebScriptFailure failure = execution.Failure ?? new WebScriptFailure("runtime", "Script execution failed.");
            return new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.RuntimeFailed,
                Diagnostics = diagnostics,
                Failure = new ScriptExecutionFailure
                {
                    Kind = ScriptFailureKind.Runtime,
                    Message = failure.Message,
                    Type = failure.ExceptionType,
                    StackTrace = failure.Line is { } line
                        ? $"{failure.FileName ?? request.FileName}:line {line}"
                        : null,
                },
            };
        }

        return new ScriptExecutionResult
        {
            Outcome = ScriptExecutionOutcome.Succeeded,
            ReturnValue = execution.Widget?.GetType().FullName,
            Diagnostics = diagnostics,
        };
    }

    private static ScriptExecutionDiagnostic Map(WebScriptDiagnostic diagnostic) => new()
    {
        Code = diagnostic.Id,
        Message = diagnostic.Message,
        Severity = diagnostic.Severity switch
        {
            WebScriptDiagnosticSeverity.Info => ScriptDiagnosticSeverity.Info,
            WebScriptDiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
            _ => ScriptDiagnosticSeverity.Error,
        },
        Span = diagnostic.Line is { } line && diagnostic.Column is { } column
            ? new ScriptSourceSpan
            {
                FileName = diagnostic.FileName ?? WebScriptCompiler.ScriptFileName,
                StartLine = line,
                StartColumn = column,
                EndLine = line,
                EndColumn = column + Math.Max(1, diagnostic.Length),
                Length = Math.Max(1, diagnostic.Length),
            }
            : null,
    };

    private static ScriptExecutionResult InfrastructureFailure(
        Exception exception,
        IReadOnlyList<ScriptExecutionDiagnostic>? diagnostics = null) => new()
    {
        Outcome = ScriptExecutionOutcome.RuntimeFailed,
        Diagnostics = diagnostics ?? [],
        Failure = new ScriptExecutionFailure
        {
            Kind = ScriptFailureKind.Infrastructure,
            Message = exception.Message,
            Type = exception.GetType().FullName,
            StackTrace = exception.StackTrace,
        },
    };
}
