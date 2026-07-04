using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>EX-M2a: RealizeScope — Effect の所有権とスコープ破棄 (SetRoot リーク修正の基盤)。</summary>
public class RealizeScopeTests
{
    private static UiBuildContext Ctx() => new() { Canvas = null!, Font = null! };

    [Fact]
    public void Effect_IsDisposedWithRootScope()
    {
        var ctx = Ctx();
        var sig = new Signal<int>(0);
        int runs = 0;
        ctx.Effect(() => { runs++; _ = sig.Value; });
        Assert.Equal(1, runs);

        sig.Value = 1;
        Assert.Equal(2, runs);

        ctx.Root.Dispose();   // SetRoot 相当: 前世代の購読が全て解除される
        sig.Value = 2;
        Assert.Equal(2, runs);   // 破棄後は走らない (旧実装ではここでリークしていた)
    }

    [Fact]
    public void Animations_AreRemovedWithScope()
    {
        var ctx = Ctx();
        ctx.AddAnimation(_ => false);
        Assert.Single(ctx.Animations);
        ctx.Root.Dispose();
        Assert.Empty(ctx.Animations);
    }

    [Fact]
    public void FocusablesAndScrollables_AreRemovedWithScope()
    {
        var ctx = Ctx();
        ctx.AddFocusable(onFocus: _ => { });
        ctx.AddScroll(null!, default, _ => { });
        Assert.Single(ctx.Focusables);
        Assert.Single(ctx.Scrollables);
        ctx.Root.Dispose();
        Assert.Empty(ctx.Focusables);
        Assert.Empty(ctx.Scrollables);
    }
}
