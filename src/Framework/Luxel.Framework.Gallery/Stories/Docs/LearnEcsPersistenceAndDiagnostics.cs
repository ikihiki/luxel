using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>ECS state の保存境界と DevTools 向け diagnostics。</summary>
public static class LearnEcsPersistenceAndDiagnostics
{
    [Story("Learn/ECS/Persistence", Order = 7, Toc = true)]
    public static StoryResult Persistence(StoryContext ctx) => $$"""
        # Persistence

        {{EcsCourseCatalog.Meta("Learn/ECS/Persistence", "Intermediate", "Headless", "CPU / JSON", "ComponentsAndTags")}}

        `WorldSave` は Friflo `EntitySerializer` を使い、World の純データ component を version 付き JSON に変換します。ファイル I/O は担当せず、文字列の入出力に限定されています。

        ## 保存と復元

        ```csharp
        string json = WorldSave.Serialize(world);

        using var restored = new World();
        WorldSave.Deserialize(restored, json);
        ```

        形式は `{ "version": 1, "entities": [...] }` です。既定の `clear: true` は既存 entity を全削除してから復元します。

        ## 既存 World へ merge する

        ```csharp
        WorldSave.Deserialize(world, json, clear: false);
        ```

        `clear: false` は persistent id を key に upsert します。同じ id は上書き、新しい id は追加されます。意図しない重複や stale entity を避けるには、通常ロードでは既定の clear を使います。

        ## 保存しないもの

        `DebugName` は `[ComponentKey(null)]` により除外されます。同様に GPU handle、Physics の runtime handle、event subscription などは保存せず、ロード後に runtime system が再発行します。

        ```csharp
        WorldSave.Deserialize(world, json);
        RebuildRuntimeResources(world);
        TransformPropagateSystem.Run(world);
        ```

        ## Version と migration

        `WorldSave.CurrentVersion` は現在1です。loader は version を読み取りますが、v1 には migration chain がまだありません。schema を変えるときは古い JSON を新形式へ変換する処理を `Deserialize` の前段へ追加してください。
        """;

    [Story("Learn/ECS/Diagnostics", Order = 8, Toc = true)]
    public static StoryResult Diagnostics(StoryContext ctx) => $$"""
        # Diagnostics

        {{EcsCourseCatalog.Meta("Learn/ECS/Diagnostics", "Intermediate", "Headless + DevTools", "CPU", "Queries、SystemsAndPhases")}}

        diagnostics は simulation を変えずに状態を観測する境界です。`EcsDiagnostics` は一覧用の軽量 summary と、選択 entity 用の detail を分け、大きな World の全 component JSON 化を避けます。

        ## 一覧を作る

        ```csharp
        IReadOnlyList<World> worlds = [world];
        DiagEcsSummary summary = EcsDiagnostics.BuildSummary(worlds, "Player");
        ```

        filter は `DebugName`、entity id、component/tag 型名に対する大小文字を無視した部分一致です。summary には component 値を含めません。

        ## 選択 entity の詳細を作る

        ```csharp
        DiagEcs detail = EcsDiagnostics.BuildDetail(
            worlds,
            selWorld: 0,
            selEntity: selectedEntity.Id);
        ```

        選択が無い場合、World の entity 数が `FullFallbackThreshold`（64）以下なら全 detail を返し、それより多ければ空にして選択を要求します。

        ## System timing

        ```csharp
        world.EnableSystemPerfMonitor();
        world.RunPhase(Phase.Update, tick);
        var timings = world.CollectSystemTimings(Phase.Update);
        ```

        performance monitor は観測が必要なときだけ有効化します。component の JSON 化に失敗した場合も diagnostics 全体は止めず、失敗した型名を値として表示します。

        ## デバッグの順序

        1. summary で対象 entity と archetype を絞る。
        2. detail で component 値を確認する。
        3. timing で重い system と phase を確認する。
        4. transform 問題なら `LocalTransform` と `GlobalTransform` を比較する。

        次からは ECS と Physics の接続を扱います。
        """;
}
