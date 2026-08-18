using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public readonly struct TestUtilityScope;

public static class ExternalTestUtilityExtensions
{
    extension(U)
    {
        public static TestUtilityScope TestPack => default;
    }

    extension(TestUtilityScope scope)
    {
        public U Emphasis(uint color) => U.Property<Widget, uint>("Background", color);
    }

    extension(TreeViewUtilityScope scope)
    {
        public U DenseRows() => U.Custom<TreeView>("TestPack.TreeView.DenseRows", UtilityKind.ControlSpecific,
            (tree, state) =>
            {
                if (state != WidgetState.Default)
                    throw new InvalidOperationException("DenseRows is a layout utility and cannot target a visual state.");
                tree.Appearance.SetBase((tree.Appearance.Get() ?? new TreeViewAppearance()) with { RowHeight = 20, RowSpacing = 1 });
            });
    }
}

public sealed class UtilityTests
{
    [Fact]
    public void UtilityDescriptorsExposeTargetValueKindAndStableNameMetadata()
    {
        U background = U.Background(0xFF010203);
        U rowHeight = U.TreeView.RowHeight(24);
        U column = U.Grid.Column(2);

        Assert.Equal("Background", background.Name);
        Assert.Equal(UtilityKind.Property, background.Kind);
        Assert.Equal(typeof(Widget), background.TargetType);
        Assert.Equal(typeof(uint), background.ValueType);
        Assert.Equal(typeof(TreeView), rowHeight.TargetType);
        Assert.Null(rowHeight.ValueType);
        Assert.Equal("Luxel.Controls.Grid.Column", column.Name);
        Assert.Equal(typeof(int), column.ValueType);
    }

    [Fact]
    public void GeneratedFactory_AppliesUtilitiesBeforeNamedParameters()
    {
        Button button = Button(
            _ => { },
            "Save",
            background: 0xFF030303,
            utilities:
            [
                U.Background(0xFF010101),
                U.Background(0xFF020202),
                U.Hover([U.Background(0xFF040404)]),
            ]);

        Assert.Equal(0xFF030303u, button.Background.Get());

        button.Hovered.Value = true;
        Assert.Equal(0xFF040404u, button.Background.Get());
    }

    [Fact]
    public void LayoutUtility_RejectsReactiveValueUntilLayoutInvalidationExists()
    {
        var width = new Signal<Length>(120);

#pragma warning disable NGUI003 // Keep runtime defense covered in addition to the compile-time diagnostic.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => U.Width(width));
#pragma warning restore NGUI003
        Assert.Contains("layout invalidation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LayoutUtility_RejectsVisualStateAtRuntime()
    {
#pragma warning disable NGUI004 // Keep runtime defense covered in addition to the compile-time diagnostic.
        U stateful = U.Hover([U.Width(120)]);
#pragma warning restore NGUI004

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => stateful.ApplyTo(Text("x")));
        Assert.Contains("state-driven layout invalidation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PaintUtility_AllowsReactiveValue()
    {
        var background = new Signal<uint>(0xFF010203);
        Button button = Button(_ => { }, "reactive", utilities: [U.Background(background)]);

        Assert.Equal(0xFF010203u, button.Background.Get());
        background.Value = 0xFF040506;
        Assert.Equal(0xFF040506u, button.Background.Get());
    }

    [Fact]
    public void GridUtility_AndFluentWrapper_UseSameAttachedProperties()
    {
        Text viaUtility = Text("utility", utilities:
        [
            U.Grid.Column(2),
            U.Grid.Row(3),
            U.Grid.ColumnSpan(4),
            U.Grid.RowSpan(5),
        ]);
        Text viaFluent = Text("fluent").GridCell(2, 3, 4, 5);

        Assert.Equal(viaFluent.GetAttached(GridProperties.Column), viaUtility.GetAttached(GridProperties.Column));
        Assert.Equal(viaFluent.GetAttached(GridProperties.Row), viaUtility.GetAttached(GridProperties.Row));
        Assert.Equal(viaFluent.GetAttached(GridProperties.ColumnSpan), viaUtility.GetAttached(GridProperties.ColumnSpan));
        Assert.Equal(viaFluent.GetAttached(GridProperties.RowSpan), viaUtility.GetAttached(GridProperties.RowSpan));
    }

    [Fact]
    public void GridAttachedPropertiesExposeOwnerStableIdTypeAndDefaultMetadata()
    {
        Assert.Equal(typeof(Grid), GridProperties.Column.OwnerType);
        Assert.Equal(typeof(int), GridProperties.Column.ValueType);
        Assert.Equal("Luxel.Controls.Grid.Column", GridProperties.Column.Id);
        Assert.Equal(0, GridProperties.Column.DefaultValue);
        Assert.Equal(1, GridProperties.ColumnSpan.DefaultValue);
    }

    [Fact]
    public void AttachedProperty_ValidatesValues()
    {
        Text text = Text("x");
        Assert.Throws<ArgumentOutOfRangeException>(() => U.Grid.Column(-1).ApplyTo(text));
        Assert.Throws<ArgumentOutOfRangeException>(() => U.Grid.ColumnSpan(0).ApplyTo(text));
    }

    [Fact]
    public void TreeViewScope_PatchesAppearanceInCollectionOrder()
    {
        TreeView tree = TreeView(
            [new TreeNode("root", "Root")],
            utilities:
            [
                U.TreeView.RowHeight(28),
                U.TreeView.Indent(12),
                U.TreeView.RowHeight(30),
            ]);

        TreeViewAppearance appearance = Assert.IsType<TreeViewAppearance>(tree.Appearance.Get());
        Assert.Equal(30, appearance.RowHeight);
        Assert.Equal(12, appearance.Indent);
    }

    [Fact]
    public void ExternalUtilityPack_CanAddNewAndExistingScopesWithoutRegistration()
    {
        Button button = Button(_ => { }, "external", utilities: [U.TestPack.Emphasis(0xFF123456)]);
        TreeView tree = TreeView([new TreeNode("root", "Root")], utilities: [U.TreeView.DenseRows()]);

        Assert.Equal(0xFF123456u, button.Background.Get());
        TreeViewAppearance appearance = Assert.IsType<TreeViewAppearance>(tree.Appearance.Get());
        Assert.Equal(20, appearance.RowHeight);
        Assert.Equal(1, appearance.RowSpacing);
    }

    [Fact]
    public void ControlSpecificUtility_RejectsWrongTarget()
    {
#pragma warning disable NGUI005 // Keep runtime defense covered in addition to the compile-time diagnostic.
        Assert.Throws<InvalidOperationException>(() => Button(
            _ => { },
            "wrong",
            utilities: [U.TreeView.RowHeight(24)]));
#pragma warning restore NGUI005
    }
}
