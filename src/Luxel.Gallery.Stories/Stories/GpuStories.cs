using System.Runtime.InteropServices;
using Luxel.Controls;
using Luxel.Resources;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// 2D/3D システムの描画結果をストーリーにする実例 (GS)。
/// - 2D: <see cref="Luxel.Controls.Canvas2D"/> — Scene2D を直接描く (UI と同じ保持型キャンバスの 1 ノード)
/// - 3D: <see cref="Luxel.Controls.GpuView"/> + <see cref="IGpuScene"/> — offscreen へ自前レンダ →
///   image プリミティブでゼロコピー合成。リソースは <c>ctx.Resources</c> (ホスト所有) から
/// 時間はすべて Tick の累積秒 — snap の固定ステップ (8 × 1/60s) で決定的。
/// </summary>
public static class GpuStories
{
    // ---- 2D: Scene2D 直描き ----

    [Story("Examples/2D/Shapes", Height = 300, Order = 110)]
    public static Widget Shapes(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(384, 220, draw: s =>
    {
        s.FillRoundedRect(Tw.Blue500, 16, 16, 140, 90, 14);
        s.FillCircle(Tw.Amber500, 230, 60, 44);
        s.StrokeRoundedRect(Tw.Green500, 3, 290, 20, 80, 80, 10);
        s.BeginFill(Tw.Red500).MoveTo(60, 130).LineTo(150, 200).LineTo(20, 200).Close().End();
        s.StrokePolyline(Tw.Slate500, 2,
            new System.Numerics.Vector2(180, 200), new System.Numerics.Vector2(230, 140),
            new System.Numerics.Vector2(280, 190), new System.Numerics.Vector2(370, 130));
    })));

    [Story("Examples/2D/Orbit", Height = 300, Order = 111)]
    public static Widget Orbit(StoryContext ctx)
    {
        Signal<float> speed = ctx.Signal("speed", 1f, "軌道アニメの速度倍率");
        ctx.Play(static d => d.Snap());
        return Frame(Canvas2D(384, 220, animate: (s, t) =>
        {
            const float cx = 192, cy = 110;
            s.FillCircle(Tw.Amber500, cx, cy, 26);                       // 恒星
            s.StrokeRoundedRect(Tw.Slate500, 1, cx - 80, cy - 80, 160, 160, 80);   // 軌道
            float a = t * speed.Value;
            s.FillCircle(Tw.Blue500, cx + MathF.Cos(a) * 80, cy + MathF.Sin(a) * 80, 12);
            float a2 = t * speed.Value * 2.6f;
            s.FillCircle(Tw.Red500, cx + MathF.Cos(a2) * 44, cy + MathF.Sin(a2) * 44, 7);
        }));
    }

    // ---- 3D: offscreen 自前レンダ → image 合成 ----

    [Story(CanonicalTriangleRecipe.Story, Width = CanonicalTriangleRecipe.Width, Height = CanonicalTriangleRecipe.Height, Order = 120,
        RuntimeBundleId = "webgpu-browser-v1", CapabilityNote = "Specialized browser WebGPU validation route.")]
    public static Widget Triangle(StoryContext ctx)
        => ctx.Snap(Frame(GpuView(CanonicalTriangleRecipe.Width, CanonicalTriangleRecipe.Height, new TriangleScene())));

    [Story("Examples/3D/TexturedQuad", Height = 320, Order = 121)]
    public static Widget TexturedQuad(StoryContext ctx)
        => ctx.Snap(Frame(GpuView(320, 240, new TexturedScene(ctx.Resources), animated: false)));

    /// <summary>The native Gallery path for the shared canonical first-triangle recipe.</summary>
    private sealed class TriangleScene : IGpuScene
    {
        private GpuDevice? _device;
        private GpuTexture? _target;
        private GpuBuffer? _vb, _out;
        private GpuPipeline? _pipeline;
        private uint _w, _h;

        public void Init(GpuDevice device, int width, int height)
        {
            _device = device;
            _w = (uint)width; _h = (uint)height;
            _target = device.CreateRenderTarget(_w, _h, GpuFormat.Rgba8Unorm);
            CanonicalTriangleRecipe.Vertex[] vertices = CanonicalTriangleRecipe.CreateVertices();
            _vb = device.Malloc(checked((ulong)vertices.Length * CanonicalTriangleRecipe.VertexSize), GpuMemoryKind.HostMapped);
            vertices.CopyTo(_vb.Span<CanonicalTriangleRecipe.Vertex>(vertices.Length));
            _out = device.Malloc(_w * _h * 4, GpuMemoryKind.HostMapped);
            _pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load(CanonicalTriangleRecipe.Shader),
                GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        }

        public (int BindlessIndex, int StridePixels) Render(float time)
        {
            var args = new CanonicalTriangleRecipe.DrawArgs { VertexBufferIndex = _vb!.BindlessIndex };
            using GpuCommandBuffer cmd = _device!.MainQueue.StartCommandRecording();
            cmd.BeginRendering(_target!, null, 0.055f, 0.07f, 0.11f, 1)
               .SetGraphicsPipeline(_pipeline!)
               .SetRootArguments(args)
               .Draw(3)
               .EndRendering()
               .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(_target!, _out!);
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
            return ((int)_out!.BindlessIndex, (int)_w);
        }

        public void Dispose()
        {
            _pipeline?.Dispose(); _pipeline = null;
            _out?.Dispose(); _out = null;
            _vb?.Dispose(); _vb = null;
            _target?.Dispose(); _target = null;
        }
    }

    /// <summary>テクスチャ付きフルスクリーン三角形 (サンプル 03 の移植)。テクスチャは
    /// <c>ctx.Resources</c> から PNG をロード (初回ロードの publish は Pump 不要なので Init で待てる)。</summary>
    private sealed class TexturedScene(ResourceSystem resources) : IGpuScene
    {
        private const string ImageUri = "src/Luxel.Gallery/assets/sample-sparkline.png";

        [StructLayout(LayoutKind.Sequential)]
        private struct DrawArgs { public uint TextureIndex; public uint SamplerIndex; }

        private GpuDevice? _device;
        private ResourceHandle<CpuImage>? _image;
        private GpuTexture? _texture, _target;
        private GpuSampler? _sampler;
        private GpuBuffer? _out;
        private GpuPipeline? _pipeline;
        private uint _w, _h;
        private bool _rendered;

        public void Init(GpuDevice device, int width, int height)
        {
            _device = device;
            _w = (uint)width; _h = (uint)height;
            _rendered = false;   // 再 Init (Dispose 後の再実体化) ではバッファが新しい — 描き直す
            _image = resources.Load<CpuImage>(ImageUri);
            try { _image.Ready.Wait(5000); } catch { /* 失敗時は 1x1 白で続行 */ }
            CpuImage img = _image.IsReady && _image.Value is { Width: > 0 } i
                ? i : new CpuImage(1, 1, [255, 255, 255, 255]);
            _texture = device.CreateTexture((uint)img.Width, (uint)img.Height, img.Pixels);
            _sampler = device.CreateSampler(GpuSamplerFilter.Point);
            _target = device.CreateRenderTarget(_w, _h, GpuFormat.Rgba8Unorm);
            _out = device.Malloc(_w * _h * 4, GpuMemoryKind.HostMapped);
            _pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("textured"),
                GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        }

        public (int BindlessIndex, int StridePixels) Render(float time)
        {
            if (!_rendered)
            {
                _rendered = true;
                var args = new DrawArgs { TextureIndex = _texture!.BindlessIndex, SamplerIndex = _sampler!.BindlessIndex };
                using GpuCommandBuffer cmd = _device!.MainQueue.StartCommandRecording();
                cmd.BeginRendering(_target!, null, 0, 0, 0, 1)
                   .SetGraphicsPipeline(_pipeline!)
                   .SetRootArguments(args)
                   .Draw(3)
                   .EndRendering()
                   .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                   .CopyTextureToBuffer(_target!, _out!);
                cmd.Finish();
                _device.MainQueue.SubmitAndWait(cmd);
            }
            return ((int)_out!.BindlessIndex, (int)_w);
        }

        public void Dispose()
        {
            _pipeline?.Dispose(); _pipeline = null;
            _out?.Dispose(); _out = null;
            _target?.Dispose(); _target = null;
            _sampler?.Dispose(); _sampler = null;
            _texture?.Dispose(); _texture = null;
            _image?.Dispose(); _image = null;
        }
    }
}
