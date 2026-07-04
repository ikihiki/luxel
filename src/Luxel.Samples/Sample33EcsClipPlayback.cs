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
/// サンプル33 (AN-M3): AnimationClip を ECS に適用。
///
/// AnimationClip を**コードで構築** (glTF JSON パースは AN-M3b として将来作業):
///   Track 1: "cube/translation"  Vector3、上下振動  Linear 補間 (t=0,0.5,1.0)
///   Track 2: "cube/rotation"     Quaternion、Y 軸 1 回転  Linear (slerp) 補間
///
/// EcsAnimationTarget が "cube/translation" → Vector3、"cube/rotation" → Quaternion を LocalTransform に
/// 分解再合成で書き込み、TransformPropagateSystem で GlobalTransform 更新 → Render3DExtractSystem で描画。
///
/// 4 フレーム (t=0/0.5/1.0/1.5) で PNG 出力、vk/dx 一致を確認。
/// </summary>
public static class Sample33EcsClipPlayback
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

        // === ECS と Entity 1 個のセットアップ ===
        var world = new Luxel.Ecs.World();
        var cube = world.Create();
        world.Set(cube, new LocalTransform(Matrix4x4.CreateScale(0.6f)));
        world.Set(cube, new Color3D(new Vector4(0.4f, 0.85f, 0.55f, 1f)));
        world.Set(cube, new MeshRef(MeshRef.Cube));
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // === AnimationClip をコードで構築 ===
        var translationTrack = Tracks.Vector3("cube/translation", InterpolationKind.Linear,
            new Keyframe<Vector3>[]
            {
                new(0.00f, new Vector3(0, -0.5f, 0)),
                new(0.75f, new Vector3(0, +0.8f, 0)),
                new(1.50f, new Vector3(0, -0.5f, 0)),
            });
        var rotationTrack = Tracks.Quaternion("cube/rotation", InterpolationKind.Linear,
            new Keyframe<Quaternion>[]
            {
                new(0.00f, Quaternion.Identity),
                new(0.50f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 0.66f, 0.2f, 0)),
                new(1.00f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 1.33f, 0.4f, 0)),
                new(1.50f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 2.00f, 0.6f, 0)),
            });
        var clip = new AnimationClip("CubeMotion", new TrackBase[] { translationTrack, rotationTrack });

        // === Target を ECS にバインド ===
        var target = new EcsAnimationTarget(world).Bind("cube", cube);

        // === Clip を Player に投入 ===
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);
        Animate.Clip(clip, target).Play(player, clock);
        Console.WriteLine($"  clip tracks={clip.Tracks.Length}, duration={clip.Duration}s, player.ActiveCount={player.ActiveCount}");

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

        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.0f, 1.5f, -3.0f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        Matrix4x4 viewProj = view * proj;

        // === 4 フレームでスナップショット ===
        float[] snapshotsAtSec = { 0.0f, 0.5f, 1.0f, 1.5f };
        var observedPos = new List<Vector3>();
        int snapIdx = 0;
        for (int frame = 0; frame <= 90 && snapIdx < snapshotsAtSec.Length; frame++)
        {
            clock.Frame = frame;
            player.Update(clock);

            while (snapIdx < snapshotsAtSec.Length
                   && clock.TimeSec + 1e-4f >= snapshotsAtSec[snapIdx])
            {
                // ECS 状態を読み、再描画
                Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
                extractor.Extract();

                // 観測: cube の Translation 成分
                var gt = world.Get<GlobalTransform>(cube).Matrix;
                Matrix4x4.Decompose(gt, out _, out _, out var trans);
                observedPos.Add(trans);

                BufferHandle hVerts; BufferHandle hInsts;
                using var rg = new Luxel.RenderGraph.RenderGraph(device);
                hVerts = rg.ImportBuffer(cubeVb, "verts");
                hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "instances");
                BufferHandle hRead = rg.ImportBuffer(readback, "readback");
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

                string png = Path.Combine(AppContext.BaseDirectory, $"clip_t{snapshotsAtSec[snapIdx]:0.0}.png");
                PngWriter.WriteRgba(png, (int)w, (int)h, readback.Span<byte>((int)fbBytes));
                Console.WriteLine($"  t={snapshotsAtSec[snapIdx]:0.0}s: cube.y={trans.Y:0.000}, PNG={Path.GetFileName(png)}");
                snapIdx++;
            }
        }

        // === 検証 ===
        bool ok = true;
        // t=0: y=-0.5
        if (Math.Abs(observedPos[0].Y - (-0.5f)) > 0.05f) { ok = false; Console.Error.WriteLine($"FAILED: t=0 y expected ≈-0.5, got {observedPos[0].Y}"); }
        // t=0.75 近傍 (今回は 1.0 をスナップショット) で y は上昇途中
        // t=1.5: y=-0.5 (ループ前の終端)
        if (Math.Abs(observedPos[3].Y - (-0.5f)) > 0.05f) { ok = false; Console.Error.WriteLine($"FAILED: t=1.5 y expected ≈-0.5, got {observedPos[3].Y}"); }
        // 0.5 → 1.0 の間で y が高い (山の頂上付近)
        if (observedPos[1].Y < -0.3f) { ok = false; Console.Error.WriteLine($"FAILED: t=0.5 y too low ({observedPos[1].Y})"); }
        if (observedPos[2].Y < -0.3f) { ok = false; Console.Error.WriteLine($"FAILED: t=1.0 y too low ({observedPos[2].Y})"); }
        // Player に Track 2 個 + マーカーは未指定 → ActiveCount = 0 (全完了)
        if (player.ActiveCount != 0) { ok = false; Console.Error.WriteLine($"FAILED: player ActiveCount expected 0, got {player.ActiveCount}"); }

        Console.WriteLine(ok ? "OK: AN-M3 (AnimationClip + Track + EcsAnimationTarget + ECS 描画) 動作"
                             : "FAILED");
        return ok ? 0 : 1;
    }
}
