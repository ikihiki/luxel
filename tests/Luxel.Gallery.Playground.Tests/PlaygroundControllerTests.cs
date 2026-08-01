using Luxel.Gallery.Playground;
using Luxel.Scripting;

namespace Luxel.Gallery.Playground.Tests;

public sealed class PlaygroundControllerTests
{
    [Fact]
    public void Default_template_and_request_include_edited_main_and_supporting_files()
    {
        var template = new PlaygroundTemplate("sample", "Sample", "", "Main.csx",
        [
            new PlaygroundFile("Main.csx", "return 1;"),
            new PlaygroundFile("Helper.cs", "class Helper {}"),
        ]);
        using var controller = new PlaygroundController(new QueueExecutor(), template);

        controller.UpdateFile("Main.csx", "return 2;");
        ScriptExecutionRequest request = controller.CreateRequest(TimeSpan.FromSeconds(9));

        Assert.Equal("Main.csx", request.FileName);
        Assert.Equal("return 2;", request.Source);
        Assert.Equal(TimeSpan.FromSeconds(9), request.Options.Timeout);
        ScriptDocument file = Assert.Single(request.Files);
        Assert.Equal("Helper.cs", file.FileName);
        Assert.Equal("class Helper {}", file.Source);
        Assert.Equal("button", PlaygroundTemplates.Button.Id);
        Assert.Contains("Log(\"Button clicked.\")", PlaygroundTemplates.Button.Files[0].Source);
        Assert.Contains("Click me", PlaygroundTemplates.Button.Files[0].Source);
    }

    [Fact]
    public async Task Run_transitions_from_running_to_succeeded()
    {
        var executor = new QueueExecutor();
        using var controller = new PlaygroundController(executor);
        var statuses = new List<PlaygroundStatus>();
        controller.StateChanged += (_, state) => statuses.Add(state.Status);

        Task<ScriptExecutionResult?> run = controller.RunAsync();
        Assert.Equal(PlaygroundStatus.Running, controller.State.Status);
        Assert.False(controller.State.CanRun);
        Assert.True(controller.State.CanCancel);

        executor.Complete(0, Success("preview"));
        ScriptExecutionResult? accepted = await run;

        Assert.NotNull(accepted);
        Assert.Equal(PlaygroundStatus.Succeeded, controller.State.Status);
        Assert.Equal("preview", controller.State.LastSuccessfulPreview);
        Assert.Equal([PlaygroundStatus.Running, PlaygroundStatus.Succeeded], statuses);
    }

    [Fact]
    public async Task Cancel_and_reset_invalidate_pending_execution_and_restore_template()
    {
        var executor = new QueueExecutor();
        using var controller = new PlaygroundController(executor);
        controller.UpdateFile("Button.csx", "changed");

        Task<ScriptExecutionResult?> run = controller.RunAsync();
        controller.Cancel();

        Assert.Equal(PlaygroundStatus.Canceled, controller.State.Status);
        Assert.Equal(ScriptExecutionOutcome.Canceled, controller.State.Result?.Outcome);
        executor.Complete(0, Success("too late"));
        Assert.Null(await run);
        Assert.Null(controller.State.LastSuccessfulPreview);

        controller.Reset();
        Assert.Equal(PlaygroundStatus.Idle, controller.State.Status);
        Assert.Equal(PlaygroundTemplates.Button.Files[0].Source, controller.State.Draft.MainFile.Source);
        Assert.Null(controller.State.Result);
    }

    [Fact]
    public async Task Older_result_is_rejected_after_a_new_run_completes()
    {
        var executor = new QueueExecutor();
        using var controller = new PlaygroundController(executor);

        Task<ScriptExecutionResult?> older = controller.RunAsync();
        controller.UpdateFile("Button.csx", "new source");
        Task<ScriptExecutionResult?> newer = controller.RunAsync();

        executor.Complete(1, Success("new preview"));
        Assert.NotNull(await newer);
        executor.Complete(0, Failure(ScriptExecutionOutcome.RuntimeFailed, "old failure"));

        Assert.Null(await older);
        Assert.Equal(PlaygroundStatus.Succeeded, controller.State.Status);
        Assert.Equal("new preview", controller.State.LastSuccessfulPreview);
        Assert.Equal(2, controller.State.ExecutionId);
    }

    [Fact]
    public async Task Compilation_failure_preserves_last_successful_preview_and_presents_new_diagnostics()
    {
        var executor = new QueueExecutor();
        using var controller = new PlaygroundController(executor);

        Task<ScriptExecutionResult?> first = controller.RunAsync();
        executor.Complete(0, Success("last good"));
        await first;

        Task<ScriptExecutionResult?> second = controller.RunAsync();
        executor.Complete(1, new ScriptExecutionResult
        {
            Outcome = ScriptExecutionOutcome.CompilationFailed,
            Diagnostics =
            [
                new ScriptExecutionDiagnostic
                {
                    Code = "CS1002",
                    Message = "; expected",
                    Severity = ScriptDiagnosticSeverity.Error,
                },
            ],
            Failure = new ScriptExecutionFailure
            {
                Kind = ScriptFailureKind.Compilation,
                Message = "Compilation failed.",
            },
        });
        await second;

        Assert.Equal(PlaygroundStatus.Failed, controller.State.Status);
        Assert.Equal("Compilation failed", controller.State.StatusText);
        Assert.Equal("last good", controller.State.LastSuccessfulPreview);
        Assert.Equal("CS1002", Assert.Single(controller.State.Result!.Diagnostics).Code);
    }

    private static ScriptExecutionResult Success(string preview) => new()
    {
        Outcome = ScriptExecutionOutcome.Succeeded,
        ReturnValue = preview,
    };

    private static ScriptExecutionResult Failure(ScriptExecutionOutcome outcome, string message) => new()
    {
        Outcome = outcome,
        Failure = new ScriptExecutionFailure { Kind = ScriptFailureKind.Runtime, Message = message },
    };

    private sealed class QueueExecutor : IScriptExecutor
    {
        private readonly List<TaskCompletionSource<ScriptExecutionResult>> _pending = [];

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<ScriptExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending.Add(completion);
            return completion.Task;
        }

        public void Complete(int index, ScriptExecutionResult result)
        {
            TaskCompletionSource<ScriptExecutionResult> completion;
            lock (_pending) completion = _pending[index];
            completion.SetResult(result);
        }
    }
}
