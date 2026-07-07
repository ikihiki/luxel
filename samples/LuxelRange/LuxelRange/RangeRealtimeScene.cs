using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.AssetRuntime;
using Luxel.Ecs;
using Luxel.Framework;
using Luxel.Input;
using Luxel.RenderGraph;
using LuxelRange.Core;

namespace LuxelRange;

/// <summary>
/// 実時間シーン (capstone ②)。<see cref="RangeGame"/> を固定 dt で駆動し、起伏メッシュ地形 (scene_pbr_lite) と
/// 的/小物 (cube_forward) を <see cref="Framebuffer"/> へ描く (提示は Program が <see cref="GpuSurface.Present"/>)。
/// この薄い exe 段ではカメラ自動旋回 + 定期発射の attract 動作で描画・物理・publish 経路を通す
/// (キーボード/マウス操作・Fox skin・パーティクル・Title/Result UI は後続)。
/// </summary>
public sealed class RangeRealtimeScene : GameScene
{
    public const int Width = 512, Height = 320;   // 512*4 = 2048B (256B 整列)

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public Matrix4x4 ViewProj; public uint VertexBufIndex, InstanceBufIndex, Pad0, Pad1; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PbrArgs { public Matrix4x4 ViewProj; public uint VertexBufIndex, IndexBufIndex, InstanceBufIndex, InstanceStart; }

    private readonly RangeGame _game;
    private readonly SceneLoopServices _loop;
    private OrbitCamera _cam = new(new Vector3(0, 0.8f, -6f), yaw: 0f, pitch: 0.40f, distance: 18f,
        fovYRadians: MathF.PI / 3.4f, aspect: (float)Width / Height);
    private float _fireTimer = 0.4f;

    private GpuBuffer? _fb, _vb, _terrainVb, _terrainIb, _terrainInst;
    private int _terrainIdxCount;
    private GpuTexture? _target, _depth;
    private GpuPipeline? _cubePipe, _pbrPipe;
    private Render3DExtractSystem? _extractor;
    private bool _init;

    // 入力アクション (矢印でカメラ旋回、Space 発射、Esc 終了)
    private Axis1DAction _orbitH = null!, _orbitV = null!;
    private ButtonAction _fire = null!, _quit = null!;
    private bool _prevFire;

    public GpuBuffer Framebuffer => _fb!;
    public uint StridePixels => Width;
    public bool QuitRequested { get; private set; }

    public RangeRealtimeScene(SceneLoopServices loop, RangeGame game) : base(loop) { _loop = loop; _game = game; }

    protected override double FixedDeltaSeconds => RangeSim.FixedDt;

    protected override void OnUpdate(UpdateContext ctx)
    {
        if (_init) return;
        _game.StartRound();   // Play 状態の sim/world を作ってから
        InitGpu();            // その world から extractor/地形バッファを作る

        _orbitH = new Axis1DAction("orbitH");
        _orbitH.ButtonPairs.Add((KeyCode.Right, KeyCode.Left));
        _orbitV = new Axis1DAction("orbitV");
        _orbitV.ButtonPairs.Add((KeyCode.Up, KeyCode.Down));
        _fire = new ButtonAction("fire", KeyCode.Space);
        _quit = new ButtonAction("quit", KeyCode.Escape);
        var inputCtx = new InputContext("range");
        inputCtx.Add(_orbitH); inputCtx.Add(_orbitV); inputCtx.Add(_fire); inputCtx.Add(_quit);
        _loop.InputStack?.Push(inputCtx);

        _init = true;
    }

    protected override void OnFixedUpdate(FixedUpdateContext ctx)
    {
        if (!_init) return;
        float dt = RangeSim.FixedDt;

        if (_quit.Value.Value) QuitRequested = true;

        // カメラ旋回 (矢印) + ゆっくり自動旋回 (無操作でも見栄えする)
        _cam.Orbit((0.08f + _orbitH.Value.Value * 1.4f) * dt, _orbitV.Value.Value * 1.0f * dt);

        // 発射 (Space の押下エッジ) — 画面中央 (カメラ前方) へ CCD 弾
        bool fh = _fire.Value.Value, fp = fh && !_prevFire;
        _prevFire = fh;
        if (fp && _game.State == RangeState.Play)
        {
            Vector3 dir = Vector3.Normalize(_cam.Target - _cam.Eye);
            _game.Fire(_cam.Eye + dir * 0.5f, dir);
        }

        _game.Step();
        _game.Sim.ClearEvents();   // exe 段では演出/音を未配線なので毎ステップ消費
    }

    protected override void OnRender(RenderContext ctx)
    {
        if (!_init) return;
        _extractor!.Extract();

        Matrix4x4 vpT = Matrix4x4.Transpose(_cam.ViewProjection);
        using var rg = new Luxel.RenderGraph.RenderGraph(Device);
        BufferHandle hV = rg.ImportBuffer(_vb!, "verts");
        BufferHandle hInst = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
        BufferHandle hTV = rg.ImportBuffer(_terrainVb!, "tv");
        BufferHandle hTI = rg.ImportBuffer(_terrainIb!, "ti");
        BufferHandle hTInst = rg.ImportBuffer(_terrainInst!, "tinst");
        rg.AddPass("Range3D", PassQueue.Graphics)
          .Read(hV).Read(hInst).Read(hTV).Read(hTI).Read(hTInst).Write(hInst)
          .Execute(p =>
          {
              p.Cmd.BeginRendering(_target!, _depth!, 0.05f, 0.06f, 0.09f, 1f, 1f)
                   .SetGraphicsPipeline(_pbrPipe!)
                   .SetRootArguments(new PbrArgs { ViewProj = vpT, VertexBufIndex = p.BindlessIndex(hTV), IndexBufIndex = p.BindlessIndex(hTI), InstanceBufIndex = p.BindlessIndex(hTInst), InstanceStart = 0 })
                   .Draw((uint)_terrainIdxCount, 1);
              p.Cmd.SetGraphicsPipeline(_cubePipe!)
                   .SetRootArguments(new DrawArgs { ViewProj = vpT, VertexBufIndex = p.BindlessIndex(hV), InstanceBufIndex = p.BindlessIndex(hInst) })
                   .Draw((uint)Luxel.Assets.CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                   .EndRendering();
          });

        using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
        rg.Execute(cmd);
        cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(_target!, _fb!);
        cmd.Finish();
        Device.MainQueue.SubmitAndWait(cmd);
    }

    private void InitGpu()
    {
        _vb = Device.Malloc((ulong)(Luxel.Assets.CubeMesh.Vertices.Length * Luxel.Assets.CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        Luxel.Assets.CubeMesh.Vertices.CopyTo(_vb.Span<Luxel.Assets.CubeMesh.Vertex>(Luxel.Assets.CubeMesh.Vertices.Length));
        _target = Device.CreateRenderTarget(Width, Height, GpuFormat.Rgba8Unorm);
        _depth = Device.CreateDepthTarget(Width, Height);
        _fb = Device.Malloc((ulong)Width * Height * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        _cubePipe = Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);
        _pbrPipe = Device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        _extractor = new Render3DExtractSystem(_game.Sim.World, Device);
        TransformPropagateSystem.Run(_game.Sim.World);
        BuildTerrain();
    }

    private void BuildTerrain()
    {
        Vector3[] pos = _game.Sim.TerrainPositions, nrm = _game.Sim.TerrainNormals;
        int[] idx = _game.Sim.TerrainIndices;
        _terrainIdxCount = idx.Length;
        _terrainVb = Device.Malloc((ulong)(pos.Length * SceneBuilder.Vertex.Stride), GpuMemoryKind.HostMapped);
        var vspan = _terrainVb.Span<SceneBuilder.Vertex>(pos.Length);
        for (int i = 0; i < pos.Length; i++) vspan[i] = new SceneBuilder.Vertex { Position = pos[i], Normal = nrm[i], TexCoord0 = Vector2.Zero };
        _terrainIb = Device.Malloc((ulong)(idx.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
        var ispan = _terrainIb.Span<uint>(idx.Length);
        for (int i = 0; i < idx.Length; i++) ispan[i] = (uint)idx[i];
        _terrainInst = Device.Malloc((ulong)SceneInstanceData.Stride, GpuMemoryKind.HostMapped);
        _terrainInst.Span<SceneInstanceData>(1)[0] = new SceneInstanceData { World = Matrix4x4.Identity, BaseColor = new Vector4(0.34f, 0.42f, 0.32f, 1f) };
    }

    public override Task OnUnloadAsync()
    {
        _extractor?.Dispose();
        _cubePipe?.Dispose(); _pbrPipe?.Dispose();
        _depth?.Dispose(); _target?.Dispose();
        _vb?.Dispose(); _fb?.Dispose();
        _terrainVb?.Dispose(); _terrainIb?.Dispose(); _terrainInst?.Dispose();
        // RangeGame は DI singleton — 破棄は DI コンテナが行う (ここで Dispose すると二重破棄)。
        return Task.CompletedTask;
    }
}
