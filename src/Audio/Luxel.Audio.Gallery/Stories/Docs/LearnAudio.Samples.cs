namespace Luxel.Audio.Gallery;

public static partial class LearnAudio
{
    [Story]
    public static StoryResult BackendLifecycleSample() => AudioStories.BackendLifecycle();

    [Story]
    public static StoryResult WaveformAndVoiceSample() => AudioStories.WaveformAndVoice();

    [Story]
    public static StoryResult BusesSample() => AudioStories.Buses();

    [Story]
    public static StoryResult SpatialAttenuationSample() => AudioStories.SpatialAttenuation();

    [Story]
    public static StoryResult StreamingQueueSample() => AudioStories.StreamingQueue();
}
