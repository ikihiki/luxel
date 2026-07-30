using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Deterministic browser-safe explanation used when a component requires an unsupported capability input.</summary>
public sealed class StoryCapabilityFallback(string component, string explanation) : Widget
{
    public string ComponentName { get; } = component;
    public string Explanation { get; } = explanation;
    public override string? DebugDetail => ComponentName + ": " + Explanation;

    protected override void PerformLayout(Constraints constraints, LayoutContext context)
        => Size = constraints.Constrain(new Size(420, 132));

    protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
    {
        UiNode root = CreateRoot(context, parent, worldOrigin);
        var background = new Scene2D();
        background.FillRoundedRect(Color2D.Rgba(30, 41, 59, 255), 0, 0, Size.Width, Size.Height, 10);
        root.Content = background;

        UiNode text = context.Canvas.AddChild(root);
        text.Transform = Affine2D.Translate(18, 18);
        text.Z = 1;
        var scene = new Scene2D();
        context.Font.AppendText(scene, ComponentName + " — deterministic browser fixture", 0, context.Font.Ascent(16), 16, Color2D.Rgba(226, 232, 240, 255));
        context.Font.AppendText(scene, Explanation, 0, 38 + context.Font.Ascent(12), 12, Color2D.Rgba(148, 163, 184, 255));
        text.Content = scene;
    }
}
