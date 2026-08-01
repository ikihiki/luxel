using Luxel.Scripting;

namespace Luxel.Gallery.Playground;

public sealed record PlaygroundFile
{
    public PlaygroundFile(string FileName, string Source)
        : this(Guid.NewGuid().ToString("N"), FileName, InferLanguage(FileName), Source, 0)
    {
    }

    public PlaygroundFile(string Id, string Path, string Language, string Source, long Version = 0)
    {
        this.Id = ValidateId(Id);
        this.Path = PlaygroundWorkspaceValidation.NormalizePath(Path);
        this.Language = ValidateLanguage(Language);
        this.Source = Source ?? throw new ArgumentNullException(nameof(Source));
        if (Version < 0) throw new ArgumentOutOfRangeException(nameof(Version));
        this.Version = Version;
    }

    public string Id { get; init; }
    public string Path { get; init; }
    public string Language { get; init; }
    public string Source { get; init; }
    public long Version { get; init; }

    // Compatibility with the original single-file playground contract.
    public string FileName => Path;

    public void Deconstruct(out string FileName, out string Source)
    {
        FileName = Path;
        Source = this.Source;
    }

    internal PlaygroundFile WithSource(string source) => new(Id, Path, Language, source, checked(Version + 1));
    internal PlaygroundFile WithPath(string path, string? language = null) =>
        new(Id, path, language ?? InferLanguage(path), Source, checked(Version + 1));

    public static string InferLanguage(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" or ".csx" => "csharp",
        ".json" => "json",
        ".md" or ".markdown" => "markdown",
        ".xml" => "xml",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".js" or ".mjs" => "javascript",
        ".ts" => "typescript",
        _ => "plaintext",
    };

    private static string ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }

    private static string ValidateLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.Trim().ToLowerInvariant();
    }
}

public sealed record PlaygroundTemplate(
    string Id,
    string Title,
    string Description,
    string MainFileName,
    IReadOnlyList<PlaygroundFile> Files)
{
    public PlaygroundDraft CreateDraft()
    {
        PlaygroundWorkspaceValidation.ValidateFiles(Files);
        PlaygroundFile main = Files.Single(file =>
            string.Equals(file.Path, PlaygroundWorkspaceValidation.NormalizePath(MainFileName), StringComparison.Ordinal));
        return new PlaygroundDraft(Id, Title, main.Id, main.Id, Files.Select(file => file with { }).ToArray(), 0);
    }
}

public sealed record PlaygroundDraft
{
    public PlaygroundDraft(
        string TemplateId,
        string Title,
        string MainFileName,
        IReadOnlyList<PlaygroundFile> Files)
        : this(
            TemplateId,
            Title,
            FindByPath(Files, MainFileName).Id,
            FindByPath(Files, MainFileName).Id,
            Files,
            0)
    {
    }

    public PlaygroundDraft(
        string TemplateId,
        string Title,
        string MainFileId,
        string SelectedFileId,
        IReadOnlyList<PlaygroundFile> Files,
        long Revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TemplateId);
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(MainFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SelectedFileId);
        if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        PlaygroundWorkspaceValidation.ValidateFiles(Files);
        if (!Files.Any(file => file.Id == MainFileId))
            throw new ArgumentException("The main file must exist in the workspace.", nameof(MainFileId));
        if (!Files.Any(file => file.Id == SelectedFileId))
            throw new ArgumentException("The selected file must exist in the workspace.", nameof(SelectedFileId));

        this.TemplateId = TemplateId;
        this.Title = Title;
        this.MainFileId = MainFileId;
        this.SelectedFileId = SelectedFileId;
        this.Files = Files.ToArray();
        this.Revision = Revision;
    }

    public string TemplateId { get; init; }
    public string Title { get; init; }
    public string MainFileId { get; init; }
    public string SelectedFileId { get; init; }
    public IReadOnlyList<PlaygroundFile> Files { get; init; }
    public long Revision { get; init; }

    public PlaygroundFile MainFile => Files.Single(file => file.Id == MainFileId);
    public PlaygroundFile SelectedFile => Files.Single(file => file.Id == SelectedFileId);
    public string MainFileName => MainFile.Path;

    public PlaygroundDraft AddFile(
        string path,
        string source = "",
        string? language = null,
        string? id = null,
        long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(path);
        EnsurePathAvailable(normalized);
        var file = new PlaygroundFile(id ?? Guid.NewGuid().ToString("N"), normalized,
            language ?? PlaygroundFile.InferLanguage(normalized), source, 0);
        return Next(Files.Append(file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft UpdateFile(string fileNameOrId, string source, long? expectedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNameOrId);
        ArgumentNullException.ThrowIfNull(source);
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        return Next(Files.Select(file => file.Id == target.Id ? file.WithSource(source) : file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft RenameFile(
        string fileNameOrId,
        string newPath,
        string? language = null,
        long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(newPath);
        EnsurePathAvailable(normalized, target.Id);
        return Next(Files.Select(file => file.Id == target.Id ? file.WithPath(normalized, language) : file).ToArray(), SelectedFileId);
    }

    public PlaygroundDraft DeleteFile(string fileNameOrId, long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        if (target.Id == MainFileId)
            throw new InvalidOperationException("The main file cannot be deleted.");
        if (Files.Count == 1)
            throw new InvalidOperationException("A workspace must contain at least one file.");
        PlaygroundFile[] remaining = Files.Where(file => file.Id != target.Id).ToArray();
        string selected = SelectedFileId == target.Id ? MainFileId : SelectedFileId;
        return Next(remaining, selected);
    }

    public PlaygroundDraft SelectFile(string fileNameOrId, long? expectedRevision = null)
    {
        EnsureRevision(expectedRevision);
        PlaygroundFile target = Find(fileNameOrId);
        if (target.Id == SelectedFileId) return this;
        return Next(Files, target.Id);
    }

    private PlaygroundDraft Next(IReadOnlyList<PlaygroundFile> files, string selectedFileId) =>
        new(TemplateId, Title, MainFileId, selectedFileId, files, checked(Revision + 1));

    private PlaygroundFile Find(string fileNameOrId)
    {
        PlaygroundFile? byId = Files.SingleOrDefault(file => file.Id == fileNameOrId);
        if (byId is not null) return byId;
        string path = PlaygroundWorkspaceValidation.NormalizePath(fileNameOrId);
        return Files.SingleOrDefault(file => file.Path == path)
            ?? throw new ArgumentException($"The draft does not contain '{fileNameOrId}'.", nameof(fileNameOrId));
    }

    private void EnsurePathAvailable(string path, string? exceptId = null)
    {
        if (Files.Any(file => file.Id != exceptId && file.Path == path))
            throw new ArgumentException($"The draft already contains '{path}'.", nameof(path));
    }

    private void EnsureRevision(long? expectedRevision)
    {
        if (expectedRevision is { } expected && expected != Revision)
            throw new StalePlaygroundRevisionException(expected, Revision);
    }

    private static PlaygroundFile FindByPath(IReadOnlyList<PlaygroundFile> files, string path)
    {
        ArgumentNullException.ThrowIfNull(files);
        string normalized = PlaygroundWorkspaceValidation.NormalizePath(path);
        return files.SingleOrDefault(file => file.Path == normalized)
            ?? throw new ArgumentException($"The draft does not contain its main file '{path}'.", nameof(path));
    }
}

public sealed class StalePlaygroundRevisionException(long expectedRevision, long actualRevision)
    : InvalidOperationException($"Workspace revision {expectedRevision} is stale; current revision is {actualRevision}.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}

public static class PlaygroundWorkspaceValidation
{
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/') || normalized.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Workspace paths must be relative normalized file paths.", nameof(path));
        string[] segments = normalized.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Workspace paths cannot contain empty, '.' or '..' segments.", nameof(path));
        if (segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Workspace paths contain invalid characters.", nameof(path));
        return normalized;
    }

    public static void ValidateFiles(IReadOnlyList<PlaygroundFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
            throw new ArgumentException("A playground workspace must contain at least one file.", nameof(files));
        if (files.Any(file => file is null))
            throw new ArgumentException("Workspace files cannot be null.", nameof(files));
        if (files.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new ArgumentException("Playground file IDs must be unique.", nameof(files));
        if (files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new ArgumentException("Playground file paths must be unique.", nameof(files));
    }
}

public static class PlaygroundContract
{
    public const string StoryPath = "Examples/Scripting/Playground";
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
                // Return a real Luxel Widget. Click it to write to the Output panel.
                var label = "Click me";
                return Kit.Button(_ => Log("Button clicked."), label);
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
