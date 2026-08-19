using Luxel.UI;

namespace Luxel.Workbench;

/// <summary>
/// ドキュメント永続化の低レベル FS 抽象 (テスト = メモリ、実機 = 実ディスク)。
/// Luxel.Resources の IVirtualFileSystem は read-only の資産読込用・Luxel.Settings の
/// IFileStore は設定専用なので、Workbench は書込 + 監視を持つ独自の薄い口を切る
/// (ADR-0010 の「新規に切る」側の選択)。
/// </summary>
public interface IFileStorage
{
    bool Exists(string path);

    /// <summary>内容を読む。無ければ null。</summary>
    string? Read(string path);

    /// <summary>内容を書く (上書き)。</summary>
    void Write(string path, string content);

    /// <summary>変更監視。未対応なら null。callback のスレッドは実装依存
    /// (実ディスクは watcher スレッド — UI へは呼び側がマーシャルする)。</summary>
    IDisposable? Watch(string path, Action onChanged);

    /// <summary>全ファイルの相対 path を列挙する ('/' 区切り、順序不定)。AssetBrowser 等の
    /// 一覧表示用。</summary>
    IEnumerable<string> List();

    /// <summary>ファイルを削除する。未対応の storage は明示的に失敗する。</summary>
    void Delete(string path) => throw new NotSupportedException("This storage does not support delete.");

    /// <summary>ファイルを移動/rename する。未対応の storage は明示的に失敗する。</summary>
    void Move(string sourcePath, string destinationPath)
        => throw new NotSupportedException("This storage does not support move.");
}

/// <summary>メモリ上の <see cref="IFileStorage"/> (テスト/一時ワークスペース用、決定的)。
/// Write/Set で watch が同期発火する。</summary>
public sealed class MemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, string> _files = new();
    private readonly Dictionary<string, List<Action>> _watchers = new();
    private readonly object _lock = new();

    public bool Exists(string path) { lock (_lock) return _files.ContainsKey(path); }

    public string? Read(string path) { lock (_lock) return _files.GetValueOrDefault(path); }

    public void Write(string path, string content)
    {
        List<Action>? cbs = null;
        lock (_lock)
        {
            _files[path] = content;
            if (_watchers.TryGetValue(path, out var list)) cbs = new List<Action>(list);
        }
        if (cbs != null) foreach (Action cb in cbs) cb();
    }

    public IDisposable Watch(string path, Action onChanged)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(path, out var list)) _watchers[path] = list = new List<Action>();
            list.Add(onChanged);
        }
        return new Token(() => { lock (_lock) { if (_watchers.TryGetValue(path, out var l)) l.Remove(onChanged); } });
    }

    public IEnumerable<string> List() { lock (_lock) return _files.Keys.ToArray(); }

    public void Delete(string path)
    {
        lock (_lock)
        {
            if (!_files.Remove(path)) throw new FileNotFoundException(path);
        }
    }

    public void Move(string sourcePath, string destinationPath)
    {
        lock (_lock)
        {
            if (!_files.TryGetValue(sourcePath, out string? content)) throw new FileNotFoundException(sourcePath);
            if (_files.ContainsKey(destinationPath)) throw new IOException($"Destination already exists: {destinationPath}");
            _files.Remove(sourcePath);
            _files[destinationPath] = content;
        }
    }

    private sealed class Token(Action dispose) : IDisposable
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }
}

/// <summary>実ディスクの <see cref="IFileStorage"/> (root 相対)。監視は FileSystemWatcher —
/// callback は watcher スレッドで来る。</summary>
public sealed class PhysicalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly StringComparison _pathComparison;

    public PhysicalFileStorage(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _rootPrefix = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;
        _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private string Full(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        bool hasDrivePrefix = normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':';
        if (Path.IsPathRooted(path) || normalized.StartsWith("/", StringComparison.Ordinal) || hasDrivePrefix)
            throw new ArgumentException("Storage path must be relative to the configured root.", nameof(path));
        if (normalized.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("Storage path cannot contain '..' segments.", nameof(path));

        string full = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_rootPrefix, _pathComparison))
            throw new ArgumentException("Storage path escapes the configured root.", nameof(path));
        RejectReparsePoints(full, path);
        return full;
    }

    private void RejectReparsePoints(string full, string path)
    {
        string relative = Path.GetRelativePath(_root, full);
        string current = _root;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Storage path cannot traverse symbolic links or reparse points.", nameof(path));
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
    }

    public bool Exists(string path) => File.Exists(Full(path));

    public string? Read(string path)
    {
        string full = Full(path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public void Write(string path, string content)
    {
        string full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public IDisposable? Watch(string path, Action onChanged)
    {
        string full = Full(path);
        string? dir = Path.GetDirectoryName(full);
        if (dir == null || !Directory.Exists(dir)) return null;
        var w = new FileSystemWatcher(dir, Path.GetFileName(full))
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        FileSystemEventHandler h = (_, _) => onChanged();
        w.Changed += h; w.Created += h;
        w.Renamed += (_, _) => onChanged();
        return w;
    }

    public IEnumerable<string> List()
    {
        if (!Directory.Exists(_root)) yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (string file in Directory.EnumerateFiles(_root, "*", options))
        {
            string relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
            _ = Full(relative); // Keep enumeration subject to the same root-confinement invariant as direct operations.
            yield return relative;
        }
    }

    public void Delete(string path)
    {
        string full = Full(path);
        if (!File.Exists(full)) throw new FileNotFoundException(path);
        File.Delete(full);
    }

    public void Move(string sourcePath, string destinationPath)
    {
        string source = Full(sourcePath);
        string destination = Full(destinationPath);
        if (!File.Exists(source)) throw new FileNotFoundException(sourcePath);
        if (File.Exists(destination)) throw new IOException($"Destination already exists: {destinationPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
    }
}

/// <summary>ドキュメント 1 つとファイルの結び付き (path + 外部変更フラグ)。</summary>
public sealed class DocumentBinding
{
    /// <summary>結び付いたファイルパス (SaveAs / Rebind で変わる)。</summary>
    public string Path { get; internal set; } = "";

    /// <summary>ファイルが外部で変わったか。</summary>
    public Signal<bool> ExternalChange { get; } = new(false);

    internal string LastSaved = "";
    internal IDisposable? WatchToken;
    internal int WatchGeneration;
}

/// <summary>Atomic document path rebind result. The binding object is preserved for already-bound documents.</summary>
public sealed record DocumentRebindResult(DocumentBinding Binding, string? PreviousPath, string Path, bool Changed);

/// <summary>Information returned when a document is detached from its file watcher/path.</summary>
public sealed record DocumentUnbindResult(IEditorDocument Document, DocumentBinding Binding, string Path);

/// <summary>
/// ドキュメントとファイルを結ぶ永続化コンポーネント (ADR-0010)。open / save / saveAs /
/// 外部変更検知を持ち、ダーティ確認 (「保存しますか」) はシェルの責務。
/// Workspace の開閉に追従して結び付きを自動で掃除する。
/// </summary>
public interface IDocumentStore
{
    IEditorDocument Open(string kind, string path);
    void Save(IEditorDocument doc);
    void SaveAs(IEditorDocument doc, string path);
    void Reload(IEditorDocument doc);
    DocumentBinding? BindingOf(IEditorDocument doc);
    IEditorDocument? DocAt(string path);

    /// <summary>
    /// Rebind an open document to an existing path without serializing/loading it. Used after an asset rename/move.
    /// The previous watcher remains active if collision, read, or watcher setup fails.
    /// </summary>
    DocumentRebindResult Rebind(IEditorDocument doc, string path);

    /// <summary>Detach a document from its path and watcher. Returns null when it was already unbound.</summary>
    DocumentUnbindResult? Unbind(IEditorDocument doc);
}

/// <inheritdoc cref="IDocumentStore"/>
public sealed class DocumentStore : IDocumentStore, IDisposable
{
    private readonly Workspace _ws;
    private readonly IFileStorage _storage;
    private readonly Dictionary<IEditorDocument, DocumentBinding> _bindings = new();
    private readonly IDisposable _prune;

    public DocumentStore(Workspace workspace, IFileStorage storage)
    {
        _ws = workspace;
        _storage = storage;
        _prune = Reactive.Effect(() =>
        {
            IReadOnlyList<IEditorDocument> open = _ws.Documents;
            foreach (IEditorDocument doc in _bindings.Keys.Where(d => !open.Contains(d)).ToList())
                Unbind(doc);
        });
    }

    public IEditorDocument Open(string kind, string path)
    {
        path = ValidatePath(path);
        if (DocAt(path) is { } existing)
        {
            _ws.Activate(existing);
            return existing;
        }
        string content = _storage.Read(path) ?? throw new FileNotFoundException(path);
        IEditorDocument doc = _ws.Open(kind, content);
        try
        {
            _bindings[doc] = CreateBinding(path, content);
            doc.Dirty.Value = false;
            return doc;
        }
        catch
        {
            _ws.Close(doc);
            throw;
        }
    }

    public void Save(IEditorDocument doc)
    {
        DocumentBinding b = _bindings.GetValueOrDefault(doc)
            ?? throw new InvalidOperationException("path 未結線のドキュメント — SaveAs を使う");
        bool wasDirty = doc.Dirty.Peek();
        bool hadExternalChange = b.ExternalChange.Peek();
        string previousSaved = b.LastSaved;
        string content;
        try
        {
            content = doc.Serialize();
            b.LastSaved = content;
            _storage.Write(b.Path, content);
        }
        catch
        {
            b.LastSaved = previousSaved;
            b.ExternalChange.Value = hadExternalChange;
            doc.Dirty.Value = wasDirty;
            throw;
        }
        doc.AcceptSavedSnapshot(content);
        b.ExternalChange.Value = false;
    }

    public void SaveAs(IEditorDocument doc, string path)
    {
        path = ValidatePath(path);
        EnsureOpenDocument(doc);
        EnsurePathAvailable(doc, path);
        DocumentBinding? old = _bindings.GetValueOrDefault(doc);
        if (old?.Path == path) { Save(doc); return; }

        bool wasDirty = doc.Dirty.Peek();
        string content = doc.Serialize();
        bool destinationExisted = _storage.Exists(path);
        string? destinationContent = destinationExisted
            ? _storage.Read(path) ?? throw new IOException($"Destination could not be read before Save As: {path}")
            : null;
        IDisposable? preparedToken = null;
        DocumentBinding? committedBinding = null;
        bool writeAttempted = false;
        try
        {
            // The destination (and, for physical storage, its directory) must exist before watcher setup.
            writeAttempted = true;
            _storage.Write(path, content);

            if (old is null)
            {
                DocumentBinding replacement = CreateBinding(path, content);
                preparedToken = replacement.WatchToken;
                _bindings[doc] = replacement;
                committedBinding = replacement;
                preparedToken = null;
            }
            else
            {
                PreparedWatcher prepared = PrepareWatcher(old, path);
                preparedToken = prepared.Token;
                CommitRebind(old, path, content, prepared);
                committedBinding = old;
                preparedToken = null;
            }
        }
        catch (Exception failure)
        {
            preparedToken?.Dispose();
            doc.Dirty.Value = wasDirty;
            if (writeAttempted)
            {
                try { RestoreDestination(path, content, destinationExisted, destinationContent); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException($"Save As failed and destination rollback also failed: {path}", failure, rollbackFailure);
                }
            }
            throw;
        }
        VerifySavedDestination(committedBinding!, path, content);
        doc.AcceptSavedSnapshot(content);
    }

    public DocumentRebindResult Rebind(IEditorDocument doc, string path)
    {
        path = ValidatePath(path);
        EnsureOpenDocument(doc);
        EnsurePathAvailable(doc, path);
        string lastSaved = _storage.Read(path) ?? throw new FileNotFoundException(path);

        if (!_bindings.TryGetValue(doc, out DocumentBinding? binding))
        {
            binding = CreateBinding(path, lastSaved);
            _bindings.Add(doc, binding);
            return new(binding, null, path, true);
        }
        bool hasExternalChange = binding.ExternalChange.Peek() || lastSaved != binding.LastSaved;
        if (binding.Path == path)
        {
            binding.ExternalChange.Value = hasExternalChange;
            return new(binding, path, path, false);
        }

        string previousPath = binding.Path;
        PreparedWatcher prepared = PrepareWatcher(binding, path);
        CommitRebind(binding, path, hasExternalChange ? binding.LastSaved : lastSaved, prepared, hasExternalChange);
        return new(binding, previousPath, path, true);
    }

    public DocumentUnbindResult? Unbind(IEditorDocument doc)
    {
        if (!_bindings.Remove(doc, out DocumentBinding? binding)) return null;
        string path = binding.Path;
        binding.WatchGeneration++;
        IDisposable? token = binding.WatchToken;
        binding.WatchToken = null;
        token?.Dispose();
        binding.ExternalChange.Value = false;
        return new(doc, binding, path);
    }

    public void Reload(IEditorDocument doc)
    {
        DocumentBinding b = _bindings.GetValueOrDefault(doc)
            ?? throw new InvalidOperationException("path 未結線のドキュメント");
        string content = _storage.Read(b.Path) ?? throw new FileNotFoundException(b.Path);
        b.LastSaved = content;
        doc.LoadFrom(content);
        doc.Dirty.Value = false;
        b.ExternalChange.Value = false;
    }

    public DocumentBinding? BindingOf(IEditorDocument doc) => _bindings.GetValueOrDefault(doc);

    public IEditorDocument? DocAt(string path)
    {
        path = ValidatePath(path);
        return _bindings.FirstOrDefault(kv => string.Equals(kv.Value.Path, path, StringComparison.Ordinal)).Key;
    }

    public void Dispose()
    {
        _prune.Dispose();
        foreach (IEditorDocument doc in _bindings.Keys.ToList()) Unbind(doc);
    }

    private DocumentBinding CreateBinding(string path, string lastSaved)
    {
        var binding = new DocumentBinding { Path = path, LastSaved = lastSaved, WatchGeneration = 1 };
        int generation = binding.WatchGeneration;
        binding.WatchToken = _storage.Watch(path, () => ObserveExternalChange(binding, path, generation));
        return binding;
    }

    private PreparedWatcher PrepareWatcher(DocumentBinding binding, string path)
    {
        int generation = binding.WatchGeneration + 1;
        IDisposable? token = _storage.Watch(path, () => ObserveExternalChange(binding, path, generation));
        return new(generation, token);
    }

    private static void CommitRebind(DocumentBinding binding, string path, string lastSaved, PreparedWatcher prepared,
        bool externalChange = false)
    {
        IDisposable? oldToken = binding.WatchToken;
        binding.Path = path;
        binding.LastSaved = lastSaved;
        binding.ExternalChange.Value = externalChange;
        binding.WatchGeneration = prepared.Generation;
        binding.WatchToken = prepared.Token;
        oldToken?.Dispose();
    }

    private void ObserveExternalChange(DocumentBinding binding, string watchedPath, int generation)
    {
        if (binding.WatchGeneration != generation || binding.Path != watchedPath) return;
        string? now = _storage.Read(watchedPath);
        if (now is null || now != binding.LastSaved) binding.ExternalChange.Value = true;
    }

    private void VerifySavedDestination(DocumentBinding binding, string path, string content)
    {
        try
        {
            if (_storage.Read(path) != content) binding.ExternalChange.Value = true;
        }
        catch
        {
            binding.ExternalChange.Value = true;
        }
    }

    private void RestoreDestination(string path, string writtenContent, bool existed, string? content)
    {
        bool existsNow = _storage.Exists(path);
        string? current = existsNow ? _storage.Read(path) : null;
        if (!existed)
        {
            if (!existsNow) return;
            if (current != writtenContent)
                throw new IOException($"Destination changed concurrently and was not overwritten during rollback: {path}");
            _storage.Delete(path);
            return;
        }

        if (existsNow && current == content) return; // The failed write left the original destination untouched.
        if (existsNow && current != writtenContent)
            throw new IOException($"Destination changed concurrently and was not overwritten during rollback: {path}");
        _storage.Write(path, content!);
    }

    private void EnsureOpenDocument(IEditorDocument doc)
    {
        if (!_ws.Documents.Contains(doc))
            throw new InvalidOperationException("Only documents open in the workspace can be bound.");
    }

    private void EnsurePathAvailable(IEditorDocument doc, string path)
    {
        if (DocAt(path) is { } other && !ReferenceEquals(other, doc))
            throw new InvalidOperationException($"別のドキュメントが開いている path へは結べない: {path}");
    }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Replace('\\', '/');
    }

    private sealed record PreparedWatcher(int Generation, IDisposable? Token);
}
