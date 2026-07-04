using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — GPU 土台の章 (GpuDevice / TwoD / RenderGraph / ThreeD)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsGpu
{
    [Story("Docs/GpuDevice", Order = 10)]
    public static Widget GpuDevice(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # GPU 抽象 (GpuDevice)

        `Luxel` (コア) は *No Graphics API* の哲学どおり、最新のバインドレス GPU を前提に **ディスクリプタセットも PSO バリアントも持たない**薄い抽象です。実装は `Luxel.Vulkan` (Vulkan 1.3) と `Luxel.D3D12` (DirectX 12) — アプリのコードはバックエンド分岐なしの 1 本で書けます。

        ## 固定パイプラインレイアウト

        全パイプラインが同じレイアウトを共有します:

        - **ルート引数** — 小さな構造体を push 定数 (Vulkan) / root 32bit 定数 (D3D12) で inline 渡し。`SetRootArguments(args)` に構造体を渡すだけ
        - **bindless heap** — すべてのバッファ/テクスチャは作成時に `BindlessIndex` を持ち、シェーダは `g_buffers[index]` / `g_textures[index]` で参照

        レイアウトが 1 つなので、ディスクリプタの束ね直しも PSO の組み合わせ爆発も起きません。「どのリソースを使うか」はルート引数の中の index が全てです。

        ## メモリとバッファ

        `device.Malloc(bytes, GpuMemoryKind)` でバッファを確保します。`HostMapped` は CPU から `Span<T>` で直接読み書きでき (staging 不要)、`DeviceLocal` は GPU 専用、`Readback` は GPU→CPU 読み戻し用です。

        ```csharp
        using var device = new GpuDevice(VulkanBackend.Create());   // dx も可
        using var input  = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);
        using var output = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);

        input.Span<float>(n)[i] = ...;                    // CPU マップへ直接書き込み

        using var pipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute01"));
        using var cmd = device.MainQueue.StartCommandRecording();
        cmd.SetComputePipeline(pipeline)
           .SetRootArguments(new Args { Input = input.BindlessIndex, Output = output.BindlessIndex, Count = n })
           .Dispatch((n + 63) / 64)
           .Barrier(GpuStage.ComputeShader, GpuStage.All);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
        // output.Span<float>(n) を読み戻して検証
        ```

        ## Slang 統一シェーダ

        シェーダは Slang で 1 回書き、SPIR-V (Vulkan) と DXIL (D3D12) の両方へコンパイルされます (`shaders/*.slang` → ビルド時に `slangc`)。

        - `g_buffers[index]` は Vulkan では set0/binding0 の storage buffer 配列、D3D12 では u0/space1 の unbounded UAV テーブルに lower される
        - シェーダはレンダーグラフにもバックエンドにも依存しない

        > [!TIP]
        > DXIL は `-validator-version 1.7` で出力しています — DXC 既定版の DXIL は OS ランタイムに拒否されることがあります。

        ## コマンドとバリア

        `MainQueue.StartCommandRecording()` → fluent にコマンドを積み → `Finish()` → `SubmitAndWait(cmd)`。同期は `Barrier(srcStage, dstStage)` の **stage バリア**だけ — リソース個別の状態遷移管理はありません (bindless + 単純化された使用パターンが前提)。

        ## 描画 (graphics PSO)

        graphics も同じ流儀です。`CreateGraphicsPipeline(shader, GpuRasterDesc)` で深度テストやブレンドを宣言し、dynamic rendering (`BeginRendering`/`EndRendering`) で RT/Depth を直接指定、頂点は**頂点プル** (頂点レイアウト宣言なし — シェーダが bindless バッファから読む) です。

        {{StoryRef(ctx, "GPU/Depth")}}

        {{StoryRef(ctx, "GPU/Blend")}}

        `StorySource` でこのデモの実装をそのまま引用できます:

        {{StorySource("GPU/Depth")}}

        ## テクスチャとレンダーターゲット

        `CreateTexture` (ピクセルアップロード) / `CreateSampler` / `CreateRenderTarget` / `CreateDepthTarget`。RT の内容は `CopyTextureToBuffer` で bindless バッファへ書き出し、後段の compute/pixel シェーダが `Load` で読みます — swapchain 提示も docs 内の GpuView 埋め込みも、すべてこの経路です。

        > [!WARNING]
        > D3D12 の `CopyTextureToBuffer` は行 256B 整列が必要です。RGBA8 なら **ターゲット幅を 64 の倍数**にしてください (このページのデモはすべて 256)。

        次: [Docs/TwoD](story:Docs/TwoD) — この GPU 抽象の上に 2D ベクターを載せます。
        """, toc: true, fences: DocsFences));

    [Story("Docs/TwoD", Order = 11)]
    public static Widget TwoD(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # 2D ベクター (Luxel.TwoD)

        GPU **コンピュートラスタライザ** (Vello 風) による 2D ベクター描画です。パスを三角形分割せず、線分のまま GPU に常駐させ、compute が画素ごとに巻き数/距離で被覆を計算して塗ります。バックエンド変更ゼロ (framebuffer は bindless バッファ)。

        ## 描けるもの

        塗り (NonZero/EvenOdd — 穴あき対応)、複数パス合成、ストローク (距離ベース・画面一定幅)、**ベクターテキスト** (TTF 輪郭 → パス → 塗り、日本語対応)、角丸:

        {{StoryRef(ctx, "2D/VectorPaths")}}

        ## Scene2D とパス構築

        ```csharp
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Blue, 40, 40, 120, 80, 12);
        using (var jp = VectorFont.LoadSystemJapanese())
            jp.AppendText(scene, "こんにちは", 50, 120, 28, Color2D.Black);

        using var raster = new Rasterizer2D(device);
        using var encoded = raster.Encode(scene);                 // GPU へ 1 回
        raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);   // ズームは Camera2D.Create(...)
        ```

        ## Camera2D — スムーズズーム

        ワールド座標で 1 回 `Encode` したら、`Camera2D` を変えるだけで連続拡縮できます — 再エンコードも再三角形分割もありません。ベクターなので拡大してもエッジが崩れないことを knob で確かめられます:

        {{StoryRef(ctx, "2D/Map", knobs: true)}}

        ## RetainedCanvas — 保持型ツリーと部分更新

        UI ライブラリのバックエンドとして、フレーム間で保持するノードツリーを提供します。データは SoA (Transform / Style / Clip / Order / Segment を分離) で、シェーダが per-path 変換を適用するため **移動 = 変換だけ書込、色変更 = スタイルだけ書込** (ジオメトリ不変) になります。

        ```csharp
        using var canvas = new RetainedCanvas(raster);
        UiNode panel = canvas.AddChild(canvas.Root);
        panel.Transform = Affine2D.Translate(40, 40);
        panel.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 400, 240, 16);

        canvas.Render(cmd, Camera2D.Pixels, w, h, fb);    // 初回はフル構築
        panel.Color = red;                                // 部分更新: スタイルのみ
        panel.Transform = Affine2D.Translate(40, 80);     // 部分更新: 変換のみ
        canvas.Render(cmd, Camera2D.Pixels, w, h, fb);    // Segment 書込 0
        ```

        - `UiNode`: ローカル変換 / 色 / 不透明度 / 矩形クリップ / Z / 子 / Content。setter が dirty を伝播
        - クリップは祖先と交差して適用 (スクロール / パネル)
        - 描画順はツリー pre-order + 兄弟内 Z の order バッファ (奥→手前 alpha 合成)
        - 部分更新量は `LastTransformWrites` / `LastStyleWrites` / `LastSegmentBytesWritten` で観測できます

        ## 設計ノート: 増分更新 — 「slot 据え置き、レンジは容量付き」

        Content 差し替え (タイプ中のエディタ、ライブ波形) を O(シーン全体) のフル再構築にしないため、ノードの線分レンジに**容量 (capacity)** を持たせています。収まる差し替えは in-place 書き込み、伸びたら末尾へ追記して旧レンジを空きに。空きが閾値を超えたときだけフル再構築 = **まれなコンパクション**に降格します。

        - パス slot の中身を書き換えても描画順 (order バッファ) は不変
        - パス数が変わるときだけ order を再構成 (軽量パス)
        - 定常フレームのコストは O(変わったノード) — 回帰は bench で監視 (使い方は Gallery の Docs 章の Contributing ページへ)

        ## 今後

        タイル binning (現状は画素×線分のブルートフォース + bbox 早期スキップ)、解析的 AA (現状 4x4 スーパーサンプル)。

        次: [Docs/RenderGraph](story:Docs/RenderGraph) — 多段パスの合成へ。
        """, toc: true, fences: DocsFences));

    [Story("Docs/RenderGraph", Order = 12)]
    public static Widget RenderGraphDocs(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # レンダーグラフ (Luxel.RenderGraph)

        UI のレンダリング結果を別パスで参照したり、compute/graphics 混在の多段パスを組み立てるための薄い管理層です。**scene-agnostic** — 入力は GPU ハンドル (`BufferHandle` / `TextureHandle`) のみで、シーン側 (RetainedCanvas / ECS) を一切知りません。

        ## 設計ノート: 業界 4 実装の収束

        Frostbite FrameGraph / Unreal RDG / Unity URP RenderGraph / Granite の 4 実装は同じ **Setup / Compile / Execute 三相**モデルに収束しています。もう 1 つの結論は「シーングラフは導入しない」— シーングラフは描画対象の階層管理、レンダーグラフはパスとリソース依存の DAG 管理で、別レイヤです。Luxel では 2D/UI 側に RetainedCanvas、3D 側に ECS が既にその役割を持っています。

        参考: [FrameGraph (GDC 2017)](https://www.gdcvault.com/play/1024612/FrameGraph-Extensible-Rendering-Architecture-in) / [Render graphs and Vulkan — a deep dive](https://themaister.net/blog/2017/08/15/render-graphs-and-vulkan-a-deep-dive/) / [Unreal RDG](https://dev.epicgames.com/documentation/unreal-engine/render-dependency-graph-in-unreal-engine) / [Unity URP Render Graph](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-introduction.html)

        ## Setup / Compile / Execute

        ```mermaid
        flowchart LR
        setup[Setup - AddPass + Read/Write 宣言] --> compile[Compile - カリング/寿命/alias/バリア]
        compile --> exec[Execute - pass lambda 駆動]
        ```

        Setup でパスと依存を宣言し、`Execute(cmd)` 1 回で Compile + Execute が走ります。Compile 相の仕事: デッドパスカリング (External への書き込みに到達しないパスを除去) → トポロジカルソート → 各リソースの first-write / last-read 計算 → transient の物理割当 (aliasing) → パス境界のバリア計算。

        ```csharp
        using var rg = new RenderGraph(device);                       // 1 フレーム使い切り
        BufferHandle hUi    = rg.ImportBuffer(ui, "ui");              // External
        BufferHandle hTmp   = rg.CreateBuffer(new BufferDesc(bytes), "blurH");   // Transient
        BufferHandle hFinal = rg.ImportBuffer(final, "final");

        rg.AddPass("BlurH", PassQueue.Compute)
          .Read(hUi).Write(hTmp)
          .Execute(ctx => ctx.Cmd.SetComputePipeline(blur)
              .SetRootArguments(new Args { Src = ctx.BindlessIndex(hUi), Dst = ctx.BindlessIndex(hTmp) })
              .Dispatch((w + 7) / 8, (h + 7) / 8));
        // …BlurV, Composite も同様…
        rg.Execute(cmd);   // Compile + Execute (寿命解析 + 自動バリア + lambda 駆動)
        ```

        ## リソースモデル: External と Transient

        | 種別 | 寿命 | 例 | aliasing |
        | --- | --- | --- | --- |
        | External | グラフ外 (`ImportBuffer/Texture`) | swapchain、永続バッファ、アセット | 不可 |
        | Transient | グラフ内 (`CreateBuffer/Texture`) | 中間バッファ、ping-pong | 寿命非重複なら可 |

        Transient は同形 (バッファはサイズ、テクスチャは幅・高さ・フォーマット・種別) で寿命が重ならなければ **物理リソースを共有** (interval scheduling) します。実物:

        {{StoryRef(ctx, "RenderGraph/Blur")}}

        下のデモは反復ブラー 4 段 + 誰も読まないパス — 論理 5 transient が物理 2 本に alias され、DeadPass がカリングされる様子が **Log パネル**に出ます (`PhysicalTransientBufferCount` / `IsAliased` / `IsPassCulled` で観測):

        {{StoryRef(ctx, "RenderGraph/Aliasing")}}

        ## 自動バリアと ResourceUsage

        パスの Read/Write 宣言から `GpuStage` の遷移を計算し、バリアを自動挿入します。既定は compute 読み書きの扱いなので、**実際の使い方が違うときは usage を明示**します: `Write(h, ResourceUsage.CopyDest)` (RT → バッファのコピー先)、`Read(h, ResourceUsage.SampledInPixelShader)` (pixel シェーダで Load) など — これで Copy→Compute / Compute→PixelShader の遷移が正しく発行されます。

        > [!WARNING]
        > **Write が 1 つもないパスはデッドパスカリングで消えます。** RT へ直接描くだけのパスは、参照する External バッファへの `Write` を「使用の宣言」として付けてください。

        ## ResourceSystem との関係 (混同注意)

        | 軸 | Luxel.Resources | Luxel.RenderGraph |
        | --- | --- | --- |
        | 単位 | アセット ((型, uri) ノード) | フレームのパス (pass × resource) |
        | 寿命 | 多フレーム (refcount + reload) | 1 フレーム (transient) + External |
        | 実行 | Io / Cpu / Gpu レーン | Graphics / Compute queue |

        両者は直交します。併用パターン: ResourceSystem でロードした `GpuTexture` を `ImportTexture` で External として取り込む。

        次: [Docs/ThreeD](story:Docs/ThreeD) — ECS と組み合わせて 3D を描きます。
        """, toc: true, fences: DocsFences));

    [Story("Docs/ThreeD", Order = 13)]
    public static Widget ThreeD(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # 3D と ECS

        3D は **ECS (Friflo Engine ECS のラッパ = Luxel.Ecs) + 抽出 + レンダーグラフ**の直列で描きます。シーングラフという独立した抽象はありません — 階層は ECS の `Parent`/`Children` コンポーネントが素直に表します。

        ## コンポーネントとシステム

        - `LocalTransform` / `GlobalTransform` — ローカル/ワールド変換。`TransformPropagateSystem.Run(world)` が Parent をたどって伝播
        - `MeshRef` / `Color3D` / `Visible` — 描画コンポーネント
        - メッシュは `CubeMesh` 等の頂点プル用バッファ (position + normal)

        ## 抽出 (IRenderExtractor)

        `Render3DExtractSystem` が ECS をクエリし、`InstanceData[]` (mat4 + color の SoA) を bindless バッファへ書きます。シーン層とレンダーグラフ層の橋渡しはこの **Extract 層だけ** — グラフ側は書き上がったバッファを Import するのみです。

        {{StoryRef(ctx, "3D/EcsCubes")}}

        ## 描画パターン

        graphics パスの lambda が `BeginRendering(rt, depth, ...)` で RT/Depth を直接管理し、instance 数ぶん `Draw` します。この形のまま合成が伸びます:

        - post-process 連鎖 (forward → blur → 加算合成) → [RenderGraph/Bloom3D](story:RenderGraph/Bloom3D)
        - world-space UI (2D UI を 3D 内の板にサンプリング) → [3D/WorldSpaceUI](story:3D/WorldSpaceUI)
        - shadow map (ライト視点 R32Float → bindless バッファ → 比較) → [3D/ShadowMap](story:3D/ShadowMap)

        shadow map も「R32F カラー RT + bindless バッファ経由の Load」— 専用のサンプラ比較ハードウェアに頼らない compute-first の流儀です。

        ## 設計ノート: UI に ECS を使わない理由

        - UI は reactive signals + 保持型ツリーのほうが DSL / 部分更新と相性が良い
        - ラベル 100 個を 100 entity にするとフレーム単位 query のオーバーヘッドが過剰
        - Bevy も UI は別系統 (bevy_ui)。Luxel でも `Luxel.UI` は signals 系のまま、**3D 側だけ ECS** です

        ## 行列レイアウトの罠

        > [!WARNING]
        > Slang/HLSL の既定行列レイアウトは column-major、`System.Numerics.Matrix4x4` は row-major です。ルート引数で行列を渡すときは **CPU 側で `Matrix4x4.Transpose`** を入れて整えます (per-instance 行列はシェーダ側で Load4 ×4 の行構築なので転置不要)。

        アニメーションを ECS へ流す例は [Animation/EcsClip](story:Animation/EcsClip) と [Animation/Graph](story:Animation/Graph) へ。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Assets", Order = 14)]
    public static Widget Assets(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
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

        {{StoryRef(ctx, "3D/GltfBox")}}

        ## アニメーション

        ノード TRS アニメーションは `SceneAnimationPlayer` が時刻 t の値を sample して entity の `LocalTransform` に書き、`TransformPropagateSystem` で伝播 → 再 Extract で instance バッファが更新されます:

        ```csharp
        var player = new SceneAnimationPlayer(world, assets, doc.Animations[0]);
        // 毎フレーム: 周期は t % Duration (snap の決定性のため wall-clock は使わない)
        player.Sample(time % doc.Animations[0].Duration);
        TransformPropagateSystem.Run(world);
        extractor.Extract(new ExtractContext(device, frameIndex: frame++));
        ```

        {{StoryRef(ctx, "3D/GltfAnimated")}}

        > [!WARNING]
        > morph target (Weights チャンネル) は未対応です — `SceneAnimationPlayer` は skip します。スキニングは `SkinningSystem` + `scene_pbr_skinned` シェーダ (頂点 56B: joints/weights 付き) が担います。

        ## ResourceSystem 統合

        実アプリでは URI ロード + キャッシュ + 依存解決を Resources に任せます。AssetsGpu の Step 群を登録すると `Load<T>` の型で変換チェーンが解決されます:

        ```csharp
        var handle = resources.Load<SceneAssets>("file:///model.glb");        // glb → doc → ECS+GPU
        var vbuf = resources.Load<GpuBuffer>("file:///model.glb#mesh/0/vertex");   // fragment URI で部分ロード
        ```

        `#mesh/N/vertex` / `#materials` のような **fragment URI** で primitive 単位の遅延ロードができます。Resources 自体の概念 (RefCount / Republish / Pump) は [Docs/Resources](story:Docs/Resources) へ。

        ## DRAW-M3: コンポーネント駆動の描画

        entity に `DrawMesh` + `DrawInstance` + `DrawMaterial` (+ `DrawSkinning`) を付けると、`DrawableCollector.Collect(world, rg)` が RenderGraph への import 済みハンドル束 (`DrawItem`) を返し、呼び出し側は for-loop で push constant を組んで Draw するだけになります。テクスチャ付きは `scene_pbr_tex` (material 32B 配列 + bindless texture)、スキニングは `scene_pbr_skinned` が対になります。

        ## 設計ノート

        - **CPU アセット層は Resources 非依存** — ツール/テストが GPU なしで AssetDocument を扱えます。GPU 化と URI 解決は AssetsGpu 側の Step が担う分離です
        - **direct-ref モデル** — index ベースの DOM ではなく `AssetMeshRef.Mesh` のような直接参照で ECS から引きます (中間テーブル無し)
        - サンプルモデルはリポジトリの `tools/khronos-samples/` (Khronos 公式 glTF テストスイート) にあります

        型 API の一覧は [Docs/Api3D](story:Docs/Api3D) へ。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Ecs", Order = 15)]
    public static Widget Ecs(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ECS (Luxel.Ecs)

        Friflo Engine ECS の薄いラッパです。3D シーン ([Docs/ThreeD](story:Docs/ThreeD)) とアセット展開 ([Docs/Assets](story:Docs/Assets)) の土台ですが、単体でも使えます。高度な操作は `world.Store` (生の Friflo `EntityStore`) を直接触ってかまいません。

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

        UI から entity を編集する例は [Animation/EcsClip](story:Animation/EcsClip) にあります。

        ## 設計ノート

        - **UI には ECS を使いません** — UI は signals + 保持型ツリー ([Docs/UI](story:Docs/UI))。3D 側だけ ECS です (理由は [Docs/ThreeD](story:Docs/ThreeD) の設計ノート)
        - ラッパを薄く保つのは、Friflo の archetype API (`ArchetypeQuery` / `ForEachEntity`) がそのまま最速経路だからです。Luxel が足すのは Phase 規約・DelegateSystem・Signal ブリッジ・perf 収集だけ

        物理 (剛体/衝突) を entity に付けるには [Docs/Physics](story:Docs/Physics) へ。型 API の一覧は [Docs/Api3D](story:Docs/Api3D) へ。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Physics", Order = 16)]
    public static Widget Physics(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
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

        {{StoryRef(ctx, "3D/PhysicsFalling")}}

        > [!TIP]
        > 形状は `Collider.Box / Sphere / Capsule`。v1 の描画は MeshRef.Cube の近似表示 (Box = Size、Sphere = 外接 2r、Capsule = (2r, len+2r, 2r)) です。`ScheduleRoot` に載せる場合の規約位置は `Phase.Update`。

        ## 接触マテリアルと「跳ね」

        Bepu v2 に古典的な restitution (反発係数) は**ありません** — 「跳ね」はめり込み回復速度の上限 `MaximumRecoveryVelocity` (`PhysicsWorld.Bounciness`) と接触ばね `SpringSettings` で表現します。重力とあわせて実行時に変更できます (callbacks は Simulation 内へコピーされるため、setter がコピー先をキャスト経由で書きます):

        ```csharp
        physics.Gravity = new Vector3(0, -1.6f, 0);   // 月面
        physics.Bounciness = 5f;                      // よく跳ねる
        ```

        {{StoryRef(ctx, "3D/PhysicsPlayground")}}

        ## レイキャスト

        ```csharp
        if (physics.RayCast(origin, direction, maxT, out PhysicsRayHit hit))
            ctx.Log($"t={hit.T} normal={hit.Normal} static={hit.IsStatic}");
        ```

        ## 設計ノート / スコープ外

        - **リセットは丸ごと再構築** — entity 削除に連動した body 掃除は持たず、World + PhysicsWorld を作り直すのが決定的な初期状態へ戻る正攻法です (Playground の reset knob がこの形)
        - `Parent` 階層下の RigidBody は未定義動作 (物理はワールド空間)
        - 将来枠: メッシュ/凸包コライダー (AssetPrimitive → Bepu Mesh/ConvexHull)、compound shape、joint/constraint の公開 API、接触イベント (trigger)、キャラクターコントローラ、CCD、2D 物理

        型 API の一覧は [Docs/Api3D](story:Docs/Api3D) へ。
        """, toc: true, fences: DocsFences));
}
