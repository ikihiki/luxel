using System.Text.Json;
using Luxel.Settings;

namespace Luxel.Gallery.Playground;

/// <summary>
/// Editable native Playground workspace with schema-v2 persistence. The session is UI agnostic so
/// native Gallery controls can reuse it without coupling persistence to a particular editor widget.
/// </summary>
public sealed class NativePlaygroundSession : IDisposable, IAsyncDisposable
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

    public NativePlaygroundSession(
        IFileStore store,
        PlaygroundTemplate template,
        NativePlaygroundResourceOptions? resourceOptions = null,
        NativeSlangLanguageServiceOptions? languageServiceOptions = null,
        ISlangLanguageServerConnectionFactory? languageServerFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        Draft = Load() ?? template.CreateDraft();
        ResourceSession = new NativePlaygroundResourceSession(Draft, resourceOptions);
        SlangLanguage = NativeSlangCodeLanguage.CreateDefault(languageServiceOptions, languageServerFactory);
        SlangLanguage.SyncWorkspace(Draft);
    }

    public PlaygroundDraft Draft { get; private set; }
    public NativePlaygroundResourceSession ResourceSession { get; }
    public NativeSlangCodeLanguage SlangLanguage { get; }

    public string ActiveFileName => Draft.SelectedFile.Path;

    public string StorageName => StoragePrefix + _template.Id + ".json";

    public void UpdateFile(string fileNameOrId, string source)
    {
        Draft = Draft.UpdateFile(fileNameOrId, source);
        SyncWorkspace();
        Save();
    }

    public PlaygroundFile AddFile(string path, string source = "", string? language = null, string? id = null)
    {
        Draft = Draft.AddFile(path, source, language, id);
        SyncWorkspace();
        Save();
        return Draft.Files[^1];
    }

    public void RenameFile(string fileNameOrId, string newPath, string? language = null)
    {
        Draft = Draft.RenameFile(fileNameOrId, newPath, language);
        SyncWorkspace();
        Save();
    }

    public void DeleteFile(string fileNameOrId)
    {
        Draft = Draft.DeleteFile(fileNameOrId);
        SyncWorkspace();
        Save();
    }

    public void Activate(string fileNameOrId)
    {
        Draft = Draft.SelectFile(fileNameOrId);
        SlangLanguage.SyncWorkspace(Draft);
        Save();
    }

    public void Reset()
    {
        Draft = _template.CreateDraft();
        SyncWorkspace();
        Save();
    }

    public void Dispose()
    {
        SlangLanguage.Dispose();
        ResourceSession.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await SlangLanguage.DisposeAsync().ConfigureAwait(false);
        await ResourceSession.DisposeAsync().ConfigureAwait(false);
    }

    private void SyncWorkspace()
    {
        ResourceSession.SyncWorkspace(Draft);
        SlangLanguage.SyncWorkspace(Draft);
    }

    public void Save()
    {
        var persisted = new PersistedWorkspace
        {
            SchemaVersion = SchemaVersion,
            TemplateId = Draft.TemplateId,
            Title = Draft.Title,
            MainFileId = Draft.MainFileId,
            SelectedFileId = Draft.SelectedFileId,
            Revision = Draft.Revision,
            Files = Draft.Files.Select(file => new PersistedFile
            {
                Id = file.Id,
                Path = file.Path,
                Language = file.Language,
                Source = file.Source,
                Version = file.Version,
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
            if (persisted is null || persisted.SchemaVersion != SchemaVersion || persisted.TemplateId != _template.Id)
                return null;

            return new PlaygroundDraft(
                persisted.TemplateId,
                persisted.Title,
                persisted.MainFileId,
                persisted.SelectedFileId,
                persisted.Files.Select(file => new PlaygroundFile(
                    file.Id, file.Path, file.Language, file.Source, file.Version)).ToArray(),
                persisted.Revision);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private sealed record PersistedWorkspace
    {
        public int SchemaVersion { get; init; }
        public string TemplateId { get; init; } = "";
        public string Title { get; init; } = "";
        public string MainFileId { get; init; } = "";
        public string SelectedFileId { get; init; } = "";
        public long Revision { get; init; }
        public IReadOnlyList<PersistedFile> Files { get; init; } = [];
    }

    private sealed record PersistedFile
    {
        public string Id { get; init; } = "";
        public string Path { get; init; } = "";
        public string Language { get; init; } = "";
        public string Source { get; init; } = "";
        public long Version { get; init; }
    }
}
