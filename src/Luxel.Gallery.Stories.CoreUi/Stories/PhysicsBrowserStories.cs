using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Controls;
using Luxel.Ecs;
using Luxel.Graphics;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.TwoD;
using Luxel.Physics;
using Luxel.Physics.Gizmos;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;
using Rg = Luxel.Graphics.RenderGraph.RenderGraph;

namespace Luxel.Gallery.Stories;

/// <summary>Browser-safe BepuPhysics samples shared by native Gallery and browser WebAssembly.</summary>
public static class PhysicsBrowserStories
{
    private const uint ViewWidth = 256;
    private const uint ViewHeight = 256;
    private const float CanvasWidth = 460;
    private const float CanvasHeight = 300;
    private const string BrowserNote = "Runs BepuPhysics through the shared Gallery WebAssembly story runner.";
    private const string TerrainKind = "demo.mesh";

    private static readonly Vector4[] Palette =
    [
        new(1.00f, 0.40f, 0.40f, 1f),
        new(0.40f, 0.95f, 0.55f, 1f),
        new(0.35f, 0.70f, 1.00f, 1f),
        new(1.00f, 0.90f, 0.35f, 1f),
        new(0.90f, 0.55f, 1.00f, 1f),
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0;
        public uint Pad1;
    }

    /// <summary>A deterministic box tower simulated by BepuPhysics and rendered from ECS extraction.</summary>
    [Story("Examples/3D/PhysicsFalling", Width = 320, Height = 320, Order = 127, CapabilityNote = BrowserNote)]
    public static Widget PhysicsFalling(StoryContext ctx) => PhysicsGpuView(ctx, null, null, null);

    /// <summary>Interactive gravity, bounciness, and deterministic reset controls.</summary>
    [Story("Examples/3D/PhysicsPlayground", Width = 320, Height = 320, Order = 128, CapabilityNote = BrowserNote)]
    public static Widget PhysicsPlayground(StoryContext ctx)
    {
        Luxel.UI.Signal<float> gravity = ctx.Signal("gravity", 9.8f, "下向き重力の強さ (m/s²)");
        Luxel.UI.Signal<float> bounciness = ctx.Signal("bounciness", 2f, "接触の反発上限 (MaximumRecoveryVelocity, m/s)");
        Luxel.UI.Signal<bool> reset = ctx.Signal("reset", false, "トグルするとシーンを初期状態へ再構築");
        return PhysicsGpuView(ctx, gravity, bounciness, reset);
    }

    private static Widget PhysicsGpuView(StoryContext ctx, Luxel.UI.Signal<float>? gravity,
        Luxel.UI.Signal<float>? bounciness, Luxel.UI.Signal<bool>? reset)
    {
        if (ctx.DeviceOrNull is not { } device)
            return ctx.Snap(Frame(GpuView(ViewWidth, ViewHeight,
                static (_, _, _) => GpuViewRenderResult.Failed,
                animated: false)));

        var demo = new PhysicsGpuDemo(device, gravity, bounciness, reset);
        return ctx.Snap(Frame(GpuView(
            ViewWidth,
            ViewHeight,
            demo.Render,
            animated: true,
            dispose: demo.Dispose)));
    }

    private sealed class PhysicsGpuDemo : IDisposable
    {
        private readonly GpuDevice _device;
        private readonly Luxel.UI.Signal<float>? _gravity;
        private readonly Luxel.UI.Signal<float>? _bounciness;
        private readonly Luxel.UI.Signal<bool>? _reset;
        private readonly GpuBuffer _vertices;
        private readonly GpuTexture _depth;
        private readonly GpuPipeline _pipeline;
        private World _world = null!;
        private PhysicsWorld _physics = null!;
        private PhysicsStepSystem _step = null!;
        private Render3DExtractSystem _extractor = null!;
        private float _lastTime = float.NaN;
        private bool _lastReset;
        private bool _disposed;

        internal PhysicsGpuDemo(GpuDevice device, Luxel.UI.Signal<float>? gravity,
            Luxel.UI.Signal<float>? bounciness, Luxel.UI.Signal<bool>? reset)
        {
            _device = device;
            _gravity = gravity;
            _bounciness = bounciness;
            _reset = reset;
            _vertices = CreateCubeVertexBuffer(device);
            _depth = device.CreateDepthTarget(ViewWidth, ViewHeight);
            _pipeline = device.CreateGraphicsPipeline(CubeShader(), DepthOn(GpuFormat.Rgba8Unorm));
            BuildSimulation();
        }

        private void BuildSimulation()
        {
            _extractor?.Dispose();
            _physics?.Dispose();
            _world?.Dispose();

            _world = new World();
            _physics = new PhysicsWorld(new PhysicsSettings { ThreadCount = 0 });
            _step = new PhysicsStepSystem(_world, _physics);

            _world.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(8f, 0.2f, 8f)
                    * Matrix4x4.CreateTranslation(0, -1.6f, 0)),
                new Color3D(new Vector4(0.85f, 0.82f, 0.78f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(8f, 0.2f, 8f),
                new StaticBody());

            for (int layer = 0; layer < 4; layer++)
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++)
            {
                int index = layer * 9 + z * 3 + x;
                float jitter = (index % 5 - 2) * 0.04f;
                Vector3 position = new((x - 1) * 0.62f + jitter,
                    -0.9f + layer * 0.62f, (z - 1) * 0.62f - jitter);
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(index * 0.11f, 0, 0);
                _world.Store.CreateEntity(
                    new LocalTransform(Matrix4x4.CreateScale(0.55f)
                        * Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(position)),
                    new Color3D(Palette[index % Palette.Length]),
                    new MeshRef(MeshRef.Cube),
                    Collider.Box(0.55f, 0.55f, 0.55f),
                    RigidBody.Dynamic());
            }

            TransformPropagateSystem.Run(_world);
            _extractor = new Render3DExtractSystem(_world, _device);
            _extractor.Extract();
        }

        internal GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
        {
            if (_reset is not null && _reset.Value != _lastReset)
            {
                _lastReset = _reset.Value;
                BuildSimulation();
                _lastTime = time;
            }
            if (_gravity is not null)
                _physics.Gravity = new Vector3(0, -MathF.Max(0, _gravity.Value), 0);
            if (_bounciness is not null)
                _physics.Bounciness = MathF.Max(0, _bounciness.Value);

            float dt = float.IsNaN(_lastTime) || time < _lastTime ? 0 : time - _lastTime;
            _lastTime = time;
            _step.Run(dt);
            TransformPropagateSystem.Run(_world);
            _extractor.Extract();

            Matrix4x4 view = Matrix4x4.CreateLookAt(
                new Vector3(3.4f, 2.2f, -4.6f), new Vector3(0, -0.4f, 0), Vector3.UnitY);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * projection;

            using var graph = new Rg(device);
            BufferHandle vertices = graph.ImportBuffer(_vertices, "vertices");
            BufferHandle instances = graph.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle output = graph.ImportBuffer(surface.Framebuffer, "framebuffer");
            graph.AddPass("RenderPhysics", PassQueue.Graphics)
                .Read(vertices)
                .Read(instances)
                .Write(output, ResourceUsage.CopyDest)
                .Execute(pass =>
                {
                    var args = new DrawArgs
                    {
                        ViewProj = Matrix4x4.Transpose(viewProj),
                        VertexBufIndex = pass.BindlessIndex(vertices),
                        InstanceBufIndex = pass.BindlessIndex(instances),
                    };
                    pass.Cmd.BeginRendering(surface.ColorTarget, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                        .SetGraphicsPipeline(_pipeline)
                        .SetRootArguments(args)
                        .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                        .EndRendering();
                    surface.CopyColorToFramebuffer(pass.Cmd);
                });

            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            graph.Execute(command);
            command.Finish();
            device.MainQueue.Submit(command);
            return GpuViewRenderResult.Ready;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pipeline.Dispose();
            _depth.Dispose();
            _extractor.Dispose();
            _vertices.Dispose();
            _physics.Dispose();
            _world.Dispose();
        }
    }

    /// <summary>Collider shapes and dynamic/static/CCD categories rendered as deterministic gizmos.</summary>
    [Story("Examples/3D/PhysicsGizmos", Width = 520, Height = 360, Order = 129, CapabilityNote = BrowserNote)]
    public static Widget PhysicsGizmosDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(
        CanvasWidth, CanvasHeight, draw: scene =>
        {
            scene.FillRect(Color2D.Rgba(20, 24, 30), 0, 0, CanvasWidth, CanvasHeight);
            using var world = new World();
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
                Collider.Box(6, 1, 6), new StaticBody());
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(-1.4f, 0.6f, 0.8f)),
                Collider.Box(1.2f, 1.2f, 1.2f), RigidBody.Dynamic());
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateFromYawPitchRoll(0.6f, 0.3f, 0.2f)
                    * Matrix4x4.CreateTranslation(0.2f, 1.5f, -0.6f)),
                Collider.Box(1, 1, 1), RigidBody.Dynamic());
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(1.7f, 0.5f, 1.2f)),
                Collider.Sphere(0.5f), RigidBody.Dynamic(ccd: true));

            ResetDebugDraw(PhysicsGizmos.Colliders);
            PhysicsGizmos.DrawColliders(world,
                Color2D.Rgba(110, 220, 130), Color2D.Rgba(150, 156, 170),
                Color2D.Rgba(240, 96, 96), Color2D.Rgba(90, 200, 240), 1.6f);
            FlushDebugDraw(scene, IsoGizmos);
        })));

    /// <summary>A falling sphere crosses a trigger and records Begin/End contact events.</summary>
    [Story("Examples/3D/PhysicsTrigger", Width = 520, Height = 360, Order = 130, CapabilityNote = BrowserNote)]
    public static Widget PhysicsTriggerDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(
        CanvasWidth, CanvasHeight, draw: scene =>
        {
            scene.FillRect(Color2D.Rgba(20, 24, 30), 0, 0, CanvasWidth, CanvasHeight);
            using var physics = new PhysicsWorld(new PhysicsSettings { ThreadCount = 0 });
            using var world = new World();
            var system = new PhysicsStepSystem(world, physics) { TrackCurrentContacts = true };
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
                Collider.Box(6, 1, 6), new StaticBody());
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 2, 0)),
                Collider.Box(2, 2, 2), new Trigger());
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 3.8f, 0)),
                Collider.Sphere(0.4f), RigidBody.Dynamic());
            for (int i = 0; i < 80; i++) system.StepFixedOnce();

            ResetDebugDraw(PhysicsGizmos.Colliders, PhysicsGizmos.Contacts);
            PhysicsGizmos.DrawColliders(world,
                Color2D.Rgba(110, 220, 130), Color2D.Rgba(150, 156, 170),
                Color2D.Rgba(240, 96, 96), Color2D.Rgba(90, 200, 240), 1.6f);
            PhysicsGizmos.ContactMarkers(system.CurrentContacts,
                Color2D.Rgba(245, 210, 90), 0.22f, 2f);
            FlushDebugDraw(scene, IsoGizmos);
        })));

    /// <summary>Static triangle terrain, a primitive sphere, and a dynamic convex hull.</summary>
    [Story("Examples/3D/PhysicsMesh", Width = 520, Height = 360, Order = 131, CapabilityNote = BrowserNote)]
    public static Widget PhysicsMeshDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(
        CanvasWidth, CanvasHeight, draw: scene =>
        {
            scene.FillRect(Color2D.Rgba(20, 24, 30), 0, 0, CanvasWidth, CanvasHeight);
            const int count = 6;
            const float size = 6;
            var vertices = new Vector3[(count + 1) * (count + 1)];
            for (int z = 0; z <= count; z++)
            for (int x = 0; x <= count; x++)
            {
                float wx = x / (float)count * size - size / 2;
                float wz = z / (float)count * size - size / 2;
                vertices[z * (count + 1) + x] = new Vector3(wx, TerrainHeight(wx, wz), wz);
            }
            var indexList = new List<int>();
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
            {
                int a = z * (count + 1) + x;
                int b = a + 1;
                int c = a + count + 1;
                int d = c + 1;
                indexList.AddRange([a, b, c, b, d, c]);
            }

            using var physics = new PhysicsWorld(new PhysicsSettings { ThreadCount = 0 });
            using var world = new World();
            var system = new PhysicsStepSystem(world, physics);
            world.CreateEntity(new LocalTransform(Matrix4x4.Identity),
                MeshCollider.Static(vertices, indexList.ToArray()));
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(-1.2f, 4, 0.6f)),
                Collider.Sphere(0.45f), RigidBody.Dynamic());
            Vector3[] tetrahedron =
            [
                new(0.4f, 0.4f, 0.4f), new(0.4f, -0.4f, -0.4f),
                new(-0.4f, 0.4f, -0.4f), new(-0.4f, -0.4f, 0.4f),
            ];
            Entity hull = world.CreateEntity(
                new LocalTransform(Matrix4x4.CreateTranslation(1.3f, 4, -0.5f)),
                HullCollider.Dynamic(tetrahedron));
            for (int i = 0; i < 150; i++) system.StepFixedOnce();

            ResetDebugDraw(TerrainKind, PhysicsGizmos.Colliders);
            uint terrainColor = Color2D.Rgba(120, 128, 145);
            for (int z = 0; z <= count; z++)
            for (int x = 0; x <= count; x++)
            {
                Vector3 point = vertices[z * (count + 1) + x];
                if (x < count)
                    DebugDraw.Line(point, vertices[z * (count + 1) + x + 1], terrainColor, 1, TerrainKind);
                if (z < count)
                    DebugDraw.Line(point, vertices[(z + 1) * (count + 1) + x], terrainColor, 1, TerrainKind);
            }
            PhysicsGizmos.DrawColliders(world,
                Color2D.Rgba(110, 220, 130), Color2D.Rgba(150, 156, 170),
                Color2D.Rgba(240, 96, 96), Color2D.Rgba(90, 200, 240), 1.6f);
            Matrix4x4 hullTransform = hull.GetComponent<LocalTransform>().Matrix;
            uint hullColor = Color2D.Rgba(235, 200, 110);
            for (int i = 0; i < tetrahedron.Length; i++)
            for (int j = i + 1; j < tetrahedron.Length; j++)
                DebugDraw.Line(Vector3.Transform(tetrahedron[i], hullTransform),
                    Vector3.Transform(tetrahedron[j], hullTransform), hullColor, 1.6f, TerrainKind);
            FlushDebugDraw(scene, IsoMesh);
        })));

    private static void ResetDebugDraw(params string[] categories)
    {
        DebugDraw.Reset();
        foreach (string category in categories) DebugDraw.Enable(category);
    }

    private static void FlushDebugDraw(Scene2D scene, WorldToScreen projection)
        => DebugDraw.Flush(scene, projection, static (_, _, _, _, _, _) => { });

    private static Vector2 IsoGizmos(Vector3 world)
    {
        const float scale = 30;
        return new Vector2(
            230 + (world.X - world.Z) * scale * 0.87f,
            208 - world.Y * scale + (world.X + world.Z) * scale * 0.5f);
    }

    private static Vector2 IsoMesh(Vector3 world)
    {
        const float scale = 34;
        return new Vector2(
            230 + (world.X - world.Z) * scale * 0.87f,
            190 - world.Y * scale + (world.X + world.Z) * scale * 0.5f);
    }

    private static float TerrainHeight(float x, float z)
        => 0.5f * MathF.Sin(x * 0.9f) * MathF.Cos(z * 0.9f);

    private static GpuBuffer CreateCubeVertexBuffer(GpuDevice device)
    {
        ReadOnlySpan<CubeMesh.Vertex> vertices = CubeMesh.Vertices;
        GpuBuffer buffer = device.Malloc((ulong)(vertices.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        vertices.CopyTo(buffer.Span<CubeMesh.Vertex>(vertices.Length));
        return buffer;
    }

    private static GpuRasterDesc DepthOn(GpuFormat format)
    {
        GpuRasterDesc raster = GpuRasterDesc.Default(format);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        return raster;
    }

    private static GpuShaderCode CubeShader() => new()
    {
        SpirV = ShaderResource("cube_forward.spv"),
        DxilVertex = ShaderResource("cube_forward.vs.dxil"),
        DxilPixel = ShaderResource("cube_forward.ps.dxil"),
        Wgsl = ShaderResource("cube_forward.wgsl"),
    };

    private static byte[] ShaderResource(string fileName)
    {
        System.Reflection.Assembly assembly = typeof(PhysicsBrowserStories).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Shaders." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded physics shader is missing: {fileName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
