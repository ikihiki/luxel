namespace Luxel.Gallery.Stories;

public static partial class LearnInput
{
    [Story]
    public static StoryResult SourcesAndBusSample() => InputActionStories.SourcesAndBus();

    [Story]
    public static StoryResult ActionsSample() => InputActionStories.Actions();

    [Story]
    public static StoryResult ContextStackSample() => InputActionStories.ContextStack();

    [Story]
    public static StoryResult BindingsSample() => InputActionStories.Bindings();
}
