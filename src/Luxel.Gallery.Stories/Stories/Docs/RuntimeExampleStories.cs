using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Small, source-backed runtime examples that connect Learn concepts to copyable bundles.</summary>
public static class RuntimeExampleStories
{
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
