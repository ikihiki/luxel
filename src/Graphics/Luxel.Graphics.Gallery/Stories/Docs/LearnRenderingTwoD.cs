using Luxel.Controls;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Graphics/2D")]
public static class LearnRenderingTwoD
{
    private static StoryResult Page(string path, string title, string objective, string mentalModel,
        string example, string contracts, StoryReference sample,
        string environment = "Gallery WASM / Standalone / Headless")
        => $$"""
        # {{title}}

        {{Toc()}}

        {{RenderingCourseCatalog.Meta(path, "Intermediate", environment, "WebGPU / Vulkan / DirectX 12 / Skia CPU", objective)}}

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

    [Story]
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
        StoryReference.To("Examples/2D/InputPaths"));

    [Story]
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
        StoryReference.To("Examples/2D/Composite"));

    [Story]
    public static StoryResult Images() => Page("Learn/Graphics/2D/Images", "Image、sprite、atlas",
        "画像全体、atlasのsub-rect、名前付きsprite、animation frameを用途に応じて描き分ける。",
        "`ImageRect`は1枚のRGBA画像全体を矩形へ写します。atlasは複数画像を1枚へ詰めたsourceで、`ImageSubRect`がpixel座標の一部だけを選びます。`SpriteAtlas`は名前から`SpriteRect`を引くmetadataで、`DrawSprite`がsub-rect選択・pivot・scaleをまとめます。`SpriteAnimation`は現在のframe名を選び、最終的には同じ`DrawSprite`へ渡されます。",
        """"
```csharp
// 画像全体
scene.ImageRect(imageIndex, imageStride, imageWidth, imageHeight,
    x: 16, y: 16, width: 64, height: 64);

// atlas内の右上32×32だけを描画
scene.ImageSubRect(atlasIndex, atlasWidth,
    srcX: 32, srcY: 0, srcW: 32, srcH: 32,
    x: 112, y: 16, w: 64, h: 64);

var atlas = new SpriteAtlas("player.png", new Dictionary<string, SpriteRect>
{
    ["idle_0"] = new(0, 0, 32, 32, PivotX: 16, PivotY: 32),
    ["idle_1"] = new(32, 0, 32, 32, PivotX: 16, PivotY: 32),
});
atlas.Bind(atlasIndex, atlasWidth: 64, atlasHeight: 32);
scene.DrawSprite(atlas, "idle_0", x: 220, y: 96, scale: 2f);

var animation = new SpriteAnimation("idle_", frameCount: 2, fps: 8f);
animation.Update(deltaSeconds);
scene.DrawSprite(atlas, animation, x: 300, y: 96, scale: 2f);
```
"""",
        "`srcStride`はbyte数ではなくpixel単位の行幅です。source bufferはRGBA8を密に並べ、sessionより長く生存させます。`SpriteAtlas`はpack処理を行わず、外部toolまたはasset metadataが作った矩形を保持します。pivotは指定world座標へ合わせるsprite内の基準点です。RGBAはpremultipliedを前提とし、atlas sub-rectは隣接cellへfilter bleedしないよう範囲内へclampされます。Skia backendはbindless image shapeを支援しないため`Rasterizer2DCapabilities.BindlessImages`を確認します。",
        StoryReference.To("Examples/2D/Sprites"));

    [Story]
    public static StoryResult Camera() => Page("Learn/Graphics/2D/Camera", "Camera2Dと描画変換",
        "world-space geometryをCamera2Dでscreenへ変換する。",
        "`Camera2D`はaffine変換です。`screen.x=A×world.x+C×world.y+E`、`screen.y=B×world.x+D×world.y+F`として、同じsceneをpan/zoomした結果へ写します。",
        """"
```csharp
// worldの(160, 90)をscreen中央へ移し、その点を中心に1.5倍する。
Camera2D camera = Camera2D.Create(
    scale: 1.5f,
    worldCenter: new Vector2(160, 90),
    screenW: width,
    screenH: height);
encoded.Render(camera, target);
```
"""",
        "geometry座標はworld-space、stroke widthはscreen pixelです。rasterizerへcameraを渡す場合、geometryを手動で同じcamera変換してはいけません。resize時はscreen寸法からcameraを作り直し、0×0 targetではrenderしません。input、pointer逆変換、hit testはこのコースの対象外です。",
        StoryReference.To("Examples/2D/CameraTransform"));

    [Story]
    public static StoryResult Backends() => Page("Learn/Graphics/2D/Backends", "GPUとSkia backend",
        "同じScene2DをGPUとSkiaで描画し、targetとcapabilityの違いを説明する。",
        "描画内容は`Scene2D`、backend選択は`IRasterizer2D`、出力先は`IRasterTarget2D`へ分かれます。GPUはcommandへ記録し、SkiaはCPU RGBAへ同期描画します。サンプルは両方の結果を`GpuView`で並べますが、Skia側の`GpuView`はCPU結果を表示するtransportであり、rasterize自体はSkiaです。",
        """"
```csharp
using var gpu = new GpuDeviceRasterizer2D(device);
using IRasterScene2D gpuScene = gpu.CreateScene(scene);
gpuScene.Render(Camera2D.Pixels,
    new GpuRasterTarget2D(command, framebuffer, width, height));

using var skia = new SkiaRasterizer2D();
using IRasterScene2D skiaScene = skia.CreateScene(scene);
var cpuTarget = new SkiaRasterTarget2D(width, height);
skiaScene.Render(Camera2D.Pixels, cpuTarget);
```
"""",
        "GPUは`GpuCommandRecording`、`BindlessImages`、`RetainedIncrementalUpdates`を提供し、Skiaは`CpuRgbaTarget`を提供します。image shapeはSkia非対応なので、backend比較にはvector-only sceneを使います。sessionをrasterizerより先にdisposeし、同じrasterizerを複数threadから同時使用しません。",
        StoryReference.To("Examples/2D/Backends"),
        environment: "Native Gallery / Standalone / Headless");

    [Story]
    public static StoryResult IncrementalUpdates() => Page("Learn/Graphics/2D/IncrementalUpdates", "RetainedCanvasと増分更新",
        "RetainedCanvasを速度改善のために導入し、mutationの種類とGPU upload量の関係を観測する。",
        "`Scene2D`はencode時のsnapshotです。同じcontentを繰り返し描き、一部のtransform/styleだけを変える場合、`RetainedCanvas`はpersistent node、stable slot、dirty rangeを使って再encodeを避けます。transform/style-only updateは小さなin-place writeになり、contentやtree構造の変更はsegment writeまたはfull rebuildになります。",
        """"
```csharp
var canvas = new RetainedCanvas();
UiNode card = canvas.AddChild(canvas.Root);
card.Content = new Scene2D()
    .FillRoundedRect(Color2D.White, 0, 0, 180, 90, 12);
card.Transform = Affine2D.Translate(24, 18);
card.Color = Color2D.Rgba(47, 111, 237);

using IRasterScene2D encoded = rasterizer.CreateScene(canvas);
encoded.Render(camera, target); // 初回はscene全体を同期

card.Transform = Affine2D.Translate(190, 92);
card.Color = Color2D.Rgba(34, 197, 94);
encoded.Render(camera, target); // transform/styleのdirty rangeだけを同期

Console.WriteLine(canvas.LastTransformWrites);
Console.WriteLine(canvas.LastStyleWrites);
Console.WriteLine(canvas.LastSegmentBytesWritten);
Console.WriteLine(canvas.LastWasFullRebuild);
```
"""",
        "`RetainedCanvas`は描画表現を増やすAPIではなく、同じsceneの更新コストを減らす最適化です。毎frame全geometryが変わる場合は`Scene2D`の再encodeの方が単純です。`HasPendingChanges`はqueued mutationを示すだけでuploadは行いません。`LastTransformWrites`、`LastStyleWrites`、`LastSegmentBytesWritten`、`LastWasFullRebuild`は直近のrender同期結果なので、値はrender後に読みます。",
        StoryReference.To("Examples/2D/RetainedUpdates"));

}
