using Luxel.Controls;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsRenderingLearn
{
    [Story("Learn/Graphics/2D/Overview", Order = 12, Toc = true)]
    public static StoryResult First2DScene() => $$"""
        # はじめての2D描画

        {{RenderingCourseCatalog.Meta("Learn/Graphics/2D/Overview", "Beginner", "Gallery WASM / Standalone / Headless", "WebGPU / Vulkan / DirectX 12 / Skia CPU", "基本的なC#")}}

        このコースの到達目標は、path・色・画像・camera transformを組み合わせた`Scene2D`を作り、render targetへ描画できることです。window、input、pointer hit test、appのframe loopは扱いません。

        ## 描画の最小フロー

        `Scene2D`はbackendに依存しない描画命令です。まずsceneを作り、`IRasterizer2D.CreateScene`でbackend用sessionへencodeし、targetへrenderします。

        ```csharp
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Rgba(47, 111, 237), 24, 24, 220, 120, 18);
        scene.FillCircle(Color2D.Rgba(255, 200, 87), 86, 84, 28);
        scene.StrokeLine(Color2D.Rgba(231, 234, 240), 4, 32, 176, 250, 176);

        using IRasterizer2D rasterizer = new SkiaRasterizer2D();
        using IRasterScene2D encoded = rasterizer.CreateScene(scene);
        encoded.Render(Camera2D.Pixels, target);
        ```

        `IRasterScene2D`はbackend resourceを所有するため、sessionをrasterizerより先にdisposeします。geometryが変わらない間はsessionを再利用できます。

        {{StoryReference.To("Examples/2D/SceneRender")}}

        ## APIの選び方

        | API | 用途 | 更新モデル | 注意点 |
        |---|---|---|---|
        | `Scene2D` | shape/path/imageを直接構築 | encode時のsnapshot | sessionの寿命をcallerが所有 |
        | `RetainedCanvas` | nodeを残してtransform/styleを更新 | dirty rangeを同期 | backendがincremental updateを支援するか確認 |
        | UI `Canvas2D` | GalleryやUI内の2D表示 | hostが描画 | standalone window APIではない |
        | `SkiaRasterizer2D` | headless/CI/CPU参照画像 | 同期RGBA target | image shape非対応、GPUとAAが完全一致しない |

        ## GPU固有の経路

        GPU backendでは`GpuDeviceRasterizer2D`、`GpuRasterTarget2D`、command recordingを使います。`Encode`やcommand submissionは`IRasterizer2D`の基本概念を理解した後に選ぶ最適化・統合APIです。

        ```csharp
        using var rasterizer = new GpuDeviceRasterizer2D(device);
        using var encoded = rasterizer.Encode(scene);
        using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
        rasterizer.Render(command, encoded, Camera2D.Pixels, width, height, framebuffer);
        command.Finish();
        device.MainQueue.Submit(command);
        ```

        ## 次に学ぶこと

        {{StoryReference.To("Examples/2D/Shapes")}}

        次は[Path、fill、stroke](story:Learn/Graphics/2D/Paths)でcontourの組み立て方を学びます。desktop/headlessの完全なprojectはHeadlessScene2Dをsource recipeとして参照できます。
        """;
}
