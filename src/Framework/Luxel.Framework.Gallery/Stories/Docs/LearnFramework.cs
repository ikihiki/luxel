using Luxel.UI;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Framework")]
public static class LearnFramework
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Framework overview

        {Toc()}

        `Luxel.Framework.Game`は一つの`IGameLoop`でframe timing、input、simulation、scene command、render snapshot、resources/audio pumpを統括します。scene lifecycleは`GameSceneSystem`、描画scheduleとGPU実行は`Luxel.Graphics.RenderSystem`へ分離されています。GPU/audio backendはplatform別projectから注入し、desktopでは`Luxel.Framework.Game.Native`の`UseVulkan()`、`UseD3D12()`、`UseAudio()`を利用できます。最初はGPU不要の`FixedTimestep`から始め、次に`IGameScene`、最後に`IRenderFeature`へ進みます。

        """;

    [Story]
    public static StoryResult Timing(StoryContext ctx) => $"""
        # Fixed timestep and phases

        {Toc()}

        可変frame dtは`FixedTimestep.Advance`へ蓄積し、返された回数だけ決定的なsimulationを進めます。上限超過は`DroppedSteps`へ記録され、`Alpha`は描画補間に使います。標準loopはrender opportunityごとにinputを更新し、FixedUpdateを0..N回、Updateを1回実行してからscene commandをcommitし、immutable render snapshotをCoordinatorへ渡します。Featureはpassだけを宣言し、submitやpresentを行いません。

        """;

    [Story]
    public static StoryResult Scenes(StoryContext ctx) => $"""
        # Scenes and rendering

        {Toc()}

        `IGameScene`は`LoadAsync / ConfigureRendering / FixedUpdate / Update / UnloadAsync`だけを実装します。起動時は`IGameSceneBootstrap`が`Push`をenqueueし、`Replace`、`Remove`、state変更もframe boundaryでcommitされます。`Running`はupdateとrender、`Paused`はrenderのみ、`Sleeping`はload状態を保ったまま両方から外れます。固定更新では可変dtを読まず、`FixedUpdateContext.FixedDeltaSeconds`だけを使います。

        scene-owned `IRenderFeature`は`RenderFeatureAssignmentBuilder.Register(Set, features)`でSetへ割り当て、`AddPasses`でRenderGraph passだけを宣言します。CadenceはHostの`ConfigureRendering`または`UseStandardCadences`で構成し、Feature自身はHz、Set、Cadenceを知りません。Set内Feature順は契約ではなく、GPU pass順はRenderGraphのresource/control dependencyだけで決まります。
        """;
}
