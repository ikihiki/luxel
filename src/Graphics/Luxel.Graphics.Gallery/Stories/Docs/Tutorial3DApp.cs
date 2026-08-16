using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>最小のGPU描画から画面効果を持つ3Dシーンまでを組み立てる。</summary>
[StoryMeta("Tutorials/3DApp")]
public static partial class Tutorial3DApp
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # 3Dアプリを作る

        {{Toc()}}

        このコースでは、GPUサーフェスを一色で消去する段階から始め、頂点を描画し、depthを使って前後関係を解決し、最後にシーン用のpassを重ねます。完成時には「初期化」「リソース」「毎フレームの描画」を分けて説明できる3Dアプリになります。

        ## 完成する構成

        ```text
        App
        └─ GpuView
           ├─ scoped resources: shader / pipeline / buffer
           ├─ camera and scene state
           └─ render callback: acquire → encode → submit
        ```

        ## 学習順

        1. [最初のフレーム](story:Tutorials/3DApp/FirstFrame)
        2. [奥行きとシーン](story:Tutorials/3DApp/DepthAndScene)
        3. [画面効果を加えて完成](story:Tutorials/3DApp/Finish)

        ## 前提

        GPU backendを利用できる環境が必要です。Native版ではVulkanまたはDirectX 12、Blazor版ではWebGPUを使います。APIごとの差はbackendへ閉じ、Story本体では共通の`GpuDevice`契約を使います。
        """;

    [Story]
    public static StoryResult FirstFrame(StoryContext ctx) => $$"""
        # 最初のフレームを描く

        {{Toc()}}

        最初は三角形を一つ描きます。ここで必要なのはshader、graphics pipeline、描画先のsurfaceだけです。リソースはStoryの`ScopedResources`に作り、プレビューを閉じたときにまとめて解放します。

        {{StoryRef("Tutorials/3DApp/TriangleSample")}}

        ## コードを三つに分ける

        - **作成時** — shaderとpipelineを読み込み、再利用するGPUリソースを準備する
        - **状態** — camera、transform、materialなど、フレーム間で変わる値を保持する
        - **描画時** — surfaceを取得し、pipelineと引数を設定してdrawを発行する

        この分離により、毎フレームpipelineを作り直す事故と、画面を閉じてもGPUリソースが残る事故を避けられます。

        次は[奥行きとシーン](story:Tutorials/3DApp/DepthAndScene)へ進みます。
        """;

    [Story]
    public static StoryResult DepthAndScene(StoryContext ctx) => $$"""
        # 奥行きとシーンを加える

        {{Toc()}}

        複数の面を3D空間へ置くと、描画順だけでは前後関係を表せません。color targetと同じ大きさのdepth targetを用意し、pipelineのdepth test/writeを有効にします。cameraはviewとprojectionを作り、objectのmodel行列と合わせてshaderへ渡します。

        {{StoryRef("Tutorials/3DApp/DepthSample", knobs: true)}}

        ## 3DシーンへUIを置く

        world-space UIも通常のscene objectとして扱い、cameraを通して投影します。UI専用の別ウィンドウへ逃がさず、scene passの中で深度や合成順を明示します。

        {{StoryRef("Tutorials/3DApp/WorldSpaceUiSample")}}

        次は[画面効果を加えて完成](story:Tutorials/3DApp/Finish)へ進みます。
        """;

    [Story]
    public static StoryResult Finish(StoryContext ctx) => $$"""
        # 画面効果を加えて完成する

        {{Toc()}}

        scene colorを直接presentせず、一度textureへ描画すると、後段でbloomやtone mappingを追加できます。各passは入力と出力を宣言し、同じtextureを同時に読み書きしないようにします。

        {{StoryRef("Tutorials/3DApp/BloomSample")}}

        ## 完成チェック

        - resize時にcolor/depth targetを同じ寸法で再作成する
        - pipelineやbufferを毎フレーム生成していない
        - cameraのaspectをsurface寸法から更新する
        - 不透明、透明、post-processの順序が明示されている
        - プレビューを閉じるとscoped resourceが解放される

        機能を増やす場合は[Graphics Learn](story:Learn/Graphics/Overview)、pass依存を体系化する場合は[RenderGraph](story:Learn/Graphics/RenderGraph/Overview)へ進んでください。
        """;
}
