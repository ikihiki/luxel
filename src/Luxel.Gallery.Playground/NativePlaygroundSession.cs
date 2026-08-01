using System.Text.Json;
using Luxel.Settings;

namespace Luxel.Gallery.Playground;

/// <summary>
/// Editable native Playground workspace with schema-v2 persistence. The session is UI agnostic so
/// native Gallery controls can reuse it without coupling persistence to a particular editor widget.
/// </summary>
public sealed class NativePlaygroundSession
{
    public const int SchemaVersion = 2;
    public const string StoragePrefix = "luxel.playground.workspace.v2:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IFileStore _store;
    private readonly PlaygroundTemplate _template;

    public NativePlaygroundSession(IFileStore store, PlaygroundTemplate template)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        ActiveFileName = template.MainFileName;
        Draft = Load() ?? template.CreateDraft();
        if (!Draft.Files.Any(file => file.FileName == ActiveFileName))
            ActiveFileName = Draft.MainFileName;
    }

    public PlaygroundDraft Draft { get; private set; }

    public string ActiveFileName { get; private set; }

    public string StorageName => StoragePrefix + _template.Id + ".json";

    /// <summary>
    /// Produces one native C# submission while retaining logical file names through #line directives.
    /// Supporting documents are emitted before the main script so declarations are available to it.
    /// </summary>
    public string CreateExecutionSource()
    {
        PlaygroundFile main = Draft.MainFile;
        string supporting = string.Join("\n\n", Draft.Files
            .Where(file => file.FileName != Draft.MainFileName)
            .Select(file => $"#line 1 \"{file.FileName}\"\n{file.Source}"));
        return supporting.Length == 0
            ? main.Source
            : $"{supporting}\n\n#line 1 \"{main.FileName}\"\n{main.Source}";
    }

    public void UpdateFile(string fileName, string source)
    {
        Draft = Draft.UpdateFile(fileName, source);
        Save();
    }

    public void Activate(string fileName)
    {
        if (!Draft.Files.Any(file => file.FileName == fileName))
            throw new ArgumentException($"The workspace does not contain '{fileName}'.", nameof(fileName));
        ActiveFileName = fileName;
        Save();
    }

    public void Reset()
    {
        Draft = _template.CreateDraft();
        ActiveFileName = Draft.MainFileName;
        Save();
    }

    public void Save()
    {
        var persisted = new PersistedWorkspace
        {
            SchemaVersion = SchemaVersion,
            TemplateId = Draft.TemplateId,
            Title = Draft.Title,
            MainFileName = Draft.MainFileName,
            ActiveFileName = ActiveFileName,
            Files = Draft.Files.Select(file => new PersistedFile
            {
                FileName = file.FileName,
                Source = file.Source,
            }).ToArray(),
        };
        _store.Write(StorageName, JsonSerializer.Serialize(persisted, JsonOptions));
    }

    private PlaygroundDraft? Load()
    {
        string? json = _store.Read(StorageName);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            PersistedWorkspace? persisted = JsonSerializer.Deserialize<PersistedWorkspace>(json, JsonOptions);
            if (persisted is null || persisted.SchemaVersion != SchemaVersion ||
                persisted.TemplateId != _template.Id || persisted.Files.Count == 0 ||
                persisted.Files.Select(file => file.FileName).Distinct(StringComparer.Ordinal).Count() != persisted.Files.Count ||
                persisted.Files.Count(file => file.FileName == persisted.MainFileName) != 1)
                return null;

            var draft = new PlaygroundDraft(
                persisted.TemplateId,
                string.IsNullOrWhiteSpace(persisted.Title) ? _template.Title : persisted.Title,
                persisted.MainFileName,
                persisted.Files.Select(file => new PlaygroundFile(file.FileName, file.Source)).ToArray());
            ActiveFileName = draft.Files.Any(file => file.FileName == persisted.ActiveFileName)
                ? persisted.ActiveFileName
                : draft.MainFileName;
            return draft;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PersistedWorkspace
    {
        public int SchemaVersion { get; init; }
        public string TemplateId { get; init; } = "";
        public string Title { get; init; } = "";
        public string MainFileName { get; init; } = "";
        public string ActiveFileName { get; init; } = "";
        public IReadOnlyList<PersistedFile> Files { get; init; } = [];
    }

    private sealed record PersistedFile
    {
        public string FileName { get; init; } = "";
        public string Source { get; init; } = "";
    }
}
