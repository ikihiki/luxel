using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>glTF assets, ECS, and physics learning units.</summary>
public static partial class DocsGpu
{
    [Story("Reference/Guides/Assets", Order = 14, Toc = true)]
    public static StoryResult Assets(StoryContext ctx) => $$"""
        # アセットパイプライン (glTF)

        glTF 2.0 (.gltf/.glb) を読み込み、ECS + GPU バッファへ展開して描くまでのパイプラインです。4 つのプロジェクトが層を分担します:

        ```mermaid
        graph LR
        G[Luxel.Gltf<br/>glTF 2.0 ローダ] --> A[Luxel.Assets<br/>CPU アセット表現]
        A --> R[Luxel.AssetRuntime<br/>ECS 展開 + アニメ/スキニング]
        A --> P[Luxel.AssetsGpu<br/>GPU アップロード + Resources 統合]
        R --> RG[RenderGraph 描画]
        P --> RG
        ```

        - `Luxel.Assets` — 形式に依存しない CPU 表現 (`AssetDocument` / `AssetMesh` / `AssetMaterial` / `AssetAnimation` / `AssetSkin`)。**Resources に依存しない**純データ層
        - `Luxel.Gltf` — `GltfLoader` が glTF/glb を `AssetDocument` へ。外部 .bin/画像は Resources 経由でも単体でも読める
        - `Luxel.AssetRuntime` — `SceneBuilder` が AssetDocument を **ECS entity + GPU バッファ**へ展開。`SceneAnimationPlayer` / `TransformPropagateSystem` / `SkinningSystem` が毎フレーム駆動
        - `Luxel.AssetsGpu` — `RenderBuffer<T>` / `AssetGpuRegistry` / Resources Step 群 (URI ロード + キャッシュ + 依存解決)

        ## 最小経路: ロードして描く

        ```csharp
        AssetDocument doc = new GltfLoader().LoadAsync("Box.gltf").GetAwaiter().GetResult();
        var world = new Luxel.Ecs.World();
        using SceneAssets assets = SceneBuilder.Build(world, doc, device);   // ECS + GPU バッファ
        TransformPropagateSystem.Run(world);

        using var extractor = new SceneRenderExtractor(world, assets);
        extractor.Extract(new ExtractContext(device, frameIndex: 0));
        // extractor.DrawList の (Primitive, InstanceStart, InstanceCount) ごとに
        // assets.Primitives[prim] の vertex/index バッファを bindless で Draw する
        ```

        シェーダは `scene_pbr_lite` (頂点 32B: pos+normal+uv、インスタンス 80B: world+baseColor)。`DrawIndexed` は無いので **index バッファもシェーダが bindless で読み**、`indexBufIndex = 0xFFFFFFFF` で non-indexed に切り替えます。

        {{StoryRef(ctx, "Examples/3D/GltfBox")}}

        ## アニメーション

        ノード TRS アニメーションは `SceneAnimationPlayer` が時刻 t の値を sample して entity の `LocalTransform` に書き、`TransformPropagateSystem` で伝播 → 再 Extract で instance バッファが更新されます:

        ```csharp
        var player = new SceneAnimationPlayer(world, assets, doc.Animations[0]);
        // 毎フレーム: 周期は t % Duration (snap の決定性のため wall-clock は使わない)
        player.Sample(time % doc.Animations[0].Duration);
        TransformPropagateSystem.Run(world);
        extractor.Extract(new ExtractContext(device, frameIndex: frame++));
        ```

        {{StoryRef(ctx, "Examples/3D/GltfAnimated")}}

        > [!WARNING]
        > morph target (Weights チャンネル) は未対応です — `SceneAnimationPlayer` は skip します。スキニングは `SkinningSystem` + `scene_pbr_skinned` シェーダ (頂点 56B: joints/weights 付き) が担います。

        ## ResourceSystem 統合

        実アプリでは URI ロード + キャッシュ + 依存解決を Resources に任せます。AssetsGpu の Step 群を登録すると `Load<T>` の型で変換チェーンが解決されます:

        ```csharp
        var handle = resources.Load<SceneAssets>("file:///model.glb");        // glb → doc → ECS+GPU
        var vbuf = resources.Load<GpuBuffer>("file:///model.glb#mesh/0/vertex");   // fragment URI で部分ロード
        ```

        `#mesh/N/vertex` / `#materials` のような **fragment URI** で primitive 単位の遅延ロードができます。Resources 自体の概念 (RefCount / Republish / Pump) は [Reference/Guides/Resources](story:Reference/Guides/Resources) へ。

        ## DRAW-M3: コンポーネント駆動の描画

        entity に `DrawMesh` + `DrawInstance` + `DrawMaterial` (+ `DrawSkinning`) を付けると、`DrawableCollector.Collect(world, rg)` が RenderGraph への import 済みハンドル束 (`DrawItem`) を返し、呼び出し側は for-loop で push constant を組んで Draw するだけになります。テクスチャ付きは `scene_pbr_tex` (material 32B 配列 + bindless texture)、スキニングは `scene_pbr_skinned` が対になります。

        ## 設計ノート

        - **CPU アセット層は Resources 非依存** — ツール/テストが GPU なしで AssetDocument を扱えます。GPU 化と URI 解決は AssetsGpu 側の Step が担う分離です
        - **direct-ref モデル** — index ベースの DOM ではなく `AssetMeshRef.Mesh` のような直接参照で ECS から引きます (中間テーブル無し)
        - サンプルモデルはリポジトリの `tools/khronos-samples/` (Khronos 公式 glTF テストスイート) にあります

        型 API は [Luxel.Assets](story:Reference/Luxel.Assets) / [Luxel.AssetsGpu](story:Reference/Luxel.AssetsGpu) / [Luxel.AssetRuntime](story:Reference/Luxel.AssetRuntime) / [Luxel.Gltf](story:Reference/Luxel.Gltf) へ。
        """;

    [Story("Reference/Guides/Ecs", Order = 15, Toc = true)]
    public static StoryResult Ecs(StoryContext ctx) => $$"""
        # ECS (Luxel.Ecs)

        Friflo Engine ECS の薄いラッパです。3D シーン ([Reference/Guides/ThreeD](story:Reference/Guides/ThreeD)) とアセット展開 ([Reference/Guides/Assets](story:Reference/Guides/Assets)) の土台ですが、単体でも使えます。高度な操作は `world.Store` (生の Friflo `EntityStore`) を直接触ってかまいません。

        ## Entity とクエリ

        ```csharp
        using var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new LocalTransform(matrix), new Color3D(color), new MeshRef(MeshRef.Cube));

        world.Query<LocalTransform, Color3D>()
             .ForEachEntity((ref LocalTransform t, ref Color3D c, Entity entity) => { /* archetype 直走査 */ });
        foreach (var (entity, t) in world.QueryEnumerable<LocalTransform>()) { /* LINQ 風の遅い版 */ }
        ```

        `Set/Get/Has/TryGet` の単発アクセサもあります。struct component + archetype 走査という Friflo の性質はそのまま — **ref で書き換える**のが基本です。

        ## 標準コンポーネントとタグ

        - `LocalTransform` / `GlobalTransform` — ローカル/ワールド行列。親子は `Parent` コンポーネント
        - `Color3D` / `MeshRef` / `Visible` — 描画用 (抽出側が読む)
        - タグ: `Enabled` / `Selected` / `Dirty` (データなしのマーカ)

        `GlobalTransform` は自動では更新されません — `TransformPropagateSystem.Run(world)` (Luxel.AssetRuntime) が `Parent` をたどって伝播します。

        ## システムと Phase

        フレーム位相は規約名 `Phase.PreUpdate / Update / PostUpdate / PreRender / Render` で分けます:

        ```csharp
        world.AddSystem(Phase.Update, () => { /* DelegateSystem — lambda で足せる */ });
        world.RunPhase(Phase.Update, new UpdateTick { deltaTime = 1f / 60 });
        ```

        `ScheduleRoot.Create(world)` は 5 phase の SystemGroup を内蔵した Friflo `SystemRoot` を作ります。PreRender = 抽出 (IRenderExtractor)、Render = RenderGraph 実行、という接続が規約です。`EnableSystemPerfMonitor()` + `CollectSystemTimings(phase)` で system 単位の実行時間も取れます (DevTools の表示元)。

        ## Signal 連携 (Luxel.Ecs.Signal)

        UI の Signal と ECS の橋渡しは本体から分離されています (Luxel.Ecs は Friflo 以外に依存しない):

        ```csharp
        using Luxel.Ecs.Signal;
        Signal<Color3D> sig = world.Signal<Color3D>(entity);   // 同じ (entity, T) は同一インスタンス
        sig.Value = new Color3D(newColor);   // Friflo の変更通知で UI 側の監視者にも伝わる
        ```

        UI から entity を編集する例は [Examples/Animation/EcsClip](story:Examples/Animation/EcsClip) にあります。

        ## 設計ノート

        - **UI には ECS を使いません** — UI は signals + 保持型ツリー ([Reference/Guides/UI](story:Reference/Guides/UI))。3D 側だけ ECS です (理由は [Reference/Guides/ThreeD](story:Reference/Guides/ThreeD) の設計ノート)
        - ラッパを薄く保つのは、Friflo の archetype API (`ArchetypeQuery` / `ForEachEntity`) がそのまま最速経路だからです。Luxel が足すのは Phase 規約・DelegateSystem・Signal ブリッジ・perf 収集だけ

        物理 (剛体/衝突) を entity に付けるには [Reference/Guides/Physics](story:Reference/Guides/Physics) へ。型 API は [Luxel.Ecs](story:Reference/Luxel.Ecs) / [Luxel.Ecs.Signal](story:Reference/Luxel.Ecs.Signal) へ。
        """;

    [Story("Reference/Guides/Physics", Order = 16, Toc = true)]
    public static StoryResult Physics(StoryContext ctx) => $$"""
        # 物理 (Luxel.Physics)

        BepuPhysics v2 (pure C#) を薄く包んだ 3D 剛体物理です。Bepu の boilerplate (Simulation.Create の callbacks 構造体、BufferPool 管理) は `PhysicsWorld` が隠蔽し、ECS 側はコンポーネントを付けるだけで動きます。

        ## PhysicsWorld と固定タイムステップ

        ```csharp
        using var physics = new PhysicsWorld();          // 既定: 重力 -9.81、1/60s、単スレッド
        physics.Step(dt);                                // 経過秒を accumulator が固定ステップへ分割
        ```

        `Step(elapsed)` は内部の accumulator に積み、**固定 1/60 秒**で刻めるだけ刻みます (巨大な elapsed は 0.25s に clamp — 停止からの復帰でスパイラルしない)。

        > [!WARNING]
        > 既定 (`ThreadCount = 0` = 単スレッド) は**決定的**です — 同じ初期状態 + 同じステップ列は常に同じ結果になり、snap 回帰 (golden) と両立します。`PhysicsSettings.ThreadCount` でマルチスレッドに切り替えられますが、**決定性を失います** (opt-in)。

        ## ECS コンポーネントと流れ

        entity に `Collider` (形状) + `RigidBody` (動的) または `StaticBody` (床/壁) を付け、`PhysicsStepSystem` を毎フレーム回します:

        ```csharp
        world.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),   // 初期 pose はここから
            new Color3D(color), new MeshRef(MeshRef.Cube),
            Collider.Box(0.5f, 0.5f, 0.5f), RigidBody.Dynamic());

        var step = new PhysicsStepSystem(world, physics);
        step.Run(dt);                             // Attach → Step → pose を LocalTransform へ書き戻し
        TransformPropagateSystem.Run(world);      // 以降は 3D 描画の既存経路がそのまま描く
        ```

        流れ: **Attach** (未発行 entity に body を発行 — 初期 pose は LocalTransform の分解) → **Step** → **Write-back** (`LocalTransform = Scale(RenderScale) × Rotation × Translation`)。描画側 (TransformPropagate → Render3DExtract → cube_forward) は物理を知りません。

        {{StoryRef(ctx, "Examples/3D/PhysicsFalling")}}

        > [!TIP]
        > 形状は `Collider.Box / Sphere / Capsule`。v1 の描画は MeshRef.Cube の近似表示 (Box = Size、Sphere = 外接 2r、Capsule = (2r, len+2r, 2r)) です。`ScheduleRoot` に載せる場合の規約位置は `Phase.Update`。

        ## 接触マテリアルと「跳ね」

        Bepu v2 に古典的な restitution (反発係数) は**ありません** — 「跳ね」はめり込み回復速度の上限 `MaximumRecoveryVelocity` (`PhysicsWorld.Bounciness`) と接触ばね `SpringSettings` で表現します。重力とあわせて実行時に変更できます (callbacks は Simulation 内へコピーされるため、setter がコピー先をキャスト経由で書きます):

        ```csharp
        physics.Gravity = new Vector3(0, -1.6f, 0);   // 月面
        physics.Bounciness = 5f;                      // よく跳ねる
        ```

        {{StoryRef(ctx, "Examples/3D/PhysicsPlayground")}}

        ## レイキャスト

        ```csharp
        if (physics.RayCast(origin, direction, maxT, out PhysicsRayHit hit))
            ctx.Log($"t={hit.T} normal={hit.Normal} static={hit.IsStatic}");
        ```

        ## CCD (連続衝突検出)

        高速な弾/投擲物が薄い壁をすり抜ける「トンネリング」は、`RigidBody.Dynamic(ccd: true)` (または `PhysicsWorld.AddDynamic(..., continuous: true)`) で防げます。Bepu の掃引 (sweep) ベース連続検出を有効にするフラグで、Attach 時に一度だけ反映されます:

        ```csharp
        world.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 1, -8)),
            Collider.Sphere(0.2f),
            RigidBody.Dynamic(initialVelocity: new Vector3(0, 0, 150), ccd: true));   // 高速弾でも壁を貫通しない
        ```

        > [!NOTE]
        > 既定 (`ccd: false`) でも Bepu は速度に応じた**投機マージン**で大抵のトンネリングを防ぎます。CCD が本当に効くのは、投機マージンを絞った構成 (`AddDynamic` の `maxSpeculativeMargin`) や、極端な速度 + 薄い壁 + 回転を伴う掃引です。掃引コストと引き換えなので、弾など必要なボディにだけ付けます。

        コライダーの可視化 (動的/静的/CCD の色分けワイヤ) は物理 gizmo で行えます — {{StoryRef(ctx, "Examples/3D/PhysicsGizmos")}}。

        ## 接触イベント + トリガー

        「何かに当たった」をゲームロジックが購読できます。`PhysicsStepSystem.ContactEvents` にそのフレームの `ContactEvent { Entity A, B; ContactPhase Phase }` が並び (Phase = Begin/End)、**フレーム内で読み切る**規約です (持ち越さない):

        ```csharp
        foreach (ContactEvent e in physicsStep.ContactEvents)
            if (e.Phase == ContactPhase.Begin)
                ctx.Log($"{e.A} が {e.B} に接触");
        ```

        接触は実接触 (めり込み depth ≥ 0) のみ — speculative な予測接触は含めません。Begin/End は「今ステップで接触したペア集合」と前ステップの差分で判定します。

        **トリガーボリューム** は `Trigger` コンポーネント (形状は同居する `Collider`) を付けた静的 collidable で、通過を検知しても**力を発生させません** (ゴール/アイテム取得/ダメージゾーン)。動的ボディが通ると同じ `ContactEvents` に Begin/End が出ます — どちらの Entity が `Trigger` を持つかで判別します:

        ```csharp
        world.Store.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(goalX, goalY, goalZ)),
            Collider.Box(2, 2, 2), new Trigger());   // ゴールゾーン
        ```

        {{StoryRef(ctx, "Examples/3D/PhysicsTrigger")}}

        > [!NOTE]
        > 接触点の詳細 (位置/法線/深度) は v1 では公開しません — Begin/End で十分な用途 (判定・カウント) を先に。接触中ペアの可視化は `PhysicsStepSystem.TrackCurrentContacts` を有効にして `CurrentContacts` を gizmo で描きます (sleep した body は narrow phase が止まるため出ません)。マルチスレッド (`ThreadCount > 0`) の接触イベントは v1 未対応 (既定の単スレッドで使う)。

        ## 設計ノート

        - **リセットは丸ごと再構築** — entity 削除に連動した body 掃除は持たず、World + PhysicsWorld を作り直すのが決定的な初期状態へ戻る正攻法です (Playground の reset knob がこの形)
        - 生の Bepu API は `PhysicsWorld.Simulation` から常に触れます — ラッパに無い機能 (constraint 等) はそこから使えます

        ## メッシュ / 凸包コライダー

        プリミティブ (箱/球/カプセル) に加えて、実メッシュ形状で衝突できます。**静的な地形/建物**は三角形スープの `MeshCollider` (Bepu の `Mesh`)、**動的な小物**は頂点群から作る `HullCollider` (Bepu の `ConvexHull`) です:

        ```csharp
        // 静的地形 (頂点 + インデックス、glTF の AssetPrimitive.Attributes.Positions / Indices から取れる)
        world.Store.CreateEntity(new LocalTransform(Matrix4x4.Identity),
            MeshCollider.Static(vertices, indices));

        // 動的な凸包 (頂点群 → 凸包)
        world.Store.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 5, 0)),
            HullCollider.Dynamic(points, mass: 1f));
        ```

        {{StoryRef(ctx, "Examples/3D/PhysicsMesh")}}

        > [!WARNING]
        > **三角形の winding** で衝突面 (法線の向き) が決まります — Bepu メッシュは片面で、法線と逆側から来た物体は貫通します。地形の上面へ物体を載せるなら、上向き法線になる巻き方にしてください (向きが逆なら 2 頂点を入れ替え)。glTF の座標系/スケールは描画と同じ変換を渡さないと絵と当たりがずれます。
        >
        > **凸包の重心オフセット**: Bepu は形状を重心原点へ recenter します。`HullCollider` は重心オフセットを内部で保持し、書き戻しで元の頂点原点に合わせます (描画メッシュと一致)。**凹メッシュの凸分解 (V-HACD 等) と動的メッシュは v1 スコープ外** — 動的な実形状は凸包で近似します。

        ## ロードマップ (v1 スコープ外)

        v1 で見送った項目と、その理由・実装の要点です。おおまかな優先度は **キャラクターコントローラ → Compound shape → Joint/constraint** (費用対効果順)。CCD・接触イベント/トリガー・メッシュ/凸包コライダーは実装済み (上記の各節)。

        ### キャラクターコントローラ

        純粋な剛体だと坂で滑り・段差で引っかかり・押されて転がるため、カプセル + 姿勢固定 + 足元レイキャスト (実装済み) + 目標速度への速度サーボという専用制御が定石です。物理の上に載るアプリ層の機能で設計判断が多く (ジャンプ/階段/斜面角)、Bepu の Demos にリファレンス実装があります。[RealWindow/Input/Gamepad](story:RealWindow/Input/Gamepad) とつなぐと良いショーケースに。

        ### Compound shape (複合形状)

        複数プリミティブを 1 剛体に束ねる形状 (机 = 天板 + 脚 4 本)。「1 entity = 1 Collider = 1 body」の対応が崩れ、子形状のローカル pose をどう表現するかのコンポーネント設計が必要 — Parent 階層の扱いと一緒に決めるべき項目です。

        ### Joint / constraint の公開 API

        ヒンジ/ボールジョイント/距離拘束など。生 API は今日でも `physics.Simulation.Solver.Add(a, b, new BallSocket ...)` で使えます — スコープ外なのは「ECS コンポーネントで宣言的に書ける層」。使用頻度の高い 3〜4 種 (BallSocket/Hinge/Distance/Weld) のコンポーネント化と、body 消滅時のハンドル無効化が本体。

        ### Entity 削除に連動した body 掃除

        entity を消しても body が Simulation に残ります (見えない衝突体)。ハンドル辞書 + 生存チェックで `Bodies.Remove` する形が素直で、弾丸や破片を大量に出し入れする段になったら必須。それまでは「リセット = 丸ごと再構築」で足ります。

        ### Parent 階層下の RigidBody

        物理はワールド空間、`LocalTransform` は親空間 — 素朴に書き戻すと親の変換が二重適用されるため v1 は未定義動作です。正しくは親の GlobalTransform の逆行列でローカル化しますが、「親子の物理」の実需要はたいてい joint で表現するのが正しく、優先度低。

        ### 2D 物理

        Canvas2D 系統 (プラットフォーマー等) 向け。Bepu は 3D 専用なので、別エンジン (Box2D 系の C# 移植) を選ぶか Z 固定 + 回転 1 軸拘束の 2.5D にするかの選択から。作るなら `Luxel.Physics.TwoD` を別プロジェクトで — ECS を使わず UiNode transform へ直接書き戻す、本ページと同じ World ラッパ + Step + 書き戻しの写しになります。

        ### マルチスレッド実行での決定性

        `ThreadCount > 0` は浮動小数の加算順がスレッドスケジューリングで変わり、実行ごとに結果が揺れます (Bepu 2.4 の仕様)。だから既定は単スレッド = 決定的で、マルチスレッドは速度と引き換えの opt-in。Bepu 2.5 系は決定性オプションが改善されているため、バージョン更新時に「固定スレッド数なら決定的」へ緩められる可能性があります。

        型 API は [Luxel.Physics](story:Reference/Luxel.Physics) へ。
        """;
}
