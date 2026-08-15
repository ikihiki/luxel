using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story]
    public static StoryResult Adr0021(StoryContext ctx) => $$"""
        # ADR-0021 — game sceneとrender executionを分離する

        {{Toc()}}

        - **Status**: Accepted
        - **Date**: 2026-08-14
        - **Deciders**: ikihiki
        - **Learn**: [Framework学習ガイド](story:Learn/Framework/Overview)

        ## Context

        旧`GameScene`はscene lifecycleだけでなく、frame waiter、input、fixed/variable update、RenderGraph生成、command recording、submit、resource/audio pumpまで所有していました。派生sceneが`OnRender`内で別のRenderGraphとsubmitを作れるため、同じframeで複数graph・複数submitが発生し、UI、World、post-process、presentationの組合せと失敗境界がsceneごとに異なっていました。

        Feature登録順やscene slot順をGPU correctnessへ利用すると、module登録やDI列挙の変更が暗黙のpipeline変更になります。dirty UIや固定Hz描画をFeature型へ埋め込むと、同じ描画処理を別scheduleで再利用できません。

        ## Decision

        Hostは一つの`IGameLoop`だけを起動します。標準`GameLoop`が一つの`RenderOpportunity`を取得し、input、fixed update、variable update、scene command commit、immutable render snapshot、cadence execution、pumpを順に統括します。`GameSceneSystem`は`IGameScene`のload/unload、Running/Paused/Sleeping state、frame-boundary command、Set assignment snapshotだけを所有します。

        `IRenderFeature`は`AddPasses(RenderFeatureContext)`でpassを宣言するだけです。FeatureはSet、Cadence、Hz、submit、presentを知りません。外部assignmentがFeatureを`RenderFeatureSetId`へreference identityでunionし、Cadence configurationがschedule、runner、unordered Set membershipを定義します。Set間のglobal orderは収集順だけを与え、Set内Feature順とGPU pass順の契約にはしません。

        GPU pass順はRenderGraphのresource version dependencyとexplicit control dependencyからstable topological sortで算出します。external outputは`Export`、resource outputを持たない副作用はside-effect markerでlive rootにします。通常描画runnerは一opportunityにつき最大一graph・一submitを所有します。

        presentationは初回実装ではdirect 1:1です。normal submission成功後の`AfterSuccess`でtargetをacquireし、presentation graphをsubmitし、GPU completion後に`PresentAsync`します。Presentation Setのgenerationはpresent成功後だけcommitし、失敗時はpendingを保持して次opportunityで再試行します。

        Set compositionの意味的正しさはapplication/userが所有します。RenderSystemはrelease buildでrequired output、runner domain、unknown Set、invalid Hz等を事前検証しません。RenderGraphはDAG構築に不可欠なforeign handle、missing/multiple producer、unknown predecessor/control target、cycleだけをcompile errorにします。

        legacy `IScene`、`GameScene`、`SceneManager`、`StartupScene`、`AddScene<T>()`はcaller移行後にcompatibility shimなしで削除します。

        ## Alternatives

        - **`GameScene`のvirtual hookを増やす** — loop、render composition、submit ownershipの集中を温存するため却下
        - **FeatureがCadenceやSetを自己宣言する** —同じFeatureをdirty/10Hz/毎frameで再利用できず、application compositionを隠すため却下
        - **Feature priority / Before / AfterでGPU順を決める** — resource correctnessを登録metadataへ分散させるため却下
        - **CadenceごとのSet orderまたはSet order DAG** — global compositionとGPU dependency graphが二重になるため却下
        - **旧scene API wrapperを残す** —旧loopと新loopのownershipが併存し、一frame一submit契約を保証できないため却下

        ## Consequences

        - ✅ Host、simulation、scene lifecycle、render scheduling、GPU execution、presentationの所有者が明確になる
        - ✅ dirty、fixed-rate、manual、AfterSuccessをFeature実装から独立して構成できる
        - ✅ Feature登録順やscene slot順を変えても、GPU correctnessはRenderGraph dependencyで維持される
        - ✅ normal renderとpresentationが独立したtransaction/generation commit境界を持つ
        - ✅ Set contractをcoreへ固定せず、application固有pipelineを構成できる
        - ⚠️ 全runtime scene、Gallery、Player、capstone sampleを同時に移行するbreaking changeになる
        - ⚠️ Feature/applicationはstable symbolic resource versionsとcontrol keysを共有する必要がある
        - ⚠️ direct modeはcompletionを同iterationで待つ。複数presentation frame、render thread、multi-queueは将来変更とする
        """;
}
