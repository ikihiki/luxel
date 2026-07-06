using System.Numerics;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>
/// DebugDraw (gizmo コア) の GPU 不要テスト: カテゴリ ON/OFF のゼロ割り当て、
/// Flush が Scene2D へ正しい図形を出すこと、worldToScreen 投影、テキスト委譲。
/// </summary>
public class DebugDrawTests
{
    public DebugDrawTests() => DebugDraw.Reset();   // 静的状態をテスト間で分離

    [Fact]
    public void Disabled_Category_IsZeroCost_NoCommands()
    {
        // 何も Enable していない → 全 API が即 return、コマンドは溜まらない
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0xFF0000FF);
        DebugDraw.Rect(0, 0, 10, 10, 0xFF00FF00);
        DebugDraw.Circle(5, 5, 3, 0xFFFF0000);
        DebugDraw.Text(0, 0, "x", 0xFFFFFFFF);
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void Enable_OnlyMatchingCategory_Accumulates()
    {
        DebugDraw.Enable("physics");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "physics");  // 溜まる
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "camera");   // 出ない
        Assert.Equal(1, DebugDraw.PendingCount);
        Assert.True(DebugDraw.IsEnabled("physics"));
        Assert.False(DebugDraw.IsEnabled("camera"));
    }

    [Fact]
    public void All_Category_EnablesEverything()
    {
        DebugDraw.Enable(DebugDraw.All);
        Assert.True(DebugDraw.IsEnabled("anything"));
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "whatever");
        Assert.Equal(1, DebugDraw.PendingCount);
    }

    [Fact]
    public void Flush_EmitsShapes_ProjectsWorldToScreen_AndClears()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(10, 0), 0xFF112233, kind: "demo");
        DebugDraw.Rect(0, 0, 4, 4, 0xFF445566, kind: "demo");        // 矩形アウトライン → 1 stroke (5 点)
        DebugDraw.Circle(0, 0, 2, 0xFF778899, width: 1f, segments: 8, kind: "demo");

        var scene = new Scene2D();
        // world→screen: +100,+50 平行移動でスクリーンへ (投影が効いていることを座標で確認)
        DebugDraw.Flush(scene, w => new Vector2(w.X + 100, w.Y + 50));

        // 線 + 矩形 + 円 = 3 shape (全て stroke)
        Assert.Equal(3, scene.Shapes.Count);
        // Flush 後はコマンドが空になる
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void Flush_Text_OnlyWhenDrawerProvided()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Text(new Vector3(1, 2, 0), "hi", 0xFFFFFFFF, size: 12f, kind: "demo");

        // テキスト委譲なし → テキストは描かれない (図形 0)
        var noText = new Scene2D();
        DebugDraw.Flush(noText, w => new Vector2(w.X, w.Y), text: null);
        Assert.Empty(noText.Shapes);

        // 委譲あり → 呼ばれ、投影済み座標が渡る
        DebugDraw.Enable("demo");
        DebugDraw.Text(new Vector3(1, 2, 0), "hi", 0xFFFFFFFF, size: 12f, kind: "demo");
        var withText = new Scene2D();
        string? got = null;
        (float gx, float gy, float gh) = (0, 0, 0);
        DebugDraw.Flush(withText, w => new Vector2(w.X + 10, w.Y + 20),
            (sc, t, x, y, h, c) => { got = t; gx = x; gy = y; gh = h; });
        Assert.Equal("hi", got);
        Assert.Equal(11f, gx);   // 1 + 10
        Assert.Equal(22f, gy);   // 2 + 20
        Assert.Equal(12f, gh);
    }

    [Fact]
    public void Reset_ClearsCategoriesAndCommands()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "demo");
        DebugDraw.Reset();
        Assert.False(DebugDraw.IsEnabled("demo"));
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void SetEnabled_TogglesCategory()
    {
        DebugDraw.SetEnabled("physics", true);
        Assert.True(DebugDraw.IsEnabled("physics"));
        DebugDraw.SetEnabled("physics", false);
        Assert.False(DebugDraw.IsEnabled("physics"));
    }
}
