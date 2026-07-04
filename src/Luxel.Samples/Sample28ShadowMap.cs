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
/// サンプル28 (RG-M5c): shadow map — ライト視点で R32Float RT に NDC z を書き、bindless buffer に
/// CopyTextureToBuffer、メイン視点パスで shadow buffer を読み比較して影を出す。
///
/// パス列:
///   1. ShadowPass   (graphics, ライト視点, R32Float RT) — instance ごとに depth を float で書く
///   2. CopyShadow   (Render3D の lambda 内) — shadow RT → shadow bindless buffer
///   3. MainPass     (graphics, メイン視点) — cube_with_shadow で N·L × shadow factor
///   4. Composite    — CopyTextureToBuffer で readback へ
///
/// 構成: 大きな床キューブ + 5 キューブを高さ違いで配置 → 床に影が落ちる。
/// vk/dx ピクセル一致を確認。
/// </summary>
public static class Sample28ShadowMap
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowArgs
    {
        public Matrix4x4 LightVP;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MainArgs
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 LightVP;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint ShadowBufIndex;
        public uint ShadowW;
        public uint ShadowH;
        public uint Pad0, Pad1, Pad2;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        const uint shadowW = 256, shadowH = 256;
        ulong fbBytes = (ulong)(w * h * 4);
        ulong shadowBytes = (ulong)(shadowW * shadowH * 4); // R32F = 4 バイト

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS: 床キューブ + 5 つの浮遊キューブ ===
        var world = new Luxel.Ecs.World();
        int entityCount = 0;
        // 床 (薄いキューブを大きくスケール)
        var floor = world.Create();
        world.Set(floor, new LocalTransform(
            Matrix4x4.CreateScale(6f, 0.05f, 6f)
          * Matrix4x4.CreateTranslation(0, -0.6f, 0)));
        world.Set(floor, new Color3D(new Vector4(0.85f, 0.82f, 0.78f, 1f)));
        world.Set(floor, new MeshRef(MeshRef.Cube));
        // 影を落とすキューブ達 (高さ違い)
        Vector3[] positions = {
            new(-1.2f, 0.2f, -0.5f),
            new( 0.0f, 0.5f, +0.5f),
            new(+1.2f, 0.0f, -0.6f),
            new(-0.6f, 0.9f, +1.0f),
            new(+0.8f, 0.7f, +1.4f),
        };
        Vector4[] colors = {
            new(0.95f, 0.40f, 0.40f, 1f),
            new(0.40f, 0.85f, 0.95f, 1f),
            new(0.95f, 0.85f, 0.40f, 1f),
            new(0.55f, 0.95f, 0.45f, 1f),
            new(0.85f, 0.50f, 0.95f, 1f),
        };
        for (int i = 0; i < positions.Length; i++)
        {
            var e = world.Create();
            Quaternion rot = Quaternion.CreateFromYawPitchRoll(i * 0.5f, 0.25f, 0.15f);
            world.Set(e, new LocalTransform(
                Matrix4x4.CreateScale(0.45f)
              * Matrix4x4.CreateFromQuaternion(rot)
              * Matrix4x4.CreateTranslation(positions[i])));
            world.Set(e, new Color3D(colors[i]));
            world.Set(e, new MeshRef(MeshRef.Cube));
        }
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === 頂点バッファ + extract ===
        ReadOnlySpan<CubeMesh.Vertex> cubeVerts = CubeMesh.Vertices;
        using GpuBuffer cubeVb = device.Malloc((ulong)(cubeVerts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        cubeVerts.CopyTo(cubeVb.Span<CubeMesh.Vertex>(cubeVerts.Length));

        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);
        extractor.Extract();

        // === RT/Depth (Shadow と Main で分ける) ===
        using GpuTexture shadowRt = device.CreateRenderTarget(shadowW, shadowH, GpuFormat.R32Float);
        using GpuTexture shadowDepth = device.CreateDepthTarget(shadowW, shadowH);
        using GpuTexture mainColor = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture mainDepth = device.CreateDepthTarget(w, h);
        using GpuBuffer shadowBuf = device.Malloc(shadowBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer readback = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        // === パイプライン ===
        var shadowRaster = GpuRasterDesc.Default(GpuFormat.R32Float);
        shadowRaster.DepthTest = true;
        shadowRaster.DepthWrite = true;
        using GpuPipeline plShadow = device.CreateGraphicsPipeline(GpuShaderCode.Load("shadow_pass"), shadowRaster);

        var mainRaster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        mainRaster.DepthTest = true;
        mainRaster.DepthWrite = true;
        using GpuPipeline plMain = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_with_shadow"), mainRaster);

        // === カメラ ===
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.8f, 2.0f, -4.0f), new Vector3(0, 0, 0), Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // ライトカメラ (ortho、上から斜めに)。シーンを覆う十分な範囲。
        Vector3 lightDir = Vector3.Normalize(new Vector3(0.5f, 0.95f, 0.25f));
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(
            cameraPosition: lightDir * 6.0f,
            cameraTarget: Vector3.Zero,
            cameraUpVector: Vector3.UnitY);
        Matrix4x4 lightProj = Matrix4x4.CreateOrthographic(6.5f, 6.5f, 0.5f, 12f);
        Matrix4x4 lightVP = lightView * lightProj;

        // === RenderGraph ===
        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hVerts    = rg.ImportBuffer(cubeVb, "verts");
        BufferHandle hInsts    = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
        BufferHandle hShadow   = rg.ImportBuffer(shadowBuf, "shadowBuf");
        BufferHandle hReadback = rg.ImportBuffer(readback, "readback");

        // Pass1: shadow 生成 (graphics) + buffer 化 (CopyTextureToBuffer)
        rg.AddPass("ShadowPass", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts)
          .Write(hShadow, ResourceUsage.CopyDest)
          .Execute(ctx =>
          {
              var args = new ShadowArgs
              {
                  LightVP = Matrix4x4.Transpose(lightVP),
                  VertexBufIndex = ctx.BindlessIndex(hVerts),
                  InstanceBufIndex = ctx.BindlessIndex(hInsts),
              };
              ctx.Cmd.BeginRendering(shadowRt, shadowDepth, 1f, 0, 0, 0, 1f)
                     .SetGraphicsPipeline(plShadow)
                     .SetRootArguments(args)
                     .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering()
                     .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                     .CopyTextureToBuffer(shadowRt, shadowBuf);
          });

        // Pass2: メイン視点で描画 (shadow buf を読む)、CopyTextureToBuffer で readback
        rg.AddPass("MainPass", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts).Read(hShadow)
          .Write(hReadback, ResourceUsage.CopyDest)
          .Execute(ctx =>
          {
              var args = new MainArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  LightVP = Matrix4x4.Transpose(lightVP),
                  VertexBufIndex = ctx.BindlessIndex(hVerts),
                  InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  ShadowBufIndex = ctx.BindlessIndex(hShadow),
                  ShadowW = shadowW, ShadowH = shadowH,
              };
              ctx.Cmd.BeginRendering(mainColor, mainDepth, 0.10f, 0.12f, 0.18f, 1f, 1f)
                     .SetGraphicsPipeline(plMain)
                     .SetRootArguments(args)
                     .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering()
                     .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                     .CopyTextureToBuffer(mainColor, readback);
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // === 検証: 床上の暗いピクセル (=影) が存在 ===
        Span<byte> px = readback.Span<byte>((int)fbBytes);
        int total = (int)(w * h);
        int colored = 0;
        // 床は灰白なので、明度低めの灰色 = 影と判定 (R≈G≈B 近接 + 中明度)
        int shadowed = 0;
        for (int i = 0; i < total; i++)
        {
            byte r = px[i * 4 + 0], g = px[i * 4 + 1], b = px[i * 4 + 2];
            int sum = r + g + b;
            if (sum > 30) colored++;
            // 灰色域 (R≈G≈B, mid-low brightness): 影候補
            int maxDiff = Math.Max(Math.Max(Math.Abs(r - g), Math.Abs(g - b)), Math.Abs(r - b));
            if (maxDiff < 25 && sum > 60 && sum < 350) shadowed++;
        }
        Console.WriteLine($"非背景ピクセル: {colored}/{total} ({colored * 100 / total}%)");
        Console.WriteLine($"灰色域 (床+影) のピクセル: {shadowed}");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_shadow.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, px);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool ok = colored > total / 4 && shadowed > 500;
        Console.WriteLine(ok ? "OK: RG-M5c (shadow map: ライト視点 R32F → bindless buffer → 比較) 動作"
                             : "FAILED: 影が落ちていません");
        return ok ? 0 : 1;
    }
}
