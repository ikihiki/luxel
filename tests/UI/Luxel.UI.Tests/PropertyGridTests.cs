using System.Numerics;
using Luxel.Controls;
using Luxel.UI;
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

    private sealed class AllTypes
    {
        public bool Boolean { get; set; } = true;
        public int Integer { get; set; } = 2;
        public float Float { get; set; } = 3.5f;
        public string Text { get; set; } = "before";
        public uint Color { get; set; } = 0xFF102030;
        public Quality Enum { get; set; } = Quality.Low;
        public Vector2 Vector2 { get; set; } = new(1, 2);
        public Vector3 Vector3 { get; set; } = new(3, 4, 5);
        public Length Length { get; set; } = Length.Percent(50);
    }

    [Fact]
    public void Controller_MapsStableDescriptorMetadata()
    {
        var first = new ReflectedPropertyController(new Config());
        var second = new ReflectedPropertyController(new Config());

        ReflectedPropertyMember opacity = first.Members.Single(m => m.Name == "Opacity");
        Assert.Equal(opacity.Descriptor.Id, second.Members.Single(m => m.Name == "Opacity").Descriptor.Id);
        Assert.Equal(typeof(Config).FullName, opacity.Descriptor.MemberKey!.Value.DeclaringType);
        Assert.Equal("Opacity", opacity.Descriptor.MemberKey.Value.Name);
        Assert.Equal("Opacity", opacity.Descriptor.DisplayName);
        Assert.Equal(Luxel.ValueDocument.ValueEditorKind.Number, opacity.Descriptor.EditorKind);
        Assert.Equal(0m, opacity.Descriptor.Numeric!.Minimum);
        Assert.Equal(1m, opacity.Descriptor.Numeric.Maximum);
        Assert.Equal(1, opacity.Descriptor.Order);
    }

    [Fact]
    public void Controller_AllSupportedTypesRoundTrip()
    {
        var target = new AllTypes();
        var controller = new ReflectedPropertyController(target);
        var expected = new Dictionary<string, object?>
        {
            ["Boolean"] = false,
            ["Integer"] = -7,
            ["Float"] = 8.25f,
            ["Text"] = "after",
            ["Color"] = 0xFFABCDEFu,
            ["Enum"] = Quality.High,
            ["Vector2"] = new Vector2(6, 7),
            ["Vector3"] = new Vector3(8, 9, 10),
            ["Length"] = Length.Em(1.5f),
        };

        foreach ((string name, object? value) in expected)
        {
            Assert.True(controller.CommitValue(name, value).Success);
            Assert.Equal(value, controller.AcceptedValue(name));
        }

        Assert.False(target.Boolean);
        Assert.Equal(-7, target.Integer);
        Assert.Equal(8.25f, target.Float);
        Assert.Equal("after", target.Text);
        Assert.Equal(0xFFABCDEFu, target.Color);
        Assert.Equal(Quality.High, target.Enum);
        Assert.Equal(new Vector2(6, 7), target.Vector2);
        Assert.Equal(new Vector3(8, 9, 10), target.Vector3);
        Assert.Equal(Length.Em(1.5f), target.Length);
    }

    [Theory]
    [InlineData("Integer", "-")]
    [InlineData("Float", "1.")]
    [InlineData("Float", "-")]
    public void Controller_InvalidNumericDraftDoesNotMutateAcceptedValue(string name, string text)
    {
        var target = new AllTypes();
        var controller = new ReflectedPropertyController(target);
        object? before = controller.AcceptedValue(name);
        long revision = controller.Document.Revision;

        controller.SetDraft(name, text);
        Luxel.ValueDocument.ValueApplyResult result = controller.CommitDraft(name);

        Assert.Equal(Luxel.ValueDocument.ValueApplyStatus.ParseFailed, result.Status);
        Assert.Equal(before, controller.AcceptedValue(name));
        Assert.Equal(revision, controller.Document.Revision);
        Assert.False(controller.CanUndo);
        Assert.NotNull(controller.DraftOf(name)!.Diagnostic);
    }

    private sealed class ThrowingSetter
    {
        public int Value
        {
            get => 4;
            set => throw new InvalidOperationException("setter rejected");
        }
    }

    [Fact]
    public void Controller_SetterExceptionProducesDiagnosticWithoutHistory()
    {
        var controller = new ReflectedPropertyController(new ThrowingSetter());

        Luxel.ValueDocument.ValueApplyResult result = controller.CommitValue("Value", 9);

        Assert.Equal(Luxel.ValueDocument.ValueApplyStatus.AdapterRejected, result.Status);
        Assert.Equal(4, controller.AcceptedValue("Value"));
        Assert.Equal(0, controller.Document.Revision);
        Assert.False(controller.CanUndo);
        Assert.Contains("setter rejected", controller.DraftOf("Value")!.Diagnostic!.Message);
    }

    [Fact]
    public void Controller_OwnsUndoRedoAcrossMemberProjectionRebuilds()
    {
        var target = new AllTypes();
        var controller = new ReflectedPropertyController(target);
        Assert.True(controller.CommitValue("Integer", 12).Success);

        _ = controller.Members.Select(member => member.Descriptor).ToArray();
        controller.RefreshFromTarget(); // a view refresh with no external value change keeps document history
        Assert.True(controller.CanUndo);
        Assert.True(controller.Undo().Success);
        Assert.Equal(2, target.Integer);
        Assert.True(controller.CanRedo);
        Assert.True(controller.Redo().Success);
        Assert.Equal(12, target.Integer);
    }

    private struct Particle
    {
        public float Speed { get; set; }
    }

    [Fact]
    public void Controller_BoxedStruct_WritesIntoBox()
    {
        object boxed = new Particle { Speed = 1 };
        var controller = new ReflectedPropertyController(boxed);
        Assert.True(controller.CommitValue("Speed", 2f).Success);
        Assert.Equal(2f, ((Particle)boxed).Speed);   // 箱へ書かれる (ECS へ戻すのはシェル)
    }
}
