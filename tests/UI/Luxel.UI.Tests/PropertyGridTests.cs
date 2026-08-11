using System.Numerics;
using Luxel.Controls;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: PropertyGrid.Discover (ADR-0014 S(C4)) — リフレクション行の発見と書き戻し。GPU 不要。</summary>
public class PropertyGridTests
{
    private enum Quality { Low, High }

    private sealed class Config
    {
        public bool Visible { get; set; } = true;
        [PropertyRange(0, 1)] public float Opacity { get; set; } = 0.5f;
        [PropertyGroup("見た目")] public uint Tint { get; set; } = 0xFF336699;
        [PropertyGroup("見た目")] public Quality Level { get; set; } = Quality.High;
        public string Title = "hello";                       // public field も対象
        public Vector2 Offset { get; set; } = new(1, 2);
        [PropertyIgnore] public int Hidden { get; set; }     // 除外
        public object? Unsupported { get; set; }             // 非対応型はスキップ
        public int ReadOnly => 1;                            // set なしはスキップ
    }

    [Fact]
    public void Discover_FindsSupportedMembers_InDeclarationOrder()
    {
        var rows = PropertyGrid.Discover(new Config());
        Assert.Equal(["Visible", "Opacity", "Tint", "Level", "Offset", "Title"],
                     rows.Select(r => r.Name).ToArray());   // プロパティ宣言順 → field は後
    }

    [Fact]
    public void Discover_RangeAndGroup()
    {
        var rows = PropertyGrid.Discover(new Config());
        PropertyRow opacity = rows.Single(r => r.Name == "Opacity");
        Assert.Equal(0, opacity.RangeMin);
        Assert.Equal(1, opacity.RangeMax);
        Assert.Equal("", opacity.Group);
        Assert.Equal("見た目", rows.Single(r => r.Name == "Tint").Group);
    }

    [Fact]
    public void Rows_ReadAndWriteTarget()
    {
        var cfg = new Config();
        var rows = PropertyGrid.Discover(cfg);

        PropertyRow visible = rows.Single(r => r.Name == "Visible");
        Assert.Equal(true, visible.Get());
        visible.Set(false);
        Assert.False(cfg.Visible);

        PropertyRow title = rows.Single(r => r.Name == "Title");
        title.Set("world");
        Assert.Equal("world", cfg.Title);

        PropertyRow offset = rows.Single(r => r.Name == "Offset");
        offset.Set(new Vector2(3, 4));
        Assert.Equal(new Vector2(3, 4), cfg.Offset);
    }

    private struct Particle
    {
        public float Speed { get; set; }
    }

    [Fact]
    public void Rows_BoxedStruct_WritesIntoBox()
    {
        object boxed = new Particle { Speed = 1 };
        var rows = PropertyGrid.Discover(boxed);
        rows.Single(r => r.Name == "Speed").Set(2f);
        Assert.Equal(2f, ((Particle)boxed).Speed);   // 箱へ書かれる (ECS へ戻すのはシェル)
    }
}
