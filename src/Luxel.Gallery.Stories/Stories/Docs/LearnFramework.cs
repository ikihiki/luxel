using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnFramework
{
    [Story("Learn/Framework/Overview", Order = 0, SampleBundle = "framework.fixed-timestep", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Framework overview

        `Luxel.Framework`はframe timing、scene lifecycle、7 phase、input/resources/audio、RenderGraphを一つのgame loopへ接続します。最初はGPU不要の`FixedTimestep`から始め、次に`IScene`、最後に`GameScene`へ進みます。

        {SampleBundle("framework.fixed-timestep")}
        """;

    [Story("Learn/Framework/FixedTimestepAndPhases", Order = 1, SampleBundle = "framework.fixed-timestep", Toc = true)]
    public static StoryResult Timing(StoryContext ctx) => $"""
        # Fixed timestep and phases

        可変frame dtは`FixedTimestep.Advance`へ蓄積し、返された回数だけ決定的なsimulationを進めます。上限超過は`DroppedSteps`へ記録され、`Alpha`は描画補間に使います。標準loopの順序はEarlyUpdate → FixedUpdate → Update → LateUpdate → PreRender → Render → PostRenderです。

        {SampleSource("samples/LuxelFramework/Program.cs", "framework-fixed-timestep")}
        """;

    [Story("Learn/Framework/ScenesAndServices", Order = 2, Toc = true)]
    public static StoryResult Scenes(StoryContext ctx) => $"""
        # Scenes and services

        最小契約は`IScene.OnLoadAsync / RunAsync / OnUnloadAsync`です。`SceneManager`は現在sceneをcancelし、Unload → Loadの順で切り替えます。通常は`GameScene`を継承し、`SceneLoopServices`からGPU、resources、audio、input、commands、UI registryを受け取ります。固定更新では可変dtを読まず、`FixedUpdateContext.FixedDeltaSeconds`だけを使います。
        """;
}
