using System.Text;
using System.Text.Json;
using Luxel.Resources;
using Luxel.Scripting.Roslyn.Web;

namespace LuxelPlaygroundBrowser;

internal sealed record BrowserWorkspaceFile(
    string Id,
    string Path,
    string Language,
    string Source,
    int Version);

internal sealed record BrowserWorkspaceSnapshot(
    int SchemaVersion,
    int Revision,
    string EntryFileId,
    string ActiveFileId,
    BrowserWorkspaceFile[] Files)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedLanguages =
    [
        "csharp-script", "csharp", "slang", "text", "plaintext", "json", "markdown",
        "xml", "html", "css", "javascript", "typescript",
    ];

    public static BrowserWorkspaceSnapshot Parse(string json, int? expectedRevision = null)
    {
        BrowserWorkspaceSnapshot snapshot = JsonSerializer.Deserialize<BrowserWorkspaceSnapshot>(json, JsonOptions)
            ?? throw new InvalidOperationException("The protocol v2 workspace snapshot is invalid.");
        if (snapshot.SchemaVersion != 2)
            throw new InvalidOperationException($"Unsupported workspace schema version {snapshot.SchemaVersion}.");
        if (snapshot.Revision < 0)
            throw new InvalidOperationException("The workspace revision cannot be negative.");
        if (expectedRevision is { } revision && snapshot.Revision != revision)
            throw new InvalidOperationException($"Workspace revision {snapshot.Revision} does not match request revision {revision}.");
        if (snapshot.Files is not { Length: > 0 })
            throw new InvalidOperationException("The workspace must contain at least one file.");
        if (snapshot.Files.Length > WorkspaceLimits.MaxFileCount)
            throw new InvalidOperationException($"The workspace contains {snapshot.Files.Length} files; the limit is {WorkspaceLimits.MaxFileCount}.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sourceBytes = 0;
        foreach (BrowserWorkspaceFile file in snapshot.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Id) || !ids.Add(file.Id))
                throw new InvalidOperationException("Workspace file IDs must be non-empty and unique.");
            string normalized = WorkspacePath.Normalize(file.Path);
            if (!string.Equals(normalized, file.Path, StringComparison.Ordinal) || !paths.Add(normalized))
                throw new InvalidOperationException($"Workspace path '{file.Path}' is not normalized or unique.");
            if (file.Source is null || file.Version < 0)
                throw new InvalidOperationException($"Workspace file '{file.Path}' has invalid source or version data.");
            int fileBytes = Encoding.UTF8.GetByteCount(file.Source);
            if ((file.Language is "csharp-script" or "csharp") && fileBytes > WorkspaceLimits.MaxCSharpFileBytes)
                throw new InvalidOperationException($"C# file '{file.Path}' exceeds the {WorkspaceLimits.MaxCSharpFileBytes} byte limit.");
            sourceBytes += fileBytes;
            if (sourceBytes > WorkspaceLimits.MaxTotalSourceBytes)
                throw new InvalidOperationException($"Workspace source exceeds the {WorkspaceLimits.MaxTotalSourceBytes} byte limit.");
            if (!SupportedLanguages.Contains(file.Language))
                throw new InvalidOperationException($"Workspace file '{file.Path}' has unsupported language '{file.Language}'.");
        }
        BrowserWorkspaceFile entry = snapshot.File(snapshot.EntryFileId);
        _ = snapshot.File(snapshot.ActiveFileId);
        if (entry.Language != "csharp-script")
            throw new InvalidOperationException("The workspace entry file must use the csharp-script language.");
        return snapshot;
    }

    public BrowserWorkspaceFile File(string id)
        => Files.FirstOrDefault(file => string.Equals(file.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Workspace file ID '{id}' does not exist.");

    public WebScriptProject ToWebScriptProject()
    {
        BrowserWorkspaceFile entry = File(EntryFileId);
        return new WebScriptProject(
            new WebScriptDocument(entry.Path, entry.Source),
            Files.Where(file => file.Id != EntryFileId && file.Language == "csharp")
                .Select(file => new WebScriptDocument(file.Path, file.Source))
                .ToArray());
    }
}
