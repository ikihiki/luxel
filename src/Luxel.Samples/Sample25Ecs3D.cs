using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.RenderGraph;

namespace Luxel.Samples;

/// <summary>
/// サンプル25 (RG-M4): 最小 ECS + 3D forward 描画 + RenderGraph 経由のシーン取り込み。
///
/// (1) World に 5×5 グリッドのキューブ entity を生成 (各自に LocalTransform / Color / MeshRef)。
/// (2) TransformPropagateSystem で GlobalTransform を更新。
/// (3) Render3DExtractSystem (IRenderExtractor) が ECS をクエリして InstanceData[] を bindless buffer へ書く。
/// (4) RenderGraph に 1 graphics パス: 頂点バッファ + インスタンスバッファ を Read、RT+Depth は lambda が直接管理。
/// (5) RT を読み戻して PNG 出力、非背景ピクセル数で検証 → vk/dx で一致を確認。
/// </summary>
public static class Sample25Ecs3D
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS 構築 (Friflo 経由) ===
        var world = new Luxel.Ecs.World();
        Vector4[] palette = {
            new(0.86f, 0.30f, 0.30f, 1f),
            new(0.30f, 0.70f, 0.40f, 1f),
            new(0.30f, 0.55f, 0.90f, 1f),
            new(0.95f, 0.80f, 0.30f, 1f),
            new(0.75f, 0.40f, 0.85f, 1f),
        };
        int entityCount = 0;
        for (int gz = 0; gz < 5; gz++)
        {
            for (int gx = 0; gx < 5; gx++)
            {
                Vector3 pos = new((gx - 2) * 1.0f, 0, (gz - 2) * 1.0f);
                Quaternion rot = Quaternion.CreateFromYawPitchRoll((gx + gz) * 0.35f, 0.25f, 0);
                Vector3 scale = new(0.4f, 0.4f, 0.4f);
                world.CreateEntity(
                    new Luxel.Ecs.LocalTransform(
                        Matrix4x4.CreateScale(scale)
                      * Matrix4x4.CreateFromQuaternion(rot)
                      * Matrix4x4.CreateTranslation(pos)),
                    new Luxel.Ecs.Color3D(palette[(gx + gz * 5) % palette.Length]),
                    new Luxel.Ecs.MeshRef(0));
                entityCount++;
            }
        }
        Console.WriteLine($"entities: {entityCount}");
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === 頂点バッファ (キューブ) ===
        ReadOnlySpan<CubeMesh.Vertex> verts = CubeMesh.Vertices;
        using GpuBuffer vbuf = device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        verts.CopyTo(vbuf.Span<CubeMesh.Vertex>(verts.Length));

        // === Extract (Friflo ECS → instance buffer) ===
        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);
        extractor.Extract();
        Console.WriteLine($"extracted instances: {extractor.InstanceCount}");

        // === RT / Depth / readback ===
        using GpuTexture color = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(w, h);
        using GpuBuffer readback = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        // === パイプライン (graphics, depth on) ===
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);

        // === カメラ ===
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            cameraPosition: new Vector3(3.0f, 2.5f, -5.0f),
            cameraTarget:   Vector3.Zero,
            cameraUpVector: Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            fieldOfView: MathF.PI / 3,
            aspectRatio: 1f,
            nearPlaneDistance: 0.1f,
            farPlaneDistance: 100f);
        Matrix4x4 viewProj = view * proj;

        // === RenderGraph (vertex/instance バッファの依存だけ宣言、target/depth は lambda で直管理) ===
        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hVerts = rg.ImportBuffer(vbuf, "verts");
        BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "instances");

        rg.AddPass("Render3D", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts)
          .Write(hInsts)  // 「使用」を宣言するため (実際の書込みは extract で完了済み)
          .Execute(ctx =>
          {
              // Slang/HLSL の既定行列レイアウトは column-major、System.Numerics.Matrix4x4 は row-major (各行が連続)。
              // 直接アップロードすると shader 側が転置として解釈するので、CPU 側で転置を入れて両者を整える。
              // (per-instance 行列は shader 側で Load4 を 4 回 + float4x4(r0..r3) で「各引数=行」として構築するので転置不要。)
              var args = new DrawArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  VertexBufIndex = ctx.BindlessIndex(hVerts),
                  InstanceBufIndex = ctx.BindlessIndex(hInsts),
              };
              ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                     .SetGraphicsPipeline(pipeline)
                     .SetRootArguments(args)
                     .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering();
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(color, readback);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // === 検証 ===
        Span<byte> px = readback.Span<byte>((int)(w * h * 4));
        int total = (int)(w * h);
        int colored = 0;
        int bgR = 13, bgG = 15, bgB = 23;  // clear color に近い背景
        for (int i = 0; i < total; i++)
        {
            byte r = px[i * 4 + 0], g = px[i * 4 + 1], b = px[i * 4 + 2];
            int diff = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
            if (diff > 20) colored++;
        }
        Console.WriteLine($"非背景ピクセル: {colored}/{total} ({colored * 100 / total}%)");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "ecs3d.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, px);
        Console.WriteLine($"PNG 出力: {pngPath}");

        // ピクセル数の概数だけ確認 (vk/dx 一致は実行ログを比較)。
        bool ok = colored > total / 20 && colored < total * 4 / 5
            && extractor.InstanceCount == 25 && entityCount == 25;
        Console.WriteLine(ok ? "OK: ECS + 3D forward + RenderGraph (RG-M4) 動作"
                             : "FAILED: ピクセル数/instance 数が予期と異なる");
        return ok ? 0 : 1;
    }
}
