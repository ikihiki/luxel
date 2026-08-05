using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static partial class DocsRenderingLearn
{
    [Story("Learn/Grapics/2D/Overview", Order = 10)]
    public static Widget First2DScene(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめての2Dシーン

        {{RenderingCourseCatalog.Meta("Learn/Grapics/2D/Overview", "Beginner", "Gallery / Standalone / Headless", "Vulkan / DirectX 12 / Skia CPU", "RenderGraph")}}

        {{StoryRef(ctx, "Examples/2D/Shapes")}}

        `Scene2D`は「何を描くか」をCPU側で組み立てる最小APIです。次のコードだけで角丸矩形、円、線を含むsceneを作れます。

        ```csharp
        using Luxel.Graphics.TwoD;

        var scene = new Scene2D();
        scene.FillRoundedRect(0xFF2F6FED, 24, 24, 220, 120, 18);
        scene.FillCircle(0xFFFFC857, 86, 84, 28);
        scene.StrokeLine(0xFFE7EAF0, 4, 32, 176, 250, 176);
        ```

        GPUへ出すstandalone側は`GpuDeviceRasterizer2D`がsceneをencodeし、commandへrenderを記録します。geometryが変わらないなら`encoded`を毎frame作り直さず保持します。

        ```csharp
        using var rasterizer = new GpuDeviceRasterizer2D(device);
        using var encoded = rasterizer.Encode(scene);
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();

        rasterizer.Render(cmd, encoded, Camera2D.Pixels,
            width, height, framebuffer);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
        ```

        ## 4つの入口の選び方

        | API | 選ぶ場面 | GPU | 注意点 |
        | --- | --- | --- | --- |
        | `Scene2D` | shape/pathを直接作る | backend次第 | `CreateScene`時点のsnapshot。session寿命をcallerが所有 |
        | `RetainedCanvas` | objectが残りtransform/styleだけ変わる | 不要 | backend-neutral。`Invalidate()`連打ではなく部分更新を使う |
        | UI `Canvas2D` | GalleryやUIへ小さな図を埋め込む | hostが提供 | standalone window APIではない |
        | `SkiaRasterizer2D` | CI、headless test、CPU参照画像 | 不要 | 同期RGBA target。image shape非対応、AA edgeはGPUと完全一致しない |

        retained treeではnodeを保持し、変更箇所だけ更新します。

        ```csharp
        var canvas = new RetainedCanvas();       // headlessでも構築可能
        UiNode card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D()
            .FillRoundedRect(Color2D.White, 20, 20, 240, 120, 16);
        card.Color = 0xFF2F6FED;
        card.Transform = Affine2D.Translate(12, 8);

        if (canvas.HasPendingChanges)
            Console.WriteLine("次のGPU renderで差分を反映する");
        ```

        UI内なら`Canvas2D(draw: scene => ...)`、headlessなら`SkiaRasterizer2D`からscene/sessionと`SkiaRasterTarget2D`を作ります。stroke widthはscreen pixel、world座標変換はcameraが担当します。透明imageはpremultiplied RGBAを前提にし、Skia backendではGPU bindless imageを描けません。実window経路はCPU pixelをpresentするupload経路をまだ持たないためGPU固定です。
        """, toc: true);
    }

}
