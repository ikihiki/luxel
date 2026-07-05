using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>KT: StoryKnob の説明/文字列書き込みと StoryContext の knob 編集キュー。</summary>
public class KnobsTests
{
    [Fact]
    public void Signal_WithDescription_ExposedOnKnob()
    {
        var ctx = new StoryContext();
        ctx.Signal("speed", 1f, "速度の倍率");
        ctx.Signal("name", "x");
        Assert.Equal("速度の倍率", ctx.Knobs[0].Description);
        Assert.Equal("float", ctx.Knobs[0].Type);
        Assert.Null(ctx.Knobs[1].Description);
    }

    [Fact]
    public void SetText_CoercesByType()
    {
        var ctx = new StoryContext();
        Signal<float> f = ctx.Signal("f", 1f);
        Signal<bool> b = ctx.Signal("b", false);
        Signal<int> i = ctx.Signal("i", 0);
        ctx.Knobs[0].SetText("2.5");
        ctx.Knobs[1].SetText("true");
        ctx.Knobs[2].SetText("42");
        Assert.Equal(2.5f, f.Value);
        Assert.True(b.Value);
        Assert.Equal(42, i.Value);
    }

    [Fact]
    public void QueueKnobEdit_AppliesOnPump_NotBefore()
    {
        var ctx = new StoryContext();
        Signal<float> f = ctx.Signal("f", 1f);
        ctx.QueueKnobEdit(ctx.Knobs[0], "3");
        Assert.Equal(1f, f.Value);          // キューしただけでは書かれない (effect 文脈保護)
        ctx.PumpKnobEdits();
        Assert.Equal(3f, f.Value);
        ctx.PumpKnobEdits();                // 空 Pump は無害
        Assert.Equal(3f, f.Value);
    }

    [Fact]
    public void QueueKnobEdit_InvalidValue_Ignored()
    {
        var ctx = new StoryContext();
        Signal<int> i = ctx.Signal("i", 7);
        ctx.QueueKnobEdit(ctx.Knobs[0], "abc");
        ctx.PumpKnobEdits();
        Assert.Equal(7, i.Value);
    }

    private enum Fruit { Apple, Banana, Cherry }

    [Fact]
    public void EnumKnob_TypeHint_And_SetText()
    {
        var ctx = new StoryContext();
        Signal<Fruit> f = ctx.Signal("fruit", Fruit.Apple, "果物");
        StoryKnob k = ctx.Knobs[0];
        Assert.Equal("enum:Apple|Banana|Cherry", k.Type);
        Assert.Equal("Apple", k.Value);
        k.SetText("Cherry");
        Assert.Equal(Fruit.Cherry, f.Value);
        k.SetText("nope");                    // 不正名は無視
        Assert.Equal(Fruit.Cherry, f.Value);
    }

    [Fact]
    public void LengthKnob_TypeHint_And_SetText()
    {
        var ctx = new StoryContext();
        Signal<Length> w = ctx.Signal("width", new Length(320, LengthUnit.Px), "幅");
        StoryKnob k = ctx.Knobs[0];
        Assert.Equal("length", k.Type);
        k.SetText("50%");
        Assert.Equal(new Length(50, LengthUnit.Percent), w.Value);
        Assert.True(Length.TryParse(k.Value, null, out Length round));   // 表示値は往復可能
        Assert.Equal(w.Value, round);
        k.SetText("junk");                    // 不正値は無視
        Assert.Equal(new Length(50, LengthUnit.Percent), w.Value);
    }
}
