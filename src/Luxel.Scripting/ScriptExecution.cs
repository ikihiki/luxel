namespace Luxel.Scripting;

/// <summary>Transport-neutral asynchronous script execution service.</summary>
public interface IScriptExecutor
{
    Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A script and its optional supporting source documents.</summary>
public sealed record ScriptExecutionRequest
{
    /// <summary>Caller-generated identifier used to correlate an execution across process boundaries.</summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Monotonic source revision. Results for older revisions can be ignored by callers.</summary>
    public long SourceRevision { get; init; }

    /// <summary>The primary script source.</summary>
    public string Source { get; init; } = "";

    /// <summary>Logical path used for diagnostics for <see cref="Source"/>.</summary>
    public string FileName { get; init; } = "script.csx";

    /// <summary>Additional source documents made available to the executor.</summary>
    public IReadOnlyList<ScriptDocument> Files { get; init; } = [];

    public ScriptExecutionOptions Options { get; init; } = new();

    public ScriptExecutionLimits Limits { get; init; } = new();
}

/// <summary>An additional source document supplied with an execution request.</summary>
public sealed record ScriptDocument
{
    public string FileName { get; init; } = "";
    public string Source { get; init; } = "";
}

/// <summary>Executor-independent behavior for one execution.</summary>
public sealed record ScriptExecutionOptions
{
    /// <summary>Maximum wall-clock time allowed for execution.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Input limits applied before an executor receives a request.</summary>
public sealed record ScriptExecutionLimits
{
    public const int DefaultMaxSourceCharacters = 256 * 1024;
    public const int DefaultMaxFileCount = 16;
    public const int DefaultMaxFileCharacters = 256 * 1024;
    public const int DefaultMaxTotalCharacters = 1024 * 1024;
    public const int DefaultMaxFileNameCharacters = 260;

    public int MaxSourceCharacters { get; init; } = DefaultMaxSourceCharacters;
    public int MaxFileCount { get; init; } = DefaultMaxFileCount;
    public int MaxFileCharacters { get; init; } = DefaultMaxFileCharacters;
    public int MaxTotalCharacters { get; init; } = DefaultMaxTotalCharacters;
    public int MaxFileNameCharacters { get; init; } = DefaultMaxFileNameCharacters;
}

public enum ScriptExecutionOutcome
{
    Succeeded,
    CompilationFailed,
    RuntimeFailed,
    InvalidRequest,
    PolicyRejected,
    InfrastructureFailed,
    TimedOut,
    Canceled,
}

public enum ScriptDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A serializable source span. Lines and columns are one-based; offsets are zero-based.</summary>
public sealed record ScriptSourceSpan
{
    public string FileName { get; init; } = "script.csx";
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public int StartOffset { get; init; }
    public int Length { get; init; }
}

/// <summary>A transport-neutral compiler or analyzer diagnostic.</summary>
public sealed record ScriptExecutionDiagnostic
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public ScriptDiagnosticSeverity Severity { get; init; }
    public ScriptSourceSpan? Span { get; init; }
}

public enum ScriptFailureKind
{
    Validation,
    Compilation,
    Runtime,
    Infrastructure,
}

/// <summary>Serializable failure information; exceptions never cross the execution boundary.</summary>
public sealed record ScriptExecutionFailure
{
    public ScriptFailureKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string? Type { get; init; }
    public string? StackTrace { get; init; }
}

public enum ScriptLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
}

/// <summary>A log entry emitted during script execution.</summary>
public sealed record ScriptLogEntry
{
    public ScriptLogLevel Level { get; init; } = ScriptLogLevel.Information;
    public string Message { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>Lifecycle timestamps recorded by the coordinator.</summary>
public sealed record ScriptExecutionLifecycle
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration => CompletedAt - StartedAt;
}

/// <summary>The serializable result of a script execution.</summary>
public sealed record ScriptExecutionResult
{
    public string RequestId { get; init; } = "";
    public long SourceRevision { get; init; }
    public ScriptExecutionOutcome Outcome { get; init; }
    public bool Success => Outcome == ScriptExecutionOutcome.Succeeded;
    public string? ReturnValue { get; init; }
    public IReadOnlyList<ScriptExecutionDiagnostic> Diagnostics { get; init; } = [];
    public ScriptExecutionFailure? Failure { get; init; }
    public IReadOnlyList<ScriptLogEntry> Logs { get; init; } = [];
    public ScriptExecutionLifecycle? Lifecycle { get; init; }
}

/// <summary>
/// Applies common validation, timeout, cancellation, and lifecycle behavior around an executor.
/// </summary>
public sealed class ScriptExecutionCoordinator : IScriptExecutor
{
    private readonly IScriptExecutor _executor;
    private readonly TimeProvider _timeProvider;

    public ScriptExecutionCoordinator(IScriptExecutor executor, TimeProvider? timeProvider = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        ScriptExecutionFailure? validationFailure = Validate(request);
        if (validationFailure is not null)
            return Complete(new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.InvalidRequest,
                Failure = validationFailure,
            }, startedAt, request);

        if (cancellationToken.IsCancellationRequested)
            return Complete(new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Canceled }, startedAt, request);

        using var timeout = new CancellationTokenSource(request.Options.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            ScriptExecutionResult result = await _executor.ExecuteAsync(request, linked.Token)
                .WaitAsync(request.Options.Timeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            return Complete(result, startedAt, request);
        }
        catch (TimeoutException)
        {
            return Complete(TimeoutResult(request.Options.Timeout), startedAt, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Complete(new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Canceled }, startedAt, request);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Complete(TimeoutResult(request.Options.Timeout), startedAt, request);
        }
        catch (Exception exception)
        {
            return Complete(new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.InfrastructureFailed,
                Failure = new ScriptExecutionFailure
                {
                    Kind = ScriptFailureKind.Infrastructure,
                    Message = exception.Message,
                    Type = exception.GetType().FullName,
                    StackTrace = exception.StackTrace,
                },
            }, startedAt, request);
        }
    }

    private static ScriptExecutionResult TimeoutResult(TimeSpan timeout) => new()
    {
        Outcome = ScriptExecutionOutcome.TimedOut,
        Failure = new ScriptExecutionFailure
        {
            Kind = ScriptFailureKind.Infrastructure,
            Message = $"Script execution exceeded the timeout of {timeout}.",
        },
    };

    private ScriptExecutionResult Complete(
        ScriptExecutionResult result,
        DateTimeOffset startedAt,
        ScriptExecutionRequest? request)
    {
        DateTimeOffset completedAt = _timeProvider.GetUtcNow();
        return result with
        {
            RequestId = string.IsNullOrEmpty(result.RequestId) ? request?.RequestId ?? "" : result.RequestId,
            SourceRevision = result.SourceRevision == 0 ? request?.SourceRevision ?? 0 : result.SourceRevision,
            Lifecycle = new ScriptExecutionLifecycle
            {
                StartedAt = startedAt,
                CompletedAt = completedAt,
            },
        };
    }

    private static ScriptExecutionFailure? Validate(ScriptExecutionRequest? request)
    {
        if (request is null)
            return Validation("The execution request is required.");
        if (request.Options is null)
            return Validation("Execution options are required.");
        if (request.Limits is null)
            return Validation("Execution limits are required.");
        if (request.Options.Timeout <= TimeSpan.Zero)
            return Validation("Timeout must be greater than zero.");

        ScriptExecutionLimits limits = request.Limits;
        if (limits.MaxSourceCharacters <= 0 || limits.MaxFileCount < 0 ||
            limits.MaxFileCharacters <= 0 || limits.MaxTotalCharacters <= 0 ||
            limits.MaxFileNameCharacters <= 0)
            return Validation("Execution limits must be positive (MaxFileCount may be zero).");

        if (string.IsNullOrWhiteSpace(request.FileName))
            return Validation("The primary file name is required.");
        if (request.FileName.Length > limits.MaxFileNameCharacters)
            return Validation($"The primary file name exceeds the limit of {limits.MaxFileNameCharacters} characters.");
        if (request.Source is null)
            return Validation("The primary source is required.");
        if (request.Source.Length > limits.MaxSourceCharacters)
            return Validation($"The primary source exceeds the limit of {limits.MaxSourceCharacters} characters.");
        if (request.Files is null)
            return Validation("Files are required.");
        if (request.Files.Count > limits.MaxFileCount)
            return Validation($"The request exceeds the limit of {limits.MaxFileCount} additional files.");

        long totalCharacters = request.Source.Length;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { request.FileName };
        foreach (ScriptDocument? file in request.Files)
        {
            if (file is null)
                return Validation("Files cannot contain null documents.");
            if (string.IsNullOrWhiteSpace(file.FileName))
                return Validation("Every file must have a file name.");
            if (file.FileName.Length > limits.MaxFileNameCharacters)
                return Validation($"File name '{file.FileName}' exceeds the limit of {limits.MaxFileNameCharacters} characters.");
            if (!names.Add(file.FileName))
                return Validation($"Duplicate file name '{file.FileName}'.");
            if (file.Source is null)
                return Validation($"File '{file.FileName}' must have source text.");
            if (file.Source.Length > limits.MaxFileCharacters)
                return Validation($"File '{file.FileName}' exceeds the limit of {limits.MaxFileCharacters} characters.");
            totalCharacters += file.Source.Length;
        }

        if (totalCharacters > limits.MaxTotalCharacters)
            return Validation($"The request exceeds the total source limit of {limits.MaxTotalCharacters} characters.");

        return null;
    }

    private static ScriptExecutionFailure Validation(string message) => new()
    {
        Kind = ScriptFailureKind.Validation,
        Message = message,
    };
}
