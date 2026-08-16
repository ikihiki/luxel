using Luxel.Controls;
using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Framework")]
public static class LearnFramework
{
    private static readonly string[] Routes =
    [
        "Learn/Framework/Overview", "Learn/Framework/Timing", "Learn/Framework/Scenes",
        "Learn/Framework/Rendering", "Learn/Framework/ResourcesAndServices",
        "Learn/Framework/DiagnosticsAndTesting",
    ];

    private static DocMarkdown Meta(string path, string prerequisites)
    {
        int index = Array.IndexOf(Routes, path);
        string previous = index > 0 ? $"**前へ:** [{Routes[index - 1].Split('/')[^1]}](story:{Routes[index - 1]})" : "";
        string next = index >= 0 && index + 1 < Routes.Length ? $"**次:** [{Routes[index + 1].Split('/')[^1]}](story:{Routes[index + 1]})" : "";
        return new DocMarkdown($"**難易度:** 中級　 **環境:** Native　 **前提:** {prerequisites}\n\n{previous}{(previous.Length > 0 && next.Length > 0 ? "　 " : "")}{next}");
    }

    [Story]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Framework学習ガイド

        {Toc()}

        {Meta("Learn/Framework/Overview", "UI、Input、Graphicsの概要")}

        `Luxel.Framework.Game`は、タイミング、入力、シミュレーション、Scene、描画、Resource、Audioを一つのアプリケーションループへ接続します。各サブシステムの実装を置き換えず、いつ更新し、どの順番で結果を公開するかを統括します。

        ## 一フレームの流れ

        ```text
        input → fixed update 0..N回 → update 1回 → scene command commit
              → immutable render snapshot → render graph → submit / present
        ```

        最初はGPUを必要としない[固定更新](story:Learn/Framework/Timing)で時間の扱いを確認し、その後にSceneと描画を接続してください。完成形は[Framework app](story:Examples/Apps/Framework/App)で確認できます。

        ## 学習順

        1. [固定更新とフェーズ](story:Learn/Framework/Timing)
        2. [Sceneの寿命](story:Learn/Framework/Scenes)
        3. [描画の接続](story:Learn/Framework/Rendering)
        4. [Resourceとサービス](story:Learn/Framework/ResourcesAndServices)
        5. [診断とテスト](story:Learn/Framework/DiagnosticsAndTesting)
        """;

    [Story]
    public static StoryResult Timing(StoryContext ctx) => $"""
        # 固定更新とフレームフェーズ

        {Toc()}

        {Meta("Learn/Framework/Timing", "Framework学習ガイド")}

        可変のフレーム時間は`FixedTimestep.Advance`へ蓄積し、返された回数だけ決定的なシミュレーションを進めます。描画は`Alpha`を使い、直前と現在の状態を補間できます。

        ## 固定更新で守ること

        - シミュレーションは`FixedUpdateContext.FixedDeltaSeconds`だけを使う
        - 描画フレームの経過時間を物理計算へ直接渡さない
        - 一度に処理する更新回数へ上限を設ける
        - 上限を超えた場合は`DroppedSteps`を診断へ記録する

        補間の動作は[DrawInterpolation](story:Examples/Framework/DrawInterpolation)で確認できます。

        > [!WARNING]
        > フレーム低下時に無制限で固定更新を追いつかせると、更新に時間を使い続けて描画へ戻れなくなります。上限超過を隠さず、負荷の診断材料として扱ってください。
        """;

    [Story]
    public static StoryResult Scenes(StoryContext ctx) => $"""
        # Sceneの寿命と遷移

        {Toc()}

        {Meta("Learn/Framework/Scenes", "固定更新とフレームフェーズ")}

        `IGameScene`は`LoadAsync`、`ConfigureRendering`、`FixedUpdate`、`Update`、`UnloadAsync`を実装します。起動時のSceneは`IGameSceneBootstrap`が追加し、置換、削除、状態変更はフレーム境界でcommitされます。

        ## Scene状態

        | 状態 | Update | Render | 読み込み状態 |
        |---|---:|---:|---|
        | Running | する | する | 保持 |
        | Paused | しない | する | 保持 |
        | Sleeping | しない | しない | 保持 |
        | Removed | しない | しない | 解放へ進む |

        Scene遷移を更新処理の途中で直接反映すると、同じフレーム内で列挙対象が変わります。commandとして予約し、決められた境界でまとめて反映してください。

        ## 所有権

        Sceneが読み込んだResource lease、入力Context、描画FeatureはSceneの寿命に合わせます。`UnloadAsync`完了前に外部サービスを破棄せず、逆に削除済みSceneの購読を残さないようにします。
        """;

    [Story]
    public static StoryResult Rendering(StoryContext ctx) => $"""
        # Sceneと描画を接続する

        {Toc()}

        {Meta("Learn/Framework/Rendering", "Sceneの寿命、RenderGraphの概要")}

        Scene所有の`IRenderFeature`は、`RenderFeatureAssignmentBuilder.Register`でSetへ割り当てます。Featureは`AddPasses`でRenderGraphのpassだけを宣言し、GPUへのsubmitやpresentを直接行いません。

        ## 責務の境界

        - Sceneは表示する世界とFeatureの寿命を所有する
        - Featureは必要なpassとResource依存を宣言する
        - RenderGraphはpass順、barrier、一時Resourceを決定する
        - Hostはcadence、submit、presentを担当する

        Set内の登録順を描画順として利用しないでください。GPU passの順序はResource依存または明示したcontrol dependencyで決まります。

        詳細は[RenderGraph](story:Learn/Graphics/RenderGraph/Overview)へ進んでください。
        """;

    [Story]
    public static StoryResult ResourcesAndServices(StoryContext ctx) => $"""
        # Resource、Audio、サービスを接続する

        {Toc()}

        {Meta("Learn/Framework/ResourcesAndServices", "Sceneと描画")}

        Frameworkは各ライブラリの所有権を奪わず、アプリケーションループへ進行点を提供します。Resourceの公開とretirement、Audio queue、Scene commandは、それぞれ定められたフレーム境界で進めます。

        ## Composition rootで決めること

        - NativeまたはBrowserのPlatform実装
        - GraphicsとAudioのバックエンド
        - Resource source、step、manager、execution domain
        - 最初のSceneとアプリケーションサービス

        製品ライブラリの中でOS固有バックエンドをnewせず、ホストから注入します。これによりHeadlessテストではFake入力やGPU不要の構成へ置き換えられます。

        Resourceの構成は[Resources](story:Learn/Resources/Overview)、Audioの構成は[Audio](story:Learn/Audio/Overview)を参照してください。
        """;

    [Story]
    public static StoryResult DiagnosticsAndTesting(StoryContext ctx) => $"""
        # Frameworkを診断してテストする

        {Toc()}

        {Meta("Learn/Framework/DiagnosticsAndTesting", "Resourceとサービス")}

        実行ループの問題は、CPU時間だけでなく、固定更新の遅延、Scene command、Resource公開、render snapshot、GPU submitを同じフレーム番号で追えるようにします。

        | 症状 | 最初に確認する値 |
        |---|---|
        | 動きが飛ぶ | fixed step回数、DroppedSteps、補間Alpha |
        | Sceneが切り替わらない | command queueとcommit境界 |
        | 描画が一フレーム古い | snapshot生成とFeature assignment |
        | 終了時に例外が出る | Scene、ResourceSystem、deviceの破棄順 |

        ## 決定的テスト

        Headlessテストでは固定dt、Fake入力、明示的なstep回数を使います。wall-clock、`Task.Delay`、実デバイス入力へ依存させません。Galleryのplayやsnapshotを使う場合も、Storyの表示内容ではなく操作後の安定した契約を検証します。

        次は[ECS](story:Learn/ECS/Overview)でシミュレーション構造を学ぶか、[Player](story:Examples/Apps/Player/Basic)でアプリケーションへの組み込みを確認してください。
        """;
}
