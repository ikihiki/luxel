using Luxel;
using Luxel.Framework;
using Luxel.Player;
using Luxel.Graphics.TwoD;
using Luxel.Typography;

namespace Luxel.Player;

/// <summary>
/// PlayerGame を実時間で駆動する GameScene (CavernRealtimeScene と同型の薄い層) —
/// OnFixedUpdate = キー供給 + world.Update (固定 dt)、OnRender = world.Render を
/// GpuDeviceRasterizer2D で Framebuffer へ焼く (Program が Present)。
/// </summary>
public sealed class PlayerRealtimeScene : GameScene
{
    private readonly PlayerGame _game;
    private readonly VectorFont _font;
    private readonly Func<ISet<string>> _keys;

    private GpuDeviceRasterizer2D? _raster;
    private GpuBuffer? _fb;
    private int _paddedW;

    public PlayerRealtimeScene(SceneLoopServices loop, PlayerGame game, VectorFont font, Func<ISet<string>> keys) : base(loop)
    {
        _game = game;
        _font = font;
        _keys = keys;
    }

    public GpuBuffer? Framebuffer => _fb;

    public uint StridePixels => (uint)_paddedW;

    private static int Align(int v, int a) => (v + a - 1) / a * a;

    protected override void OnFixedUpdate(FixedUpdateContext ctx)
    {
        IPlayerWorld world = _game.World;
        world.KeysDown.Clear();
        foreach (string k in _keys()) world.KeysDown.Add(k);
        world.Update(ctx.FixedDeltaSeconds);
        _game.ApplySceneRequest();
    }

    protected override void OnRender(RenderContext ctx)
    {
        int w = _game.Project.WindowWidth, h = _game.Project.WindowHeight;
        if (_raster is null)
        {
            _paddedW = Align(w, 64);
            _raster = new GpuDeviceRasterizer2D(Device);
            _fb = Device.Malloc((ulong)(_paddedW * h * 4), GpuMemoryKind.HostMapped);
        }
        var s = new Scene2D();
        _game.World.Render(s, w, h, _font);

        using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
        using GpuEncodedScene2D encoded = _raster.Encode(s);
        _raster.Render(cmd, encoded, Camera2D.Pixels, (uint)_paddedW, (uint)h, _fb!);
        cmd.Finish();
        Device.MainQueue.SubmitAndWait(cmd);
    }
}
