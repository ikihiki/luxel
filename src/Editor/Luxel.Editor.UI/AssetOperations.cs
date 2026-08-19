using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

public enum EditorCapabilityAvailability { Enabled, Disabled, Unsupported }
public sealed record EditorCapabilityState(EditorCapabilityAvailability Availability, string? Reason = null)
{
    public bool CanExecute => Availability == EditorCapabilityAvailability.Enabled;
}

public interface IEditorAssetHost
{
    IReadOnlyList<(string Name, string Content)> PickImportFiles();
    void Reveal(string path);
}

public sealed class NullEditorAssetHost : IEditorAssetHost
{
    public static NullEditorAssetHost Instance { get; } = new();
    public IReadOnlyList<(string Name, string Content)> PickImportFiles() => [];
    public void Reveal(string path) => throw new NotSupportedException("Reveal in File Manager is not available on this host.");
}

public interface IAssetStorage
{
    IEnumerable<string> List();
    bool Exists(string path);
    string? Read(string path);
    void Write(string path, string content);
    void Delete(string path);
    void Move(string sourcePath, string destinationPath);
}

public sealed class FileAssetStorage(IFileStorage files) : IAssetStorage
{
    public IEnumerable<string> List() => files.List();
    public bool Exists(string path) => files.Exists(path);
    public string? Read(string path) => files.Read(path);
    public void Write(string path, string content) => files.Write(path, content);
    public void Delete(string path) => files.Delete(path);
    public void Move(string sourcePath, string destinationPath) => files.Move(sourcePath, destinationPath);
}

public enum AssetMutationKind { Create, Rename, Move, Duplicate, Delete, Import }

/// <summary>A single path transition. null old/new paths represent creation/deletion.</summary>
public sealed record AssetPathMutation(string? OldPath, string? NewPath);

public sealed record AssetMutationFailure(string Path, string Message);

/// <summary>
/// Portable asset mutation outcome. Changes contain only storage operations that completed successfully; failures
/// are reported per path so session composition can update document bindings even after a partial batch failure.
/// </summary>
public sealed record AssetMutationResult
{
    public AssetMutationResult(AssetMutationKind kind, IReadOnlyList<AssetPathMutation> changes,
        IReadOnlyList<AssetMutationFailure>? failures = null)
    {
        Kind = kind;
        Changes = changes;
        Failures = failures ?? [];
    }

    public AssetMutationKind Kind { get; }
    public IReadOnlyList<AssetPathMutation> Changes { get; }
    public IReadOnlyList<AssetMutationFailure> Failures { get; }
    public bool Succeeded => Failures.Count == 0;
    public IReadOnlyList<string> CreatedPaths => Changes.Where(x => x.NewPath is not null).Select(x => x.NewPath!).ToArray();
    public IReadOnlyList<string> RemovedPaths => Changes.Where(x => x.OldPath is not null).Select(x => x.OldPath!).ToArray();
    public string FailureMessage => string.Join(Environment.NewLine, Failures.Select(x => $"{x.Path}: {x.Message}"));
}

public interface IAssetOperations
{
    IAssetStorage Storage { get; }
    EditorCapabilityState RevealCapability { get; }
    EditorCapabilityState ImportCapability { get; }

    /// <summary>Raised after every attempted mutation that reached storage, including per-item partial outcomes.</summary>
    event Action<AssetMutationResult>? Mutated;

    AssetMutationResult CreateAsset(string path, string content = "");
    AssetMutationResult RenameAsset(string path, string newName);
    AssetMutationResult MoveAsset(string path, string folder);
    AssetMutationResult DuplicateAsset(string path, string? destination = null);
    AssetMutationResult DeleteAssets(IEnumerable<string> paths);
    AssetMutationResult ImportAssets(string folder, IEnumerable<(string Name, string Content)> files);

    // Compatibility conveniences for existing callers. Detailed results/events are the canonical binding bridge.
    string Create(string path, string content = "") => CreateAsset(path, content).CreatedPaths.Single();
    string Rename(string path, string newName) => RenameAsset(path, newName).Changes.Single().NewPath!;
    string Move(string path, string folder) => MoveAsset(path, folder).Changes.Single().NewPath!;
    string Duplicate(string path, string? destination = null) => DuplicateAsset(path, destination).CreatedPaths.Single();
    void Delete(IEnumerable<string> paths) => DeleteAssets(paths);
    IReadOnlyList<string> Import(string folder, IEnumerable<(string Name, string Content)> files)
        => ImportAssets(folder, files).CreatedPaths;
}

public sealed class AssetOperations : IAssetOperations
{
    public AssetOperations(IAssetStorage storage, EditorCapabilityState? reveal = null, EditorCapabilityState? import = null)
    {
        Storage = storage;
        RevealCapability = reveal ?? new(EditorCapabilityAvailability.Unsupported, "Reveal in File Manager is not available on this host.");
        ImportCapability = import ?? new(EditorCapabilityAvailability.Enabled);
    }

    public IAssetStorage Storage { get; }
    public EditorCapabilityState RevealCapability { get; }
    public EditorCapabilityState ImportCapability { get; }
    public event Action<AssetMutationResult>? Mutated;

    // Keep the original concrete API source-compatible while detailed methods/results remain canonical.
    public string Create(string path, string content = "") => CreateAsset(path, content).CreatedPaths.Single();
    public string Rename(string path, string newName) => RenameAsset(path, newName).Changes.Single().NewPath!;
    public string Move(string path, string folder) => MoveAsset(path, folder).Changes.Single().NewPath!;
    public string Duplicate(string path, string? destination = null) => DuplicateAsset(path, destination).CreatedPaths.Single();
    public void Delete(IEnumerable<string> paths) => DeleteAssets(paths);
    public IReadOnlyList<string> Import(string folder, IEnumerable<(string Name, string Content)> files)
        => ImportAssets(folder, files).CreatedPaths;

    public AssetMutationResult CreateAsset(string path, string content = "")
    {
        path = AssetPath.Validate(path);
        EnsureFree(path);
        Storage.Write(path, content);
        return Completed(AssetMutationKind.Create, new AssetPathMutation(null, path));
    }

    public AssetMutationResult RenameAsset(string path, string newName)
    {
        path = AssetPath.Validate(path);
        newName = AssetPath.ValidateName(newName);
        string destination = AssetPath.Join(AssetPath.Parent(path), newName);
        MoveCore(path, destination);
        return Completed(AssetMutationKind.Rename, new AssetPathMutation(path, destination));
    }

    public AssetMutationResult MoveAsset(string path, string folder)
    {
        path = AssetPath.Validate(path);
        string destination = AssetPath.Join(AssetPath.ValidateFolder(folder), AssetPath.Name(path));
        MoveCore(path, destination);
        return Completed(AssetMutationKind.Move, new AssetPathMutation(path, destination));
    }

    public AssetMutationResult DuplicateAsset(string path, string? destination = null)
    {
        path = AssetPath.Validate(path);
        string content = Storage.Read(path) ?? throw new FileNotFoundException(path);
        destination = destination is null ? NextCopyPath(path) : AssetPath.Validate(destination);
        EnsureFree(destination);
        Storage.Write(destination, content);
        return Completed(AssetMutationKind.Duplicate, new AssetPathMutation(null, destination));
    }

    public AssetMutationResult DeleteAssets(IEnumerable<string> paths)
    {
        string[] normalized = paths.Select(AssetPath.Validate).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string path in normalized)
            if (!Storage.Exists(path)) throw new FileNotFoundException(path);

        var changes = new List<AssetPathMutation>();
        var failures = new List<AssetMutationFailure>();
        foreach (string path in normalized)
        {
            try
            {
                Storage.Delete(path);
                changes.Add(new(path, null));
            }
            catch (Exception ex)
            {
                failures.Add(new(path, ex.Message));
            }
        }
        return Completed(AssetMutationKind.Delete, changes, failures);
    }

    public AssetMutationResult ImportAssets(string folder, IEnumerable<(string Name, string Content)> files)
    {
        if (!ImportCapability.CanExecute) throw new NotSupportedException(ImportCapability.Reason);
        folder = AssetPath.ValidateFolder(folder);
        (string Path, string Content)[] pending = files
            .Select(x => (AssetPath.Join(folder, AssetPath.ValidateName(x.Name)), x.Content))
            .ToArray();
        if (pending.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() != pending.Length)
            throw new IOException("Import contains duplicate asset names.");
        foreach ((string path, _) in pending) EnsureFree(path);

        var changes = new List<AssetPathMutation>();
        var failures = new List<AssetMutationFailure>();
        foreach ((string path, string content) in pending)
        {
            try
            {
                Storage.Write(path, content);
                changes.Add(new(null, path));
            }
            catch (Exception ex)
            {
                failures.Add(new(path, ex.Message));
            }
        }
        return Completed(AssetMutationKind.Import, changes, failures);
    }

    private void MoveCore(string source, string destination)
    {
        if (source == destination) return;
        if (!Storage.Exists(source)) throw new FileNotFoundException(source);
        EnsureFree(destination);
        Storage.Move(source, destination);
    }

    private AssetMutationResult Completed(AssetMutationKind kind, params AssetPathMutation[] changes)
        => Completed(kind, changes, []);

    private AssetMutationResult Completed(AssetMutationKind kind, IReadOnlyList<AssetPathMutation> changes,
        IReadOnlyList<AssetMutationFailure> failures)
    {
        var result = new AssetMutationResult(kind, changes, failures);
        Mutated?.Invoke(result);
        return result;
    }

    private string NextCopyPath(string path)
    {
        string parent = AssetPath.Parent(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string suffix = i == 1 ? " copy" : $" copy {i}";
            string candidate = AssetPath.Join(parent, name + suffix + extension);
            if (!Storage.Exists(candidate)) return candidate;
        }
    }

    private void EnsureFree(string path)
    {
        if (Storage.Exists(path)) throw new IOException($"Asset already exists: {path}");
    }
}

public static class AssetPath
{
    public static string Validate(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(path) || path.Contains(':'))
            throw new ArgumentException("Asset path must be relative.", nameof(path));
        string[] segments = normalized.Split('/');
        if (segments.Any(x => x is "" or "." or ".."))
            throw new ArgumentException("Asset path contains an invalid segment.", nameof(path));
        if (segments.Any(x => x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Asset path contains invalid characters.", nameof(path));
        return string.Join('/', segments);
    }

    public static string ValidateName(string name)
    {
        string normalized = Validate(name);
        if (normalized.Contains('/')) throw new ArgumentException("A name cannot contain path separators.", nameof(name));
        return normalized;
    }

    public static string ValidateFolder(string folder) => string.IsNullOrWhiteSpace(folder) ? "" : Validate(folder);
    public static string Parent(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
    public static string Name(string path) => Path.GetFileName(path);
    public static string Join(string folder, string name) => string.IsNullOrEmpty(folder) ? Validate(name) : Validate($"{folder}/{name}");
}

public sealed record AssetBrowserItem(string Path, string Name, bool IsFolder);

public sealed class AssetBrowserModel
{
    private readonly IAssetOperations _operations;
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    public AssetBrowserModel(IAssetOperations operations) { _operations = operations; Refresh(); }
    public Signal<int> Version { get; } = new(0);
    public Signal<string> CurrentFolder { get; } = new("");
    public Signal<string> Filter { get; } = new("");
    public Signal<string?> Error { get; } = new(null);
    public IReadOnlySet<string> Selection => _selection;
    public IReadOnlyList<string> Paths { get; private set; } = [];

    public bool Refresh()
    {
        try
        {
            Paths = _operations.Storage.List().Select(AssetPath.Validate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            _selection.RemoveWhere(x => !Paths.Contains(x, StringComparer.Ordinal));
            string folder = CurrentFolder.Peek();
            if (folder.Length > 0 && !Paths.Any(x => x.StartsWith(folder + "/", StringComparison.Ordinal)))
                CurrentFolder.Value = "";
            Error.Value = null;
            Version.Value++;
            return true;
        }
        catch (Exception ex)
        {
            Error.Value = ex.Message;
            Version.Value++;
            return false;
        }
    }

    public void OpenFolder(string folder)
    {
        CurrentFolder.Value = AssetPath.ValidateFolder(folder);
        _selection.Clear();
        Version.Value++;
    }

    public void Select(string path, bool additive = false, bool toggle = false)
    {
        if (!additive) _selection.Clear();
        if (Paths.Contains(path, StringComparer.Ordinal))
        {
            if (!toggle || !_selection.Remove(path)) _selection.Add(path);
        }
        Version.Value++;
    }

    public void SelectMany(IEnumerable<string> paths)
    {
        _selection.Clear();
        foreach (string path in paths)
            if (Paths.Contains(path, StringComparer.Ordinal)) _selection.Add(path);
        Version.Value++;
    }

    public IReadOnlyList<AssetBrowserItem> CurrentItems()
    {
        string folder = CurrentFolder.Peek();
        string prefix = string.IsNullOrEmpty(folder) ? "" : folder + "/";
        string filter = Filter.Peek();
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<AssetBrowserItem>();
        foreach (string path in Paths)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rest = path[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash >= 0) folders.Add(rest[..slash]);
            else if (Matches(rest, filter)) files.Add(new(path, rest, false));
        }
        return folders.Where(x => Matches(x, filter))
            .Order(StringComparer.Ordinal).Select(x => new AssetBrowserItem(AssetPath.Join(folder, x), x, true))
            .Concat(files.OrderBy(x => x.Name, StringComparer.Ordinal)).ToArray();
    }

    public IReadOnlyList<TreeNode> FolderTree()
    {
        var root = new FolderNode();
        foreach (string path in Paths)
        {
            string[] parts = path.Split('/');
            FolderNode current = root;
            for (int i = 0; i < parts.Length - 1; i++)
                current = current.Children.TryGetValue(parts[i], out FolderNode? child)
                    ? child : current.Children[parts[i]] = new FolderNode();
        }
        return ToTree(root, "");
    }

    private static bool Matches(string value, string filter)
        => string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<TreeNode> ToTree(FolderNode node, string prefix)
        => node.Children.Select(x =>
        {
            string path = prefix.Length == 0 ? x.Key : $"{prefix}/{x.Key}";
            return new TreeNode(path, x.Key, ToTree(x.Value, path), Tag: path);
        }).ToArray();

    private sealed class FolderNode
    {
        public SortedDictionary<string, FolderNode> Children { get; } = new(StringComparer.Ordinal);
    }
}
