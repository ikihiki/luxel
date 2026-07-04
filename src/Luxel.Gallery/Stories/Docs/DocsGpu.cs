using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — GPU 土台の章 (GpuDevice / TwoD / RenderGraph / ThreeD)。</summary>
public static class DocsGpu
{
    // C# コード例は { } が hole と衝突するため生 markdown hole (DocMarkdown) で差し込む

    private static readonly DocMarkdown ComputeExample = new("""
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
        """);

    [Story("Docs/GpuDevice", Width = 800, Height = 480, Order = 10)]
    public static Widget GpuDevice(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # GPU 抽象 (GpuDevice)

        `Luxel` (コア) は *No Graphics API* の哲学どおり、最新のバインドレス GPU を前提に
        **ディスクリプタセットも PSO バリアントも持たない**薄い抽象です。実装は
        `Luxel.Vulkan` (Vulkan 1.3) と `Luxel.D3D12` (DirectX 12) — アプリのコードは
        バックエンド分岐なしの 1 本で書けます。

        ## 固定パイプラインレイアウト

        全パイプラインが同じレイアウトを共有します:

        - **ルート引数** — 小さな構造体を push 定数 (Vulkan) / root 32bit 定数 (D3D12) で
          inline 渡し。`SetRootArguments(args)` に構造体を渡すだけ
        - **bindless heap** — すべてのバッファ/テクスチャは作成時に `BindlessIndex` を持ち、
          シェーダは `g_buffers[index]` / `g_textures[index]` で参照

        レイアウトが 1 つなので、ディスクリプタの束ね直しも PSO の組み合わせ爆発も
        起きません。「どのリソースを使うか」はルート引数の中の index が全てです。

        ## メモリとバッファ

        `device.Malloc(bytes, GpuMemoryKind)` でバッファを確保します。`HostMapped` は
        CPU から `Span<T>` で直接読み書きでき (staging 不要)、`DeviceLocal` は GPU 専用、
        `Readback` は GPU→CPU 読み戻し用です。

        {ComputeExample}

        ## Slang 統一シェーダ

        シェーダは Slang で 1 回書き、SPIR-V (Vulkan) と DXIL (D3D12) の両方へ
        コンパイルされます (`shaders/*.slang` → ビルド時に `slangc`)。

        - `g_buffers[index]` は Vulkan では set0/binding0 の storage buffer 配列、
          D3D12 では u0/space1 の unbounded UAV テーブルに lower される
        - シェーダはレンダーグラフにもバックエンドにも依存しない

        > [!TIP]
        > DXIL は `-validator-version 1.7` で出力しています — DXC 既定版の DXIL は
        > OS ランタイムに拒否されることがあります。

        ## コマンドとバリア

        `MainQueue.StartCommandRecording()` → fluent にコマンドを積み → `Finish()` →
        `SubmitAndWait(cmd)`。同期は `Barrier(srcStage, dstStage)` の **stage バリア**だけ —
        リソース個別の状態遷移管理はありません (bindless + 単純化された使用パターンが前提)。

        ## 描画 (graphics PSO)

        graphics も同じ流儀です。`CreateGraphicsPipeline(shader, GpuRasterDesc)` で
        深度テストやブレンドを宣言し、dynamic rendering (`BeginRendering`/`EndRendering`)
        で RT/Depth を直接指定、頂点は**頂点プル** (頂点レイアウト宣言なし — シェーダが
        bindless バッファから読む) です。

        {StoryRef(ctx, "GPU/Depth")}

        {StoryRef(ctx, "GPU/Blend")}

        `StorySource` でこのデモの実装をそのまま引用できます:

        {StorySource("GPU/Depth")}

        ## テクスチャとレンダーターゲット

        `CreateTexture` (ピクセルアップロード) / `CreateSampler` / `CreateRenderTarget` /
        `CreateDepthTarget`。RT の内容は `CopyTextureToBuffer` で bindless バッファへ
        書き出し、後段の compute/pixel シェーダが `Load` で読みます — swapchain 提示も
        docs 内の GpuView 埋め込みも、すべてこの経路です。

        > [!WARNING]
        > D3D12 の `CopyTextureToBuffer` は行 256B 整列が必要です。RGBA8 なら
        > **ターゲット幅を 64 の倍数**にしてください (このページのデモはすべて 256)。

        次: [Docs/TwoD](story:Docs/TwoD) — この GPU 抽象の上に 2D ベクターを載せます。
        """, toc: true, fences: DocsFences));

    private static readonly DocMarkdown Scene2DExample = new("""
        ```csharp
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Blue, 40, 40, 120, 80, 12);
        using (var jp = VectorFont.LoadSystemJapanese())
            jp.AppendText(scene, "こんにちは", 50, 120, 28, Color2D.Black);

        using var raster = new Rasterizer2D(device);
        using var encoded = raster.Encode(scene);                 // GPU へ 1 回
        raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);   // ズームは Camera2D.Create(...)
        ```
        """);

    private static readonly DocMarkdown RetainedExample = new("""
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
        """);

    [Story("Docs/TwoD", Width = 800, Height = 480, Order = 11)]
    public static Widget TwoD(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # 2D ベクター (Luxel.TwoD)

        GPU **コンピュートラスタライザ** (Vello 風) による 2D ベクター描画です。パスを
        三角形分割せず、線分のまま GPU に常駐させ、compute が画素ごとに巻き数/距離で
        被覆を計算して塗ります。バックエンド変更ゼロ (framebuffer は bindless バッファ)。

        ## 描けるもの

        塗り (NonZero/EvenOdd — 穴あき対応)、複数パス合成、ストローク (距離ベース・
        画面一定幅)、**ベクターテキスト** (TTF 輪郭 → パス → 塗り、日本語対応)、角丸:

        {StoryRef(ctx, "2D/VectorPaths")}

        ## Scene2D とパス構築

        {Scene2DExample}

        ## Camera2D — スムーズズーム

        ワールド座標で 1 回 `Encode` したら、`Camera2D` を変えるだけで連続拡縮できます —
        再エンコードも再三角形分割もありません。ベクターなので拡大してもエッジが
        崩れないことを knob で確かめられます:

        {StoryRef(ctx, "2D/Map", knobs: true)}

        ## RetainedCanvas — 保持型ツリーと部分更新

        UI ライブラリのバックエンドとして、フレーム間で保持するノードツリーを提供します。
        データは SoA (Transform / Style / Clip / Order / Segment を分離) で、シェーダが
        per-path 変換を適用するため **移動 = 変換だけ書込、色変更 = スタイルだけ書込**
        (ジオメトリ不変) になります。

        {RetainedExample}

        - `UiNode`: ローカル変換 / 色 / 不透明度 / 矩形クリップ / Z / 子 / Content。
          setter が dirty を伝播
        - クリップは祖先と交差して適用 (スクロール / パネル)
        - 描画順はツリー pre-order + 兄弟内 Z の order バッファ (奥→手前 alpha 合成)
        - 部分更新量は `LastTransformWrites` / `LastStyleWrites` / `LastSegmentBytesWritten`
          で観測できます

        ## 設計ノート: 増分更新 — 「slot 据え置き、レンジは容量付き」

        Content 差し替え (タイプ中のエディタ、ライブ波形) を O(シーン全体) のフル再構築に
        しないため、ノードの線分レンジに**容量 (capacity)** を持たせています。収まる
        差し替えは in-place 書き込み、伸びたら末尾へ追記して旧レンジを空きに。空きが
        閾値を超えたときだけフル再構築 = **まれなコンパクション**に降格します。

        - パス slot の中身を書き換えても描画順 (order バッファ) は不変
        - パス数が変わるときだけ order を再構成 (軽量パス)
        - 定常フレームのコストは O(変わったノード) — 回帰は bench で監視
          (使い方は Gallery の Docs 章の Contributing ページへ)

        ## 今後

        タイル binning (現状は画素×線分のブルートフォース + bbox 早期スキップ)、
        解析的 AA (現状 4x4 スーパーサンプル)。

        次: [Docs/RenderGraph](story:Docs/RenderGraph) — 多段パスの合成へ。
        """, toc: true, fences: DocsFences));

    private static readonly DocMarkdown RgExample = new("""
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
        """);

    [Story("Docs/RenderGraph", Width = 800, Height = 480, Order = 12)]
    public static Widget RenderGraphDocs(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # レンダーグラフ (Luxel.RenderGraph)

        UI のレンダリング結果を別パスで参照したり、compute/graphics 混在の多段パスを
        組み立てるための薄い管理層です。**scene-agnostic** — 入力は GPU ハンドル
        (`BufferHandle` / `TextureHandle`) のみで、シーン側 (RetainedCanvas / ECS) を
        一切知りません。

        ## 設計ノート: 業界 4 実装の収束

        Frostbite FrameGraph / Unreal RDG / Unity URP RenderGraph / Granite の 4 実装は
        同じ **Setup / Compile / Execute 三相**モデルに収束しています。もう 1 つの結論は
        「シーングラフは導入しない」— シーングラフは描画対象の階層管理、レンダーグラフは
        パスとリソース依存の DAG 管理で、別レイヤです。Luxel では 2D/UI 側に
        RetainedCanvas、3D 側に ECS が既にその役割を持っています。

        参考: [FrameGraph (GDC 2017)](https://www.gdcvault.com/play/1024612/FrameGraph-Extensible-Rendering-Architecture-in) /
        [Render graphs and Vulkan — a deep dive](https://themaister.net/blog/2017/08/15/render-graphs-and-vulkan-a-deep-dive/) /
        [Unreal RDG](https://dev.epicgames.com/documentation/unreal-engine/render-dependency-graph-in-unreal-engine) /
        [Unity URP Render Graph](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-introduction.html)

        ## Setup / Compile / Execute

        ```mermaid
        flowchart LR
        setup[Setup - AddPass + Read/Write 宣言] --> compile[Compile - カリング/寿命/alias/バリア]
        compile --> exec[Execute - pass lambda 駆動]
        ```

        Setup でパスと依存を宣言し、`Execute(cmd)` 1 回で Compile + Execute が走ります。
        Compile 相の仕事: デッドパスカリング (External への書き込みに到達しないパスを除去) →
        トポロジカルソート → 各リソースの first-write / last-read 計算 → transient の
        物理割当 (aliasing) → パス境界のバリア計算。

        {RgExample}

        ## リソースモデル: External と Transient

        | 種別 | 寿命 | 例 | aliasing |
        | --- | --- | --- | --- |
        | External | グラフ外 (`ImportBuffer/Texture`) | swapchain、永続バッファ、アセット | 不可 |
        | Transient | グラフ内 (`CreateBuffer/Texture`) | 中間バッファ、ping-pong | 寿命非重複なら可 |

        Transient は同形 (バッファはサイズ、テクスチャは幅・高さ・フォーマット・種別) で
        寿命が重ならなければ **物理リソースを共有** (interval scheduling) します。実物:

        {StoryRef(ctx, "RenderGraph/Blur")}

        下のデモは反復ブラー 4 段 + 誰も読まないパス — 論理 5 transient が物理 2 本に
        alias され、DeadPass がカリングされる様子が **Log パネル**に出ます
        (`PhysicalTransientBufferCount` / `IsAliased` / `IsPassCulled` で観測):

        {StoryRef(ctx, "RenderGraph/Aliasing")}

        ## 自動バリアと ResourceUsage

        パスの Read/Write 宣言から `GpuStage` の遷移を計算し、バリアを自動挿入します。
        既定は compute 読み書きの扱いなので、**実際の使い方が違うときは usage を明示**します:
        `Write(h, ResourceUsage.CopyDest)` (RT → バッファのコピー先)、
        `Read(h, ResourceUsage.SampledInPixelShader)` (pixel シェーダで Load) など —
        これで Copy→Compute / Compute→PixelShader の遷移が正しく発行されます。

        > [!WARNING]
        > **Write が 1 つもないパスはデッドパスカリングで消えます。** RT へ直接描くだけの
        > パスは、参照する External バッファへの `Write` を「使用の宣言」として付けてください。

        ## ResourceSystem との関係 (混同注意)

        | 軸 | Luxel.Resources | Luxel.RenderGraph |
        | --- | --- | --- |
        | 単位 | アセット ((型, uri) ノード) | フレームのパス (pass × resource) |
        | 寿命 | 多フレーム (refcount + reload) | 1 フレーム (transient) + External |
        | 実行 | Io / Cpu / Gpu レーン | Graphics / Compute queue |

        両者は直交します。併用パターン: ResourceSystem でロードした `GpuTexture` を
        `ImportTexture` で External として取り込む。

        次: [Docs/ThreeD](story:Docs/ThreeD) — ECS と組み合わせて 3D を描きます。
        """, toc: true, fences: DocsFences));

    [Story("Docs/ThreeD", Width = 800, Height = 480, Order = 13)]
    public static Widget ThreeD(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # 3D と ECS

        3D は **ECS (Friflo Engine ECS のラッパ = Luxel.Ecs) + 抽出 + レンダーグラフ**の
        直列で描きます。シーングラフという独立した抽象はありません — 階層は ECS の
        `Parent`/`Children` コンポーネントが素直に表します。

        ## コンポーネントとシステム

        - `LocalTransform` / `GlobalTransform` — ローカル/ワールド変換。
          `TransformPropagateSystem.Run(world)` が Parent をたどって伝播
        - `MeshRef` / `Color3D` / `Visible` — 描画コンポーネント
        - メッシュは `CubeMesh` 等の頂点プル用バッファ (position + normal)

        ## 抽出 (IRenderExtractor)

        `Render3DExtractSystem` が ECS をクエリし、`InstanceData[]` (mat4 + color の SoA) を
        bindless バッファへ書きます。シーン層とレンダーグラフ層の橋渡しはこの
        **Extract 層だけ** — グラフ側は書き上がったバッファを Import するのみです。

        {StoryRef(ctx, "3D/EcsCubes")}

        ## 描画パターン

        graphics パスの lambda が `BeginRendering(rt, depth, ...)` で RT/Depth を直接管理し、
        instance 数ぶん `Draw` します。この形のまま合成が伸びます:

        - post-process 連鎖 (forward → blur → 加算合成) → [RenderGraph/Bloom3D](story:RenderGraph/Bloom3D)
        - world-space UI (2D UI を 3D 内の板にサンプリング) → [3D/WorldSpaceUI](story:3D/WorldSpaceUI)
        - shadow map (ライト視点 R32Float → bindless バッファ → 比較) → [3D/ShadowMap](story:3D/ShadowMap)

        shadow map も「R32F カラー RT + bindless バッファ経由の Load」— 専用のサンプラ比較
        ハードウェアに頼らない compute-first の流儀です。

        ## 設計ノート: UI に ECS を使わない理由

        - UI は reactive signals + 保持型ツリーのほうが DSL / 部分更新と相性が良い
        - ラベル 100 個を 100 entity にするとフレーム単位 query のオーバーヘッドが過剰
        - Bevy も UI は別系統 (bevy_ui)。Luxel でも `Luxel.UI` は signals 系のまま、
          **3D 側だけ ECS** です

        ## 行列レイアウトの罠

        > [!WARNING]
        > Slang/HLSL の既定行列レイアウトは column-major、`System.Numerics.Matrix4x4` は
        > row-major です。ルート引数で行列を渡すときは **CPU 側で `Matrix4x4.Transpose`**
        > を入れて整えます (per-instance 行列はシェーダ側で Load4 ×4 の行構築なので転置不要)。

        アニメーションを ECS へ流す例は [Animation/EcsClip](story:Animation/EcsClip) と
        [Animation/Graph](story:Animation/Graph) へ。
        """, toc: true, fences: DocsFences));
}
