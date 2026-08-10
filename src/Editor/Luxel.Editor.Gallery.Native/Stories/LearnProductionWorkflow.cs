using Luxel.UI;

namespace Luxel.Gallery.Stories;

public static class LearnProductionWorkflow

{
    [Story("Learn/Production/StudioToPlayer", Order = 0, Toc = true)]
    public static StoryResult StudioToPlayer(StoryContext ctx) => $$"""
        # Studio to Player workflow

        Studioでproject/sceneを編集し、PlayerのPlay-in-Editorで同じdataを実行し、standalone hostへ渡します。editor-only stateをscene dataへ混ぜず、保存前にruntime schemaへ変換します。

        [Studio Shell](story:Apps/Studio/Shell)
        [Play-in-Editor](story:Apps/Player/PlayInEditor)
        """;

    [Story("Learn/Production/Workbench", Order = 1, Toc = true)]
    public static StoryResult Workbench(StoryContext ctx) => $$"""
        # Workbench workflow

        Workbenchはcode、files、material、inspectorを同じdock shellで扱います。変更はresource/script reload境界へ送り、編集中のdocument stateと実行中runtime stateを分離します。

        [Workbench Shell](story:Examples/Workbench/Shell)
        [Workbench Files](story:Examples/Workbench/Files)
        [Workbench Material](story:Examples/Workbench/Material)
        """;

    [Story("Learn/Production/ValidateAndShip", Order = 2, Toc = true)]
    public static StoryResult Ship(StoryContext ctx) => $$"""
        # Validate and ship

        Gallery play/golden、headless logic smoke、GPU one-frame smoke、別cwd publish smokeを順に通します。assets、shader cache、font licenseをoutputへ含め、machine固有のabsolute pathへ依存しないことを確認します。

        [Cavern](story:Game/Cavern)
        [Range](story:Apps/Game/Range)
        """;
}
