using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Ecs;
using Luxel.Framework;
using Luxel.Gltf;
using Luxel.Input;
using Luxel.Particles;
using Luxel.Particles.ThreeD;
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
    [StructLayout(LayoutKind.Sequential)]
    private struct SkinnedArgs { public Matrix4x4 ViewProj; public uint VertexBufIndex, IndexBufIndex, InstanceBufIndex, JointBufIndex, InstanceStart, Pad0, Pad1, Pad2; }

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

    // 動く的 = Fox.glb の skin モデル (別 world、instance World で FoxPosition へ配置)
    private Luxel.Ecs.World? _foxWorld;
    private SceneAssets? _foxAssets;
    private SceneAnimationPlayer? _foxAnim;
    private float _foxAnimDur, _foxAnimTime;
    private ScenePrimitiveGpu? _foxPrim;
    private RenderBuffer<Matrix4x4>? _foxJoints;
    private GpuBuffer? _foxInst;
    private GpuPipeline? _skinPipe;
    private const float FoxModelScale = 0.018f, FoxHalfY = 0.6f;

    // 命中パーティクル (火花バースト)
    private ParticleSystem? _burstPs;
    private ParticleBillboards? _hitBurst;

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
        if (!_game.Sim.FoxFlinching) _foxAnimTime += dt;   // ひるみ中は歩行停止
        if (_foxPrim is not null) UploadFox();
        foreach (RangeEvent ev in _game.Sim.Events)
            if (ev.Kind is RangeEventKind.TargetHit or RangeEventKind.FoxHit)
                _burstPs!.Emit(ev.Position, 32);
        _game.Sim.ClearEvents();
        _burstPs!.Update(dt);
    }

    protected override void OnRender(RenderContext ctx)
    {
        if (!_init) return;
        _extractor!.Extract();
        _hitBurst?.Sync();
        (Vector3 billRight, Vector3 billUp) = ParticleBillboards.CameraAxes(_cam.Eye, _cam.Target);

        Matrix4x4 viewProj = _cam.ViewProjection;
        Matrix4x4 vpT = Matrix4x4.Transpose(viewProj);
        using var rg = new Luxel.RenderGraph.RenderGraph(Device);
        BufferHandle hV = rg.ImportBuffer(_vb!, "verts");
        BufferHandle hInst = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
        BufferHandle hTV = rg.ImportBuffer(_terrainVb!, "tv");
        BufferHandle hTI = rg.ImportBuffer(_terrainIb!, "ti");
        BufferHandle hTInst = rg.ImportBuffer(_terrainInst!, "tinst");
        var pass = rg.AddPass("Range3D", PassQueue.Graphics)
          .Read(hV).Read(hInst).Read(hTV).Read(hTI).Read(hTInst).Write(hInst);
        bool drawFox = _foxPrim is not null;
        BufferHandle hFV = default, hFI = default, hFInst = default, hFJoint = default;
        if (drawFox)
        {
            hFV = rg.ImportBuffer(_foxPrim!.VertexBuffer, "fv");
            hFI = rg.ImportBuffer(_foxPrim.IndexBuffer, "fi");
            hFInst = rg.ImportBuffer(_foxInst!, "finst");
            hFJoint = rg.ImportBuffer(_foxJoints!.Buffer, "fj");
            pass = pass.Read(hFV).Read(hFI).Read(hFInst).Read(hFJoint);
        }
        pass.Execute(p =>
          {
              p.Cmd.BeginRendering(_target!, _depth!, 0.05f, 0.06f, 0.09f, 1f, 1f)
                   .SetGraphicsPipeline(_pbrPipe!)
                   .SetRootArguments(new PbrArgs { ViewProj = vpT, VertexBufIndex = p.BindlessIndex(hTV), IndexBufIndex = p.BindlessIndex(hTI), InstanceBufIndex = p.BindlessIndex(hTInst), InstanceStart = 0 })
                   .Draw((uint)_terrainIdxCount, 1);
              p.Cmd.SetGraphicsPipeline(_cubePipe!)
                   .SetRootArguments(new DrawArgs { ViewProj = vpT, VertexBufIndex = p.BindlessIndex(hV), InstanceBufIndex = p.BindlessIndex(hInst) })
                   .Draw((uint)Luxel.Assets.CubeMesh.VertexCount, (uint)_extractor.InstanceCount);
              if (drawFox)
                  p.Cmd.SetGraphicsPipeline(_skinPipe!)
                       .SetRootArguments(new SkinnedArgs { ViewProj = vpT, VertexBufIndex = p.BindlessIndex(hFV), IndexBufIndex = p.BindlessIndex(hFI), InstanceBufIndex = p.BindlessIndex(hFInst), JointBufIndex = p.BindlessIndex(hFJoint), InstanceStart = 0 })
                       .Draw((uint)_foxPrim!.IndexCount, 1);
              _hitBurst?.Draw(p.Cmd, viewProj, billRight, billUp);   // 命中パーティクル (ビルボード)
              p.Cmd.EndRendering();
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
        BuildFox();

        var cfg = new ParticleConfig(
            Life: ParticleValue.Range(0.30f, 0.70f), Speed: ParticleValue.Range(2f, 5f),
            SpreadRadians: MathF.PI, BaseAngle: 0f, Gravity: -6f, Drag: 0.3f, Size: 0.09f,
            Color: new ParticleColor(Rgba(255, 220, 120, 255), Rgba(240, 90, 40, 0)), Shape: ParticleShape.Circle, Spherical: true);
        _burstPs = new ParticleSystem(cfg, capacity: 400, seed: 0x2A11);
        _hitBurst = new ParticleBillboards(Device, _burstPs);
    }

    private static uint Rgba(byte r, byte g, byte b, byte a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));

    /// <summary>Fox.glb (exe 隣の assets/) を別 world で組み、skin 描画資源を用意して初期ポーズを焼く。</summary>
    private void BuildFox()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "Fox.glb");
        if (!File.Exists(path)) return;   // アセット無しなら Fox 描画スキップ (物理 proxy は箱)

        AssetDocument doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        for (int i = 0; i < doc.Materials.Count; i++) doc.Materials[i].BaseColorFactor = new Vector4(0.80f, 0.52f, 0.28f, 1f);
        _foxWorld = new Luxel.Ecs.World();
        _foxAssets = SceneBuilder.Build(_foxWorld, doc, Device);
        if (doc.Animations.Count > 0)
        {
            int walk = Math.Min(1, doc.Animations.Count - 1);
            _foxAnim = new SceneAnimationPlayer(_foxWorld, _foxAssets, doc.Animations[walk]);
            _foxAnimDur = MathF.Max(0.01f, doc.Animations[walk].Duration);
        }
        PoseFox(0f, out Matrix4x4[] jointMats);
        if (_foxPrim is null) { _foxWorld = null; _foxAssets = null; return; }

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        _foxJoints = new RenderBuffer<Matrix4x4>(Device, Math.Max(1, jointMats.Length), "foxJoints");
        _foxInst = Device.Malloc((ulong)SceneInstanceData.Stride, GpuMemoryKind.HostMapped);
        _skinPipe = Device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_skinned"), raster);
        UploadFox();
    }

    private void PoseFox(float t, out Matrix4x4[] jointMats)
    {
        _foxAnim?.Sample(_foxAnimDur > 0 ? t % _foxAnimDur : 0f);
        TransformPropagateSystem.Run(_foxWorld!);
        SkinningSystem.Run(_foxWorld!, _foxAssets!);
        Matrix4x4[] mats = Array.Empty<Matrix4x4>();
        _foxWorld!.Query<AssetMeshRef, AssetSkinRef, JointMatrices>().ForEachEntity(
            (ref AssetMeshRef mr, ref AssetSkinRef _, ref JointMatrices jm, Friflo.Engine.ECS.Entity _) =>
            {
                AssetPrimitive p = mr.Mesh.Primitives[0];
                if (_foxAssets!.Primitives.TryGetValue(p, out ScenePrimitiveGpu gpu) && gpu.HasSkinning) { _foxPrim = gpu; mats = jm.Matrices; }
            });
        jointMats = mats;
    }

    private void UploadFox()
    {
        if (_foxPrim is null) return;
        PoseFox(_foxAnimTime, out Matrix4x4[] mats);
        for (int i = 0; i < mats.Length && i < _foxJoints!.Data.Length; i++) _foxJoints.Data[i] = mats[i];
        _foxJoints!.MarkDirty();
        _foxJoints.FlushImmediate();
        Vector3 pos = _game.Sim.FoxPosition;
        Matrix4x4 world = Matrix4x4.CreateScale(FoxModelScale) * Matrix4x4.CreateRotationY(MathF.PI / 2)
                        * Matrix4x4.CreateTranslation(pos.X, pos.Y - FoxHalfY, pos.Z);
        _foxInst!.Span<SceneInstanceData>(1)[0] = new SceneInstanceData { World = world, BaseColor = new Vector4(0.80f, 0.52f, 0.28f, 1f) };
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
        _foxAssets?.Dispose(); _foxJoints?.Dispose(); _foxInst?.Dispose(); _skinPipe?.Dispose();
        _hitBurst?.Dispose();
        // RangeGame は DI singleton — 破棄は DI コンテナが行う (ここで Dispose すると二重破棄)。
        return Task.CompletedTask;
    }
}
