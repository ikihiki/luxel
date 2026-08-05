using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Small, source-backed runtime examples that connect Learn concepts to copyable bundles.</summary>
public static class RuntimeExampleStories
{
    [Story("Examples/Audio/WaveformAndVoice", Order = 0, SampleBundle = "audio.tone")]
    public static Widget AudioWaveform(StoryContext ctx) => RuntimeExample(ctx, "Audio: waveform and voice",
        "procedural PCM16 waveformから`AudioClip`を作り、voiceへqueueしてplaying状態とsubmitted bytesを観測します。",
        "samples/LuxelAudio/Program.cs", "audio-tone", "audio.tone");

    [Story("Examples/Audio/Buses", Order = 1, SampleBundle = "audio.tone")]
    public static Widget AudioBuses(StoryContext ctx) => RuntimeExample(ctx, "Audio: mixer and buses",
        "`AudioMixer`を中心にone-shotとvoice lifecycleを構成します。階層音量はLearnのbusページからこの最小bundleへ追加します。",
        "samples/LuxelAudio/Program.cs", "audio-tone", "audio.tone");

    [Story("Examples/Audio/SpatialAttenuation", Order = 2, SampleBundle = "audio.tone")]
    public static Widget AudioSpatial(StoryContext ctx) => RuntimeExample(ctx, "Audio: spatial attenuation",
        "headless bundleを基準点に、`AudioSource3D`の距離減衰とpanを追加します。実音ではなく計算値とvoice状態をtestします。",
        "samples/LuxelAudio/Program.cs", "audio-tone", "audio.tone");

    [Story("Examples/Audio/StreamingQueue", Order = 3, SampleBundle = "audio.tone")]
    public static Widget AudioStreaming(StoryContext ctx) => RuntimeExample(ctx, "Audio: streaming queue",
        "短いclipのqueue観測を、`WavStream`/`LoopingStream`と`StreamingVoice.Pump()`へ拡張する接続点を示します。",
        "samples/LuxelAudio/Program.cs", "audio-tone", "audio.tone");

    [Story("Examples/Resources/Pipeline", Order = 0, SampleBundle = "resources.pipeline")]
    public static Widget ResourcePipeline(StoryContext ctx) => RuntimeExample(ctx, "Resources: typed pipeline",
        "memory sourceから`byte[] → TextAsset` stepを合成し、typed handleがReadyになるまでの最小pipelineです。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/DependencyDag", Order = 1, SampleBundle = "resources.pipeline")]
    public static Widget ResourceDag(StoryContext ctx) => RuntimeExample(ctx, "Resources: dependency DAG",
        "`LoadContext.Load`で作る依存edgeの基礎として、同じURIとrequested typeが同じcache nodeへ収束する構成を確認します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/Reload", Order = 2, SampleBundle = "resources.pipeline")]
    public static Widget ResourceReload(StoryContext ctx) => RuntimeExample(ctx, "Resources: reload boundary",
        "この決定的pipelineへ`Watch`/`Republish`を追加し、非同期計算後のvalue swapを`Pump()`境界で観測します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/Lifetime", Order = 3, SampleBundle = "resources.pipeline")]
    public static Widget ResourceLifetime(StoryContext ctx) => RuntimeExample(ctx, "Resources: handle lifetime",
        "`ResourceHandle<T>`の所有者を明示し、最後のhandle dispose後にdependentの無いnodeがevictされる基準を示します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    private static Widget RuntimeExample(StoryContext ctx, string title, string description, string source, string region, string bundle)
        => DocNew(ctx, $$"""
        # {{title}}

        {{description}}

        {{SampleSource(source, region)}}

        {{SampleBundle(bundle)}}
        """, toc: true);
}
