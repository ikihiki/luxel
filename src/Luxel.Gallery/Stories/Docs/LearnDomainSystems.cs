using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnDomainSystems
{
    [Story("Learn/Assets/Overview", Order = 0, SampleBundle = "game.range")]
    public static Widget AssetsOverview(StoryContext ctx) => DocNew(ctx, $$"""
        # Assets and glTF overview

        asset pathは`ResourceSystem → GltfLoader → CPU scene → AssetsGpu / AssetRuntime → render extraction`です。まず静的boxを確認し、次にskin、morph、animationへ進みます。

        {{StoryRef(ctx, "Examples/3D/GltfBox")}}
        {{SampleBundle("game.range")}}
        """, toc: true);

    [Story("Learn/Assets/GltfRuntime", Order = 1)]
    public static Widget GltfRuntime(StoryContext ctx) => DocNew(ctx, $$"""
        # glTF runtime ownership

        glTFのbuffer/image/node/skin/animationはCPU decode結果です。GPU uploadとruntime instanceは別所有者にし、resource handleの寿命より先にbindless bufferやtextureを破棄しません。

        {{StoryRef(ctx, "Examples/3D/GltfSkinned")}}
        {{StoryRef(ctx, "Examples/3D/GltfMorph")}}
        """, toc: true);

    [Story("Learn/ECSPhysics/Overview", Order = 0, SampleBundle = "game.range")]
    public static Widget EcsOverview(StoryContext ctx) => DocNew(ctx, $$"""
        # ECS and physics overview

        `World`はcomponent data、systemはphaseごとの処理です。physics worldとECS entityの対応を一箇所で管理し、固定stepでsimulation、Update/Renderで結果抽出を行います。

        {{StoryRef(ctx, "Examples/3D/PhysicsFalling")}}
        {{SampleBundle("game.range")}}
        """, toc: true);

    [Story("Learn/ECSPhysics/CollisionsAndGizmos", Order = 1)]
    public static Widget Physics(StoryContext ctx) => DocNew(ctx, $$"""
        # Collisions, triggers, and gizmos

        collision responseとtrigger eventを分離し、mesh colliderは静的geometryへ限定します。まずprimitive falling、次にtrigger、最後にmeshとgizmoでshape/normal/contactを可視化します。

        {{StoryRef(ctx, "Examples/3D/PhysicsTrigger")}}
        {{StoryRef(ctx, "Examples/3D/PhysicsMesh")}}
        {{StoryRef(ctx, "Examples/3D/PhysicsGizmos")}}
        """, toc: true);

    [Story("Learn/AnimationParticles/Overview", Order = 0, SampleBundle = "game.cavern")]
    public static Widget AnimationOverview(StoryContext ctx) => DocNew(ctx, $$"""
        # Animation and particles overview

        値補間はTween、状態遷移はStateMachine、骨格animationはclip/graph、短命なvisual eventはparticle emitterへ分けます。simulation値と描画補間値を混同しません。

        {{StoryRef(ctx, "Examples/Animation/Tween")}}
        {{StoryRef(ctx, "Examples/2D/Particles")}}
        {{SampleBundle("game.cavern")}}
        """, toc: true);

    [Story("Learn/AnimationParticles/GraphsAndEmitters", Order = 1)]
    public static Widget AnimationGraphs(StoryContext ctx) => DocNew(ctx, $$"""
        # Graphs, clips, and emitters

        clipは時間からpose/valueを評価し、graphはblendとtransitionを決めます。particle emitterはspawn policy、lifetime、velocity、color/size curveを持ち、2D/3D backendへinstanceを渡します。

        {{StoryRef(ctx, "Examples/Animation/Graph")}}
        {{StoryRef(ctx, "Examples/3D/Particles")}}
        """, toc: true);

    [Story("Learn/Scripting/Overview", Order = 0, SampleBundle = "scripting.gallery")]
    public static Widget ScriptingOverview(StoryContext ctx) => DocNew(ctx, $$"""
        # Scripting overview

        `ScriptHost`はC# scriptをcompileし、`ScriptGlobals`で許可したhost capabilityだけを公開します。REPL、notebook、file hot reloadは同じcompile/diagnostic/cancellation境界を共有します。

        {{StoryRef(ctx, "Examples/Scripting/LiveCsx")}}
        {{SampleBundle("scripting.gallery")}}
        """, toc: true);

    [Story("Learn/Scripting/ReloadAndIsolation", Order = 1)]
    public static Widget ScriptingReload(StoryContext ctx) => DocNew(ctx, $$"""
        # Reload, diagnostics, and isolation

        compile errorは前の実行可能instanceを保持し、成功時だけswapします。長時間処理にはcancellation tokenを渡し、script assemblyが保持するeventやEffectをreload時にdisposeします。

        {{StoryRef(ctx, "Examples/Scripting/HotReload")}}
        {{StoryRef(ctx, "Examples/Scripting/Notebook")}}
        """, toc: true);
}

public static class LearnProductionWorkflow
{
    [Story("Learn/Production/StudioToPlayer", Order = 0)]
    public static Widget StudioToPlayer(StoryContext ctx) => DocNew(ctx, $$"""
        # Studio to Player workflow

        Studioでproject/sceneを編集し、PlayerのPlay-in-Editorで同じdataを実行し、standalone hostへ渡します。editor-only stateをscene dataへ混ぜず、保存前にruntime schemaへ変換します。

        {{StoryRef(ctx, "Apps/Studio/Shell")}}
        {{StoryRef(ctx, "Apps/Player/PlayInEditor")}}
        """, toc: true);

    [Story("Learn/Production/Workbench", Order = 1)]
    public static Widget Workbench(StoryContext ctx) => DocNew(ctx, $$"""
        # Workbench workflow

        Workbenchはcode、files、material、inspectorを同じdock shellで扱います。変更はresource/script reload境界へ送り、編集中のdocument stateと実行中runtime stateを分離します。

        {{StoryRef(ctx, "Examples/Workbench/Shell")}}
        {{StoryRef(ctx, "Examples/Workbench/Files")}}
        {{StoryRef(ctx, "Examples/Workbench/Material")}}
        """, toc: true);

    [Story("Learn/Production/ValidateAndShip", Order = 2)]
    public static Widget Ship(StoryContext ctx) => DocNew(ctx, $$"""
        # Validate and ship

        Gallery play/golden、headless logic smoke、GPU one-frame smoke、別cwd publish smokeを順に通します。assets、shader cache、font licenseをoutputへ含め、machine固有のabsolute pathへ依存しないことを確認します。

        {{StoryRef(ctx, "Game/Cavern")}}
        {{StoryRef(ctx, "Apps/Game/Range")}}
        """, toc: true);
}
