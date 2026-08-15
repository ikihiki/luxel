using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

[StoryMeta("Examples/Animation")]
public static class TransitionStories
{
    [Story]
    public static Widget TransitionStates(StoryContext ctx) => ctx.Snap(Frame(
        Button(_ => ctx.Log("click"), "Hover / Press",
                background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 200, height: 64)
            .When(WidgetState.Hover, background: Tw.Red500, scaleX: 1.12f, scaleY: 0.94f, rotate: 0.03f)
            .When(WidgetState.Pressed, background: Tw.Green500)
            .Transition(0.4f, CubicBezierCurve.EaseInOut, ButtonProps.Background)
            .Transition(0.12f, Transform.ScaleX)
            .Transition(0.30f, CubicBezierCurve.EaseInOut, Transform.ScaleY)
            .TransitionTo(WidgetState.Hover, 0.08f, ButtonProps.Background)
            .TransitionTo(WidgetState.Pressed, 0f)
            .TransitionBetween(WidgetState.Pressed, WidgetState.Hover, 0f)));
}
