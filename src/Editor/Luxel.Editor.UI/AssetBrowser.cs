using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// アセット/ファイルツリー (ADR-0014): <see cref="IFileStorage"/> の一覧を <see cref="TreeView"/> で
/// 表示し、ファイルのクリックを <see cref="OnOpen"/> (path) で返す — シェルはそれを
/// IDocumentStore.Open へ配線する。フォルダはグループ見出し (クリックで開閉)、
/// ファイル追加/削除の反映は <see cref="Refresh"/>。絞り込みは <see cref="Filter"/> (部分一致)。
/// </summary>
[UiComponent]
public sealed partial class AssetBrowser : CompositeControl
{
    /// <summary>一覧の源 (List() を使う)。</summary>
    [UiParam] private readonly Bindable<IFileStorage> _storage = new();
    /// <summary>絞り込みクエリ (TreeView へ透過)。</summary>
    [UiParam] private readonly BindableString _filter = new();
    /// <summary>展開状態のセット (呼び出し側所有で共有可)。</summary>
    [UiParam] private readonly Bindable<ISet<string>> _expanded = new();

    /// <summary>ファイルクリック (第一引数 = 発火元, path)。</summary>
    [UiEvent] public UiEvent<AssetBrowser, string> OnOpen;

    private readonly Signal<int> _version = new(0);
    private readonly Signal<string> _selected = new("");

    /// <summary>選択中の path (ハイライト)。</summary>
    public string Selected => _selected.Peek();

    /// <summary>ファイルの増減を反映し直す (Peek ベース — Effect 内から呼ばれても自己購読しない)。</summary>
    public void Refresh() => _version.Value = _version.Peek() + 1;

    /// <summary>フラットな path 列 ('/' 区切り) をフォルダ優先 + 名前順の木にする (テスト用に公開)。
    /// Key = フル path (フォルダも)、ファイルは Tag = path。</summary>
    public static List<TreeNode> BuildTree(IEnumerable<string> paths)
    {
        var root = new Dir();
        foreach (string p in paths)
        {
            string[] parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Dir cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
                cur = cur.Dirs.TryGetValue(parts[i], out Dir? d) ? d : cur.Dirs[parts[i]] = new Dir();
            if (parts.Length > 0) cur.Files.Add(parts[^1]);
        }
        return ToNodes(root, "");
    }

    private sealed class Dir
    {
        public readonly SortedDictionary<string, Dir> Dirs = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Files = new(StringComparer.Ordinal);
    }

    private static List<TreeNode> ToNodes(Dir dir, string prefix)
    {
        var nodes = new List<TreeNode>();
        foreach ((string name, Dir sub) in dir.Dirs)
        {
            string path = prefix.Length == 0 ? name : $"{prefix}/{name}";
            nodes.Add(new TreeNode(path, name, ToNodes(sub, path)));   // Tag=null = 見出し (開閉)
        }
        foreach (string f in dir.Files)
        {
            string path = prefix.Length == 0 ? f : $"{prefix}/{f}";
            nodes.Add(new TreeNode(path, f, Tag: path));
        }
        return nodes;
    }

    protected override Widget Build()
    {
        _ = _version.Value;
        string sel = _selected.Value;
        IFileStorage? storage = Storage.Get();
        List<TreeNode> roots = storage is null ? [] : BuildTree(storage.List());
        return TreeView(roots,
            expanded: Expanded.Get()!,
            selected: sel,
            filter: Filter.Or(""),
            onSelect: (_, n) =>
            {
                if (n.Tag is not string path) return;
                _selected.Value = path;
                OnOpen.Invoke(this, path);
            });
    }
}
