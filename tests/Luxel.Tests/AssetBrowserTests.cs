using Luxel.Controls;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: AssetBrowser.BuildTree + IFileStorage.List (ADR-0014 S(C4))。GPU 不要。</summary>
public class AssetBrowserTests
{
    [Fact]
    public void BuildTree_FoldersFirst_Sorted_NestedKeys()
    {
        var roots = AssetBrowser.BuildTree(["readme.md", "src/Main.cs", "src/App.cs", "assets/img/logo.png"]);

        Assert.Equal(["assets", "src", "readme.md"], roots.Select(n => n.Label).ToArray());   // フォルダ優先 + 名前順
        TreeNode assets = roots[0];
        Assert.Null(assets.Tag);                          // フォルダ = 見出し (開閉)
        Assert.Equal("assets", assets.Key);
        TreeNode img = Assert.Single(assets.Children!);
        Assert.Equal("assets/img", img.Key);
        Assert.Equal("assets/img/logo.png", img.Children![0].Key);
        Assert.Equal("assets/img/logo.png", img.Children[0].Tag);   // ファイル = Tag に path

        TreeNode src = roots[1];
        Assert.Equal(["App.cs", "Main.cs"], src.Children!.Select(n => n.Label).ToArray());
    }

    [Fact]
    public void BuildTree_Empty_ReturnsEmpty()
    {
        Assert.Empty(AssetBrowser.BuildTree([]));
    }

    [Fact]
    public void MemoryFileStorage_List_ReturnsAllPaths()
    {
        var fs = new MemoryFileStorage();
        fs.Write("a.txt", "1");
        fs.Write("dir/b.txt", "2");
        Assert.Equal(["a.txt", "dir/b.txt"], fs.List().OrderBy(p => p).ToArray());
    }
}
