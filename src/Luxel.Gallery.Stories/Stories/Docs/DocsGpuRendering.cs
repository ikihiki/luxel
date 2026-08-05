using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>RenderGraph and 3D/ECS learning units.</summary>
public static partial class DocsGpu
{
    [Story("Reference/Guides/RenderGraph", Order = 12, Toc = true)]
    public static StoryResult RenderGraphDocs(StoryContext ctx) => $$"""
        # レンダーグラフ (Luxel.Graphics.RenderGraph)

        APIを使いながら順番に学ぶ場合は、[RenderGraph入門](story:Learn/Graphics/RenderGraph/Overview)から始めてください。このページは設計とAPIをまとめて確認するreferenceです。

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

        Setup でパスと依存を宣言し、`Execute(cmd)` 1 回で Compile + Execute が走ります。Compile 相の仕事: デッドパスカリング (External への書き込みに到達しないパスを除去) → 各リソースの first-write / last-read 計算 → transient の物理割当 (aliasing) → パス境界のバリア計算。現在の実行順はトポロジカルソートではなく**パスの登録順**なので、producerをconsumerより先に登録します。

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

        {{StoryRef(ctx, "Examples/RenderGraph/Blur")}}

        下のデモは反復ブラー 4 段 + 誰も読まないパス — 論理 5 transient が物理 2 本に alias され、DeadPass がカリングされる様子が **Log パネル**に出ます (`PhysicalTransientBufferCount` / `IsAliased` / `IsPassCulled` で観測):

        {{StoryRef(ctx, "Examples/RenderGraph/Aliasing")}}

        ## 自動バリアと ResourceUsage

        パスの Read/Write 宣言から `GpuStage` の遷移を計算し、バリアを自動挿入します。既定は compute 読み書きの扱いなので、**実際の使い方が違うときは usage を明示**します: `Write(h, ResourceUsage.CopyDest)` (RT → バッファのコピー先)、`Read(h, ResourceUsage.SampledInPixelShader)` (pixel シェーダで Load) など — これで Copy→Compute / Compute→PixelShader の遷移が正しく発行されます。

        > [!WARNING]
        > **Write が 1 つもないパスはデッドパスカリングで消えます。** RT へ直接描くだけのパスは、参照する External バッファへの `Write` を「使用の宣言」として付けてください。

        ## ResourceSystem との関係 (混同注意)

        | 軸 | Luxel.Resources | Luxel.Graphics.RenderGraph |
        | --- | --- | --- |
        | 単位 | アセット ((型, uri) ノード) | フレームのパス (pass × resource) |
        | 寿命 | 多フレーム (refcount + reload) | 1 フレーム (transient) + External |
        | 実行 | Io / Cpu / Gpu レーン | Graphics / Compute queue |

        両者は直交します。併用パターン: ResourceSystem でロードした `GpuTexture` を `ImportTexture` で External として取り込む。

        次: [Reference/Guides/ThreeD](story:Reference/Guides/ThreeD) — ECS と組み合わせて 3D を描きます。
        """;

    [Story("Reference/Guides/ThreeD", Order = 13, Toc = true)]
    public static StoryResult ThreeD(StoryContext ctx) => $$"""
        # 3D と ECS

        3D は **ECS (Friflo Engine ECS のラッパ = Luxel.Ecs) + 抽出 + レンダーグラフ**の直列で描きます。シーングラフという独立した抽象はありません — 階層は ECS の `Parent`/`Children` コンポーネントが素直に表します。

        ## コンポーネントとシステム

        - `LocalTransform` / `GlobalTransform` — ローカル/ワールド変換。`TransformPropagateSystem.Run(world)` が Parent をたどって伝播
        - `MeshRef` / `Color3D` / `Visible` — 描画コンポーネント
        - メッシュは `CubeMesh` 等の頂点プル用バッファ (position + normal)

        ## 抽出 (IRenderExtractor)

        `Render3DExtractSystem` が ECS をクエリし、`InstanceData[]` (mat4 + color の SoA) を bindless バッファへ書きます。シーン層とレンダーグラフ層の橋渡しはこの **Extract 層だけ** — グラフ側は書き上がったバッファを Import するのみです。

        {{StoryRef(ctx, "Examples/3D/EcsCubes")}}

        ## 描画パターン

        graphics パスの lambda が `BeginRendering(rt, depth, ...)` で RT/Depth を直接管理し、instance 数ぶん `Draw` します。この形のまま合成が伸びます:

        - post-process 連鎖 (forward → blur → 加算合成) → [Examples/RenderGraph/Bloom3D](story:Examples/RenderGraph/Bloom3D)
        - world-space UI (2D UI を 3D 内の板にサンプリング) → [Examples/3D/WorldSpaceUI](story:Examples/3D/WorldSpaceUI)
        - shadow map (ライト視点 R32Float → bindless バッファ → 比較) → [Examples/3D/ShadowMap](story:Examples/3D/ShadowMap)

        shadow map も「R32F カラー RT + bindless バッファ経由の Load」— 専用のサンプラ比較ハードウェアに頼らない compute-first の流儀です。

        ## OrbitCamera — 軌道カメラ

        注視点を中心に yaw/pitch/distance で周回する軌道カメラ `OrbitCamera` (`Luxel.Mathematics`) が `ViewProjection` (`view * proj`) を計算します。`Orbit(dYaw, dPitch)` でドラッグ回転 (pitch はジンバルロック手前でクランプ)、`Dolly(factor, min, max)` でズームします。ルート引数へ渡すときは行列レイアウトの罠 (下記) に従い `Matrix4x4.Transpose` を入れます。2D の `CameraRig2D` (追従/シェイク/境界) に対し、3D は v1 では viewProj 算出とドラッグ操作までがスコープです。

        ## 設計ノート: UI に ECS を使わない理由

        - UI は reactive signals + 保持型ツリーのほうが DSL / 部分更新と相性が良い
        - ラベル 100 個を 100 entity にするとフレーム単位 query のオーバーヘッドが過剰
        - Bevy も UI は別系統 (bevy_ui)。Luxel でも `Luxel.UI` は signals 系のまま、**3D 側だけ ECS** です

        ## 行列レイアウトの罠

        > [!WARNING]
        > Slang/HLSL の既定行列レイアウトは column-major、`System.Numerics.Matrix4x4` は row-major です。ルート引数で行列を渡すときは **CPU 側で `Matrix4x4.Transpose`** を入れて整えます (per-instance 行列はシェーダ側で Load4 ×4 の行構築なので転置不要)。

        アニメーションを ECS へ流す例は [Examples/Animation/EcsClip](story:Examples/Animation/EcsClip) と [Examples/Animation/Graph](story:Examples/Animation/Graph) へ。
        """;
}
