namespace Luxel.UI;

/// <summary>
/// 複数の <see cref="UiHost"/> を保持し DevTools に一括で見せるための登録簿。
/// ゲームは HUD / コクピット / ミニマップ 等の UI を独立した <see cref="UiHost"/> で作れる。
/// 各 host は「表示名」で識別され、Diagnostics 経由で tree 群がまとめて emit される。
/// </summary>
public sealed class UiRegistry
{
    private readonly List<(string Name, UiHost Host)> _entries = new();

    public IReadOnlyList<(string Name, UiHost Host)> Entries => _entries;

    public void Register(string name, UiHost host)
    {
        if (host is null) return;
        host.DebugName ??= name;   // ui.set の "ui" 判別に使う
        _entries.Add((name, host));
    }

    public void Unregister(UiHost host) => _entries.RemoveAll(t => ReferenceEquals(t.Host, host));

    /// <summary>名前で host を引く (リモート入力の "ui" ルーティング用)。重複名は先勝ち。</summary>
    public UiHost? GetByName(string name)
    {
        foreach ((string Name, UiHost Host) e in _entries)
            if (e.Name == name) return e.Host;
        return null;
    }

    /// <summary>登録された全 host の DebugNode を配列で取得する (Scene が emit するのに使う)。</summary>
    public (string Name, DebugNode? Root)[] Snapshot()
    {
        var arr = new (string, DebugNode?)[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
            arr[i] = (_entries[i].Name, _entries[i].Host.DebugSnapshot());
        return arr;
    }

    /// <summary>ui.set リクエストの path prefix "N:0.1.2" で N 番目の host にディスパッチする補助。
    /// 現状 <see cref="UiHost"/> が自身で AllListeners subscribe するので使用不要だが、複数 host が
    /// path 名前空間を共有する場合の runtime lookup 用途に残す。</summary>
    public UiHost? GetByIndex(int index)
        => index >= 0 && index < _entries.Count ? _entries[index].Host : null;
}
