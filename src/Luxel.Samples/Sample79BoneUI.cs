using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Scene.UI;

namespace Luxel.Samples;

/// <summary>
/// Sample 79: Box.gltf + BoneEditor (UI 操作) でメッシュの TRS を変更 → PNG 比較で
/// UI 経由の編集が実描画に反映されることを検証。
/// </summary>
public static class Sample79BoneUI
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
        const uint W = 256, H = 256;
        Console.WriteLine("=== Sample 79: BoneEditor UI 操作 → 実描画 PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "Box.gltf"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "Box.gltf"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null) { Console.Error.WriteLine("FAILED: Box.gltf"); return 1; }

        var doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        if (doc.Materials.Count > 0) doc.Materials[0].BaseColor = new Vector4(0.95f, 0.80f, 0.30f, 1f);

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        var editor = new BoneEditor(world, assets.NodeEntities);

        // GPU setup
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        var view = Matrix4x4.CreateLookAt(new Vector3(2.5f, 2.0f, -3.0f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        // mesh ノード (assets.NodeEntities[?] で mesh を持つ entity を探す)
        int meshNodeIdx = -1;
        for (int i = 0; i < doc.Nodes.Count; i++)
            if (doc.Nodes[i].MeshIndex is not null) { meshNodeIdx = i; break; }
        if (meshNodeIdx < 0) { Console.Error.WriteLine("FAILED: no mesh node"); return 1; }

        // 3 状態: 初期 / TX=2.0 / RY=45度
        byte[][] snaps = new byte[3][];
        string[] labels = { "initial", "TX=+2", "RY=45deg + TX=+2" };

        Action render = () =>
        {
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            using var extractor = new SceneRenderExtractor(world, assets);
            extractor.Extract();

            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "insts");
            var mesh = assets.Meshes[0];
            BufferHandle hVerts = rg.ImportBuffer(mesh.VertexBuffer, "verts");

            rg.AddPass("Mesh", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Write(hInsts)
              .Execute(ctx =>
              {
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(pipeline)
                         .SetRootArguments(args)
                         .Draw((uint)mesh.VertexCount, (uint)extractor.InstanceCount)
                         .EndRendering();
              });

            using var cmd = device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(color, readback);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        };

        for (int step = 0; step < 3; step++)
        {
            // BoneEditor の Signal を UI から変更
            editor.SelectedIndex.Value = meshNodeIdx;
            if (step == 1) { editor.TX.Value = 2.0f; editor.Apply(); }
            if (step == 2) { editor.TX.Value = 2.0f; editor.RY.Value = 45.0f; editor.Apply(); }

            render();
            var px = readback.Span<byte>((int)(W * H * 4));
            snaps[step] = px.ToArray();
            string png = Path.Combine(AppContext.BaseDirectory, $"bone_ui_{step}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, px);
            Console.WriteLine($"  step{step}: PNG={Path.GetFileName(png)} ({labels[step]})");
        }

        long diff01 = 0, diff12 = 0;
        for (int i = 0; i < snaps[0].Length; i++)
        {
            diff01 += Math.Abs(snaps[0][i] - snaps[1][i]);
            diff12 += Math.Abs(snaps[1][i] - snaps[2][i]);
        }
        Console.WriteLine($"  initial → TX=+2 diff: {diff01} (UI で translation 編集)");
        Console.WriteLine($"  TX=+2 → +RY=45 diff: {diff12} (UI で rotation 編集)");

        bool ok = diff01 > 1000 && diff12 > 1000;
        Console.WriteLine(ok ? "OK: DEMO-M5 (BoneEditor UI 操作 → 実描画 反映) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
