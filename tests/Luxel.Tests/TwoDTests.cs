using System.Numerics;
using Luxel.Graphics.TwoD;
using Xunit;

namespace Luxel.Tests;

public class Affine2DTests
{
    [Fact]
    public void TranslateApply()
    {
        var t = Affine2D.Translate(10, 20);
        Assert.Equal(new Vector2(15, 25), t.Apply(new Vector2(5, 5)));
    }

    [Fact]
    public void Compose_AppliesSecondThenFirst()
    {
        // world = parent ∘ local。local で平行移動してから parent でスケール。
        var parent = Affine2D.Scale(2, 2);
        var local = Affine2D.Translate(3, 4);
        var world = Affine2D.Mul(parent, local);
        // 点 (0,0): local→(3,4)→parentでスケール→(6,8)
        Assert.Equal(new Vector2(6, 8), world.Apply(Vector2.Zero));
    }

    [Fact]
    public void Rotate90_MapsXtoY()
    {
        var r = Affine2D.Rotate(MathF.PI / 2);
        Vector2 p = r.Apply(new Vector2(1, 0));
        Assert.True(MathF.Abs(p.X) < 1e-5 && MathF.Abs(p.Y - 1) < 1e-5);
    }
}

public class Color2DTests
{
    [Fact]
    public void RgbaPacksLittleEndian()
    {
        uint c = Color2D.Rgba(0x11, 0x22, 0x33, 0x44);
        Assert.Equal(0x44332211u, c);
    }
}
