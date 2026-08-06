using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnDomainSystems
{
    [Story("Learn/AnimationParticles/Overview", Order = 0, SampleBundle = "game.cavern", Toc = true)]
    public static StoryResult AnimationOverview(StoryContext ctx) => $$"""
        # Animation and particles overview

        値補間はTween、状態遷移はStateMachine、骨格animationはclip/graph、短命なvisual eventはparticle emitterへ分けます。simulation値と描画補間値を混同しません。

        {{StoryRef(ctx, "Examples/Animation/Tween")}}
        {{StoryRef(ctx, "Examples/2D/Particles")}}
        {{SampleBundle("game.cavern")}}
        """;

    [Story("Learn/AnimationParticles/GraphsAndEmitters", Order = 1, Toc = true)]
    public static StoryResult AnimationGraphs(StoryContext ctx) => $$"""
        # Graphs, clips, and emitters

        clipは時間からpose/valueを評価し、graphはblendとtransitionを決めます。particle emitterはspawn policy、lifetime、velocity、color/size curveを持ち、2D/3D backendへinstanceを渡します。

        {{StoryRef(ctx, "Examples/Animation/Graph")}}
        {{StoryRef(ctx, "Examples/3D/Particles")}}
        """;

    [Story("Learn/Scripting/Overview", Order = 0, SampleBundle = "scripting.gallery", Toc = true)]
    public static StoryResult ScriptingOverview(StoryContext ctx) => $$"""
        # Scripting overview

        `ScriptHost`はC# scriptをcompileし、`ScriptGlobals`で許可したhost capabilityだけを公開します。REPL、notebook、file hot reloadは同じcompile/diagnostic/cancellation境界を共有します。

        {{StoryRef(ctx, "Examples/Scripting/LiveCsx")}}
        {{SampleBundle("scripting.gallery")}}
        """;

    [Story("Learn/Scripting/ReloadAndIsolation", Order = 1, Toc = true)]
    public static StoryResult ScriptingReload(StoryContext ctx) => $$"""
        # Reload, diagnostics, and isolation

        compile errorは前の実行可能instanceを保持し、成功時だけswapします。長時間処理にはcancellation tokenを渡し、script assemblyが保持するeventやEffectをreload時にdisposeします。

        {{StoryRef(ctx, "Examples/Scripting/HotReload")}}
        {{StoryRef(ctx, "Examples/Scripting/Notebook")}}
        """;
}

public static class LearnProductionWorkflow
{
    [Story("Learn/Production/StudioToPlayer", Order = 0, Toc = true)]
    public static StoryResult StudioToPlayer(StoryContext ctx) => $$"""
        # Studio to Player workflow

        Studioでproject/sceneを編集し、PlayerのPlay-in-Editorで同じdataを実行し、standalone hostへ渡します。editor-only stateをscene dataへ混ぜず、保存前にruntime schemaへ変換します。

        {{StoryRef(ctx, "Apps/Studio/Shell")}}
        {{StoryRef(ctx, "Apps/Player/PlayInEditor")}}
        """;

    [Story("Learn/Production/Workbench", Order = 1, Toc = true)]
    public static StoryResult Workbench(StoryContext ctx) => $$"""
        # Workbench workflow

        Workbenchはcode、files、material、inspectorを同じdock shellで扱います。変更はresource/script reload境界へ送り、編集中のdocument stateと実行中runtime stateを分離します。

        {{StoryRef(ctx, "Examples/Workbench/Shell")}}
        {{StoryRef(ctx, "Examples/Workbench/Files")}}
        {{StoryRef(ctx, "Examples/Workbench/Material")}}
        """;

    [Story("Learn/Production/ValidateAndShip", Order = 2, Toc = true)]
    public static StoryResult Ship(StoryContext ctx) => $$"""
        # Validate and ship

        Gallery play/golden、headless logic smoke、GPU one-frame smoke、別cwd publish smokeを順に通します。assets、shader cache、font licenseをoutputへ含め、machine固有のabsolute pathへ依存しないことを確認します。

        {{StoryRef(ctx, "Game/Cavern")}}
        {{StoryRef(ctx, "Apps/Game/Range")}}
        """;
}
