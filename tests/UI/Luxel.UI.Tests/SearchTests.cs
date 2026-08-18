using Luxel.Controls;
using Xunit;

namespace Luxel.Tests;

/// <summary>SR: サイドバーのツリー絞り込み (TreeView.FilterTree)。
/// (docs 全文検索は新スタック TextEditorView.SetSearch = TextSearch.FindAll に移行済み。)</summary>
public class SearchTests
{
    private static TreeNode[] Tree() =>
    [
        new("g:Docs", "Docs", [
            new("Controls/Button/Docs", "Button", [new("Controls/Button/Docs#2", "Variant (形)")],
                Tag: "story", SearchText: "ボタンの使い方 variant intent"),
            new("Start/Welcome", "GettingStarted", Tag: "story", SearchText: "MDX 風 docs ページ"),
        ]),
        new("g:Icon", "Icon", [new("Icon/Kinds", "Kinds", Tag: "story")]),
    ];

    [Fact]
    public void FilterTree_MatchBySearchText_KeepsAncestors()
    {
        var f = TreeView.FilterTree(Tree(), "variant");
        // "variant" は Controls/Button/Docs の本文と見出しにヒット — Icon グループは消える
        Assert.Single(f);
        Assert.Equal("g:Docs", f[0].Key);
        Assert.Single(f[0].Children!);
        Assert.Equal("Controls/Button/Docs", f[0].Children![0].Key);
    }

    [Fact]
    public void FilterTree_MatchByLabel_Component()
    {
        var f = TreeView.FilterTree(Tree(), "icon");
        Assert.Single(f);
        Assert.Equal("g:Icon", f[0].Key);
        // 自身がマッチした親は、マッチしない子を残さない (ページ側でマッチした場合のみ子孫が出る)
        Assert.Null(f[0].Children);
    }

    [Fact]
    public void FilterTree_HeadingLabelMatch_KeepsChain()
    {
        var f = TreeView.FilterTree(Tree(), "形");
        Assert.Equal("Controls/Button/Docs#2", f[0].Children![0].Children![0].Key);
    }

    [Fact]
    public void FilterTree_NoMatch_Empty()
    {
        Assert.Empty(TreeView.FilterTree(Tree(), "zzzz"));
    }
}
