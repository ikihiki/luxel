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
/// サンプル30 (RG-M6 動的解像度): 同じ ECS シーンを 3 つの異なる解像度 (256/192/128) で順次レンダ。
/// 各フレームで RG を新規構築し transient テクスチャを確保 → Dispose で一括解放。
/// 「動的解像度」(フレーム間で RT 寸法を変える) が RenderGraph の transient API で素直に表現できることを示す。
///
/// レンダーグラフは 1 フレーム使い切りの設計のため、解像度ごとに別 RG を回し、3 PNG を出力。
/// </summary>
public static class Sample30DynamicResolution
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CubeArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS は 1 回だけ作る (シーンは共通) ===
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

        var raster3d = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster3d.DepthTest = true;
        raster3d.DepthWrite = true;
        using GpuPipeline plCube = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster3d);

        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(3.0f, 2.5f, -5.0f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // === 3 解像度をループ ===
        (uint Size, string Tag)[] resolutions = { (256, "hi"), (192, "med"), (128, "lo") };
        int totalOk = 0;
        int totalPhys = 0;
        foreach (var (size, tag) in resolutions)
        {
            ulong fbBytes = (ulong)(size * size * 4);
            using GpuBuffer readback = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
            readback.Span<byte>((int)fbBytes).Clear();

            // 解像度ごとに新規 RG を作る。Transient テクスチャは RG の生存期間 = このフレームで完結。
            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hVerts = rg.ImportBuffer(cubeVb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
            BufferHandle hRead  = rg.ImportBuffer(readback, "readback");
            TextureHandle hRt    = rg.CreateTexture(new TextureDesc(size, size, GpuFormat.Rgba8Unorm), "rt");
            TextureHandle hDepth = rg.CreateTexture(new TextureDesc(size, size, GpuFormat.D32Float, TextureKind.Depth), "depth");

            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts)
              .Write(hRt, TextureUsage.ColorAttachment)
              .Write(hDepth, TextureUsage.DepthAttachment)
              .Write(hRead, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var args = new CubeArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  GpuTexture color = ctx.Texture(hRt);
                  GpuTexture depth = ctx.Texture(hDepth);
                  ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(plCube)
                         .SetRootArguments(args)
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

            Span<byte> px = readback.Span<byte>((int)fbBytes);
            int total = (int)(size * size);
            int colored = 0;
            int bgR = 13, bgG = 15, bgB = 23;
            for (int i = 0; i < total; i++)
            {
                byte r = px[i * 4 + 0], g = px[i * 4 + 1], b = px[i * 4 + 2];
                int diff = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
                if (diff > 20) colored++;
            }
            int phys = rg.PhysicalTransientTextureCount;
            totalPhys += phys;
            Console.WriteLine($"  [{tag}] {size}×{size}: 非背景 {colored}/{total} ({colored * 100 / total}%), 物理 transient テクスチャ = {phys}");

            string pngPath = Path.Combine(AppContext.BaseDirectory, $"rendergraph_dynres_{tag}.png");
            PngWriter.WriteRgba(pngPath, (int)size, (int)size, px);
            Console.WriteLine($"  PNG: {pngPath}");

            if (colored > total / 20 && phys == 2) totalOk++;
        }

        // 期待: 3 解像度すべて成功 + 各 RG で物理 transient = 2 (color + depth)
        bool ok = totalOk == 3 && totalPhys == 6;
        Console.WriteLine(ok ? "OK: RG-M6 動的解像度 (フレームごとの transient テクスチャ寸法変更) 動作"
                             : $"FAILED: ok={totalOk}/3, totalPhys={totalPhys}");
        return ok ? 0 : 1;
    }
}
