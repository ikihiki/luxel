namespace Luxel.Gallery.Stories;

public static partial class LearnAnimation
{
    [Story]
    public static StoryResult CurvesSample() => AnimationStories.Curves();

    [Story]
    public static StoryResult TweenSample() => AnimationStories.Tween();

    [Story]
    public static StoryResult CssKeyframesSample(StoryContext ctx) => AnimationStories.CssKeyframes(ctx);

    [Story]
    public static StoryResult StateMachineSample(StoryContext ctx) => AnimationStories.StateMachineDemo(ctx);

    [Story]
    public static StoryResult EcsClipSample() => AnimationStories.EcsClip();

    [Story]
    public static StoryResult GraphSample(StoryContext ctx) => AnimationStories.Graph(ctx);
}
