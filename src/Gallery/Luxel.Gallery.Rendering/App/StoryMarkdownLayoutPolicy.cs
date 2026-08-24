using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

internal readonly record struct StoryMarkdownLayout(
    float ViewportWidth,
    float ViewportHeight,
    float ContentWidth,
    float EmbedWidth,
    bool StackNavigation,
    float NavigationButtonWidth,
    float NavigationGap);

/// <summary>Native Markdown docs の利用可能幅と最大読書幅を決める純粋 policy。</summary>
internal static class StoryMarkdownLayoutPolicy
{
    public const float DefaultViewportWidth = 800f;
    public const float DefaultViewportHeight = 480f;
    public const float MaximumReadingWidth = 960f;

    public static StoryMarkdownLayout Calculate(float availableWidth, float availableHeight)
    {
        float viewportWidth = Sanitize(availableWidth, DefaultViewportWidth);
        float viewportHeight = Sanitize(availableHeight, DefaultViewportHeight);
        float sidePadding = viewportWidth >= 720 ? 32f : viewportWidth >= 480 ? 24f : 12f;
        float contentWidth = MathF.Min(MaximumReadingWidth, MathF.Max(1f, viewportWidth - sidePadding * 2));
        float embedWidth = MathF.Max(1f, contentWidth - 24f);
        float navigationWidth = MathF.Max(1f, embedWidth - 16f);
        bool stackNavigation = navigationWidth < 520f;
        float gap = stackNavigation ? 8f : 16f;
        float buttonWidth = stackNavigation
            ? navigationWidth
            : MathF.Max(1f, (navigationWidth - gap) / 2f);
        return new StoryMarkdownLayout(
            viewportWidth,
            viewportHeight,
            contentWidth,
            embedWidth,
            stackNavigation,
            buttonWidth,
            gap);
    }

    private static float Sanitize(float value, float fallback)
        => float.IsFinite(value) && value > 0 ? value : fallback;
}

/// <summary>TextEditorView を可変 viewport 内で中央寄せし、最大読書幅だけを制約する。</summary>
internal sealed class ReadableDocumentFrame(TextEditorView document, float initialWidth, float initialHeight) : Widget
{
    public override IEnumerable<Widget> DebugChildren() => [document];

    protected override void PerformLayout(Constraints constraints, LayoutContext context)
    {
        float viewportWidth = float.IsFinite(constraints.MaxW) ? constraints.MaxW : initialWidth;
        float viewportHeight = float.IsFinite(constraints.MaxH) ? constraints.MaxH : initialHeight;
        StoryMarkdownLayout layout = StoryMarkdownLayoutPolicy.Calculate(viewportWidth, viewportHeight);
        Size = constraints.Constrain(new Size(layout.ViewportWidth, layout.ViewportHeight));
        float contentWidth = MathF.Min(Size.Width, layout.ContentWidth);
        Size childSize = document.Layout(
            new Constraints(contentWidth, contentWidth, Size.Height, Size.Height),
            context,
            parentUsesSize: true);
        document.Offset = new Point(MathF.Max(0, (Size.Width - childSize.Width) / 2f), 0);
    }

    protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(context, parent, worldOrigin);
        document.Realize(context, node, WorldPos);
    }
}
