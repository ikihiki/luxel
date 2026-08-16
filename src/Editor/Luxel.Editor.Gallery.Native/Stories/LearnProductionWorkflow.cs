using Luxel.UI;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Production")]
public static class LearnProductionWorkflow

{
    [Story]
    public static StoryResult StudioToPlayer(StoryContext ctx) => $$"""
        # Studio to Player workflow

        {{Toc()}}

        Studioでproject/sceneを編集し、PlayerのPlay-in-Editorで同じdataを実行し、standalone hostへ渡します。editor-only stateをscene dataへ混ぜず、保存前にruntime schemaへ変換します。

        [Studio Shell](story:Examples/Apps/Studio/Shell)
        [Play-in-Editor](story:Examples/Apps/Player/PlayInEditor)
        """;

    [Story]
    public static StoryResult Workbench(StoryContext ctx) => $$"""
        # Workbench workflow

        {{Toc()}}

        Workbenchはcode、files、material、inspectorを同じdock shellで扱います。変更はresource/script reload境界へ送り、編集中のdocument stateと実行中runtime stateを分離します。

        [Workbench Shell](story:Examples/Workbench/Shell)
        [Workbench Files](story:Examples/Workbench/Files)
        [Workbench Material](story:Examples/Workbench/Material)
        """;

    [Story]
    public static StoryResult Ship(StoryContext ctx) => $$"""
        # Validate and ship

        {{Toc()}}

        Gallery play/golden、headless logic smoke、GPU one-frame smoke、別cwd publish smokeを順に通します。assets、shader cache、font licenseをoutputへ含め、machine固有のabsolute pathへ依存しないことを確認します。

        [Cavern](story:Examples/Apps/Game/Cavern)
        [Range](story:Examples/Apps/Game/Range)
        """;
}
