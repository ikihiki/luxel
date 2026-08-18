using Luxel.UI;

namespace Luxel.Controls;

/// <summary><c>U.TreeView.*</c> utility の名前空間スコープ。</summary>
public readonly struct TreeViewUtilityScope;

/// <summary>Luxel.Controls が提供する共通/Control固有 Utility。</summary>
public static class ControlUtilityExtensions
{
    extension(U)
    {
        public static U Background(Bindable<uint> value) => U.Property<Widget, uint>("Background", value);
        public static U Foreground(Bindable<uint> value) => U.Property<Widget, uint>("Foreground", value);
        public static U Opacity(Bindable<float> value) => U.Property<Widget, float>("Opacity", value);
        public static U Padding(Bindable<Thickness> value) => U.Property<Widget, Thickness>("Padding", value, UtilityKind.Layout);
        public static U Rounded(Bindable<float> value) => U.Property<Widget, float>("Rounded", value);
        public static TreeViewUtilityScope TreeView => default;
    }

    extension(TreeViewUtilityScope scope)
    {
        [UtilityTarget(typeof(TreeView))]
        public U RowHeight(float value) => PatchAppearance("TreeView.RowHeight", appearance => appearance with { RowHeight = value });
        [UtilityTarget(typeof(TreeView))]
        public U RowSpacing(float value) => PatchAppearance("TreeView.RowSpacing", appearance => appearance with { RowSpacing = value });
        [UtilityTarget(typeof(TreeView))]
        public U Indent(float value) => PatchAppearance("TreeView.Indent", appearance => appearance with { Indent = value });
        [UtilityTarget(typeof(TreeView))]
        public U PaddingX(float value) => PatchAppearance("TreeView.PaddingX", appearance => appearance with { PaddingX = value });
        [UtilityTarget(typeof(TreeView))]
        public U Radius(float value) => PatchAppearance("TreeView.Radius", appearance => appearance with { Radius = value });
        [UtilityTarget(typeof(TreeView))]
        public U SelectedBackground(uint value) => PatchAppearance("TreeView.SelectedBackground", appearance => appearance with { SelectedBackground = value });

        private static U PatchAppearance(string name, Func<TreeViewAppearance, TreeViewAppearance> patch)
            => U.Custom<TreeView>(name, UtilityKind.ControlSpecific, (tree, state) =>
            {
                if (state != WidgetState.Default)
                    throw new InvalidOperationException($"Layout utility '{name}' cannot be applied to state '{state}'.");
                tree.Appearance.SetBase(patch(tree.Appearance.Get() ?? new TreeViewAppearance()));
            });
    }
}
