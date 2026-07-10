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

    private sealed class Token(Action dispose) : IDisposable
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }
}

/// <summary>実ディスクの <see cref="IFileStorage"/> (root 相対)。監視は FileSystemWatcher —
/// callback は watcher スレッドで来る。</summary>
public sealed class PhysicalFileStorage(string root) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(root);

    private string Full(string path) => Path.GetFullPath(Path.Combine(_root, path));

    public bool Exists(string path) => File.Exists(Full(path));

    public string? Read(string path) => File.Exists(Full(path)) ? File.ReadAllText(Full(path)) : null;

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
        foreach (string f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            yield return Path.GetRelativePath(_root, f).Replace('\\', '/');
    }
}

/// <summary>ドキュメント 1 つとファイルの結び付き (path + 外部変更フラグ)。</summary>
public sealed class DocumentBinding
{
    /// <summary>結び付いたファイルパス (SaveAs で変わる)。</summary>
    public string Path { get; internal set; } = "";

    /// <summary>ファイルが外部で変わったか (エディタ外の編集)。シェルがこれを見て
    /// 「再読込しますか」を出し、<see cref="DocumentStore.Reload"/> で取り込むか
    /// <c>Value = false</c> で無視する。</summary>
    public Signal<bool> ExternalChange { get; } = new(false);

    internal string LastSaved = "";
    internal IDisposable? WatchToken;
}

/// <summary>
/// ドキュメントとファイルを結ぶ永続化コンポーネント (ADR-0010)。open / save / saveAs /
/// 外部変更検知を持ち、ダーティ確認 (「保存しますか」) はシェルの責務。
/// Workspace の開閉に追従して結び付きを自動で掃除する。
/// </summary>
public interface IDocumentStore
{
    /// <summary>ファイルを開く (読取 → Workspace へ open → path を結ぶ)。既に同じ path が
    /// 開いていればアクティブ化して返す。無いファイルは FileNotFoundException。</summary>
    IEditorDocument Open(string kind, string path);

    /// <summary>上書き保存 (Serialize → Write、Dirty/ExternalChange 解除)。
    /// path 未結線 (New で作った doc) は InvalidOperationException — SaveAs を使う。</summary>
    void Save(IEditorDocument doc);

    /// <summary>path を結び直して保存 (新規 doc の初回保存にも)。別 doc が同じ path を
    /// 開いていれば InvalidOperationException。</summary>
    void SaveAs(IEditorDocument doc, string path);

    /// <summary>外部変更をファイルから取り込み直す (LoadFrom、Dirty/ExternalChange 解除)。</summary>
    void Reload(IEditorDocument doc);

    /// <summary>doc の結び付き。未結線 (New 直後) は null。</summary>
    DocumentBinding? BindingOf(IEditorDocument doc);

    /// <summary>path を開いている doc。無ければ null。</summary>
    IEditorDocument? DocAt(string path);
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
        // Workspace の開閉に追従 — 閉じた doc の結び付き (watch 込み) を自動で外す
        _prune = Reactive.Effect(() =>
        {
            IReadOnlyList<IEditorDocument> open = _ws.Documents;
            foreach (IEditorDocument doc in _bindings.Keys.Where(d => !open.Contains(d)).ToList())
                Unbind(doc);
        });
    }

    public IEditorDocument Open(string kind, string path)
    {
        if (DocAt(path) is { } existing)
        {
            _ws.Activate(existing);
            return existing;
        }
        string content = _storage.Read(path) ?? throw new FileNotFoundException(path);
        IEditorDocument doc = _ws.Open(kind, content);
        Bind(doc, path, content);
        doc.Dirty.Value = false;
        return doc;
    }

    public void Save(IEditorDocument doc)
    {
        DocumentBinding b = _bindings.GetValueOrDefault(doc)
            ?? throw new InvalidOperationException("path 未結線のドキュメント — SaveAs を使う");
        string content = doc.Serialize();
        b.LastSaved = content;   // 自書込の watch エコーを LastSaved 比較で無視するため Write より先
        _storage.Write(b.Path, content);
        doc.Dirty.Value = false;
        b.ExternalChange.Value = false;
    }

    public void SaveAs(IEditorDocument doc, string path)
    {
        if (DocAt(path) is { } other && !ReferenceEquals(other, doc))
            throw new InvalidOperationException($"別のドキュメントが開いている path へは保存できない: {path}");
        if (_bindings.GetValueOrDefault(doc) is { } old)
        {
            old.WatchToken?.Dispose();
            _bindings.Remove(doc);
        }
        string content = doc.Serialize();
        Bind(doc, path, content);
        _storage.Write(path, content);
        doc.Dirty.Value = false;
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
        => _bindings.FirstOrDefault(kv => kv.Value.Path == path).Key;

    public void Dispose()
    {
        _prune.Dispose();
        foreach (IEditorDocument doc in _bindings.Keys.ToList()) Unbind(doc);
    }

    private void Bind(IEditorDocument doc, string path, string lastSaved)
    {
        var b = new DocumentBinding { Path = path, LastSaved = lastSaved };
        _bindings[doc] = b;
        b.WatchToken = _storage.Watch(path, () =>
        {
            // 自分の Write のエコー (または実質同内容) は無視 — 保存直前に LastSaved を更新している
            string? now = _storage.Read(path);
            if (now != null && now != b.LastSaved) b.ExternalChange.Value = true;
        });
    }

    private void Unbind(IEditorDocument doc)
    {
        if (_bindings.Remove(doc, out DocumentBinding? b)) b.WatchToken?.Dispose();
    }
}
