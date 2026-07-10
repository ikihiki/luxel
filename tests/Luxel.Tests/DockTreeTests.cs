using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: DockTree (ADR-0010 S(C2)) — 再帰木の分割/移動/畳み込み/直列化。純ロジック。</summary>
public class DockTreeTests
{
    [Fact]
    public void Single_OneGroupWithTabs()
    {
        DockTree t = DockTree.Single("a", "b");
        var g = Assert.IsType<DockGroup>(t.Root);
        Assert.Equal(new[] { "a", "b" }, g.Tabs);
        Assert.Equal(0, g.Active);
        Assert.Equal(-1, ((DockGroup)DockTree.Single().Root).Active);
    }

    [Fact]
    public void AddTab_AppendsAndActivates_ExistingTabMoves()
    {
        DockTree t = DockTree.Single("a");
        int gid = t.Root.Id;
        t = t.AddTab(gid, "b");
        var g = (DockGroup)t.Root;
        Assert.Equal(new[] { "a", "b" }, g.Tabs);
        Assert.Equal(1, g.Active);

        // 既存タブの AddTab = 移動 (重複しない)
        t = t.AddTab(gid, "a");
        g = (DockGroup)t.Root;
        Assert.Equal(new[] { "b", "a" }, g.Tabs);
        Assert.Equal(1, g.Active);
    }

    [Fact]
    public void ActivateTab_SetsIndex_UnknownIsNoop()
    {
        DockTree t = DockTree.Single("a", "b", "c").ActivateTab("c");
        Assert.Equal(2, ((DockGroup)t.Root).Active);
        Assert.Same(t, t.ActivateTab("zz"));
    }

    [Fact]
    public void RemoveTab_AdjustsActive()
    {
        // アクティブより前を消す → active が繰り上がる
        DockTree t = DockTree.Single("a", "b", "c").ActivateTab("c").RemoveTab("a");
        var g = (DockGroup)t.Root;
        Assert.Equal(new[] { "b", "c" }, g.Tabs);
        Assert.Equal(1, g.Active);

        // アクティブ自身 (末尾) を消す → 新しい末尾へ
        t = t.RemoveTab("c");
        g = (DockGroup)t.Root;
        Assert.Equal(0, g.Active);
    }

    [Fact]
    public void RemoveLastTab_RootSurvivesAsEmptyGroup()
    {
        DockTree t = DockTree.Single("a").RemoveTab("a");
        var g = Assert.IsType<DockGroup>(t.Root);
        Assert.Empty(g.Tabs);
        Assert.Equal(-1, g.Active);
    }

    [Fact]
    public void Dock_Right_WrapsRootInHorizontalSplit()
    {
        DockTree t = DockTree.Single("a", "b");
        int gid = t.Root.Id;
        t = t.Dock("b", gid, DockSide.Right);

        var s = Assert.IsType<DockSplit>(t.Root);
        Assert.True(s.Horizontal);
        Assert.Equal(2, s.Children.Count);
        Assert.Equal(new[] { 0.5f, 0.5f }, s.Sizes);
        Assert.Equal(new[] { "a" }, ((DockGroup)s.Children[0]).Tabs);   // 元グループから外れた
        Assert.Equal(new[] { "b" }, ((DockGroup)s.Children[1]).Tabs);
        Assert.Equal(gid, s.Children[0].Id);                            // 対象グループの id は不変
    }

    [Fact]
    public void Dock_Left_InsertsBefore()
    {
        DockTree t = DockTree.Single("a", "b");
        t = t.Dock("b", t.Root.Id, DockSide.Left);
        var s = (DockSplit)t.Root;
        Assert.Equal(new[] { "b" }, ((DockGroup)s.Children[0]).Tabs);
        Assert.Equal(new[] { "a" }, ((DockGroup)s.Children[1]).Tabs);
    }

    [Fact]
    public void Dock_SameAxisAgain_InsertsAdjacent_NotNested()
    {
        // a|b → b の右に c: 同方向なので 3 分割に挿入 (入れ子にしない)。b の取り分 0.5 を半分こ
        DockTree t = DockTree.Single("a", "b", "c");
        int gid = t.Root.Id;
        t = t.Dock("b", gid, DockSide.Right);
        int rightGid = ((DockSplit)t.Root).Children[1].Id;
        t = t.Dock("c", rightGid, DockSide.Right);

        var s = (DockSplit)t.Root;
        Assert.Equal(3, s.Children.Count);
        Assert.Equal(new[] { "a" }, ((DockGroup)s.Children[0]).Tabs);
        Assert.Equal(new[] { "b" }, ((DockGroup)s.Children[1]).Tabs);
        Assert.Equal(new[] { "c" }, ((DockGroup)s.Children[2]).Tabs);
        Assert.Equal(0.5f, s.Sizes[0], 3);
        Assert.Equal(0.25f, s.Sizes[1], 3);
        Assert.Equal(0.25f, s.Sizes[2], 3);
    }

    [Fact]
    public void Dock_CrossAxis_Nests()
    {
        // a|b → b の下に c: 右側が縦分割で入れ子になる
        DockTree t = DockTree.Single("a", "b", "c");
        t = t.Dock("b", t.Root.Id, DockSide.Right);
        int rightGid = ((DockSplit)t.Root).Children[1].Id;
        t = t.Dock("c", rightGid, DockSide.Bottom);

        var root = (DockSplit)t.Root;
        Assert.Equal(2, root.Children.Count);
        var nested = Assert.IsType<DockSplit>(root.Children[1]);
        Assert.False(nested.Horizontal);
        Assert.Equal(new[] { "b" }, ((DockGroup)nested.Children[0]).Tabs);
        Assert.Equal(new[] { "c" }, ((DockGroup)nested.Children[1]).Tabs);
    }

    [Fact]
    public void Dock_SoleTabOntoOwnGroup_IsNoop()
    {
        DockTree t = DockTree.Single("a");
        Assert.Same(t, t.Dock("a", t.Root.Id, DockSide.Right));
    }

    [Fact]
    public void RemoveTab_CollapsesEmptyGroupAndSingleChildSplit()
    {
        DockTree t = DockTree.Single("a", "b");
        int gid = t.Root.Id;
        t = t.Dock("b", gid, DockSide.Right);
        t = t.RemoveTab("b");   // 右グループが空に → split ごと畳まれ左グループがルートへ

        var g = Assert.IsType<DockGroup>(t.Root);
        Assert.Equal(new[] { "a" }, g.Tabs);
        Assert.Equal(gid, g.Id);
    }

    [Fact]
    public void Collapse_FlattensSameOrientationNesting()
    {
        // H[a | V[H 相当を作るため: b の下に c、その後 c 側へ d を右 dock] ...] を作り、
        // 縦分割の片方を消して H[a|b] 入れ子が現れたとき、親 H へ畳まれることを確認
        DockTree t = DockTree.Single("a", "b", "c", "d");
        int gid = t.Root.Id;
        t = t.Dock("b", gid, DockSide.Right);                      // H[a | b]
        int bGid = t.GroupOf("b")!.Id;
        t = t.Dock("c", bGid, DockSide.Bottom);                    // H[a | V[b | c]]
        int cGid = t.GroupOf("c")!.Id;
        t = t.Dock("d", cGid, DockSide.Right);                     // H[a | V[b | H[c | d]]]
        t = t.RemoveTab("b");                                      // V が畳まれ H[c|d] が親 H の子に → 平坦化

        var s = Assert.IsType<DockSplit>(t.Root);
        Assert.True(s.Horizontal);
        Assert.Equal(3, s.Children.Count);                         // a, c, d が同列に
        Assert.Equal(new[] { "a" }, ((DockGroup)s.Children[0]).Tabs);
        Assert.Equal(new[] { "c" }, ((DockGroup)s.Children[1]).Tabs);
        Assert.Equal(new[] { "d" }, ((DockGroup)s.Children[2]).Tabs);
        Assert.Equal(1f, s.Sizes.Sum(), 3);
    }

    [Fact]
    public void MoveTab_BetweenGroups_EmptySourceCollapses()
    {
        DockTree t = DockTree.Single("a", "b");
        t = t.Dock("b", t.Root.Id, DockSide.Right);
        int leftGid = t.GroupOf("a")!.Id;
        t = t.MoveTab("b", leftGid);   // 右の唯一タブを左へ → 右グループ + split が畳まれる

        var g = Assert.IsType<DockGroup>(t.Root);
        Assert.Equal(new[] { "a", "b" }, g.Tabs);
        Assert.Equal(1, g.Active);     // 移動したタブがアクティブ
    }

    [Fact]
    public void MoveTab_ReorderWithinGroup()
    {
        DockTree t = DockTree.Single("a", "b", "c");
        t = t.MoveTab("c", t.Root.Id, 0);
        var g = (DockGroup)t.Root;
        Assert.Equal(new[] { "c", "a", "b" }, g.Tabs);
        Assert.Equal(0, g.Active);
    }

    [Fact]
    public void WithSizes_Normalizes_MismatchThrows()
    {
        DockTree t = DockTree.Single("a", "b");
        t = t.Dock("b", t.Root.Id, DockSide.Right);
        int sid = t.Root.Id;

        t = t.WithSizes(sid, new[] { 3f, 1f });
        var s = (DockSplit)t.Root;
        Assert.Equal(0.75f, s.Sizes[0], 3);
        Assert.Equal(0.25f, s.Sizes[1], 3);

        Assert.Throws<ArgumentException>(() => t.WithSizes(sid, new[] { 1f }));
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        DockTree t = DockTree.Single("a", "b", "c");
        t = t.Dock("b", t.Root.Id, DockSide.Right);
        t = t.Dock("c", t.GroupOf("b")!.Id, DockSide.Bottom);
        t = t.WithSizes(t.Root.Id, new[] { 2f, 1f });
        t = t.ActivateTab("a");

        DockTree back = DockTree.Deserialize(t.Serialize());

        Assert.Equal(t.NextId, back.NextId);
        Assert.Equal(t.Serialize(), back.Serialize());   // 構造 + id + sizes + active が保存される
        var s = Assert.IsType<DockSplit>(back.Root);
        Assert.Equal(2f / 3, s.Sizes[0], 3);
        Assert.Equal(t.GroupOf("c")!.Id, back.GroupOf("c")!.Id);

        // 復元後の id 割当が衝突しない (NextId 継続)
        DockTree grown = back.Dock("a", back.GroupOf("c")!.Id, DockSide.Right);
        var ids = new List<int>();
        void Collect(DockNode n) { ids.Add(n.Id); if (n is DockSplit sp) foreach (DockNode c in sp.Children) Collect(c); }
        Collect(grown.Root);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
