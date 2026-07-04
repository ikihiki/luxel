using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Input;
using Luxel.RenderGraph;

namespace Luxel.Samples;

/// <summary>
/// Sample 87 (INPUT-M7): WASD で cube 移動 + M でメニュー切替。
/// FakeInputSource で 4 フレームぶんの入力をシミュレートし、各フレームで PNG 出力 → 位置変化を検証。
///
/// Frame 0: 入力なし → cube 原点。
/// Frame 1: W 押下 → gameplay context の "Move" (Axis2D) が (0,1) → cube が Z 方向へ移動。
/// Frame 2: M 押下 → StateMachine が Gameplay→Menu 遷移 → gameplay context suspend。
/// Frame 3: W 押下継続 → menu 中は反応せず cube 停止 (前フレームの位置を維持)。
/// </summary>
public static class Sample87Input
{
    private enum AppState { Gameplay, Menu }

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
        const uint W = 320, H = 320;
        Console.WriteLine("=== Sample 87: Input System (WASD 移動 + M でメニュー切替) ===");
        using GpuDevice device = createDevice();

        // === ECS: 単一 cube ===
        var world = new Luxel.Ecs.World();
        var cube = world.CreateEntity(
            new Luxel.Ecs.LocalTransform(Matrix4x4.Identity),
            new Luxel.Ecs.Color3D(new Vector4(0.30f, 0.70f, 0.95f, 1f)),
            new Luxel.Ecs.MeshRef(0));

        // === Input セットアップ ===
        var bus = new InputBus();
        var fake = new FakeInputSource();

        var gameplay = new InputContext("gameplay");
        var move = gameplay.Add(new Axis2DAction("Move"));
        move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
        var toggle = gameplay.Add(new ButtonAction("ToggleMenu", KeyCode.M));

        var menu = new InputContext("menu");
        var back = menu.Add(new ButtonAction("Back", KeyCode.M));   // menu 側でも M を使う

        var stack = new InputStack();
        var sm = new InputStateMachine<AppState>(stack, AppState.Gameplay)
            .Register(AppState.Gameplay, gameplay)
            .Register(AppState.Menu, menu);
        sm.Activate();

        // ToggleMenu.Triggered / Back.Triggered で state を反転
        toggle.Triggered += () => sm.State.Value = AppState.Menu;
        back.Triggered += () => sm.State.Value = AppState.Gameplay;

        Vector3 cubePos = Vector3.Zero;

        // === 頂点バッファ ===
        ReadOnlySpan<CubeMesh.Vertex> verts = CubeMesh.Vertices;
        using GpuBuffer vbuf = device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        verts.CopyTo(vbuf.Span<CubeMesh.Vertex>(verts.Length));

        // === Pipeline ===
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 3, -5), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        using var extractor = new Luxel.AssetRuntime.Render3DExtractSystem(world, device);

        (byte[] Px, Vector3 Pos, AppState State) SimFrame(int frameIdx, Action<FakeInputSource> setup)
        {
            setup(fake);
            fake.Poll(bus);
            stack.Update(bus);
            sm.Sync();

            // move.Value.Value を cube position に反映 (per-frame delta、単純化のため 0.5 unit/frame)
            if (move.IsActive.Value)
            {
                var v = move.Value.Value;
                cubePos += new Vector3(v.X, 0, v.Y) * 0.5f;
            }
            cube.GetComponent<Luxel.Ecs.LocalTransform>();   // no-op read
            cube.RemoveComponent<Luxel.Ecs.LocalTransform>();
            cube.AddComponent(new Luxel.Ecs.LocalTransform(Matrix4x4.CreateTranslation(cubePos)));

            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            extractor.Extract();

            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hV = rg.ImportBuffer(vbuf, "verts");
            BufferHandle hInst = rg.ImportBuffer(extractor.InstanceBuffer, "insts");
            rg.AddPass("Render3D", PassQueue.Graphics).Read(hV).Read(hInst).Write(hInst)
              .Execute(ctx =>
              {
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hV),
                      InstanceBufIndex = ctx.BindlessIndex(hInst),
                  };
                  ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(pipeline)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)extractor.InstanceCount)
                         .EndRendering();
              });
            using (var cmd = device.MainQueue.StartCommandRecording())
            {
                rg.Execute(cmd);
                cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(color, readback);
                cmd.Finish();
                device.MainQueue.SubmitAndWait(cmd);
            }
            var pxArr = readback.Span<byte>((int)(W * H * 4)).ToArray();
            string png = Path.Combine(AppContext.BaseDirectory, $"input_frame_{frameIdx}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, pxArr);
            Console.WriteLine($"  frame {frameIdx}: state={sm.State.Peek()}, cubePos={cubePos}, move={move.Value.Value}");
            return (pxArr, cubePos, sm.State.Peek());
        }

        var f0 = SimFrame(0, _ => { });                                    // 入力なし
        var f1 = SimFrame(1, s => s.PressKey(KeyCode.W));                  // W 押下
        var f2 = SimFrame(2, s => { s.ReleaseKey(KeyCode.W); s.PressKey(KeyCode.M); });   // W 離す + M press (次フレームで Triggered)
        var f3 = SimFrame(3, s => { s.ReleaseKey(KeyCode.M); s.PressKey(KeyCode.W); });   // M release + W: menu 中は無反応

        // 検証: W 押下で Z 方向に移動、menu 遷移後は移動停止
        bool moved01 = f0.Pos != f1.Pos;
        bool stateSwitched = f2.State == AppState.Menu;
        bool stayedInMenu = f2.Pos == f3.Pos;   // f3 で W 押下しても position 動かず
        Console.WriteLine($"  moved 0→1: {moved01}, switched to menu at 2: {stateSwitched}, stayed at 2→3: {stayedInMenu}");

        long DiffL1(byte[] a, byte[] b) { long d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]); return d; }
        Console.WriteLine($"  pixel diff 0↔1: {DiffL1(f0.Px, f1.Px)}, 2↔3: {DiffL1(f2.Px, f3.Px)}");

        bool ok = moved01 && stateSwitched && stayedInMenu;
        Console.WriteLine(ok ? "OK: Input system で cube 移動 + menu 切替が動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
