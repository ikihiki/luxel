using Luxel.Controls;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>EV: [UiEvent] — コールバックの UI パラメータ化 (第一引数 = 発火元の UI 自身)。
/// ファクトリ引数 / 暗黙変換 / 生成 InvokeEvent / ハンドラなしの安全性。</summary>
public class UiEventTests
{
    [Fact]
    public void FactoryArg_AssignsHandler_And_InvokeEventFires()
    {
        int n = 0;
        Button? sender = null;
        Button b = Kit.Button(s => { n++; sender = s; }, "x");
        Assert.True(b.OnClick.HasHandler);
        Assert.True(b.InvokeEvent("OnClick"));   // 生成された switch 経由 (sender = 自身)
        Assert.Equal(1, n);
        Assert.Same(b, sender);
    }

    [Fact]
    public void NoHandler_IsSafe_And_InvokeEventReportsMatch()
    {
        Button b = Kit.Button(null, "x");        // EV: onClick は省略可能 (ctor 引数ではない)
        Assert.False(b.OnClick.HasHandler);
        Assert.True(b.InvokeEvent("OnClick"));   // 名前は一致 (発火は no-op)
        Assert.False(b.InvokeEvent("Nope"));
    }

    [Fact]
    public void ImplicitConversion_FromActionVariable()
    {
        int got = -1;
        ListView? sender = null;
        var lv = Kit.ListView(100f, onSelect: (s, i) => { sender = s; got = i; });
        Assert.True(lv.OnSelect.HasHandler);
        lv.OnSelect.Invoke(lv, 7);
        Assert.Equal(7, got);
        Assert.Same(lv, sender);

        Action<ListView, int, int> reorder = (_, a, b) => got = a * 100 + b;
        lv.OnReorder = reorder;                  // Action 変数 → UiEvent<ListView,int,int> 暗黙変換
        lv.OnReorder.Invoke(lv, 3, 5);
        Assert.Equal(305, got);
    }

    [Fact]
    public void TypedEvents_AreNotInvokableByName()
    {
        var lv = Kit.ListView(100f, onSelect: (_, _) => { });
        Assert.False(lv.InvokeEvent("OnSelect"));   // sender 以外の引数付きイベントは名前発火の対象外
    }

    [Fact]
    public void ItemsParam_AcceptsSignal_AndReflectsSwap()
    {
        var items = new Signal<IReadOnlyList<string>>(["a", "b"]);
        var lv = Kit.ListView(100f, items: items);
        Assert.Equal("2 行", lv.DebugDetail);
        items.Value = ["a", "b", "c"];
        Assert.Equal("3 行", lv.DebugDetail);
    }
}
