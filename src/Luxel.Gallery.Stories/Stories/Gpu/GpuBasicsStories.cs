using System.Runtime.InteropServices;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// GPU 抽象の基礎デモ — 固定パイプラインレイアウト (最大192Bのルート引数 + bindless heap) の上で
/// 深度テスト / アルファブレンドがどう書けるかを示す。docs の Reference/Guides/GpuDevice から参照される。
/// </summary>
public static class GpuBasicsStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float Px, Py, Pz, Pw, R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    /// <summary>頂点 3 つの三角形バッファ (vertex pulling — 頂点レイアウト宣言はなく、
    /// シェーダが bindless バッファから読む)。</summary>
    private static GpuBuffer MakeTriangle(GpuDevice d, float z, (float X, float Y)[] xy,
                                          (float R, float G, float B, float A) c)
    {
        var vb = d.Malloc(3 * 32, GpuMemoryKind.HostMapped);
        Span<Vertex> v = vb.Span<Vertex>(3);
        for (int i = 0; i < 3; i++)
            v[i] = new Vertex
            {
                Px = xy[i].X,
                Py = xy[i].Y,
                Pz = z,
                Pw = 1,
                R = c.R,
                G = c.G,
                B = c.B,
                A = c.A
            };
        return vb;
    }

    [Story("Examples/3D/Depth", Height = 320, Order = 100)]
    public static Widget Depth() => Frame(GpuView(256, 256, new DepthScene(), animated: false));

    [Story("Examples/3D/Blend", Height = 320, Order = 101)]
    public static Widget Blend() => Frame(GpuView(256, 256, new BlendScene(), animated: false));

    /// <summary>深度テスト: 手前 (緑, z=0.3) → 奥 (赤, z=0.7) の順に描く。描画順だけなら
    /// 後から描いた赤が勝つはずだが、LessEqual の深度テストで重なり中心は緑が残る。</summary>
    private sealed class DepthScene : GpuSceneBase
    {
        private GpuTexture _depth = null!;
        private GpuBuffer _near = null!, _far = null!;
        private GpuPipeline _pipeline = null!;

        protected override void OnInit()
        {
            _depth = Track(Device.CreateDepthTarget(W, H));
            _near = Track(MakeTriangle(Device, 0.3f, [(-0.6f, -0.6f), (0.6f, -0.6f), (0.0f, 0.6f)], (0, 1, 0, 1)));
            _far = Track(MakeTriangle(Device, 0.7f, [(-0.6f, 0.6f), (0.6f, 0.6f), (0.0f, -0.6f)], (1, 0, 0, 1)));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), raster));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, _depth, 0.09f, 0.1f, 0.12f, 1, 1f)
               .SetGraphicsPipeline(_pipeline)
               .SetRootArguments(new DrawArgs { VertexBufferIndex = _near.BindlessIndex })
               .Draw(3)                                            // 手前 (緑) を先に
               .SetRootArguments(new DrawArgs { VertexBufferIndex = _far.BindlessIndex })
               .Draw(3)                                            // 奥 (赤) を後に → 深度で中心は負ける
               .EndRendering()
               .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    /// <summary>アルファブレンド: 赤 (不透明) の上に青 (α=0.5) を重ねる。
    /// 重なり中心は 0.5×青 + 0.5×赤 = 紫になる。</summary>
    private sealed class BlendScene : GpuSceneBase
    {
        private GpuBuffer _red = null!, _blue = null!;
        private GpuPipeline _pipeline = null!;

        protected override void OnInit()
        {
            _red = Track(MakeTriangle(Device, 0f, [(-0.6f, -0.6f), (0.6f, -0.6f), (0.0f, 0.6f)], (1, 0, 0, 1)));
            _blue = Track(MakeTriangle(Device, 0f, [(-0.6f, 0.6f), (0.6f, 0.6f), (0.0f, -0.6f)], (0, 0, 1, 0.5f)));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.Blend = GpuBlendMode.AlphaBlend;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), raster));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, null, 0.09f, 0.1f, 0.12f, 1, 1f)
               .SetGraphicsPipeline(_pipeline)
               .SetRootArguments(new DrawArgs { VertexBufferIndex = _red.BindlessIndex })
               .Draw(3)
               .SetRootArguments(new DrawArgs { VertexBufferIndex = _blue.BindlessIndex })
               .Draw(3)
               .EndRendering()
               .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
