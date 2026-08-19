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
    public void SerializeIncludesSchemaVersionAndLegacyLayoutsRemainReadable()
    {
        string current = DockTree.Single("a", "b").Serialize();
        Assert.Contains($"\"schemaVersion\":{DockTree.LayoutSchemaVersion}", current);

        const string legacy = "{\"nextId\":2,\"root\":{\"id\":1,\"tabs\":[\"a\",\"b\"],\"active\":1}}";
        DockGroup restored = Assert.IsType<DockGroup>(DockTree.Deserialize(legacy).Root);
        Assert.Equal(new[] { "a", "b" }, restored.Tabs);
        Assert.Equal(1, restored.Active);
    }

    [Fact]
    public void DeserializeRejectsFutureSchemaVersions()
    {
        string json = DockTree.Single("a").Serialize()
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":999", StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => DockTree.Deserialize(json));
    }

    // ---- 窓内フローティング ----

    [Fact]
    public void Float_RemovesFromDock_CreatesFloatGroup()
    {
        DockTree t = DockTree.Single("a", "b").Float("b", 100, 50, 200, 150);

        Assert.Equal(new[] { "a" }, ((DockGroup)t.Root).Tabs);
        DockFloat fl = Assert.Single(t.Floats);
        Assert.Equal(new[] { "b" }, fl.Group.Tabs);
        Assert.Equal((100, 50, 200, 150), (fl.X, fl.Y, fl.W, fl.H));
        Assert.Same(fl.Group, t.GroupOf("b"));            // 参照はフロートも含む
        Assert.Same(fl.Group, t.Group(fl.Group.Id));
        Assert.Same(fl, t.FloatOf(fl.Group.Id));
    }

    [Fact]
    public void MoveTab_DockToFloat_AndBack_EmptyFloatVanishes()
    {
        DockTree t = DockTree.Single("a", "b", "c").Float("c", 10, 10, 200, 150);
        int floatGid = t.Floats[0].Group.Id;
        int dockGid = t.GroupOf("a")!.Id;

        t = t.MoveTab("b", floatGid);                     // ドック → フロート
        Assert.Equal(new[] { "c", "b" }, t.Floats[0].Group.Tabs);

        t = t.MoveTab("c", dockGid).MoveTab("b", dockGid);   // 全部戻すとフロートが消える
        Assert.Empty(t.Floats);
        Assert.Equal(3, t.GroupOf("a")!.Tabs.Count);
    }

    [Fact]
    public void RemoveTab_LastFloatTab_RemovesFloat()
    {
        DockTree t = DockTree.Single("a", "b").Float("b", 0, 0, 200, 150).RemoveTab("b");
        Assert.Empty(t.Floats);
    }

    [Fact]
    public void MoveFloat_Moves_ResizeClampsMin()
    {
        DockTree t = DockTree.Single("a", "b").Float("b", 10, 10, 200, 150);
        int gid = t.Floats[0].Group.Id;

        t = t.MoveFloat(gid, 60, 70);
        Assert.Equal((60f, 70f), (t.Floats[0].X, t.Floats[0].Y));

        t = t.ResizeFloat(gid, 10, 10);                   // 最小 120×80 でクランプ
        Assert.Equal((120f, 80f), (t.Floats[0].W, t.Floats[0].H));
    }

    [Fact]
    public void Dock_TargetFloatGroup_AppendsInsteadOfSplit()
    {
        DockTree t = DockTree.Single("a", "b", "c").Float("c", 0, 0, 200, 150);
        int floatGid = t.Floats[0].Group.Id;

        t = t.Dock("b", floatGid, DockSide.Right);        // フロートは分割しない → 末尾追加

        Assert.Single(t.Floats);
        Assert.Equal(new[] { "c", "b" }, t.Floats[0].Group.Tabs);
        Assert.IsType<DockGroup>(t.Root);                 // ドック側に分割はできていない
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips_WithFloats()
    {
        DockTree t = DockTree.Single("a", "b").Float("b", 30, 40, 220, 160).ActivateTab("a");
        DockTree back = DockTree.Deserialize(t.Serialize());
        Assert.Equal(t.Serialize(), back.Serialize());
        DockFloat fl = Assert.Single(back.Floats);
        Assert.Equal((30f, 40f, 220f, 160f), (fl.X, fl.Y, fl.W, fl.H));
        Assert.Equal(new[] { "b" }, fl.Group.Tabs);
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
