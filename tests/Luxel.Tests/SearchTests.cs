using Luxel.Controls;
using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>SR: docs 検索 — マッチ列挙 (RichTextEditor.FindMatches) とツリー絞り込み (FilterTree)。</summary>
public class SearchTests
{
    private static List<(int Line, int Start, int Len)> Find(string md, string query)
    {
        var into = new List<(int, int, int)>();
        RichTextEditor.FindMatches(Markdown.Parse(md), query, into);
        return into;
    }

    [Fact]
    public void FindMatches_CaseInsensitive_AcrossLines()
    {
        var m = Find("# Alpha\n\nalpha beta ALPHA\n\nbeta", "alpha");
        // 見出し 1 + 段落内 2 (空行の空段落はマッチなし)
        Assert.Equal(3, m.Count);
        Assert.Equal(0, m[0].Line);
        Assert.Equal(2, m[1].Line);      // 空段落 (行 1) を挟む
        Assert.Equal(0, m[1].Start);
        Assert.Equal(11, m[2].Start);     // "alpha beta " の後
    }

    [Fact]
    public void FindMatches_MultipleInOneBlock_NoOverlap()
    {
        var m = Find("aaaa", "aa");
        Assert.Equal(2, m.Count);         // 0..2 と 2..4 — 重なりは数えない
    }

    [Fact]
    public void FindMatches_EmptyQuery_NoMatch()
    {
        Assert.Empty(Find("abc", ""));
        Assert.Empty(Find("abc", "  "));
    }

    [Fact]
    public void FindMatches_CodeBlockText_IsSearchable()
    {
        var m = Find("```cs\nvar counter = 1;\n```", "counter");
        Assert.Single(m);
    }

    [Fact]
    public void FindMatches_MultiLineCode_ReportsPerLine()
    {
        var m = Find("```cs\nvar a = 1;\nvar b = a;\n```", "var");
        Assert.Equal(2, m.Count);
        Assert.Equal(0, m[0].Line);
        Assert.Equal(1, m[1].Line);   // コード 2 行目 = 行 index 1
    }

    // ---- FilterTree ----

    private static TreeNode[] Tree() =>
    [
        new("g:Docs", "Docs", [
            new("Docs/Button", "Button", [new("Docs/Button#2", "Variant (形)")],
                Tag: "story", SearchText: "ボタンの使い方 variant intent"),
            new("Docs/GettingStarted", "GettingStarted", Tag: "story", SearchText: "MDX 風 docs ページ"),
        ]),
        new("g:Icon", "Icon", [new("Icon/Kinds", "Kinds", Tag: "story")]),
    ];

    [Fact]
    public void FilterTree_MatchBySearchText_KeepsAncestors()
    {
        var f = TreeView.FilterTree(Tree(), "variant");
        // "variant" は Docs/Button の本文と見出しにヒット — Icon グループは消える
        Assert.Single(f);
        Assert.Equal("g:Docs", f[0].Key);
        Assert.Single(f[0].Children!);
        Assert.Equal("Docs/Button", f[0].Children![0].Key);
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
        Assert.Equal("Docs/Button#2", f[0].Children![0].Children![0].Key);
    }

    [Fact]
    public void FilterTree_NoMatch_Empty()
    {
        Assert.Empty(TreeView.FilterTree(Tree(), "zzzz"));
    }
}
