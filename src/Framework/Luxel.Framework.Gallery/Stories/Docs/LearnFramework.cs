using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnFramework
{
    [Story("Learn/Framework/Overview", Order = 0, SampleBundle = "framework.fixed-timestep", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Framework overview

        `Luxel.Framework.Game`は一つの`IGameLoop`でframe timing、input、simulation、scene command、render snapshot、resources/audio pumpを統括します。scene lifecycleは`GameSceneSystem`、描画scheduleとGPU実行は`Luxel.Graphics.RenderSystem`へ分離されています。GPU/audio backendはplatform別projectから注入し、desktopでは`Luxel.Framework.Game.Native`の`UseVulkan()`、`UseD3D12()`、`UseAudio()`を利用できます。

        {SampleBundle("framework.fixed-timestep")}
        """;

    [Story("Learn/Framework/FixedTimestepAndPhases", Order = 1, SampleBundle = "framework.fixed-timestep", Toc = true)]
    public static StoryResult Timing(StoryContext ctx) => $"""
        # Fixed timestep and phases

        可変frame dtは`FixedTimestep.Advance`へ蓄積し、返された回数だけ決定的なsimulationを進めます。標準loopはrender opportunityごとにinputを更新し、FixedUpdateを0..N回、Updateを1回実行してからscene commandをcommitし、immutable render snapshotをCoordinatorへ渡します。Featureはpassだけを宣言し、submitやpresentを行いません。

        {SampleSource("samples/LuxelFramework/Program.cs", "framework-fixed-timestep")}
        """;

    [Story("Learn/Framework/ScenesAndRendering", Order = 2, Toc = true)]
    public static StoryResult Scenes(StoryContext ctx) => $"""
        # Scenes and rendering

        `IGameScene`はload、render assignment、fixed/update、unloadだけを実装します。`Push`、`Replace`、`Remove`、state変更は`GameSceneSystem`のcommand queueへ積み、frame boundaryでcommitします。`Running`はupdateとrender、`Paused`はrenderのみ、`Sleeping`はload状態を保ったまま両方から外れます。

        scene-owned `IRenderFeature`は`RenderFeatureAssignmentBuilder.Register(Set, features)`でSetへ割り当てます。CadenceはHostの`ConfigureRendering`または`UseStandardCadences`で構成し、Feature自身はHz、Set、Cadenceを知りません。Set内Feature順は契約ではなく、GPU pass順はRenderGraphのresource/control dependencyだけで決まります。
        """;
}
