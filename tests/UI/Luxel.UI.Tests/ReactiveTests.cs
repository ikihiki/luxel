using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

public class ReactiveTests
{
    [Fact]
    public void Effect_RerunsOnSignalChange()
    {
        var s = new Signal<int>(1);
        int observed = 0, runs = 0;
        using var _ = Reactive.Effect(() => { observed = s.Value; runs++; });
        Assert.Equal(1, observed);
        Assert.Equal(1, runs);
        s.Value = 5;
        Assert.Equal(5, observed);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Effect_NoRerunWhenValueUnchanged()
    {
        var s = new Signal<int>(1);
        int runs = 0;
        using var eff = Reactive.Effect(() => { _ = s.Value; runs++; });
        s.Value = 1;                 // 同値 → 通知なし
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Computed_DerivesAndPropagates()
    {
        var a = new Signal<int>(2);
        var b = new Signal<int>(3);
        using var sum = new Computed<int>(() => a.Value + b.Value);
        int observed = 0;
        using var _ = Reactive.Effect(() => observed = sum.Value);
        Assert.Equal(5, observed);
        a.Value = 10;
        Assert.Equal(13, observed);
    }

    [Fact]
    public void Peek_DoesNotTrack()
    {
        var s = new Signal<int>(1);
        int runs = 0;
        using var eff = Reactive.Effect(() => { _ = s.Peek(); runs++; });
        s.Value = 2;                 // Peek は購読しない → 再実行なし
        Assert.Equal(1, runs);
    }
}
