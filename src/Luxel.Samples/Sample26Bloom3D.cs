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
/// サンプル26 (RG-M5): 3D forward + post-process bloom 連鎖を 1 つの RenderGraph で組む。
///
/// パス列 (登録順):
///   1. Render3D      — ECS 抽出→キューブを RT に描画 → CopyTextureToBuffer で buf へ
///   2. BlurH         — buf → tmp (水平ガウシアン, compute)
///   3. BlurV         — tmp → blur (垂直ガウシアン, compute)
///   4. BloomCombine  — base=buf + intensity*blur → final (compute)
///
/// Aliasing 効果: tmp は BlurH 後 / BlurV 入力で寿命終了、Composite/BloomCombine では使われないので物理共有可能。
/// vk/dx で同一ピクセルを生成し、3D + post-process がレンダーグラフで宣言的に組めることを示す。
/// </summary>
public static class Sample26Bloom3D
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurArgs
    {
        public uint SrcIndex, DstIndex, Width, Height;
        public int DirX, DirY;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomArgs
    {
        public uint BaseIndex, BlurIndex, DstIndex, Width, Height;
        public float Intensity;
        public uint Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS (Sample25 と類似だが、明るい色多めで bloom が映えるように) ===
        var world = new Luxel.Ecs.World();
        int entityCount = 0;
        Vector4[] palette = {
            new(1.00f, 0.40f, 0.40f, 1f),
            new(0.40f, 0.95f, 0.55f, 1f),
            new(0.35f, 0.70f, 1.00f, 1f),
            new(1.00f, 0.90f, 0.35f, 1f),
            new(0.90f, 0.55f, 1.00f, 1f),
        };
        for (int gz = 0; gz < 5; gz++)
        {
            for (int gx = 0; gx < 5; gx++)
            {
                var e = world.Create();
                Vector3 pos = new((gx - 2) * 1.0f, 0, (gz - 2) * 1.0f);
                Quaternion rot = Quaternion.CreateFromYawPitchRoll((gx + gz) * 0.35f, 0.25f, 0);
                Vector3 scale = new(0.4f);
                world.Set(e, new LocalTransform(
                    Matrix4x4.CreateScale(scale)
                  * Matrix4x4.CreateFromQuaternion(rot)
                  * Matrix4x4.CreateTranslation(pos)));
                world.Set(e, new Color3D(palette[(gx + gz * 5) % palette.Length]));
                world.Set(e, new MeshRef(MeshRef.Cube));
            }
        }
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === 頂点バッファ + Extract ===
        ReadOnlySpan<CubeMesh.Vertex> verts = CubeMesh.Vertices;
        using GpuBuffer vbuf = device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        verts.CopyTo(vbuf.Span<CubeMesh.Vertex>(verts.Length));

        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);
        extractor.Extract();

        // === RT / Depth ===
        using GpuTexture color = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(w, h);

        // === Bindless buffers (3D ピクセル中継 + post-process 中間) ===
        using GpuBuffer base3d = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer final = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        base3d.Span<byte>((int)fbBytes).Clear();
        final.Span<byte>((int)fbBytes).Clear();

        // === パイプライン ===
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        using GpuPipeline pl3d = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);
        using GpuPipeline plBlur = device.CreateComputePipeline(GpuShaderCode.Load("compute_blur"));
        using GpuPipeline plBloom = device.CreateComputePipeline(GpuShaderCode.Load("compute_bloom_combine"));

        // === カメラ ===
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(3.0f, 2.5f, -5.0f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // === RenderGraph ===
        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hVerts  = rg.ImportBuffer(vbuf, "verts");
        BufferHandle hInsts  = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
        BufferHandle hBase   = rg.ImportBuffer(base3d, "base3d");
        BufferHandle hFinal  = rg.ImportBuffer(final, "final");
        BufferHandle hTmp    = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp");
        BufferHandle hBlur   = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur");

        Action<PassContext> MakeBlur(BufferHandle src, BufferHandle dst, int dx, int dy) => ctx =>
        {
            var a = new BlurArgs { SrcIndex = ctx.BindlessIndex(src), DstIndex = ctx.BindlessIndex(dst), Width = w, Height = h, DirX = dx, DirY = dy };
            ctx.Cmd.SetComputePipeline(plBlur).SetRootArguments(a).Dispatch((w + 7) / 8, (h + 7) / 8);
        };

        // Pass1: 3D 描画 + RT → buf へコピー (lambda が color/depth を直接管理)。
        // Write(hBase) は CopyDest 経由 (compute から見ると Copy→Compute の barrier が必要なので usage を明示)。
        rg.AddPass("Render3D", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts).Write(hBase, ResourceUsage.CopyDest)
          .Execute(ctx =>
          {
              var args = new DrawArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  VertexBufIndex = ctx.BindlessIndex(hVerts),
                  InstanceBufIndex = ctx.BindlessIndex(hInsts),
              };
              ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                     .SetGraphicsPipeline(pl3d)
                     .SetRootArguments(args)
                     .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering()
                     .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                     .CopyTextureToBuffer(color, base3d);
          });

        // Pass2-4: post-process 連鎖 (全 compute)
        rg.AddPass("BlurH", PassQueue.Compute).Read(hBase).Write(hTmp).Execute(MakeBlur(hBase, hTmp, 1, 0));
        rg.AddPass("BlurV", PassQueue.Compute).Read(hTmp).Write(hBlur).Execute(MakeBlur(hTmp, hBlur, 0, 1));
        rg.AddPass("BloomCombine", PassQueue.Compute)
          .Read(hBase).Read(hBlur).Write(hFinal)
          .Execute(ctx =>
          {
              var a = new BloomArgs
              {
                  BaseIndex = ctx.BindlessIndex(hBase),
                  BlurIndex = ctx.BindlessIndex(hBlur),
                  DstIndex = ctx.BindlessIndex(hFinal),
                  Width = w, Height = h, Intensity = 0.6f,
              };
              ctx.Cmd.SetComputePipeline(plBloom).SetRootArguments(a).Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // === 検証: 元と最終の差 (bloom で明るくなる)、aliasing の効果 ===
        Span<byte> bp = base3d.Span<byte>((int)fbBytes);
        Span<byte> fp = final.Span<byte>((int)fbBytes);
        int bloomBrighter = 0, sampled = 0, exactMatch = 0;
        for (int i = 0; i < (int)(w * h); i++)
        {
            int b = bp[i * 4] + bp[i * 4 + 1] + bp[i * 4 + 2];
            int f = fp[i * 4] + fp[i * 4 + 1] + fp[i * 4 + 2];
            if (b > 5) sampled++;
            if (f >= b + 4) bloomBrighter++;
            if (f == b) exactMatch++;
        }
        Console.WriteLine($"  bloom で明るくなったピクセル: {bloomBrighter}/{sampled}");
        Console.WriteLine($"  完全一致 (背景含む): {exactMatch}");
        Console.WriteLine($"  passes: {rg.PassCount} (executed {rg.LastExecutedPassCount}), physical transient={rg.PhysicalTransientBufferCount}");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_bloom3d.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, fp);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool ok = bloomBrighter > sampled / 3 && rg.LastExecutedPassCount == 4;
        Console.WriteLine(ok ? "OK: RG-M5 (3D + post-process bloom 連鎖) 動作"
                             : "FAILED: bloom 効果またはパス数が予期と異なる");
        return ok ? 0 : 1;
    }
}
