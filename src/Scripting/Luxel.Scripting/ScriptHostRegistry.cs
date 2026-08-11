using System.Reflection;

namespace Luxel.Scripting;

/// <summary>
/// スクリプトの「方言」= 1 役割分の参照 (使える型のアセンブリ)・using・globals 型。
/// UI を組むスクリプト / ECS ロジックのスクリプト等、役割ごとに別プロファイルを持つ。
/// </summary>
public sealed record ScriptProfile(
    string Name,
    IReadOnlyList<Assembly> References,
    IReadOnlyList<string> Usings,
    Type GlobalsType);

/// <summary>
/// 役割 (<see cref="ScriptProfile"/>) ごとに <see cref="ScriptHost"/> / <see cref="ScriptWorkspace"/> を
/// **遅延生成してキャッシュ**する登録簿。ゲームは ECS ロジック用・UI 用など複数プロファイルを登録し、
/// DI で 1 個のこの登録簿を共有する (各ストーリー/システムが static な Laz&lt;ScriptHost&gt; を個別に抱えない)。
/// Roslyn の重い初期コンパイルは各プロファイル初回参照まで遅延される。スレッドセーフ。
/// </summary>
public sealed class ScriptHostRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required ScriptProfile Profile;
        public required Lazy<ScriptHost> Host;
        public required Lazy<ScriptWorkspace> Workspace;
    }

    /// <summary>プロファイルを登録する (同名が既にあれば無視 — 暖まった host/workspace を保つ)。</summary>
    public void Register(ScriptProfile profile) => GetEntry(profile);

    /// <summary>プロファイルの <see cref="ScriptHost"/> を取得する (未登録なら登録して返す)。初回参照で構築。</summary>
    public ScriptHost GetOrAdd(ScriptProfile profile) => GetEntry(profile).Host.Value;

    /// <summary>登録済み役割名の <see cref="ScriptHost"/> (未登録は例外)。</summary>
    public ScriptHost Host(string name) => Lookup(name).Host.Value;

    /// <summary>登録済み役割名の <see cref="ScriptWorkspace"/> (補完/診断の言語サービス用。未登録は例外)。</summary>
    public ScriptWorkspace Workspace(string name) => Lookup(name).Workspace.Value;

    public bool Contains(string name)
    {
        lock (_gate) return _entries.ContainsKey(name);
    }

    public IReadOnlyCollection<string> Names
    {
        get { lock (_gate) return _entries.Keys.ToArray(); }
    }

    private Entry GetEntry(ScriptProfile p)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(p.Name, out Entry? e)) return e;
            e = new Entry
            {
                Profile = p,
                Host = new Lazy<ScriptHost>(() => new ScriptHost(p.References, p.Usings, p.GlobalsType)),
                Workspace = new Lazy<ScriptWorkspace>(() => new ScriptWorkspace(p.References, p.Usings)),
            };
            _entries[p.Name] = e;
            return e;
        }
    }

    private Entry Lookup(string name)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out Entry? e)) return e;
            throw new KeyNotFoundException(
                $"script profile '{name}' が未登録です。Register/GetOrAdd で先に登録してください。");
        }
    }
}
