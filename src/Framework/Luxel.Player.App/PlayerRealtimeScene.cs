using Luxel;
using Luxel.Framework.Game;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.RenderSystem;
using Luxel.Graphics.TwoD;
using Luxel.Typography;

namespace Luxel.Player;

/// <summary>
/// PlayerGame を実時間で駆動する scene。固定更新は world を進め、描画は標準 render feature set に委譲する。
/// </summary>
public sealed class PlayerRealtimeScene : IGameScene
{
    private readonly PlayerGame _game;
    private readonly Func<ISet<string>> _keys;
    private readonly PlayerRenderFeature _renderFeature;

    public PlayerRealtimeScene(GpuDevice device, PlayerGame game, VectorFont font, Func<ISet<string>> keys)
    {
        _game = game;
        _keys = keys;
        _renderFeature = new PlayerRenderFeature(device, game, font);
    }

    public GpuBuffer? Framebuffer => _renderFeature.Framebuffer;

    public uint StridePixels => _renderFeature.StridePixels;

    public ValueTask LoadAsync(GameSceneContext context, CancellationToken token) => ValueTask.CompletedTask;

    public void ConfigureRendering(
        RenderFeatureSetCatalog featureSets,
        RenderFeatureAssignmentBuilder assignments)
        => assignments.Register(RenderFeatureSets.RenderOutput, _renderFeature);

    public void FixedUpdate(in FixedUpdateContext context)
    {
        IPlayerWorld world = _game.World;
        world.KeysDown.Clear();
        foreach (string key in _keys()) world.KeysDown.Add(key);
        world.Update(context.FixedDeltaSeconds);
        _game.ApplySceneRequest();
    }

    public void Update(in UpdateContext context) { }

    public ValueTask UnloadAsync(GameSceneContext context, CancellationToken token)
    {
        _renderFeature.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class PlayerRenderFeature : IRenderFeature, IDisposable
    {
        private readonly PlayerGame _game;
        private readonly VectorFont _font;
        private readonly GpuDeviceRasterizer2D _rasterizer;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public PlayerRenderFeature(GpuDevice device, PlayerGame game, VectorFont font)
        {
            _game = game;
            _font = font;
            _width = game.Project.WindowWidth;
            _height = game.Project.WindowHeight;
            StridePixels = (uint)Align(_width, 64);
            _rasterizer = new GpuDeviceRasterizer2D(device);
            Framebuffer = device.Malloc((ulong)(StridePixels * (uint)_height * 4), GpuMemoryKind.HostMapped);
        }

        public GpuBuffer Framebuffer { get; }

        public uint StridePixels { get; }

        public void AddPasses(RenderFeatureContext context)
        {
            BufferHandle framebuffer = context.Graph.ImportBuffer(Framebuffer, "player-framebuffer");
            context.Graph.AddPass("Player2D", PassQueue.Compute)
                .Write(framebuffer, ResourceUsage.StorageBufferWrite)
                .Execute(pass =>
                {
                    var scene = new Scene2D();
                    _game.World.Render(scene, _width, _height, _font);
                    using GpuEncodedScene2D encoded = _rasterizer.Encode(scene);
                    _rasterizer.Render(
                        pass.Cmd,
                        encoded,
                        Camera2D.Pixels,
                        StridePixels,
                        (uint)_height,
                        Framebuffer);
                });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Framebuffer.Dispose();
            _rasterizer.Dispose();
        }

        private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
    }
}

public sealed class PlayerGameSceneBootstrap(PlayerRealtimeScene scene) : IGameSceneBootstrap
{
    public ValueTask BootstrapAsync(IGameSceneSystem scenes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        scenes.Enqueue(new GameSceneCommand.Push(GameSceneId.New(), scene));
        return ValueTask.CompletedTask;
    }
}
