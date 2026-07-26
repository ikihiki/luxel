using System.Numerics;
using Luxel;
using DrawArgs3D = TutorialAbi.DrawArgs3D;
using Vertex3D = TutorialAbi.Vertex3D;

internal sealed class TriangleRenderer : IDisposable
{
    private readonly GpuDevice _device;
    private readonly TutorialStage _stage;
    private readonly GpuBuffer _vertices;
    private readonly GpuBuffer _indices;
    private readonly GpuTexture _texture;
    private readonly GpuSampler _sampler;
    private readonly GpuPipeline _pipeline;
    private readonly uint _indexCount;
    private GpuTexture? _target;
    private GpuTexture? _depth;
    private GpuBuffer? _framebuffer;
    private uint _width;
    private uint _height;
    private uint _frame;

    public TriangleRenderer(GpuDevice device, TutorialStage stage)
    {
        _device = device;
        _stage = stage;

        if (stage == TutorialStage.Triangle)
        {
            TutorialAbi.Vertex[] vertices = CreateTriangleVertices();
            _indexCount = 3;
            _vertices = device.Malloc(checked((ulong)vertices.Length * TutorialAbi.VertexSize), GpuMemoryKind.HostMapped);
            vertices.CopyTo(_vertices.Span<TutorialAbi.Vertex>(vertices.Length));
            _indices = device.Malloc(sizeof(uint), GpuMemoryKind.HostMapped); // Unused by the first-triangle stage.
        }
        else
        {
            Vertex3D[] vertices = stage == TutorialStage.Texture ? CreateQuadVertices() : CreateCubeVertices();
            uint[] indices = stage == TutorialStage.Texture ? CreateQuadIndices() : CreateCubeIndices();
            _indexCount = (uint)indices.Length;
            _vertices = device.Malloc(checked((ulong)vertices.Length * TutorialAbi.Vertex3DSize), GpuMemoryKind.HostMapped);
            vertices.CopyTo(_vertices.Span<Vertex3D>(vertices.Length));
            _indices = device.Malloc(checked((ulong)indices.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
            indices.CopyTo(_indices.Span<uint>(indices.Length));
        }

        _texture = device.CreateTexture(4, 4, CreateCheckerboard());
        _sampler = device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);

        GpuRasterDesc raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = UsesDepth(stage);
        raster.DepthWrite = UsesDepth(stage);
        raster.CullMode = stage == TutorialStage.Lighting ? GpuCullMode.Back : GpuCullMode.None;
        raster.FrontFace = GpuFrontFace.CounterClockwise;
        string shader = stage == TutorialStage.Triangle ? "tutorial_triangle" : "tutorial_3d";
        _pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load(shader), raster);
    }

    public GpuBuffer Framebuffer
        => _framebuffer ?? throw new InvalidOperationException("Renderer has no framebuffer. Call Resize with a positive size first.");

    public uint StridePixels { get; private set; }
    public float AspectRatio => _height == 0 ? 0 : TutorialAbi.VisibleAspect((int)_width, (int)_height);

    public void Resize(int width, int height)
    {
        _framebuffer?.Dispose();
        _framebuffer = null;
        _depth?.Dispose();
        _depth = null;
        _target?.Dispose();
        _target = null;
        StridePixels = 0;
        _width = 0;
        _height = 0;

        if (width <= 0 || height <= 0) return;
        _width = (uint)width;
        _height = (uint)height;
        StridePixels = (uint)Align(width, 64); // D3D12 readback row pitch is 256 bytes for RGBA8.
        _target = _device.CreateRenderTarget(_width, _height, GpuFormat.Rgba8Unorm);
        if (UsesDepth(_stage))
            _depth = _device.CreateDepthTarget(_width, _height);
        _framebuffer = _device.Malloc(checked((ulong)StridePixels * _height * 4), GpuMemoryKind.HostMapped);
    }

    public void Render()
    {
        if (_target is null || _framebuffer is null) return;

        if (_stage == TutorialStage.Triangle)
        {
            var triangleArgs = new TutorialAbi.DrawArgs { VertexBufferIndex = _vertices.BindlessIndex };
            using GpuCommandBuffer triangleCommand = _device.MainQueue.StartCommandRecording();
            triangleCommand.BeginRendering(_target, null, 0.055f, 0.07f, 0.11f, 1)
                .SetGraphicsPipeline(_pipeline)
                .SetRootArguments(triangleArgs)
                .Draw(3)
                .EndRendering()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToBuffer(_target, _framebuffer, StridePixels);
            triangleCommand.Finish();
            _device.MainQueue.SubmitAndWait(triangleCommand);
            _frame++;
            return;
        }

        Matrix4x4 model = _stage == TutorialStage.Texture
            ? Matrix4x4.Identity
            : Matrix4x4.CreateRotationY(_frame * 0.012f) * Matrix4x4.CreateRotationX(-0.28f);
        Matrix4x4 viewProjection = Matrix4x4.Identity;
        if (UsesDepth(_stage))
        {
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.4f, 1.7f, -4.2f), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, AspectRatio, 0.1f, 100f);
            viewProjection = view * projection;
        }

        var args = new DrawArgs3D
        {
            VertexBufferIndex = _vertices.BindlessIndex,
            IndexBufferIndex = _indices.BindlessIndex,
            TextureIndex = _texture.BindlessIndex,
            SamplerIndex = _sampler.BindlessIndex,
            Model = Matrix4x4.Transpose(model),
            ViewProjection = Matrix4x4.Transpose(viewProjection),
            LightDirection = new Vector4(Vector3.Normalize(new Vector3(-0.45f, 0.8f, -0.35f)), 0),
            Stage = (uint)_stage - 1,
        };

        using GpuCommandBuffer command = _device.MainQueue.StartCommandRecording();
        command.BeginRendering(_target, _depth, 0.025f, 0.035f, 0.06f, 1)
            .SetGraphicsPipeline(_pipeline)
            .SetRootArguments(args)
            .Draw(_indexCount)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(_target, _framebuffer, StridePixels);
        command.Finish();
        _device.MainQueue.SubmitAndWait(command);
        _frame++;
    }

    public void Dispose()
    {
        _device.MainQueue.WaitIdle();
        _framebuffer?.Dispose();
        _depth?.Dispose();
        _target?.Dispose();
        _pipeline.Dispose();
        _sampler.Dispose();
        _texture.Dispose();
        _indices.Dispose();
        _vertices.Dispose();
    }

    private static bool UsesDepth(TutorialStage stage)
        => stage is TutorialStage.Transform or TutorialStage.Lighting;

    private static TutorialAbi.Vertex[] CreateTriangleVertices() =>
    [
        new() { Px = 0, Py = -0.72f, Pz = 0, Pw = 1, R = 1, G = 0.18f, B = 0.18f, A = 1 },
        new() { Px = 0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.18f, G = 1, B = 0.28f, A = 1 },
        new() { Px = -0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.2f, G = 0.42f, B = 1, A = 1 },
    ];

    private static Vertex3D V(float x, float y, float z, Vector3 normal, float u, float v)
        => new() { Position = new Vector3(x, y, z), Normal = normal, Uv = new Vector2(u, v) };

    private static Vertex3D[] CreateQuadVertices() =>
    [
        V(-0.75f, -0.75f, 0, -Vector3.UnitZ, 0, 1),
        V( 0.75f, -0.75f, 0, -Vector3.UnitZ, 1, 1),
        V( 0.75f,  0.75f, 0, -Vector3.UnitZ, 1, 0),
        V(-0.75f,  0.75f, 0, -Vector3.UnitZ, 0, 0),
    ];

    private static uint[] CreateQuadIndices() => [0, 1, 2, 0, 2, 3];

    private static Vertex3D[] CreateCubeVertices() =>
    [
        V(-1,-1, 1,  Vector3.UnitZ, 0,1), V( 1,-1, 1,  Vector3.UnitZ, 1,1), V( 1, 1, 1,  Vector3.UnitZ, 1,0), V(-1, 1, 1,  Vector3.UnitZ, 0,0),
        V( 1,-1,-1, -Vector3.UnitZ, 0,1), V(-1,-1,-1, -Vector3.UnitZ, 1,1), V(-1, 1,-1, -Vector3.UnitZ, 1,0), V( 1, 1,-1, -Vector3.UnitZ, 0,0),
        V(-1,-1,-1, -Vector3.UnitX, 0,1), V(-1,-1, 1, -Vector3.UnitX, 1,1), V(-1, 1, 1, -Vector3.UnitX, 1,0), V(-1, 1,-1, -Vector3.UnitX, 0,0),
        V( 1,-1, 1,  Vector3.UnitX, 0,1), V( 1,-1,-1,  Vector3.UnitX, 1,1), V( 1, 1,-1,  Vector3.UnitX, 1,0), V( 1, 1, 1,  Vector3.UnitX, 0,0),
        V(-1, 1, 1,  Vector3.UnitY, 0,1), V( 1, 1, 1,  Vector3.UnitY, 1,1), V( 1, 1,-1,  Vector3.UnitY, 1,0), V(-1, 1,-1,  Vector3.UnitY, 0,0),
        V(-1,-1,-1, -Vector3.UnitY, 0,1), V( 1,-1,-1, -Vector3.UnitY, 1,1), V( 1,-1, 1, -Vector3.UnitY, 1,0), V(-1,-1, 1, -Vector3.UnitY, 0,0),
    ];

    private static uint[] CreateCubeIndices()
    {
        var indices = new uint[36];
        for (uint face = 0; face < 6; face++)
        {
            uint vertex = face * 4;
            int i = (int)face * 6;
            indices[i + 0] = vertex; indices[i + 1] = vertex + 1; indices[i + 2] = vertex + 2;
            indices[i + 3] = vertex; indices[i + 4] = vertex + 2; indices[i + 5] = vertex + 3;
        }
        return indices;
    }

    private static byte[] CreateCheckerboard()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            bool bright = ((x ^ y) & 1) == 0;
            int i = (y * 4 + x) * 4;
            pixels[i + 0] = bright ? (byte)240 : (byte)35;
            pixels[i + 1] = bright ? (byte)175 : (byte)95;
            pixels[i + 2] = bright ? (byte)55 : (byte)220;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static int Align(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);
}
