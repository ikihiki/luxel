using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Ecs;
using Luxel.Graphics;
using Luxel.Graphics.RenderGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;
using Rg = Luxel.Graphics.RenderGraph.RenderGraph;

namespace Luxel.Gallery.Stories;

/// <summary>Browser-safe ECS + 3D extraction example shared by native Gallery and browser WebAssembly.</summary>
public static class EcsCubesStories
{
    private const uint Width = 256;
    private const uint Height = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0;
        public uint Pad1;
    }

    /// <summary>5×5 cube grid extracted from ECS and drawn through a browser-safe RenderGraph pass.</summary>
    public static StoryResult EcsCubes(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device)
            return ctx.Snap(Frame(GpuView(Width, Height,
                static (_, _, _) => GpuViewRenderResult.Failed,
                animated: false)));

        var demo = new EcsCubesDemo(device);
        return ctx.Snap(Frame(GpuView(
            Width,
            Height,
            demo.Render,
            animated: true,
            dispose: demo.Dispose)));
    }

    private sealed class EcsCubesDemo : IDisposable
    {
        private readonly World _world;
        private readonly GpuBuffer _vertices;
        private readonly Render3DExtractSystem _extractor;
        private readonly GpuTexture _depth;
        private readonly GpuPipeline _pipeline;

        internal EcsCubesDemo(GpuDevice device)
        {
            _world = CreateCubeGrid(5);
            _vertices = CreateCubeVertexBuffer(device);
            _extractor = new Render3DExtractSystem(_world, device);
            _extractor.Extract();
            _depth = device.CreateDepthTarget(Width, Height);
            _pipeline = device.CreateGraphicsPipeline(CubeShader(), new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.D32Float)));
        }

        internal GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
        {
            Matrix4x4 viewProj = OrbitViewProj(time * 0.4f);
            using var graph = new Rg(device);
            BufferHandle vertices = graph.ImportBuffer(_vertices, "vertices");
            BufferHandle instances = graph.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle output = graph.ImportBuffer(surface.Framebuffer, "framebuffer");

            graph.AddPass("RenderEcsCubes", PassQueue.Graphics)
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
                        .SetRasterizerState(GpuRasterizerState.Default)
                        .SetDepthStencilState(GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true })
                        .SetBlendState(GpuBlendState.None)
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
            _pipeline.Dispose();
            _depth.Dispose();
            _extractor.Dispose();
            _vertices.Dispose();
            _world.Dispose();
        }
    }

    private static World CreateCubeGrid(int count)
    {
        var world = new World();
        Vector4[] palette =
        [
            new(1.00f, 0.40f, 0.40f, 1f),
            new(0.40f, 0.95f, 0.55f, 1f),
            new(0.35f, 0.70f, 1.00f, 1f),
            new(1.00f, 0.90f, 0.35f, 1f),
            new(0.90f, 0.55f, 1.00f, 1f),
        ];
        for (int z = 0; z < count; z++)
        for (int x = 0; x < count; x++)
        {
            int half = count / 2;
            Vector3 position = new((x - half) * 0.9f, 0, (z - half) * 0.9f);
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll((x + z) * 0.35f, 0.25f, 0);
            world.CreateEntity(
                new LocalTransform(
                    Matrix4x4.CreateScale(new Vector3(0.4f))
                    * Matrix4x4.CreateFromQuaternion(rotation)
                    * Matrix4x4.CreateTranslation(position)),
                new Color3D(palette[(x + z * count) % palette.Length]),
                new MeshRef(MeshRef.Cube));
        }
        TransformPropagateSystem.Run(world);
        return world;
    }

    private static GpuBuffer CreateCubeVertexBuffer(GpuDevice device)
    {
        ReadOnlySpan<CubeMesh.Vertex> vertices = CubeMesh.Vertices;
        GpuBuffer buffer = device.Malloc(
            (ulong)(vertices.Length * CubeMesh.VertexStride),
            GpuMemoryKind.HostMapped);
        vertices.CopyTo(buffer.Span<CubeMesh.Vertex>(vertices.Length));
        return buffer;
    }

    private static Matrix4x4 OrbitViewProj(float angle)
    {
        Vector3 position = new(MathF.Sin(angle) * 5.8f, 2.5f, -MathF.Cos(angle) * 5.8f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        return view * projection;
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
        System.Reflection.Assembly assembly = typeof(EcsCubesStories).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Shaders." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded ECS cube shader is missing: {fileName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
