using Luxel.Scripting;

namespace Luxel.Gallery.Playground;

public sealed record PlaygroundFile(string FileName, string Source);

public sealed record PlaygroundTemplate(
    string Id,
    string Title,
    string Description,
    string MainFileName,
    IReadOnlyList<PlaygroundFile> Files)
{
    public PlaygroundDraft CreateDraft() => new(
        TemplateId: Id,
        Title: Title,
        MainFileName: MainFileName,
        Files: Files.Select(file => file with { }).ToArray());
}

public sealed record PlaygroundDraft(
    string TemplateId,
    string Title,
    string MainFileName,
    IReadOnlyList<PlaygroundFile> Files)
{
    public PlaygroundFile MainFile => Files.First(file =>
        string.Equals(file.FileName, MainFileName, StringComparison.Ordinal));

    public PlaygroundDraft UpdateFile(string fileName, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(source);
        if (!Files.Any(file => string.Equals(file.FileName, fileName, StringComparison.Ordinal)))
            throw new ArgumentException($"The draft does not contain '{fileName}'.", nameof(fileName));

        return this with
        {
            Files = Files.Select(file => string.Equals(file.FileName, fileName, StringComparison.Ordinal)
                ? file with { Source = source }
                : file).ToArray(),
        };
    }
}

public static class PlaygroundTemplates
{
    public static PlaygroundTemplate Button { get; } = new(
        Id: "button",
        Title: "Button",
        Description: "A minimal button playground that records a click in the output log.",
        MainFileName: "Button.csx",
        Files:
        [
            new PlaygroundFile("Button.csx", """
                // The host supplies Luxel UI references and renders the returned value.
                var label = "Click me";
                Console.WriteLine($"Rendering button: {label}");
                return $"Button: {label}";
                """),
        ]);

    public static IReadOnlyList<PlaygroundTemplate> All { get; } = [Button];
}

public enum PlaygroundStatus
{
    Idle,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

public sealed record PlaygroundState
{
    public required PlaygroundDraft Draft { get; init; }
    public PlaygroundStatus Status { get; init; } = PlaygroundStatus.Idle;
    public long ExecutionId { get; init; }
    public ScriptExecutionResult? Result { get; init; }
    public ScriptExecutionResult? LastSuccessfulResult { get; init; }
    public string? LastSuccessfulPreview => LastSuccessfulResult?.ReturnValue;
    public bool CanRun => Status != PlaygroundStatus.Running;
    public bool CanCancel => Status == PlaygroundStatus.Running;
    public string StatusText => Status switch
    {
        PlaygroundStatus.Idle => "Ready",
        PlaygroundStatus.Running => "Running",
        PlaygroundStatus.Succeeded => "Succeeded",
        PlaygroundStatus.Canceled => "Canceled",
        _ => Result?.Outcome switch
        {
            ScriptExecutionOutcome.CompilationFailed => "Compilation failed",
            ScriptExecutionOutcome.RuntimeFailed => "Runtime failed",
            ScriptExecutionOutcome.InvalidRequest => "Invalid request",
            ScriptExecutionOutcome.TimedOut => "Timed out",
            _ => "Failed",
        },
    };
}
