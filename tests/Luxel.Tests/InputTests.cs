using System.Numerics;
using Luxel.Input;

namespace Luxel.Tests;

public class InputTests
{
    private static (InputBus, FakeInputSource) NewBus()
    {
        return (new InputBus(), new FakeInputSource());
    }

    [Fact]
    public void ButtonAction_ActivatesOnKeyDown_AndFiresTriggered()
    {
        var (bus, src) = NewBus();
        var ctx = new InputContext("test");
        var jump = ctx.Add(new ButtonAction("Jump", KeyCode.Space));
        int triggered = 0, released = 0;
        jump.Triggered += () => triggered++;
        jump.Released += () => released++;
        var stack = new InputStack();
        stack.Push(ctx);

        // idle
        stack.Update(bus);
        Assert.False(jump.IsActive.Value);

        // press
        src.PressKey(KeyCode.Space);
        src.Poll(bus);
        stack.Update(bus);
        Assert.True(jump.IsActive.Value);
        Assert.Equal(1, triggered);

        // release
        src.ReleaseKey(KeyCode.Space);
        src.Poll(bus);
        stack.Update(bus);
        Assert.False(jump.IsActive.Value);
        Assert.Equal(1, released);
    }

    [Fact]
    public void Axis1D_ButtonPair_ProducesSignedValue()
    {
        var (bus, src) = NewBus();
        var ctx = new InputContext("test");
        var horizontal = ctx.Add(new Axis1DAction("Horizontal"));
        horizontal.ButtonPairs.Add((KeyCode.D, KeyCode.A));   // right = +, left = -
        var stack = new InputStack();
        stack.Push(ctx);

        src.PressKey(KeyCode.D);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(1f, horizontal.Value.Value, 3);

        src.ReleaseKey(KeyCode.D);
        src.PressKey(KeyCode.A);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(-1f, horizontal.Value.Value, 3);

        // 両方: 相殺
        src.PressKey(KeyCode.D);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(0f, horizontal.Value.Value, 3);
    }

    [Fact]
    public void Axis2D_WASD_NormalizesDiagonals()
    {
        var (bus, src) = NewBus();
        var ctx = new InputContext("test");
        var move = ctx.Add(new Axis2DAction("Move"));
        move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
        var stack = new InputStack();
        stack.Push(ctx);

        // W のみ → (0, 1)
        src.PressKey(KeyCode.W);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(new Vector2(0, 1), move.Value.Value);

        // W + D → 正規化された 45 度
        src.PressKey(KeyCode.D);
        src.Poll(bus); stack.Update(bus);
        Vector2 diag = Vector2.Normalize(new Vector2(1, 1));
        Assert.Equal(diag.X, move.Value.Value.X, 3);
        Assert.Equal(diag.Y, move.Value.Value.Y, 3);
    }

    [Fact]
    public void Axis1D_GamepadAxisValue_UsedDirectly()
    {
        var (bus, src) = NewBus();
        var ctx = new InputContext("test");
        var horizontal = ctx.Add(new Axis1DAction("Horizontal"));
        horizontal.Axes.Add(AxisCode.GamepadLeftStickX);
        var stack = new InputStack();
        stack.Push(ctx);

        src.SetAxis(AxisCode.GamepadLeftStickX, 0.7f);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(0.7f, horizontal.Value.Value, 3);
    }

    [Fact]
    public void HigherContextConsumesKey_LowerDoesNotSee()
    {
        var (bus, src) = NewBus();
        var gameplay = new InputContext("gameplay");
        var gpConfirm = gameplay.Add(new ButtonAction("Confirm", KeyCode.Enter));
        var menu = new InputContext("menu");
        var menuConfirm = menu.Add(new ButtonAction("Confirm", KeyCode.Enter));

        var stack = new InputStack();
        stack.Push(gameplay);
        stack.Push(menu);   // menu が上位 (最後に Push)

        src.PressKey(KeyCode.Enter);
        src.Poll(bus); stack.Update(bus);
        Assert.True(menuConfirm.IsActive.Value);
        Assert.False(gpConfirm.IsActive.Value);   // menu が consume したので gameplay には来ない
    }

    [Fact]
    public void SuspendedContext_DoesNotActivate()
    {
        var (bus, src) = NewBus();
        var gameplay = new InputContext("gameplay");
        var move = gameplay.Add(new Axis1DAction("Horizontal"));
        move.ButtonPairs.Add((KeyCode.D, KeyCode.A));
        var stack = new InputStack();
        stack.Push(gameplay);
        stack.SetSuspended(gameplay, true);

        src.PressKey(KeyCode.D);
        src.Poll(bus); stack.Update(bus);
        Assert.Equal(0f, move.Value.Value, 3);
        Assert.False(move.IsActive.Value);
    }

    [Fact]
    public void SignalValue_TracksActiveState()
    {
        var (bus, src) = NewBus();
        var ctx = new InputContext("test");
        var fire = ctx.Add(new ButtonAction("Fire", KeyCode.Mouse0));
        var stack = new InputStack(); stack.Push(ctx);

        // Signal.Reloaded ← ちがう。Signal<T>.OnChanged for reactive update. Use direct read.
        src.PressKey(KeyCode.Mouse0);
        src.Poll(bus); stack.Update(bus);
        Assert.True(fire.Value.Value);
    }
}
