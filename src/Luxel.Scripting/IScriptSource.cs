namespace Luxel.Scripting;

/// <summary>
/// スクリプト本文の供給元 + 変更通知。hot reload の file IO をテストから切り離すための抽象
/// (実ファイル = <see cref="FileScriptSource"/>、テスト/デモ = <see cref="MemoryScriptSource"/>)。
/// <see cref="Changed"/> は複数回発火してよい — 消費側 (ScriptSystem/GameLoop) がフレーム先頭で
/// フラグを畳んで 1 回だけ Reload する (= デバウンス)。
/// </summary>
public interface IScriptSource
{
    /// <summary>現在の本文を取得する。</summary>
    string Read();

    /// <summary>本文が変わった通知。</summary>
    event Action? Changed;
}

/// <summary>インメモリのスクリプト源 (テスト/ライブ編集デモ用)。<see cref="Set"/> で本文差し替え + Changed 発火。</summary>
public sealed class MemoryScriptSource : IScriptSource
{
    private string _text;

    public MemoryScriptSource(string text = "") => _text = text;

    public string Read() => _text;

    public event Action? Changed;

    /// <summary>本文を差し替えて <see cref="Changed"/> を発火する。</summary>
    public void Set(string text)
    {
        _text = text;
        Changed?.Invoke();
    }
}

/// <summary>
/// 実ファイルのスクリプト源。<see cref="FileSystemWatcher"/> で変更を監視し <see cref="Changed"/> を発火する。
/// 保存直後はエディタがロック/連続イベントを出すので、読み込みは共有 + リトライ付き。連続イベントの畳み込み
/// (デバウンス) は消費側のフレーム先頭 Reload に委ねる。
/// </summary>
public sealed class FileScriptSource : IScriptSource, IDisposable
{
    private readonly string _path;
    private readonly FileSystemWatcher _watcher;

    public FileScriptSource(string path)
    {
        _path = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(_path) ?? ".";
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public string Read()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(20);   // 保存中のロック — 短く待って再試行
            }
        }
    }

    public event Action? Changed;

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed?.Invoke();

    public void Dispose()
    {
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Renamed -= OnChanged;
        _watcher.Dispose();
    }
}
