using Luxel.Gallery.UI;

namespace Luxel.Particles.Gallery;

/// <summary>利用者向け ParticleView の構造化された日本語 Docs。</summary>
internal static class ParticleViewDocs
{
    private static readonly ControlDocsPage Page = new(
        "global::Luxel.Particles.UI.ParticleView",
        "ParticleView",
        "CPU 側の `ParticleSystem` を Retained 2D canvas の一つの `ParticleNode` へ変換し、必要なら UI animation tick ごとに simulation と描画同期を進める表示専用 Widget です。GPU particle simulator や 3D billboard renderer ではなく、UI／Gallery 内へ小規模な 2D effect を埋め込む adapter です。",
        [
            "`ParticleSystem` の 2D burst／continuous effect を通常の Widget layout、設定画面、preview、Gallery story に埋め込む場合。",
            "UI host の tick を simulation clock としてよく、一つの `ParticleView` に `Update → Sync` の所有をまとめる場合。",
            "固定 seed と制御された dt の Gallery play で、particle configuration の見た目を決定的に検証する場合。",
        ],
        [
            "ゲーム loop、ECS、fixed-step scheduler が既に同じ `ParticleSystem.Update` を所有している場合。`animated:true` と二重更新しないでください。",
            "3D particle、camera-facing billboard、depth／blend pipeline、GPU compute simulation が必要な場合。`ParticleBillboards` など ThreeD integration を使います。",
            "数千～大量の particle、offscreen culling、batching、strict fixed-step、visibility pause、interactive controls を ParticleView 自身に求める場合。",
        ],
        [
            new("Canvas2D", "particle model を使わず、少数の独自 2D shape を直接描画・animation する場合。"),
            new("GpuView", "独自 GPU renderer／texture output を UI 領域へ埋め込み、device／command ownership を呼び出し側が持つ場合。"),
            new("ParticleNode", "game loop や fixed-step owner が `ParticleSystem.Update` と `Sync` の時刻を明示的に制御する場合。"),
        ],
        """
        var particles = new ParticleSystem(config, capacity: 256, seed: 42);
        particles.Emit(new Vector3(160, 90, 0), 80);

        Widget view = ParticleView(
            particles,
            viewWidth: 320,
            viewHeight: 180,
            animated: true,
            circleSegments: 12);
        """,
        "一つの透明な `ParticleView` root と、realize ごとに一度作る `ParticleNode` から構成します。`ParticleNode` は `RetainedCanvas` の child node 一つに `ContentColors=true` を設定し、構築時の `ParticleSystem.Capacity` と shape／`circleSegments` から path／segment 容量を予約します。生存 particle は buffer 順に、一個一 path の axis-aligned quad または regular polygon として描きます。",
        "`animated:true` は realization scope に継続 animation callback を登録し、各 UI tick で `ParticleSystem.Update(dt)` の直後に `ParticleNode.Sync()` します。`animated:false` は realize 時の初回 `Sync()` だけを行う snapshot で、その後に外部で system を更新しても内部 node は自動同期されません。手動／fixed-step の反復同期が必要なら `ParticleNode` を直接所有します。`circleSegments` は circle geometry の一個あたり segment 数で、構築時に取り込まれます。",
        "emission、seed／random state、`ParticleConfig`、`Forces`、SoA `ParticleBuffer`、`Alive`／`Capacity` と simulation 時刻は渡した `ParticleSystem` が正本です。ParticleView は system を生成・clear・dispose せず、`animated:true` の間だけ UI tick の `Update` と自身の node の `Sync` を所有します。RetainedCanvas／backend resource と animation callback は Widget realization scope が所有します。同じ system を複数の animated view または外部 loop と共有すると一 tick に複数回進むため、update owner は必ず一つにします。",
        "表示専用で pointer hit、hover、drag、scroll、context menu を登録しません。再生、停止、burst、continuous emission、reset、preset／数値変更は隣接する Button、Slider、PropertyGrid 等から `ParticleSystem.Emit`、`SetEmission`、`StopEmission`、`Clear`、`Config` へ適用します。ParticleView の矩形は clipping surface ではないため、領域外へ移動した particle を自動で clip／cull しません。",
        [],
        "focus target、activation、dismissal はありません。keyboard 操作は隣接するラベル付き control が所有します。motion reduction や pause が必要なら、owner が `animated:false` の snapshot、view の取り外し、または外部 fixed-step／`ParticleNode` 構成を選びます。",
        new ControlDocsAccessibility(
            "隣接する見出しと文字で effect 名、目的、再生／停止、`Alive / Capacity`、重要な結果を示します。",
            "ParticleView は image、animation、status、progress の semantic role を公開せず、各 particle も accessibility tree に現れません。",
            "生存数、capacity overflow、emission 状態、完了、error は自動公開しません。必要な状態を別の Text、Badge、StatusBar、live status に同期します。",
            "particle の absolute color／tint と透明な背景の組み合わせを、実際の親 surface と light／dark theme の双方で確認します。色だけで種類や成功を伝えないでください。",
            "`animated:true` は UI tick ごとに位置、size、color を変えます。reduced-motion preference を自動参照しないため、呼び出し側が停止／snapshot／低 rate の代替を提供します。",
            "視覚 effect だけに重要情報を依存させず、再生制御には明示ラベル付きの通常 control を用意します。focus、screen-reader summary、pause-on-hidden、flash／motion 安全性の自動判定はありません。"),
        new ControlDocsThemeLayout(
            "ParticleView は `UiTheme` を読まず背景も描きません。particle color は `ParticleConfig.Color` と per-particle tint の absolute color で決まり、親の Border／surface が背景・frame・padding を所有します。",
            "`viewWidth` と `viewHeight` を親 constraints に通し、particle の X/Y を Widget local coordinates として描きます。Z は 2D rendering では無視され、親 transform で view 全体を配置します。root は clip を設定しないため、必要なら clip を提供する親 surface を選びます。",
            "要求寸法は各軸を最低 1 px に補正した値で、親 constraints によりさらに制約されます。finite な preview size と emission origin を明示し、領域外 particle が layout size を広げないことに注意します。"),
        new ControlDocsConstraints(
            "default simulation は CPU SoA で、`Sync()` は各生存 particle から新しい `Scene2D` path を組みます。cost は概ね Alive×（circle は `circleSegments`、quad は4）で増え、毎 sync content が再 upload されます。予約容量は capacity×構築時 segment 数です。capacity は正の固定値で、満杯を超える `Emit` は例外や eviction なしに残りを無視します。`circleSegments` は正値へ clamp／validate されないため、呼び出し側が品質と cost に合う正の値を渡します。`Update` は負／巨大 dt を検証しないため、production owner は正で上限付きの dt を渡します。",
            "`ParticleSystem` は呼び出し側が所有し、ParticleView は reset／dispose しません。animation callback と canvas node は realization scope の解放で停止・撤去されます。`animated:false` は初回 snapshot のみです。構築後に `Config.Shape` を quad から circle へ変える、capacity／shape／`circleSegments` に依存する reservation を変える場合は view／node を再作成します。同じ system の update owner を一つに保ちます。",
            "Retained 2D canvas と animation tick を提供する Browser／Native／Gallery UI host で利用できます。canvas backend が GPU upload を行う場合も、ParticleView 自身は GPU device、buffer、command encoder、blend／depth state を所有しません。3D／ECS では system を game loop で更新し、対応 backend adapter を直接使います。"),
        new ControlDocsApi(
            "ParticleView",
            "`particles`、`viewWidth`、`viewHeight`、`animated`、`circleSegments`。関連する model API は `ParticleSystem.Emit`、`SetEmission`、`StopEmission`、`Update`、`Clear`、`Config`、`Forces`、`Alive`、`Capacity`、`Buffer` です。",
            "ParticleView 固有の event／callback はありません。`animated:true` は内部で `Update(dt) → Sync()`、`false` は初回 `Sync()` だけです。再生 control、telemetry、overflow warning、completion、error、resource disposal は ParticleSystem owner／周囲の UI が通知・更新します。"),
        new ControlDocsStory("Controls/Rendering/ParticleView/Basic", "基本例", "ParticleView の canonical Basic story を実行します。", StoryKind.Basic),
        [
            new("Controls/Rendering/ParticleView/Playground", "プレイグラウンド", "生成済み `viewWidth`、`viewHeight`、`animated`、`circleSegments` の引数を確認します。", StoryKind.Playground),
            new("Learn/Animation/Particles/Rendering2DAndUI", "2D／UI rendering guide", "ParticleNode と ParticleView の update／sync、reservation、shape 変更時の再作成条件を説明します。", StoryKind.Unspecified),
            new("Learn/Animation/Particles/ParticleViewSample", "ParticleView sample", "固定 seed の burst を UI tick で進める runnable sample です。", StoryKind.Unspecified),
        ]);

    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
        => ControlDocsRenderer.Register(builder, descriptors, [Page],
            "日本語で執筆した構造化 ParticleView Docs。");
}
