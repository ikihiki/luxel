namespace Luxel.Settings;

/// <summary>
/// 設定ファイルの読み書き抽象 (Read/Write/Exists)。単体テストはインメモリ実装で file IO 非依存にできる
/// (01 の IScriptSource と同じ判断)。<see cref="Luxel.Resources"/> の read-only な IVirtualFileSystem とは別 —
/// こちらは設定の永続化 (書き込みあり) 専用の薄い口。
/// </summary>
public interface IFileStore
{
    /// <summary>ファイルが存在するか。</summary>
    bool Exists(string name);
    /// <summary>内容を読む。無ければ null。</summary>
    string? Read(string name);
    /// <summary>内容を書く (上書き)。</summary>
    void Write(string name, string content);
}

/// <summary>インメモリの <see cref="IFileStore"/> (テスト用、決定的)。スレッド安全。</summary>
public sealed class InMemoryFileStore : IFileStore
{
    private readonly Dictionary<string, string> _files = new();
    private readonly object _lock = new();

    public bool Exists(string name) { lock (_lock) return _files.ContainsKey(name); }
    public string? Read(string name) { lock (_lock) return _files.TryGetValue(name, out var v) ? v : null; }
    public void Write(string name, string content) { lock (_lock) _files[name] = content; }
}

/// <summary>実ディスクの <see cref="IFileStore"/>。<paramref name="root"/> 直下にファイルを置く
/// (既定は <c>%APPDATA%/&lt;ゲーム名&gt;</c> 等をアプリが渡す)。書き込み時にディレクトリを作る。</summary>
public sealed class PhysicalFileStore : IFileStore
{
    private readonly string _root;

    public PhysicalFileStore(string root) => _root = Path.GetFullPath(root);

    private string Path_(string name) => Path.Combine(_root, name);

    public bool Exists(string name) => File.Exists(Path_(name));
    public string? Read(string name) => File.Exists(Path_(name)) ? File.ReadAllText(Path_(name)) : null;

    public void Write(string name, string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path_(name), content);
    }
}
