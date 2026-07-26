using Luxel.Platform.Silk;
using Silk.NET.GLFW;

namespace Luxel.Platform.Silk.Tests;

public sealed class SilkInputTests
{
    [Theory]
    [InlineData(Keys.A, WindowKey.A)]
    [InlineData(Keys.Z, WindowKey.Z)]
    [InlineData(Keys.Number0, WindowKey.D0)]
    [InlineData(Keys.Number9, WindowKey.D9)]
    [InlineData(Keys.F1, WindowKey.F1)]
    [InlineData(Keys.F12, WindowKey.F12)]
    [InlineData(Keys.KeypadEnter, WindowKey.Enter)]
    [InlineData(Keys.KeypadDivide, WindowKey.Slash)]
    [InlineData(Keys.SuperLeft, WindowKey.Unknown)]
    public void MapsPortableKeys(Keys source, WindowKey expected)
        => Assert.Equal(expected, SilkInput.MapKey(source));

    [Fact]
    public void MapsModifierSnapshot()
    {
        WindowKeyModifiers modifiers = SilkInput.MapModifiers(
            KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Super);

        Assert.Equal(
            WindowKeyModifiers.Control | WindowKeyModifiers.Shift |
            WindowKeyModifiers.Alt | WindowKeyModifiers.Meta,
            modifiers);
    }

    [Theory]
    [InlineData(MouseButton.Left, WindowPointerButton.Left)]
    [InlineData(MouseButton.Right, WindowPointerButton.Right)]
    [InlineData(MouseButton.Middle, WindowPointerButton.Middle)]
    [InlineData(MouseButton.Button4, WindowPointerButton.X1)]
    [InlineData(MouseButton.Button5, WindowPointerButton.X2)]
    [InlineData(MouseButton.Button8, WindowPointerButton.None)]
    public void MapsPortablePointerButtons(MouseButton source, WindowPointerButton expected)
        => Assert.Equal(expected, SilkInput.MapButton(source));

    [Theory]
    [InlineData(InputAction.Press, false)]
    [InlineData(InputAction.Release, false)]
    [InlineData(InputAction.Repeat, true)]
    public void MapsRepeatState(InputAction action, bool expected)
        => Assert.Equal(expected, SilkInput.IsRepeat(action));

    [Fact]
    public void ConvertsUnicodeScalarToUtf16String()
    {
        Assert.Equal("A", SilkInput.CodePointToString('A'));
        Assert.Equal("😀", SilkInput.CodePointToString(0x1F600));
        Assert.Null(SilkInput.CodePointToString(0xD800));
        Assert.Null(SilkInput.CodePointToString(0x110000));
    }
}
