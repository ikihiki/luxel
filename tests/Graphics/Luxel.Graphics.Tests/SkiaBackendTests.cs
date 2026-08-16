using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>SkiaSharp CPU バックエンド — **GpuDevice なし**で 2D システム (保持ツリー/即時シーン/
/// UiHost) をラスタライズして検証する。GPU (GpuDeviceRasterizer2D) とは AA 実装が違うため、
/// 検証は形状内部のピクセル・構造・増分更新統計で行う (エッジ画素は比較しない)。</summary>
public class SkiaBackendTests
{
    private static (byte R, byte G, byte B, byte A) Px(byte[] rgba, int w, int x, int y)
    {
        uint p = SkiaRenderer.PixelAt(rgba, w, x, y);
        return ((byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24));
    }

    private static void AssertNear(byte expected, byte actual, int tol = 8)
        => Assert.True(Math.Abs(expected - actual) <= tol, $"expected≈{expected} actual={actual}");

    private static byte[] Render(RetainedCanvas canvas, Camera2D camera, int width, int height, bool transparent = false)
    {
        using var rasterizer = new SkiaRasterizer2D();
        using IRasterScene2D scene = rasterizer.CreateScene(canvas);
        var target = new SkiaRasterTarget2D((uint)width, (uint)height);
        scene.Render(camera, target, transparent);
        return target.ToArray();
    }

    private static byte[] Render(Scene2D source, Camera2D camera, int width, int height, bool transparent = false)
    {
        using var rasterizer = new SkiaRasterizer2D();
        using IRasterScene2D scene = rasterizer.CreateScene(source);
        var target = new SkiaRasterTarget2D((uint)width, (uint)height);
        scene.Render(camera, target, transparent);
        return target.ToArray();
    }


    [Fact]
    public void RasterizerContract_DoesNotExposeLegacyGpuApi()
    {
        System.Reflection.Assembly assembly = typeof(IRasterizer2D).Assembly;
        Assert.Null(assembly.GetType("Luxel.Graphics.TwoD.Rasterizer2D"));
        Assert.Null(assembly.GetType("Luxel.Graphics.TwoD.EncodedScene"));

        System.Reflection.ConstructorInfo constructor = Assert.Single(typeof(RetainedCanvas).GetConstructors());
        Assert.Empty(constructor.GetParameters());
        Assert.DoesNotContain(typeof(RetainedCanvas).GetMethods(), method => method.Name == "Render");
        Assert.DoesNotContain(typeof(RetainedCanvas).GetProperties(), property => property.Name == "Rasterizer");
    }

    [Fact]
    public void RasterizerContract_ReportsCapabilitiesAndRejectsWrongTarget()
    {
        using var rasterizer = new SkiaRasterizer2D();
        Assert.Equal("SkiaSharp", rasterizer.Name);
        Assert.True(rasterizer.Capabilities.HasFlag(Rasterizer2DCapabilities.CpuRgbaTarget));
        Assert.False(rasterizer.Capabilities.HasFlag(Rasterizer2DCapabilities.GpuCommandRecording));

        using IRasterScene2D scene = rasterizer.CreateScene(new Scene2D().FillRect(Color2D.Red, 0, 0, 10, 10));
        Assert.Throws<ArgumentException>(() => scene.Render(Camera2D.Pixels, new FakeTarget()));
    }

    [Fact]
    public void RasterizerContract_ReusesRetainedSessionAcrossChanges()
    {
        var canvas = new RetainedCanvas();
        UiNode node = canvas.AddChild(canvas.Root);
        node.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 20, 20);
        node.Color = Color2D.Red;

        using var rasterizer = new SkiaRasterizer2D();
        using IRasterScene2D scene = rasterizer.CreateScene(canvas);
        var target = new SkiaRasterTarget2D(50, 50);
        scene.Render(Camera2D.Pixels, target);
        Assert.Equal(Color2D.Red, SkiaRenderer.PixelAt(target.ToArray(), 50, 10, 10));

        node.Color = Color2D.Blue;
        scene.Render(Camera2D.Pixels, target);
        Assert.Equal(Color2D.Blue, SkiaRenderer.PixelAt(target.ToArray(), 50, 10, 10));
    }

    [Fact]
    public void RasterizerContract_RejectsBindlessImages()
    {
        using var rasterizer = new SkiaRasterizer2D();
        Scene2D scene = new Scene2D().ImageRect(1, 4, 4, 4, 0, 0, 4, 4);
        Assert.Throws<NotSupportedException>(() => rasterizer.CreateScene(scene));
    }

    [Fact]
    public void RasterizerContract_RejectsBindlessAlphaMasks()
    {
        using var rasterizer = new SkiaRasterizer2D();
        var scene = new Scene2D();
        scene.MaskSubRect(1, 4, 0, 0, 1, 1, 0, 0, 1, 1, Color2D.White);
        Assert.Throws<NotSupportedException>(() => rasterizer.CreateScene(scene));
    }

    [Fact]
    public void RasterizerContract_RejectsUseAfterSessionOrRasterizerDispose()
    {
        var rasterizer = new SkiaRasterizer2D();
        IRasterScene2D scene = rasterizer.CreateScene(new Scene2D().FillRect(Color2D.Red, 0, 0, 10, 10));
        var target = new SkiaRasterTarget2D(10, 10);

        rasterizer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scene.Render(Camera2D.Pixels, target));

        scene.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scene.Render(Camera2D.Pixels, target));
        Assert.Throws<ObjectDisposedException>(() => rasterizer.CreateScene(new Scene2D()));
    }

    [Fact]
    public void ProjectDependencies_KeepSkiaBehindTheBackendAssembly()
    {
        string[] coreReferences = typeof(IRasterizer2D).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.DoesNotContain(coreReferences, name => name.Contains("Skia", StringComparison.OrdinalIgnoreCase));

        string[] skiaReferences = typeof(SkiaRasterizer2D).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.Contains("Luxel.Graphics.TwoD", skiaReferences);
    }

    [Fact]
    public void LegacySkiaRendererFacadeStillWorks()
    {
        byte[] pixels = SkiaRenderer.RenderRgba(new Scene2D().FillRect(Color2D.Green, 0, 0, 8, 8),
            Camera2D.Pixels, 8, 8);
        Assert.Equal(Color2D.Green, SkiaRenderer.PixelAt(pixels, 8, 4, 4));
    }

    private sealed class FakeTarget : IRasterTarget2D
    {
        public uint Width => 1;
        public uint Height => 1;
    }

    // ---- 保持ツリー ----

    [Fact]
    public void FillRect_NodeColor_RendersWithoutDevice()
    {
        var canvas = new RetainedCanvas();   // ヘッドレス — GpuDevice 不要
        UiNode n = canvas.AddChild(canvas.Root);
        n.Content = new Scene2D().FillRect(Color2D.White, 10, 10, 40, 40);   // 白描き
        n.Color = Color2D.Red;                                               // ノード色が実色

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Red, SkiaRenderer.PixelAt(px, 100, 30, 30));    // 矩形内部
        Assert.Equal(Color2D.Rgba(255, 255, 255), SkiaRenderer.PixelAt(px, 100, 70, 70));   // 背景は白
    }

    [Fact]
    public void Transform_MovesShape()
    {
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 20, 20);
        n.Color = Color2D.Blue;
        n.Transform = Affine2D.Translate(50, 0);

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Blue, SkiaRenderer.PixelAt(px, 100, 60, 10));   // 移動先
        Assert.Equal(Color2D.Rgba(255, 255, 255), SkiaRenderer.PixelAt(px, 100, 10, 10));   // 元位置は空
    }

    [Fact]
    public void ZOrder_HigherZ_DrawsOnTop()
    {
        var canvas = new RetainedCanvas();
        UiNode a = canvas.AddChild(canvas.Root);
        a.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 50, 50);
        a.Color = Color2D.Red;
        UiNode b = canvas.AddChild(canvas.Root);
        b.Content = new Scene2D().FillRect(Color2D.White, 20, 20, 50, 50);
        b.Color = Color2D.Blue;
        b.Z = 1;

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Blue, SkiaRenderer.PixelAt(px, 100, 30, 30));   // 重なりは Z 高が勝つ

        b.Z = -1;   // 背面へ
        px = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Red, SkiaRenderer.PixelAt(px, 100, 30, 30));
    }

    [Fact]
    public void Opacity_IsInheritedAndBlended()
    {
        var canvas = new RetainedCanvas();
        UiNode parent = canvas.AddChild(canvas.Root);
        parent.Opacity = 0.5f;   // 実効 opacity は親 × 自分
        UiNode n = canvas.AddChild(parent);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);
        n.Color = Color2D.Rgba(0, 0, 0);   // 黒 50% over 白 ≈ 127 グレー

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        (byte r, byte g, byte b, _) = Px(px, 100, 20, 20);
        AssertNear(127, r); AssertNear(127, g); AssertNear(127, b);
    }

    [Fact]
    public void Clip_CutsOutsideAabb()
    {
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        n.Clip = new RectClip(0, 0, 25, 25);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 60, 60);
        n.Color = Color2D.Green;

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Green, SkiaRenderer.PixelAt(px, 100, 10, 10));                 // クリップ内
        Assert.Equal(Color2D.Rgba(255, 255, 255), SkiaRenderer.PixelAt(px, 100, 40, 10));   // クリップ外
    }

    [Fact]
    public void RoundedClip_CutsCornersAndKeepsSelectedSquareCorners()
    {
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        n.Clip = new RectClip(10, 10, 60, 60, 16,
            RectCorners.TopLeft | RectCorners.BottomRight);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 90, 90);
        n.Color = Color2D.Green;

        byte[] px = Render(canvas, Camera2D.Pixels, 100, 100);
        uint white = Color2D.Rgba(255, 255, 255);
        Assert.Equal(white, SkiaRenderer.PixelAt(px, 100, 11, 11));       // 丸めた左上の外側
        Assert.Equal(Color2D.Green, SkiaRenderer.PixelAt(px, 100, 68, 11)); // 選ばない右上は直角
        Assert.Equal(Color2D.Green, SkiaRenderer.PixelAt(px, 100, 30, 30)); // 中央
        Assert.Equal(white, SkiaRenderer.PixelAt(px, 100, 68, 68));       // 丸めた右下の外側
    }

    [Fact]
    public void AbsoluteColor_And_ImmediateScene_UseShapeColor()
    {
        // 即時モード: シェイプ自身の色で描かれる (GPU の GpuDeviceRasterizer2D.Encode と同じ)
        var scene = new Scene2D().FillCircle(Color2D.Blue, 50, 50, 30);
        byte[] px = Render(scene, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Blue, SkiaRenderer.PixelAt(px, 100, 50, 50));

        // 保持ツリーの AbsoluteColor: ノード色に畳まれない (カラー絵文字レイヤ等)
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        var s = new Scene2D();
        s.BeginFill(Color2D.Green, absoluteColor: true)
         .MoveTo(0, 0).LineTo(30, 0).LineTo(30, 30).LineTo(0, 30).Close().End();
        n.Content = s;
        n.Color = Color2D.Red;   // AbsoluteColor シェイプには効かない
        byte[] px2 = Render(canvas, Camera2D.Pixels, 100, 100);
        Assert.Equal(Color2D.Green, SkiaRenderer.PixelAt(px2, 100, 15, 15));
    }

    [Fact]
    public void Camera_ScalesScene()
    {
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 10, 10);
        n.Color = Color2D.Red;

        var cam = new Camera2D { A = 4, D = 4 };   // 4 倍ズーム
        byte[] px = Render(canvas, cam, 100, 100);
        Assert.Equal(Color2D.Red, SkiaRenderer.PixelAt(px, 100, 35, 35));   // 10px 矩形が 40px に
    }

    // ---- 増分更新統計 (ヘッドレス Flush — 従来「GPU が要るため実窓 E2E」だった検証) ----

    [Fact]
    public void Headless_Flush_TracksIncrementalUpdates()
    {
        var canvas = new RetainedCanvas();
        UiNode n = canvas.AddChild(canvas.Root);
        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);

        canvas.Flush(100, 100);
        Assert.True(canvas.LastWasFullRebuild);   // 初回はフル構築

        n.Transform = Affine2D.Translate(5, 5);   // 移動 = transform 部分更新のみ
        canvas.Flush(100, 100);
        Assert.False(canvas.LastWasFullRebuild);
        Assert.True(canvas.LastTransformWrites >= 1);

        n.Color = Color2D.Red;                    // 色 = スタイル部分更新のみ
        canvas.Flush(100, 100);
        Assert.False(canvas.LastWasFullRebuild);
        Assert.True(canvas.LastStyleWrites >= 1);

        n.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 30, 30);   // 同規模差し替え = in-place
        canvas.Flush(100, 100);
        Assert.False(canvas.LastWasFullRebuild);
        Assert.Equal(1, canvas.LastContentWrites);
    }

    // ---- UiHost E2E (レイアウト + 実体化 + 入力 + 描画をデバイスなしで) ----

    [Fact]
    public void UiHost_ButtonClick_RendersAndReacts_WithoutGpu()
    {
        using VectorFont font = VectorFont.LoadSystem();
        var canvas = new RetainedCanvas();
        var host = new UiHost(canvas, font, 200, 100);

        int clicks = 0;
        Widget btn = Button(_ => clicks++, "OK");
        host.SetRoot(btn);

        // 描画: ボタンの塗り (テーマ Primary) が背景以外のピクセルを作る
        byte[] px = Render(canvas, Camera2D.Pixels, 200, 100);
        int nonWhite = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != 255 || px[i + 1] != 255 || px[i + 2] != 255) nonWhite++;
        Assert.True(nonWhite > 50, $"ボタンが描画されていない (非白ピクセル {nonWhite})");

        // 入力: ボタン中心をクリック → ハンドラ発火
        Assert.True(host.Click(btn.Size.Width / 2, btn.Size.Height / 2));
        Assert.Equal(1, clicks);
    }
}
