using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Small, source-backed runtime examples that connect Learn concepts to copyable bundles.</summary>
public static class RuntimeExampleStories
{
    [Story("Examples/Audio/WaveformAndVoice", Order = 0, SampleBundle = "audio.tone", Toc = true)]
    public static StoryResult AudioWaveform(StoryContext ctx) => RuntimeExample(ctx, "Audio: waveform and voice",
        "48 kHz mono PCM16を手続き生成し、`AudioClip`のframe数・duration・byte数を決定的に検証します。続いて`AudioMixer.PlayOneShot`でsubmit/playし、`BuffersQueued`とvoice parameterを観測します。",
        "samples/LuxelAudio/AudioConceptSamples.cs", "audio-mixer-voice", "audio.tone");

    [Story("Examples/Audio/Buses", Order = 1, SampleBundle = "audio.tone", Toc = true)]
    public static StoryResult AudioBuses(StoryContext ctx) => RuntimeExample(ctx, "Audio: source and buses",
        "Master/Music/SFXの親子busを作り、`EffectiveVolume`の乗算と`AudioSource.Tick()`が最終voice volumeへ反映することを検証します。",
        "samples/LuxelAudio/AudioConceptSamples.cs", "audio-source-bus", "audio.tone");

    [Story("Examples/Audio/SpatialAttenuation", Order = 2, SampleBundle = "audio.tone", Toc = true)]
    public static StoryResult AudioSpatial(StoryContext ctx) => RuntimeExample(ctx, "Audio: spatial attenuation",
        "listener右側、距離5、減衰範囲1..9という固定配置で、`AudioSource3D`のlinear attenuation=0.5とpan=+1を実音なしで検証します。",
        "samples/LuxelAudio/AudioConceptSamples.cs", "audio-spatial", "audio.tone");

    [Story("Examples/Audio/StreamingQueue", Order = 3, SampleBundle = "audio.tone", Toc = true)]
    public static StoryResult AudioStreaming(StoryContext ctx) => RuntimeExample(ctx, "Audio: streaming queue",
        "in-memory PCM16 WAVを`WavStream`へ渡し、`StreamingVoice.Pump()`がqueue depth 2までprebufferし、`Stop()`でdrain/finishedになる契約を検証します。",
        "samples/LuxelAudio/AudioConceptSamples.cs", "audio-streaming", "audio.tone");

    [Story("Examples/Resources/Pipeline", Order = 0, SampleBundle = "resources.pipeline")]
    public static StoryResult ResourcePipeline(StoryContext ctx) => RuntimeExample(ctx, "Resources: typed pipeline",
        "memory sourceから`byte[] → TextAsset` stepを合成し、typed handleがReadyになるまでの最小pipelineです。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/DependencyDag", Order = 1, SampleBundle = "resources.pipeline")]
    public static StoryResult ResourceDag(StoryContext ctx) => RuntimeExample(ctx, "Resources: dependency DAG",
        "`LoadContext.Load`で作る依存edgeの基礎として、同じURIとrequested typeが同じcache nodeへ収束する構成を確認します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/Reload", Order = 2, SampleBundle = "resources.pipeline")]
    public static StoryResult ResourceReload(StoryContext ctx) => RuntimeExample(ctx, "Resources: reload boundary",
        "この決定的pipelineへ`Watch`/`Republish`を追加し、非同期計算後のvalue swapを`Pump()`境界で観測します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    [Story("Examples/Resources/Lifetime", Order = 3, SampleBundle = "resources.pipeline", Toc = true)]
    public static StoryResult ResourceLifetime(StoryContext ctx) => RuntimeExample(ctx, "Resources: handle lifetime",
        "`ResourceHandle<T>`の所有者を明示し、最後のhandle dispose後にdependentの無いnodeがevictされる基準を示します。",
        "samples/LuxelResources/Program.cs", "resource-pipeline", "resources.pipeline");

    private static StoryResult RuntimeExample(StoryContext ctx, string title, string description, string source, string region, string bundle)
        => $$"""
        # {{title}}

        {{description}}

        {{SampleSource(source, region)}}

        {{SampleBundle(bundle)}}
        """;
}
