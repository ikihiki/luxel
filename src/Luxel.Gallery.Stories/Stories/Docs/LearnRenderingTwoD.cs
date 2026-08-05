using Luxel.Controls;

namespace Luxel.Gallery.Stories;

public static class LearnRenderingTwoD
{
    private static StoryResult Page(string path, string title, string objective, string mentalModel,
        string example, string contracts, StoryReference sample)
        => $$"""
        # {{title}}

        {{RenderingCourseCatalog.Meta(path, "Intermediate", "Gallery WASM / Standalone / Headless", "WebGPU / Vulkan / DirectX 12 / Skia CPU", objective)}}

        ## このページでできるようになること

        {{objective}}

        ## メンタルモデル

        {{mentalModel}}

        ## 最小例

        {{new DocMarkdown(example)}}

        ## APIの契約と注意点

        {{contracts}}

        {{sample}}
        """;

    [Story("Learn/Graphics/2D/Paths", Order = 11, Toc = true)]
    public static StoryResult Paths() => Page("Learn/Graphics/2D/Paths", "Path、fill、stroke",
        "contourを開始・確定し、open strokeとclosed fillを意図どおり描き分ける。",
        "`BeginFill`/`BeginStroke`でshapeを開始し、`MoveTo`でcontourを開始します。`MoveTo`は直前のcontourを確定し、`End`がshapeをsceneへ追加します。curveはCPUでline segmentへflattenされます。",
        """"
```csharp
var scene = new Scene2D();
scene.BeginStroke(Color2D.Rgba(245, 158, 11), 6)
    .MoveTo(20, 160).LineTo(90, 40).LineTo(160, 160).End();
scene.BeginFill(Color2D.Rgba(59, 130, 246), FillRule.EvenOdd)
    .MoveTo(220, 170).LineTo(285, 45).LineTo(350, 170).Close().End();
```
"""",
        "fillとimage contourはencode時に閉じます。strokeは`Close`を呼んだcontourだけが閉じます。`LineTo`/`QuadTo`/`CubicTo`にはcurrent contourが必要で、2点未満のcontourは破棄されます。`FlattenTolerance`の単位はworld unitです。",
        StoryReference.To("Examples/2D/Rasterizer/InputPathsLive"));

    [Story("Learn/Graphics/2D/Compositing", Order = 12, Toc = true)]
    public static StoryResult Compositing() => Page("Learn/Graphics/2D/Compositing", "Fill ruleと合成",
        "NonZero/EvenOdd、painter order、premultiplied source-overを説明できる。",
        "NonZeroはwinding count、EvenOddは交差回数の偶奇で内外を決めます。shapeはsceneへ追加した順に描かれ、後のshapeが前の結果へsource-over合成されます。",
        """"
```csharp
scene.BeginFill(Color2D.Rgba(59, 130, 246, 190), FillRule.EvenOdd);
AddCircle(scene, 120, 100, 70);
AddCircle(scene, 120, 100, 34);
scene.EndFill();
```
"""",
        "`Color2D`はredがlow byteのpacked表現です。`0xAARRGGBB`を想定したliteralは避け、`Color2D.Rgba(r,g,b,a)`を使います。premultiplied colorはRGBにもalphaを掛け、source-overは`out = src + dst × (1-src.a)`です。",
        StoryReference.To("Examples/2D/Rasterizer/CompositeLive"));

    [Story("Learn/Graphics/2D/Images", Order = 13, Toc = true)]
    public static StoryResult Images() => Page("Learn/Graphics/2D/Images", "Image、sprite、atlas",
        "image全体、sub-rect、sprite atlasの描画方法を選ぶ。",
        "image shapeもvector shapeと同じpainter orderに参加します。`ImageRect`は全体、`ImageSubRect`はatlasの一部、`DrawSprite`は名前付きframeを描きます。",
        """"
```csharp
scene.ImageSubRect(imageIndex, imageStride,
    srcX: 32, srcY: 0, srcWidth: 32, srcHeight: 32,
    x: 24, y: 24, width: 96, height: 96);
```
"""",
        "source座標とstrideはpixel単位です。bufferはencoded scene/sessionより長く生存させます。RGBAはpremultipliedを前提とし、atlas端のfilter bleedを避けます。現在のSkia backendはimage shapeを支援しないため`Rasterizer2DCapabilities.BindlessImages`を確認します。",
        StoryReference.To("Examples/2D/Sprites"));

    [Story("Learn/Graphics/2D/Camera", Order = 14, Toc = true)]
    public static StoryResult Camera() => Page("Learn/Graphics/2D/Camera", "Camera2Dと描画変換",
        "world-space geometryをCamera2Dでscreenへ変換する。",
        "`Camera2D`はaffine変換です。`screen.x=A×world.x+C×world.y+E`、`screen.y=B×world.x+D×world.y+F`として、同じsceneをpan/zoomした結果へ写します。",
        """"
```csharp
Camera2D camera = Camera2D.Create(
    scale: 1.5f,
    worldCenter: new Vector2(160, 90),
    screenW: width,
    screenH: height);
encoded.Render(camera, target);
```
"""",
        "geometry座標はworld-space、stroke widthはscreen pixelです。rasterizerへcameraを渡す場合、geometryを手動で同じcamera変換してはいけません。resize時はscreen寸法からcameraを作り直し、0×0 targetではrenderしません。input、pointer逆変換、hit testはこのコースの対象外です。",
        StoryReference.To("Examples/2D/CameraRig"));

    [Story("Learn/Graphics/2D/Backends", Order = 15, Toc = true)]
    public static StoryResult Backends() => Page("Learn/Graphics/2D/Backends", "GPUとSkia backend",
        "同じScene2Dに適したrasterizerとtargetを選ぶ。",
        "描画コードは`IRasterizer2D`へ依存し、GPU固有のcommand recordingやbindless imageはcapabilityで分岐します。",
        """"
```csharp
using IRasterizer2D rasterizer = useCpu
    ? new SkiaRasterizer2D()
    : new GpuDeviceRasterizer2D(device);
using IRasterScene2D encoded = rasterizer.CreateScene(scene);
encoded.Render(Camera2D.Pixels, target);
```
"""",
        "GPUは`GpuCommandRecording`、`BindlessImages`、`RetainedIncrementalUpdates`、Skiaは`CpuRgbaTarget`を提供します。sessionをrasterizerより先にdisposeし、同じrasterizerを複数threadから同時使用しません。browser live sampleはWebGPU、headless referenceはSkiaを使います。",
        StoryReference.To("Examples/2D/SceneRender"));

    [Story("Learn/Graphics/2D/RetainedCanvas", Order = 16, Toc = true)]
    public static StoryResult RetainedCanvasPage() => Page("Learn/Graphics/2D/RetainedCanvas", "RetainedCanvas",
        "immediate Scene2Dとpersistent node treeを使い分ける。",
        "`Scene2D`はencode時のsnapshotです。`RetainedCanvas`はnode treeとencoded display dataを分け、content、transform、style、clip、orderのmutationを追跡します。",
        """"
```csharp
var canvas = new RetainedCanvas();
UiNode card = canvas.AddChild(canvas.Root);
card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 180, 90, 12);
card.Transform = Affine2D.Translate(24, 18);
card.Color = Color2D.Rgba(47, 111, 237);
```
"""",
        "`HasPendingChanges`はqueued mutationを示すだけでuploadは行いません。backendとの同期はrender時です。transform/style変更はgeometryを再生成せず、contentやtree構造の変更はsegment更新またはfull rebuildを発生させます。",
        StoryReference.To("Examples/2D/RetainedCanvasLive"));

    [Story("Learn/Graphics/2D/IncrementalUpdates", Order = 17, Toc = true)]
    public static StoryResult IncrementalUpdates() => Page("Learn/Graphics/2D/IncrementalUpdates", "増分更新",
        "mutationの種類とGPU upload量の関係を観測する。",
        "retained backendはstable slotとdirty rangeを使い、transform/style-only updateを小さなin-place writeへ変換します。geometryや構造が変わるとsegment writeやfull rebuildになります。",
        """"
```csharp
node.Transform = Affine2D.Translate(40, 12);
encoded.Render(camera, target);
Console.WriteLine(canvas.LastTransformWrites);
Console.WriteLine(canvas.LastSegmentBytesWritten);
Console.WriteLine(canvas.LastWasFullRebuild);
```
"""",
        "`LastTransformWrites`、`LastStyleWrites`、`LastSegmentBytesWritten`、`LastWasFullRebuild`は直近の同期結果です。値を読む前にrenderを完了し、`HasPendingChanges`と混同しません。",
        StoryReference.To("Examples/2D/Rasterizer/RetainedUpdatesLive"));
}
