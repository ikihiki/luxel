using System.Text.Json;
using Luxel.Scripting;

namespace Luxel.Tests;

public class ScriptExecutionTests
{
    [Fact]
    public void Defaults_AreFiniteAndUseful()
    {
        var request = new ScriptExecutionRequest();

        Assert.Equal("", request.Source);
        Assert.Equal("script.csx", request.FileName);
        Assert.Empty(request.Files);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Options.Timeout);
        Assert.True(request.Limits.MaxSourceCharacters > 0);
        Assert.True(request.Limits.MaxFileCount > 0);
        Assert.True(request.Limits.MaxFileCharacters > 0);
        Assert.True(request.Limits.MaxTotalCharacters >= request.Limits.MaxSourceCharacters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidTimeout_IsRejectedWithoutCallingExecutor(int milliseconds)
    {
        var executor = new DelegateExecutor((_, _) =>
            Task.FromResult(new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Succeeded }));
        var coordinator = new ScriptExecutionCoordinator(executor);

        ScriptExecutionResult result = await coordinator.ExecuteAsync(new ScriptExecutionRequest
        {
            Options = new ScriptExecutionOptions { Timeout = TimeSpan.FromMilliseconds(milliseconds) },
        });

        Assert.Equal(ScriptExecutionOutcome.InvalidRequest, result.Outcome);
        Assert.Equal(ScriptFailureKind.Validation, result.Failure!.Kind);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task SourceAndFileLimits_AreValidatedBeforeExecution()
    {
        var executor = new DelegateExecutor((_, _) =>
            Task.FromResult(new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Succeeded }));
        var coordinator = new ScriptExecutionCoordinator(executor);
        var limits = new ScriptExecutionLimits
        {
            MaxSourceCharacters = 3,
            MaxFileCount = 1,
            MaxFileCharacters = 2,
            MaxTotalCharacters = 5,
            MaxFileNameCharacters = 20,
        };

        ScriptExecutionResult sourceResult = await coordinator.ExecuteAsync(new ScriptExecutionRequest
        {
            Source = "four",
            Limits = limits,
        });
        ScriptExecutionResult fileResult = await coordinator.ExecuteAsync(new ScriptExecutionRequest
        {
            Source = "ok",
            Files = [new ScriptDocument { FileName = "support.cs", Source = "long" }],
            Limits = limits,
        });

        Assert.Equal(ScriptExecutionOutcome.InvalidRequest, sourceResult.Outcome);
        Assert.Contains("primary source", sourceResult.Failure!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ScriptExecutionOutcome.InvalidRequest, fileResult.Outcome);
        Assert.Contains("support.cs", fileResult.Failure!.Message);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task Timeout_IsDistinctFromCallerCancellation()
    {
        var neverCompletes = new TaskCompletionSource<ScriptExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new DelegateExecutor((_, _) => neverCompletes.Task);
        var coordinator = new ScriptExecutionCoordinator(executor);

        ScriptExecutionResult result = await coordinator.ExecuteAsync(new ScriptExecutionRequest
        {
            Options = new ScriptExecutionOptions { Timeout = TimeSpan.FromMilliseconds(20) },
        });

        Assert.Equal(ScriptExecutionOutcome.TimedOut, result.Outcome);
        Assert.NotNull(result.Lifecycle);
        Assert.True(result.Lifecycle.CompletedAt >= result.Lifecycle.StartedAt);
    }

    [Fact]
    public async Task CallerCancellation_IsDistinctFromTimeout()
    {
        var executor = new DelegateExecutor(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ScriptExecutionResult { Outcome = ScriptExecutionOutcome.Succeeded };
        });
        var coordinator = new ScriptExecutionCoordinator(executor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ScriptExecutionResult result = await coordinator.ExecuteAsync(
            new ScriptExecutionRequest(), cancellation.Token);

        Assert.Equal(ScriptExecutionOutcome.Canceled, result.Outcome);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public void RequestContracts_RoundTripThroughJson()
    {
        var request = new ScriptExecutionRequest
        {
            Source = "answer()",
            FileName = "main.csx",
            Files = [new ScriptDocument { FileName = "support.cs", Source = "int answer() => 42;" }],
            Options = new ScriptExecutionOptions { Timeout = TimeSpan.FromSeconds(2) },
            Limits = new ScriptExecutionLimits { MaxFileCount = 3 },
        };

        ScriptExecutionRequest? copy = JsonSerializer.Deserialize<ScriptExecutionRequest>(
            JsonSerializer.Serialize(request));

        Assert.NotNull(copy);
        Assert.Equal("answer()", copy.Source);
        Assert.Equal("main.csx", copy.FileName);
        Assert.Equal("support.cs", Assert.Single(copy.Files).FileName);
        Assert.Equal(TimeSpan.FromSeconds(2), copy.Options.Timeout);
        Assert.Equal(3, copy.Limits.MaxFileCount);
    }

    [Fact]
    public void Contracts_RoundTripThroughJson_WithDiagnosticSpan()
    {
        var result = new ScriptExecutionResult
        {
            Outcome = ScriptExecutionOutcome.CompilationFailed,
            Diagnostics =
            [
                new ScriptExecutionDiagnostic
                {
                    Code = "CS1002",
                    Message = "; expected",
                    Severity = ScriptDiagnosticSeverity.Error,
                    Span = new ScriptSourceSpan
                    {
                        FileName = "main.csx",
                        StartLine = 2,
                        StartColumn = 4,
                        EndLine = 2,
                        EndColumn = 7,
                        StartOffset = 12,
                        Length = 3,
                    },
                },
            ],
            Failure = new ScriptExecutionFailure
            {
                Kind = ScriptFailureKind.Compilation,
                Message = "Compilation failed.",
            },
            Logs =
            [
                new ScriptLogEntry
                {
                    Level = ScriptLogLevel.Warning,
                    Message = "warning",
                    Timestamp = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
                },
            ],
        };

        string json = JsonSerializer.Serialize(result);
        ScriptExecutionResult? copy = JsonSerializer.Deserialize<ScriptExecutionResult>(json);

        Assert.NotNull(copy);
        Assert.Equal(result.Outcome, copy.Outcome);
        ScriptExecutionDiagnostic diagnostic = Assert.Single(copy.Diagnostics);
        Assert.Equal("CS1002", diagnostic.Code);
        Assert.Equal(2, diagnostic.Span!.StartLine);
        Assert.Equal(4, diagnostic.Span.StartColumn);
        Assert.Equal(2, diagnostic.Span.EndLine);
        Assert.Equal(7, diagnostic.Span.EndColumn);
        Assert.Equal(12, diagnostic.Span.StartOffset);
        Assert.Equal(3, diagnostic.Span.Length);
        Assert.Equal(ScriptFailureKind.Compilation, copy.Failure!.Kind);
        Assert.Equal("warning", Assert.Single(copy.Logs).Message);
    }

    [Fact]
    public async Task SuccessfulExecution_PreservesPayloadAndAddsLifecycle()
    {
        var executor = new DelegateExecutor((_, _) => Task.FromResult(new ScriptExecutionResult
        {
            Outcome = ScriptExecutionOutcome.Succeeded,
            ReturnValue = "42",
            Logs = [new ScriptLogEntry { Message = "done" }],
        }));

        ScriptExecutionResult result = await new ScriptExecutionCoordinator(executor)
            .ExecuteAsync(new ScriptExecutionRequest { Source = "40 + 2" });

        Assert.True(result.Success);
        Assert.Equal("42", result.ReturnValue);
        Assert.Equal("done", Assert.Single(result.Logs).Message);
        Assert.NotNull(result.Lifecycle);
    }

    private sealed class DelegateExecutor(
        Func<ScriptExecutionRequest, CancellationToken, Task<ScriptExecutionResult>> execute) : IScriptExecutor
    {
        public int CallCount { get; private set; }

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return execute(request, cancellationToken);
        }
    }
}
