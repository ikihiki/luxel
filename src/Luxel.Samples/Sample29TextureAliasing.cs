using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.RenderGraph;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// サンプル29 (RG-M6): texture transient aliasing — 同形 RT/Depth を寿命非重複で複数定義し、
/// レンダーグラフが物理リソースを 1 つに alias することを観測。
///
/// 構成: 同じ 3D シーンを 2 つの異なる視点 (front / side) で順次レンダ。
///   - View1 RT/Depth (transient) を使って描画 → CopyTextureToBuffer で External buf1 へ
///   - View2 RT/Depth (transient) を使って描画 → CopyTextureToBuffer で External buf2 へ
///   - Composite (compute): buf1, buf2 を左右に並べた最終フレーム
/// View1 と View2 の Transient RT/Depth は寿命非重複 → 物理 1 つに alias (各 0 + 0 = 物理 1 + 1)。
/// </summary>
public static class Sample29TextureAliasing
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
    private struct CompositeArgs
    {
        public uint AIndex, BIndex, DstIndex, Width, Height, SplitX, Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS (5×5 cubes、sample 25 と類似) ===
        var world = new Luxel.Ecs.World();
        Vector4[] palette = {
            new(1.00f, 0.40f, 0.40f, 1f),
            new(0.40f, 0.95f, 0.55f, 1f),
            new(0.35f, 0.70f, 1.00f, 1f),
            new(1.00f, 0.90f, 0.35f, 1f),
            new(0.90f, 0.55f, 1.00f, 1f),
        };
        for (int gz = 0; gz < 5; gz++)
        for (int gx = 0; gx < 5; gx++)
        {
            var e = world.Create();
            Vector3 pos = new((gx - 2) * 1.0f, 0, (gz - 2) * 1.0f);
            Quaternion rot = Quaternion.CreateFromYawPitchRoll((gx + gz) * 0.35f, 0.25f, 0);
            world.Set(e, new LocalTransform(
                Matrix4x4.CreateScale(0.4f)
              * Matrix4x4.CreateFromQuaternion(rot)
              * Matrix4x4.CreateTranslation(pos)));
            world.Set(e, new Color3D(palette[(gx + gz * 5) % palette.Length]));
            world.Set(e, new MeshRef(MeshRef.Cube));
        }
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        ReadOnlySpan<CubeMesh.Vertex> cubeVerts = CubeMesh.Vertices;
        using GpuBuffer cubeVb = device.Malloc((ulong)(cubeVerts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        cubeVerts.CopyTo(cubeVb.Span<CubeMesh.Vertex>(cubeVerts.Length));

        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);
        extractor.Extract();

        // === External 出力先 ===
        using GpuBuffer buf1 = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer buf2 = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer final = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        buf1.Span<byte>((int)fbBytes).Clear();
        buf2.Span<byte>((int)fbBytes).Clear();
        final.Span<byte>((int)fbBytes).Clear();

        // === パイプライン ===
        var raster3d = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster3d.DepthTest = true;
        raster3d.DepthWrite = true;
        using GpuPipeline plCube = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster3d);
        using GpuPipeline plComposite = device.CreateComputePipeline(GpuShaderCode.Load("compute_composite"));

        // === 2 つのカメラ ===
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewFront = Matrix4x4.CreateLookAt(new Vector3(3.0f, 2.5f, -5.0f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 viewSide  = Matrix4x4.CreateLookAt(new Vector3(5.0f, 1.5f, +0.5f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 vpFront = viewFront * proj;
        Matrix4x4 vpSide  = viewSide  * proj;

        // === RenderGraph: 同形 Transient RT/Depth を 2 つ宣言 → aliasing で物理 1 つに ===
        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hVerts = rg.ImportBuffer(cubeVb, "verts");
        BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
        BufferHandle hBuf1  = rg.ImportBuffer(buf1, "buf1");
        BufferHandle hBuf2  = rg.ImportBuffer(buf2, "buf2");
        BufferHandle hFinal = rg.ImportBuffer(final, "final");
        TextureHandle hRt1    = rg.CreateTexture(new TextureDesc(w, h, GpuFormat.Rgba8Unorm), "rt1");
        TextureHandle hRt2    = rg.CreateTexture(new TextureDesc(w, h, GpuFormat.Rgba8Unorm), "rt2");
        TextureHandle hDepth1 = rg.CreateTexture(new TextureDesc(w, h, GpuFormat.D32Float, TextureKind.Depth), "depth1");
        TextureHandle hDepth2 = rg.CreateTexture(new TextureDesc(w, h, GpuFormat.D32Float, TextureKind.Depth), "depth2");

        Action<PassContext> RenderView(Matrix4x4 vp, TextureHandle rt, TextureHandle depth, BufferHandle outBuf) => ctx =>
        {
            var args = new CubeArgs
            {
                ViewProj = Matrix4x4.Transpose(vp),
                VertexBufIndex = ctx.BindlessIndex(hVerts),
                InstanceBufIndex = ctx.BindlessIndex(hInsts),
            };
            GpuTexture colorTex = ctx.Texture(rt);
            GpuTexture depthTex = ctx.Texture(depth);
            GpuBuffer dstBuf = ctx.Buffer(outBuf);
            ctx.Cmd.BeginRendering(colorTex, depthTex, 0.05f, 0.06f, 0.09f, 1f, 1f)
                   .SetGraphicsPipeline(plCube)
                   .SetRootArguments(args)
                   .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                   .EndRendering()
                   .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                   .CopyTextureToBuffer(colorTex, dstBuf);
        };

        rg.AddPass("View1Front", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts)
          .Write(hRt1, TextureUsage.ColorAttachment)
          .Write(hDepth1, TextureUsage.DepthAttachment)
          .Write(hBuf1, ResourceUsage.CopyDest)
          .Execute(RenderView(vpFront, hRt1, hDepth1, hBuf1));

        rg.AddPass("View2Side", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts)
          .Write(hRt2, TextureUsage.ColorAttachment)
          .Write(hDepth2, TextureUsage.DepthAttachment)
          .Write(hBuf2, ResourceUsage.CopyDest)
          .Execute(RenderView(vpSide, hRt2, hDepth2, hBuf2));

        rg.AddPass("Composite", PassQueue.Compute)
          .Read(hBuf1).Read(hBuf2).Write(hFinal)
          .Execute(ctx =>
          {
              var args = new CompositeArgs
              {
                  AIndex = ctx.BindlessIndex(hBuf1),
                  BIndex = ctx.BindlessIndex(hBuf2),
                  DstIndex = ctx.BindlessIndex(hFinal),
                  Width = w, Height = h, SplitX = w / 2,
              };
              ctx.Cmd.SetComputePipeline(plComposite).SetRootArguments(args).Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // === 検証 ===
        int physTex = rg.PhysicalTransientTextureCount;
        bool rt1Aliased = rg.IsAliased(hRt1);
        bool rt2Aliased = rg.IsAliased(hRt2);
        bool depth1Aliased = rg.IsAliased(hDepth1);
        bool depth2Aliased = rg.IsAliased(hDepth2);
        int rt1Slot = rg.GetPhysicalSlot(hRt1);
        int rt2Slot = rg.GetPhysicalSlot(hRt2);
        Console.WriteLine($"  論理 Transient テクスチャ: 4 (rt1/rt2/depth1/depth2)");
        Console.WriteLine($"  物理 Transient テクスチャ: {physTex} (期待: 2 = 1 color + 1 depth、aliasing 後)");
        Console.WriteLine($"  rt1↔rt2 同 slot: rt1.slot={rt1Slot}, rt2.slot={rt2Slot} (aliased rt1={rt1Aliased}, rt2={rt2Aliased})");
        Console.WriteLine($"  depth1/depth2 aliased: {depth1Aliased}, {depth2Aliased}");

        Span<byte> fpx = final.Span<byte>((int)fbBytes);
        int total = (int)(w * h);
        int colored = 0;
        for (int i = 0; i < total; i++)
        {
            byte r = fpx[i * 4 + 0], g = fpx[i * 4 + 1], b = fpx[i * 4 + 2];
            int sum = r + g + b;
            if (sum > 80) colored++;
        }
        Console.WriteLine($"  非背景ピクセル: {colored}/{total} ({colored * 100 / total}%)");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_texalias.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, fpx);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool ok = physTex == 2 && rt1Aliased && rt2Aliased && depth1Aliased && depth2Aliased
            && rt1Slot == rt2Slot && colored > total / 10;
        Console.WriteLine(ok ? "OK: RG-M6 (Texture transient aliasing) 動作"
                             : "FAILED: aliasing が期待通りに作用していません");
        return ok ? 0 : 1;
    }
}
