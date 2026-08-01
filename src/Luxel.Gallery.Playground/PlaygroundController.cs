using Luxel.Scripting;

namespace Luxel.Gallery.Playground;

/// <summary>Owns editable draft state and coordinates execution without depending on an execution implementation.</summary>
public sealed class PlaygroundController : IDisposable
{
    private readonly object _gate = new();
    private readonly IScriptExecutor _executor;
    private readonly PlaygroundTemplate _template;
    private CancellationTokenSource? _executionCancellation;
    private long _executionId;
    private PlaygroundState _state;

    public PlaygroundController(IScriptExecutor executor, PlaygroundTemplate? template = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _template = template ?? PlaygroundTemplates.Button;
        ValidateTemplate(_template);
        _state = new PlaygroundState { Draft = _template.CreateDraft() };
    }

    public event EventHandler<PlaygroundState>? StateChanged;

    public PlaygroundState State
    {
        get { lock (_gate) return _state; }
    }

    public void UpdateFile(string fileName, string source)
    {
        PlaygroundState state;
        lock (_gate)
        {
            _state = _state with { Draft = _state.Draft.UpdateFile(fileName, source) };
            state = _state;
        }
        OnStateChanged(state);
    }

    public ScriptExecutionRequest CreateRequest(TimeSpan? timeout = null)
    {
        lock (_gate) return CreateRequest(_state.Draft, timeout);
    }

    public async Task<ScriptExecutionResult?> RunAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        long executionId;
        ScriptExecutionRequest request;
        CancellationTokenSource cancellation;
        PlaygroundState running;

        lock (_gate)
        {
            _executionCancellation?.Cancel();
            _executionCancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executionCancellation = cancellation;
            executionId = ++_executionId;
            request = CreateRequest(_state.Draft, timeout) with
            {
                RequestId = $"playground-{executionId}",
                SourceRevision = executionId,
            };
            _state = _state with
            {
                Status = PlaygroundStatus.Running,
                ExecutionId = executionId,
                Result = null,
            };
            running = _state;
        }
        OnStateChanged(running);

        ScriptExecutionResult result;
        try
        {
            result = await _executor.ExecuteAsync(request, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            result = new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Canceled };
        }
        catch (Exception exception)
        {
            result = new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.RuntimeFailed,
                Failure = new ScriptExecutionFailure
                {
                    Kind = ScriptFailureKind.Infrastructure,
                    Message = exception.Message,
                    Type = exception.GetType().FullName,
                },
            };
        }

        PlaygroundState? completed = null;
        lock (_gate)
        {
            if (executionId == _executionId)
            {
                ScriptExecutionResult? lastSuccessful = result.Success ? result : _state.LastSuccessfulResult;
                _state = _state with
                {
                    Status = StatusFor(result.Outcome),
                    Result = result,
                    LastSuccessfulResult = lastSuccessful,
                };
                completed = _state;
                if (ReferenceEquals(_executionCancellation, cancellation))
                    _executionCancellation = null;
            }
        }
        cancellation.Dispose();
        if (completed is not null) OnStateChanged(completed);
        return completed is null ? null : result;
    }

    public void Cancel()
    {
        PlaygroundState? canceled = null;
        lock (_gate)
        {
            if (_state.Status != PlaygroundStatus.Running) return;
            ++_executionId;
            _executionCancellation?.Cancel();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            _state = _state with
            {
                Status = PlaygroundStatus.Canceled,
                ExecutionId = _executionId,
                Result = new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Canceled },
            };
            canceled = _state;
        }
        OnStateChanged(canceled);
    }

    public void Reset()
    {
        PlaygroundState reset;
        lock (_gate)
        {
            ++_executionId;
            _executionCancellation?.Cancel();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            _state = new PlaygroundState
            {
                Draft = _template.CreateDraft(),
                ExecutionId = _executionId,
            };
            reset = _state;
        }
        OnStateChanged(reset);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ++_executionId;
            _executionCancellation?.Cancel();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
        }
    }

    private static ScriptExecutionRequest CreateRequest(PlaygroundDraft draft, TimeSpan? timeout)
    {
        PlaygroundFile main = draft.MainFile;
        return new ScriptExecutionRequest
        {
            Source = main.Source,
            FileName = main.FileName,
            Files = draft.Files
                .Where(file => !string.Equals(file.FileName, draft.MainFileName, StringComparison.Ordinal))
                .Select(file => new ScriptDocument { FileName = file.FileName, Source = file.Source })
                .ToArray(),
            Options = new ScriptExecutionOptions { Timeout = timeout ?? TimeSpan.FromSeconds(5) },
        };
    }

    private static PlaygroundStatus StatusFor(ScriptExecutionOutcome outcome) => outcome switch
    {
        ScriptExecutionOutcome.Succeeded => PlaygroundStatus.Succeeded,
        ScriptExecutionOutcome.Canceled => PlaygroundStatus.Canceled,
        _ => PlaygroundStatus.Failed,
    };

    private static void ValidateTemplate(PlaygroundTemplate template)
    {
        if (template.Files.Count == 0)
            throw new ArgumentException("A playground template must contain at least one file.", nameof(template));
        if (template.Files.Count(file => string.Equals(file.FileName, template.MainFileName, StringComparison.Ordinal)) != 1)
            throw new ArgumentException("A playground template must contain its main file exactly once.", nameof(template));
        if (template.Files.Select(file => file.FileName).Distinct(StringComparer.Ordinal).Count() != template.Files.Count)
            throw new ArgumentException("Playground file names must be unique.", nameof(template));
    }

    private void OnStateChanged(PlaygroundState state) => StateChanged?.Invoke(this, state);
}
