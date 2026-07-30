using Luxel.UI;
using Xunit;
using Luxel.Gallery;

namespace Luxel.Tests;

/// <summary>DW2: [Story(Order)] による表示順 — コンポーネントは最小 Order、内部は Order → Path。</summary>
public class StoryOrderTests
{
    private static StoryInfo S(string path, int order) => new(path, 1, 1, null, _ => null!, order);

    [Fact]
    public void All_SortsByComponentMinOrder_ThenStoryOrder()
    {
        // 一意なコンポーネント名で登録 (グローバル registry — 他テストと衝突しない名前)
        StoryRegistry.Register(S("OrdTestB/Second", 2));
        StoryRegistry.Register(S("OrdTestB/First", 1));
        StoryRegistry.Register(S("OrdTestA/Late", 50));

        IReadOnlyList<StoryInfo> all = StoryRegistry.All;
        int b1 = IndexOf(all, "OrdTestB/First");
        int b2 = IndexOf(all, "OrdTestB/Second");
        int a = IndexOf(all, "OrdTestA/Late");

        Assert.True(b1 < b2);   // コンポーネント内は Order 順
        Assert.True(b2 < a);    // グループは最小 Order 順 (B=1 < A=50) — 名前順なら A が先になるはず
        // 既定 Order (1000) のストーリーより前に来る
        int def = all.ToList().FindIndex(s => s.Order == 1000);
        if (def >= 0) Assert.True(a < def);
    }

    private static int IndexOf(IReadOnlyList<StoryInfo> all, string path)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].Path == path) return i;
        return -1;
    }
}
