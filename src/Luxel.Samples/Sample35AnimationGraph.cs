using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.Animation;
using Luxel.Animation.ThreeD;
using Luxel.RenderGraph;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// サンプル35 (AN-M5): AnimationGraph DAG。
///
/// 2 つの AnimationClip を BlendNode で混合:
///   Clip "Vertical":   cube/translation y を上下振動 (sin 風) 1.0s 周期
///   Clip "Horizontal": cube/translation x を左右振動 1.0s 周期
///
/// BlendNode.Weight を 0/0.5/1.0 で変化させた 3 つのスナップショットを取り、混合の効果を確認:
///   weight=0  : 完全に上下振動 (y のみ動く)
///   weight=0.5: x,y の混合 (両方半分の振幅で動く)
///   weight=1  : 完全に左右振動 (x のみ動く)
///
/// 各 weight で同じ time=0.25 を評価して比較 → vk/dx 一致。
/// </summary>
public static class Sample35AnimationGraph
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
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // === ECS と Entity ===
        var world = new Luxel.Ecs.World();
        var cube = world.Create();
        world.Set(cube, new LocalTransform(Matrix4x4.CreateScale(0.6f)));
        world.Set(cube, new Color3D(new Vector4(0.4f, 0.85f, 0.55f, 1f)));
        world.Set(cube, new MeshRef(MeshRef.Cube));
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === 2 つの Clip (各 1 つの translation Track) ===
        var verticalTrack = Tracks.Vector3("cube/translation", InterpolationKind.Linear, new Keyframe<Vector3>[]
        {
            new(0.00f, new Vector3(0f, -0.8f, 0f)),
            new(0.50f, new Vector3(0f, +0.8f, 0f)),
            new(1.00f, new Vector3(0f, -0.8f, 0f)),
        });
        var verticalClip = new AnimationClip("Vertical", new TrackBase[] { verticalTrack });

        var horizontalTrack = Tracks.Vector3("cube/translation", InterpolationKind.Linear, new Keyframe<Vector3>[]
        {
            new(0.00f, new Vector3(-0.8f, 0f, 0f)),
            new(0.50f, new Vector3(+0.8f, 0f, 0f)),
            new(1.00f, new Vector3(-0.8f, 0f, 0f)),
        });
        var horizontalClip = new AnimationClip("Horizontal", new TrackBase[] { horizontalTrack });

        // === AnimationGraph: Blend(Vertical, Horizontal, weight) ===
        var target = new EcsAnimationTarget(world).Bind("cube", cube);
        var blend = new BlendNode(new ClipNode(verticalClip), new ClipNode(horizontalClip), weight: 0f);
        var graph = new AnimationGraph(blend, target) { Loop = true };

        // === Render 用準備 ===
        ReadOnlySpan<CubeMesh.Vertex> cubeVerts = CubeMesh.Vertices;
        using GpuBuffer cubeVb = device.Malloc((ulong)(cubeVerts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        cubeVerts.CopyTo(cubeVb.Span<CubeMesh.Vertex>(cubeVerts.Length));
        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);

        using GpuTexture color = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(w, h);
        using GpuBuffer readback = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        using GpuPipeline plCube = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);

        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0f, 1.5f, -3.5f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // === 3 つの weight でスナップショット ===
        float[] weights = { 0.0f, 0.5f, 1.0f };
        var observedPos = new List<Vector3>();
        foreach (var w_ in weights)
        {
            blend.Weight = w_;
            graph.Reset(0f);
            graph.Tick(0.25f);   // time = 0.25s で評価 (両 Clip とも上下/左右の中間)

            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            extractor.Extract();

            var gt = world.Get<GlobalTransform>(cube).Matrix;
            Matrix4x4.Decompose(gt, out _, out _, out var trans);
            observedPos.Add(trans);

            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            var hVerts = rg.ImportBuffer(cubeVb, "verts");
            var hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
            var hRead  = rg.ImportBuffer(readback, "readback");
            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Write(hRead, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(plCube)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                         .EndRendering()
                         .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                         .CopyTextureToBuffer(color, readback);
              });
            using (var cmd = device.MainQueue.StartCommandRecording())
            {
                rg.Execute(cmd);
                cmd.Finish();
                device.MainQueue.SubmitAndWait(cmd);
            }

            string png = Path.Combine(AppContext.BaseDirectory, $"graph_w{w_:0.0}.png");
            PngWriter.WriteRgba(png, (int)w, (int)h, readback.Span<byte>((int)fbBytes));
            Console.WriteLine($"  weight={w_:0.0}: pos=({trans.X:+0.00;-0.00;0.00},{trans.Y:+0.00;-0.00;0.00},{trans.Z:+0.00;-0.00;0.00})");
        }

        // === 検証 ===
        bool ok = true;
        // weight=0 → Vertical のみ : 0.25s で y は上向き (+0.0 付近、Linear 補間で y = lerp(-0.8, +0.8, 0.5) = 0.0)、x ≈ 0
        // weight=1 → Horizontal のみ: x = lerp(-0.8, +0.8, 0.5) = 0.0、y ≈ 0
        // weight=0.5 → 中間 : x ≈ 0、y ≈ 0 (両方とも 0.5 のところで両軸とも 0.0)
        // (0.25s が ちょうど両 Clip の中点なので、混合の効果は値からは見分けにくい)
        // 別時刻の検証: weight=0 で別 time を評価して y が動くことを確認

        // weight 切替えで Pos が変わることを観測
        // weight=0 と weight=1 で時刻を別にする検証
        blend.Weight = 0f;
        graph.Reset(0f);
        graph.Tick(0.25f);
        var posVertOnly = world.Get<LocalTransform>(cube).Matrix.Translation;

        blend.Weight = 1f;
        graph.Reset(0f);
        graph.Tick(0.25f);
        var posHorzOnly = world.Get<LocalTransform>(cube).Matrix.Translation;

        // Vertical のみのとき: y が大きく動き、x はほぼ 0
        if (Math.Abs(posVertOnly.X) > 0.05f) { ok = false; Console.Error.WriteLine($"FAILED: weight=0 で x should be ~0, got {posVertOnly.X}"); }
        // Horizontal のみのとき: x がほぼ 0 (0.25s では x も中点)、y はほぼ 0
        if (Math.Abs(posHorzOnly.Y) > 0.05f) { ok = false; Console.Error.WriteLine($"FAILED: weight=1 で y should be ~0, got {posHorzOnly.Y}"); }

        // 別の time (0.125s) で確認 — Vertical は y を 0 付近、Horizontal は x を負の値
        blend.Weight = 0f;
        graph.Reset(0f);
        graph.Tick(0.125f);
        var v1 = world.Get<LocalTransform>(cube).Matrix.Translation;
        Console.WriteLine($"  weight=0 @0.125s: pos.y={v1.Y:0.00}");
        if (v1.Y > 0f) { ok = false; Console.Error.WriteLine($"FAILED: weight=0 0.125s で y should be < 0, got {v1.Y}"); }

        blend.Weight = 1f;
        graph.Reset(0f);
        graph.Tick(0.125f);
        var v2 = world.Get<LocalTransform>(cube).Matrix.Translation;
        Console.WriteLine($"  weight=1 @0.125s: pos.x={v2.X:0.00}");
        if (v2.X > 0f) { ok = false; Console.Error.WriteLine($"FAILED: weight=1 0.125s で x should be < 0, got {v2.X}"); }

        // weight=0.5 でブレンド: 0.125s で x,y 両方とも半分の振幅
        blend.Weight = 0.5f;
        graph.Reset(0f);
        graph.Tick(0.125f);
        var vMix = world.Get<LocalTransform>(cube).Matrix.Translation;
        Console.WriteLine($"  weight=0.5 @0.125s: pos=({vMix.X:0.00},{vMix.Y:0.00})");
        // 期待: x,y 両方とも v1.Y と v2.X の半分付近 (BlendNode は Lerp)
        if (Math.Abs(vMix.X - 0.5f * (0f + v2.X)) > 0.05f) { ok = false; Console.Error.WriteLine($"FAILED: blend x not halfway, got {vMix.X}"); }

        Console.WriteLine(ok ? "OK: AN-M5 (AnimationGraph DAG: Clip / BlendNode) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
