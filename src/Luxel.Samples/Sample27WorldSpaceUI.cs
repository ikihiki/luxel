using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.RenderGraph;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル27 (RG-M5b): world-space UI — 2D ベクター UI を bindless buffer にラスタライズ → 3D 空間内に
/// 傾けた quad で表示。3D キューブ群とともに 1 つの RenderGraph で組む。
///
/// パス:
///   1. RasterizeUI (compute) — Scene2D → ui_buf (Luxel.TwoD の Rasterizer2D)
///   2. Render3D  (graphics) — cube_forward でキューブ群 + ui_quad_3d で UI quad、深度有効、同じ RT へ
///      末尾で CopyTextureToBuffer → base3d
///
/// vk/dx ピクセル一致を確認。RG は ui_buf を transient として宣言し、Compute→PixelShader のバリアを自動挿入。
/// </summary>
public static class Sample27WorldSpaceUI
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CubeArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UiQuadArgs
    {
        public Matrix4x4 Mvp;
        public uint VertexBufIndex;
        public uint UiBufferIndex;
        public uint UiWidth;
        public uint UiHeight;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        const uint uiW = 256, uiH = 256;
        ulong fbBytes = (ulong)(w * h * 4);
        ulong uiBytes = (ulong)(uiW * uiH * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === 2D UI シーン (Scene2D → ラスタライザ) ===
        using var raster = new Rasterizer2D(device);
        var uiScene = new Scene2D();
        uiScene.FillRoundedRect(Color2D.Rgba(245, 240, 230, 255), 0, 0, (int)uiW, (int)uiH, 0); // 背景
        uiScene.FillRoundedRect(Color2D.Rgba(60, 130, 240, 255),  20, 20, 100, 40, 8);
        uiScene.FillRoundedRect(Color2D.Rgba(230, 80, 100, 255), 140, 20, 100, 40, 8);
        uiScene.FillRoundedRect(Color2D.Rgba(40, 200, 120, 255),  20, 80, 220, 60, 12);
        uiScene.FillRoundedRect(Color2D.Rgba(180, 90, 220, 255),  20, 160, 100, 80, 14);
        uiScene.FillRoundedRect(Color2D.Rgba(255, 200, 60, 255), 140, 160, 100, 80, 14);
        using var encodedUi = raster.Encode(uiScene);
        using GpuBuffer uiBuf = device.Malloc(uiBytes, GpuMemoryKind.HostMapped);
        uiBuf.Span<byte>((int)uiBytes).Clear();
        using (var c = device.MainQueue.StartCommandRecording())
        {
            raster.Render(c, encodedUi, Camera2D.Pixels, uiW, uiH, uiBuf);
            c.Finish();
            device.MainQueue.SubmitAndWait(c);
        }

        // === ECS (キューブ群、UI quad はキューブの後ろに置く) ===
        var world = new Luxel.Ecs.World();
        int entityCount = 0;
        Vector4[] palette = {
            new(1.00f, 0.40f, 0.40f, 1f),
            new(0.40f, 0.95f, 0.55f, 1f),
            new(0.35f, 0.70f, 1.00f, 1f),
            new(1.00f, 0.90f, 0.35f, 1f),
            new(0.90f, 0.55f, 1.00f, 1f),
        };
        // 3×3 の小さなキューブ群を前方に
        for (int gz = 0; gz < 3; gz++)
        {
            for (int gx = 0; gx < 3; gx++)
            {
                var e = world.Create();
                Vector3 pos = new((gx - 1) * 0.9f, -0.5f, (gz - 1) * 0.9f);
                Quaternion rot = Quaternion.CreateFromYawPitchRoll((gx + gz) * 0.35f, 0.2f, 0);
                Vector3 scale = new(0.45f);
                world.Set(e, new LocalTransform(
                    Matrix4x4.CreateScale(scale)
                  * Matrix4x4.CreateFromQuaternion(rot)
                  * Matrix4x4.CreateTranslation(pos)));
                world.Set(e, new Color3D(palette[(gx + gz * 3) % palette.Length]));
                world.Set(e, new MeshRef(MeshRef.Cube));
            }
        }
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === 頂点バッファ (キューブ) + extract ===
        ReadOnlySpan<CubeMesh.Vertex> cubeVerts = CubeMesh.Vertices;
        using GpuBuffer cubeVb = device.Malloc((ulong)(cubeVerts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        cubeVerts.CopyTo(cubeVb.Span<CubeMesh.Vertex>(cubeVerts.Length));

        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);
        extractor.Extract();

        // === 頂点バッファ (UI quad) ===
        ReadOnlySpan<UiQuadMesh.Vertex> quadVerts = UiQuadMesh.Vertices;
        using GpuBuffer quadVb = device.Malloc((ulong)(quadVerts.Length * UiQuadMesh.VertexStride), GpuMemoryKind.HostMapped);
        quadVerts.CopyTo(quadVb.Span<UiQuadMesh.Vertex>(quadVerts.Length));

        // === RT / Depth / readback ===
        using GpuTexture color = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(w, h);
        using GpuBuffer readback = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        // === パイプライン ===
        var raster3d = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster3d.DepthTest = true;
        raster3d.DepthWrite = true;
        using GpuPipeline plCube = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster3d);
        using GpuPipeline plUiQuad = device.CreateGraphicsPipeline(GpuShaderCode.Load("ui_quad_3d"), raster3d);

        // === カメラ ===
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.4f, 1.8f, -4.0f), new Vector3(0, 0.2f, 0.5f), Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // UI quad の world: 背景に立てた板。中心 (0, 0.5, 2.5)、サイズ 4.0、わずかに傾ける。
        Matrix4x4 uiWorld =
            Matrix4x4.CreateScale(4.0f, 4.0f, 1f)
          * Matrix4x4.CreateRotationY(-0.18f)
          * Matrix4x4.CreateTranslation(0f, 0.5f, 2.5f);
        Matrix4x4 uiMvp = uiWorld * viewProj;

        // === RenderGraph ===
        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hUi    = rg.ImportBuffer(uiBuf, "uiBuf");
        BufferHandle hCubeVb = rg.ImportBuffer(cubeVb, "cubeVb");
        BufferHandle hCubeInst = rg.ImportBuffer(extractor.InstanceBuffer, "cubeInst");
        BufferHandle hQuadVb = rg.ImportBuffer(quadVb, "quadVb");
        BufferHandle hRead   = rg.ImportBuffer(readback, "readback");

        rg.AddPass("Render3D", PassQueue.Graphics)
          .Read(hCubeVb).Read(hCubeInst).Read(hQuadVb)
          .Read(hUi, ResourceUsage.SampledInPixelShader)
          .Write(hRead, ResourceUsage.CopyDest)
          .Execute(ctx =>
          {
              var cubeArgs = new CubeArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  VertexBufIndex = ctx.BindlessIndex(hCubeVb),
                  InstanceBufIndex = ctx.BindlessIndex(hCubeInst),
              };
              var quadArgs = new UiQuadArgs
              {
                  Mvp = Matrix4x4.Transpose(uiMvp),
                  VertexBufIndex = ctx.BindlessIndex(hQuadVb),
                  UiBufferIndex = ctx.BindlessIndex(hUi),
                  UiWidth = uiW, UiHeight = uiH,
              };
              ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                     // UI quad 先 (背景), 深度書込で後ろに沈める
                     .SetGraphicsPipeline(plUiQuad)
                     .SetRootArguments(quadArgs)
                     .Draw((uint)UiQuadMesh.VertexCount, 1)
                     // キューブ群を前景
                     .SetGraphicsPipeline(plCube)
                     .SetRootArguments(cubeArgs)
                     .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering()
                     .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                     .CopyTextureToBuffer(color, readback);
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // === 検証: UI の色が画面に出ていること (オレンジ・緑・青・紫等のはっきりした色) ===
        Span<byte> px = readback.Span<byte>((int)fbBytes);
        int total = (int)(w * h);
        int colored = 0;
        int bgR = 13, bgG = 15, bgB = 23;
        int uiBgLike = 0;  // UI 背景の (245, 240, 230) に近いピクセル
        for (int i = 0; i < total; i++)
        {
            byte r = px[i * 4 + 0], g = px[i * 4 + 1], b = px[i * 4 + 2];
            int diff = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
            if (diff > 20) colored++;
            if (r > 220 && g > 220 && b > 210) uiBgLike++;
        }
        Console.WriteLine($"非背景ピクセル: {colored}/{total} ({colored * 100 / total}%)");
        Console.WriteLine($"UI 背景色 (≒オフホワイト) 検出: {uiBgLike} ピクセル");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_worldui.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, px);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool ok = colored > total / 4 && uiBgLike > 500;
        Console.WriteLine(ok ? "OK: RG-M5b (world-space UI: 2D を 3D quad へサンプリング) 動作"
                             : "FAILED: UI が 3D 内に出ていません");
        return ok ? 0 : 1;
    }
}
