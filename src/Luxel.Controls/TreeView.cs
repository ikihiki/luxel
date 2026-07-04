using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>ツリーのノード 1 つ (イミュータブルなデータ)。<see cref="Key"/> は展開/選択状態の
/// 永続キー (ツリー再構築をまたいで一意なパス文字列を推奨)。<see cref="SearchText"/> は
/// 絞り込み (Filter) 時に Label に加えて照合される本文 (docs 全文検索用、表示はされない)。</summary>
public sealed record TreeNode(string Key, string Label, IReadOnlyList<TreeNode>? Children = null,
                              object? Tag = null, string? SearchText = null)
{
    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>
/// ツリービュー (展開/折りたたみ + 選択 + 絞り込み)。行 = インデント + シェブロン (子持ちのみ) + ラベル。
/// <list type="bullet">
/// <item>葉のクリック = <see cref="OnSelect"/> (sender-first)</item>
/// <item>子持ちノード: <c>Tag == null</c> (見出し行) はラベルクリック = 開閉。
///   <c>Tag != null</c> (選択できるページ等) はラベルクリック = <see cref="OnSelect"/> + 展開、
///   開閉はシェブロンのクリック</item>
/// <item>展開状態は ctor の <c>expanded</c> セットに保持 — 呼び出し側が所有すれば
///   ツリーの作り直し (chrome 再構築等) をまたいで生き残る</item>
/// <item>選択ハイライトは <see cref="Selected"/> (Key 一致でアクセント色)</item>
/// <item><see cref="Filter"/> が非空なら、Label/SearchText がマッチするノードとその祖先だけを
///   全展開で表示する (開閉状態は変更しない — クリアで元の表示に戻る)</item>
/// </list>
/// </summary>
[UiComponent]
public sealed partial class TreeView : CompositeControl
{
    private readonly IReadOnlyList<TreeNode> _roots;
    private readonly ISet<string> _expanded;
    private readonly Signal<int> _version = new(0);   // 開閉トグルで進める (TrackBuild が再構築)

    /// <summary>葉ノード選択 (第一引数 = 発火元)。Tag 付き子持ちノードのラベルクリックでも発火する。</summary>
    [UiEvent] public UiEvent<TreeView, TreeNode> OnSelect;

    /// <summary>選択中ノードの Key (ハイライト表示)。</summary>
    [UiParam] public readonly Bindable<string> Selected = new();

    /// <summary>絞り込みクエリ (大小無視の部分一致)。signal を渡せばタイプ毎に TreeView だけ再構築される。</summary>
    [UiParam] public readonly BindableString Filter = new();

    [UiCtor]
    internal TreeView(IReadOnlyList<TreeNode> roots, ISet<string>? expanded = null)
    {
        _roots = roots;
        _expanded = expanded ?? new HashSet<string>();
    }

    /// <summary>ノードを展開する (プログラム操作 — 選択パスを見せる等)。</summary>
    public void Expand(string key)
    {
        if (_expanded.Add(key)) _version.Value++;
    }

    protected override Widget Build()
    {
        _ = _version.Value;   // 開閉を追跡
        string sel = Selected.Or("");
        string filter = Filter.Or("").Trim();
        var flat = new List<(TreeNode Node, int Depth)>();
        if (filter.Length > 0)
        {
            // 絞り込み: マッチするノード + 祖先だけを全展開で (開閉セットは触らない)
            FlattenFiltered(FilterTree(_roots, filter), 0, flat);
        }
        else
        {
            Flatten(_roots, _expanded, 0, flat);
        }
        var rows = new List<Widget>();
        foreach ((TreeNode n, int depth) in flat)
            rows.Add(Row(n, depth, n.Key == sel, filtered: filter.Length > 0));
        if (rows.Count == 0)
            rows.Add(LinkText(null, "(該当なし)", margin: new Thickness(4, 2, 0, 0)));
        return VStack(3)[rows.ToArray()];
    }

    /// <summary>展開状態に従って可視行を列挙する (テスト用に分離)。</summary>
    internal static void Flatten(IReadOnlyList<TreeNode> roots, ISet<string> expanded, int depth,
                                 List<(TreeNode Node, int Depth)> into)
    {
        foreach (TreeNode n in roots)
        {
            into.Add((n, depth));
            if (n.HasChildren && expanded.Contains(n.Key))
                Flatten(n.Children!, expanded, depth + 1, into);
        }
    }

    /// <summary>クエリにマッチするノード (自身 or 子孫がマッチ) だけを残した木を返す (テスト用に分離)。
    /// マッチ = Label または SearchText の大小無視部分一致。</summary>
    internal static List<TreeNode> FilterTree(IReadOnlyList<TreeNode> roots, string query)
    {
        var result = new List<TreeNode>();
        foreach (TreeNode n in roots)
        {
            List<TreeNode>? kids = n.HasChildren ? FilterTree(n.Children!, query) : null;
            bool self = n.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (n.SearchText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            if (self || kids is { Count: > 0 })
                result.Add(n with { Children = kids is { Count: > 0 } ? kids : null });
        }
        return result;
    }

    private static void FlattenFiltered(IReadOnlyList<TreeNode> roots, int depth,
                                        List<(TreeNode Node, int Depth)> into)
    {
        foreach (TreeNode n in roots)
        {
            into.Add((n, depth));
            if (n.HasChildren) FlattenFiltered(n.Children!, depth + 1, into);
        }
    }

    private Widget Row(TreeNode n, int depth, bool selected, bool filtered)
    {
        // 他フレームワークのツリー同様: 左寄せの LinkText 行、1 段 = 12px、
        // 葉はシェブロン列 (12px) 分を足して親ラベルと左端を揃える
        float indent = 4 + depth * 12;
        if (!n.HasChildren)
            return LinkText(_ => OnSelect.Invoke(this, n), n.Label, active: selected,
                hAlign: Align.Stretch, margin: new Thickness(indent + 12, 0, 0, 0));

        bool open = filtered || _expanded.Contains(n.Key);
        // Tag 付きの子持ち (選択できるページ) はラベル = 選択、シェブロン = 開閉。
        // Tag なし (グループ見出し) は従来どおりラベルでも開閉
        Action<LinkText> onLabel = n.Tag is null
            ? _ => Toggle(n)
            : _ => { Expand(n.Key); OnSelect.Invoke(this, n); };
        return HStack(2)[
            Icon(open ? IconKind.ChevronDown : IconKind.ChevronRight, 10, stroke: 1.5f,
                 onClick: _ => Toggle(n), margin: new Thickness(indent, 3, 0, 0)),
            LinkText(onLabel, n.Label, active: selected)];
    }

    private void Toggle(TreeNode n)
    {
        if (!_expanded.Remove(n.Key)) _expanded.Add(n.Key);
        _version.Value++;
    }
}
