using System.Numerics;
using Luxel;
using Luxel.Framework;
using Luxel.Input;
using Luxel.Particles;
using Luxel.TwoD;
using Luxel.Typography;
using LuxelCavern.Core;

namespace LuxelCavern;

/// <summary>
/// 「Luxel Cavern」の実時間ゲームシーン — <see cref="LuxelHostBuilder"/> の <see cref="GameLoop"/> が駆動する。
/// <see cref="OnFixedUpdate"/> で入力を読み <see cref="GameFlow"/>/<see cref="CavernSim"/> を固定 dt で進め、
/// <see cref="OnRender"/> で毎フレーム即時モードのシーンを組んで <see cref="Framebuffer"/> へ描く
/// (提示は Program 側が <see cref="GpuSurface.Present"/> で行う)。カメラは <see cref="CameraRig2D"/> で追従 + 被弾シェイク。
/// </summary>
public sealed class CavernRealtimeScene : GameScene
{
    public const int Width = 960, Height = 540;
    private static int Align(int v, int a) => (v + a - 1) / a * a;
    private const int AtlasW = 32, AtlasH = 32;

    private readonly SceneLoopServices _loop;
    private readonly VectorFont _font;

    private Rasterizer2D _raster = null!;
    private GpuBuffer _fb = null!;
    private GpuBuffer _atlas = null!;
    private int _paddedW;
    private bool _init;

    private readonly GameFlow _flow = new();
    private readonly CameraRig2D _rig = new();
    private ParticleSystem _fx = null!;

    private Axis1DAction _move = null!;
    private ButtonAction _jump = null!, _pause = null!, _confirm = null!;
    private bool _prevJump, _prevPause, _prevConfirm;

    private static readonly uint TorchTint = Color2D.Rgba(255, 160, 60), DustTint = Color2D.Rgba(200, 180, 150);
    private static readonly uint CoinTint = Color2D.Rgba(250, 225, 70), DefeatTint = Color2D.Rgba(230, 90, 90);

    /// <summary>提示対象のフレームバッファ (Program が Present する)。</summary>
    public GpuBuffer Framebuffer => _fb;
    public uint StridePixels => (uint)_paddedW;

    public CavernRealtimeScene(SceneLoopServices loop, VectorFont font) : base(loop)
    {
        _loop = loop;
        _font = font;
    }

    private void Init()
    {
        _paddedW = Align(Width, 64);
        _raster = new Rasterizer2D(Device);
        _fb = Device.Malloc((ulong)(_paddedW * Height * 4), GpuMemoryKind.HostMapped);
        BakeAtlas();

        _move = new Axis1DAction("move");
        _move.ButtonPairs.Add((KeyCode.D, KeyCode.A));
        _move.ButtonPairs.Add((KeyCode.Right, KeyCode.Left));
        _jump = new ButtonAction("jump", KeyCode.Space, KeyCode.W, KeyCode.Up);
        _pause = new ButtonAction("pause", KeyCode.Escape);
        _confirm = new ButtonAction("confirm", KeyCode.Enter);
        var ctx = new InputContext("gameplay");
        ctx.Add(_move); ctx.Add(_jump); ctx.Add(_pause); ctx.Add(_confirm);
        _loop.InputStack?.Push(ctx);

        _fx = new ParticleSystem(new ParticleConfig(
            Life: ParticleValue.Range(0.4f, 0.8f), Speed: ParticleValue.Range(20, 60),
            SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: -40, Drag: 0.6f,
            Size: 3f, Color: new ParticleColor(Color2D.Rgba(255, 255, 255, 255), Color2D.Rgba(255, 255, 255, 0)),
            Shape: ParticleShape.Circle), capacity: 600, seed: 0xCA5E);

        StartLevel();
    }

    private void BakeAtlas()
    {
        _atlas = Device.Malloc(AtlasW * AtlasH * 4, GpuMemoryKind.HostMapped);
        Span<byte> px = _atlas.Span<byte>(AtlasW * AtlasH * 4);
        (int Ox, int Oy, byte R, byte G, byte B)[] cells =
            [(0, 0, 70, 175, 85), (16, 0, 140, 92, 52), (0, 16, 120, 122, 135), (16, 16, 210, 70, 70)];
        foreach (var (ox, oy, r, g, b) in cells)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int i = ((oy + y) * AtlasW + ox + x) * 4;
                    bool edge = x == 0 || y == 0 || x == 15 || y == 15;
                    px[i] = (byte)(edge ? r * 3 / 4 : r);
                    px[i + 1] = (byte)(edge ? g * 3 / 4 : g);
                    px[i + 2] = (byte)(edge ? b * 3 / 4 : b);
                    px[i + 3] = 255;
                }
    }

    private void StartLevel()
    {
        _flow.StartNew();
        CavernSim sim = _flow.Sim!;
        sim.Map.TileSet.Atlas.Bind(_atlas.BindlessIndex, AtlasW, AtlasH);
        _rig.Zoom = 3f;
        _rig.Deadzone = new Vector2(120, 80);
        _rig.WorldBounds = new RectF(0, 0, CavernLevel.Width * CavernLevel.Tile, CavernLevel.Height * CavernLevel.Tile);
        _rig.Smoothing = 0.12f;
        _rig.Target = sim.PlayerCenter;
        _rig.SnapToTarget();
        _fx.Clear();
    }

    protected override void OnUpdate(UpdateContext ctx)
    {
        if (!_init) { Init(); _init = true; }

        bool ph = _pause.Value.Value;
        if (ph && !_prevPause) _flow.TogglePause();
        _prevPause = ph;

        bool ch = _confirm.Value.Value;
        if (ch && !_prevConfirm && _flow.State is GameState.GameOver or GameState.Clear) StartLevel();
        _prevConfirm = ch;
    }

    protected override void OnFixedUpdate(FixedUpdateContext ctx)
    {
        if (!_init) return;
        float dt = ctx.FixedDeltaSeconds;

        float move = _move.Value.Value;
        bool jh = _jump.Value.Value;
        bool jp = jh && !_prevJump;
        _prevJump = jh;

        _flow.Step(dt, move, jp);
        CavernSim? sim = _flow.Sim;
        if (sim is not null && _flow.State == GameState.Playing)
        {
            _rig.Target = sim.PlayerCenter;
            _rig.Update(dt, Width, Height);
            if (sim.ShakeRequested) _rig.Shake(7f, 0.3f, 0xBEEF);
            if (sim.LandedThisStep)
                _fx.Emit(new Vector3(sim.PlayerPos.X + sim.PlayerSize.X * 0.5f, sim.PlayerPos.Y + sim.PlayerSize.Y, 0), 8, DustTint);
            foreach (Vector2 c in sim.PickupsThisStep) _fx.Emit(new Vector3(c, 0), 10, CoinTint);
            foreach (Vector2 d in sim.DefeatsThisStep) _fx.Emit(new Vector3(d, 0), 14, DefeatTint);
        }
        foreach (Vector2 t in CavernLevel.Torches) _fx.Emit(new Vector3(t, 0), 1, TorchTint);
        _fx.Update(dt);
    }

    protected override void OnRender(RenderContext ctx)
    {
        if (!_init || _flow.Sim is not { } sim) return;
        Camera2D cam = _rig.Camera(Width, Height);
        var s = new Scene2D();
        DrawWorld(s, sim, cam);
        CavernHud.Draw(s, (sc, t, x, y, h, c) => _font.AppendText(sc, t, x, y, h, c), sim, CameraCenter(cam), _rig.EffectiveZoom, Width, Height);
        DrawOverlay(s, cam);

        using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
        using EncodedScene encoded = _raster.Encode(s);
        _raster.Render(cmd, encoded, cam, (uint)_paddedW, (uint)Height, _fb);
        cmd.Finish();
        Device.MainQueue.SubmitAndWait(cmd);
    }

    private void DrawWorld(Scene2D s, CavernSim sim, Camera2D cam)
    {
        Vector2 c = CameraCenter(cam);
        float zoom = _rig.EffectiveZoom;
        float vw = Width / zoom, vh = Height / zoom;
        s.FillRect(Color2D.Rgba(120, 170, 220), c.X - vw * 0.5f, c.Y - vh * 0.5f, vw, vh);   // 空

        // タイル (可視チャンク)
        var view = new RectF(c.X - vw * 0.5f, c.Y - vh * 0.5f, vw, vh);
        (int x0, int y0, int x1, int y1) = sim.Map.VisibleChunks(view);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                sim.Map.AppendChunk(cx, cy, s);

        // 扉 / チェックポイント / 収集 / 敵 / プレイヤー
        s.FillRoundedRect(sim.DoorOpen ? Color2D.Rgba(90, 200, 120) : Color2D.Rgba(120, 80, 50),
            sim.DoorPos.X, sim.DoorPos.Y, sim.DoorSize.X, sim.DoorSize.Y, 3);
        foreach (Checkpoint cp in sim.Checkpoints)
        {
            float px = cp.Pos.X + cp.Size.X * 0.5f;
            s.FillRoundedRect(Color2D.Rgba(180, 180, 190), px - 1, cp.Pos.Y, 2, cp.Size.Y, 0);
            s.FillRoundedRect(cp.Reached ? Color2D.Rgba(90, 200, 120) : Color2D.Rgba(150, 150, 160), px + 1, cp.Pos.Y, 11, 7, 1);
        }
        foreach (Pickup p in sim.Pickups)
            if (!p.Collected)
                s.FillCircle(p.IsKey ? Color2D.Rgba(240, 210, 90) : Color2D.Rgba(250, 225, 70),
                    p.Pos.X + p.Size * 0.5f, p.Pos.Y + p.Size * 0.5f, p.Size * 0.5f, 12);
        foreach (Walker w in sim.Enemies)
            if (w.Alive) s.FillRoundedRect(Color2D.Rgba(220, 80, 90), w.Pos.X, w.Pos.Y, w.Size.X, w.Size.Y, 2);
        foreach (Flyer fl in sim.Flyers)
            if (fl.Alive) { Vector2 fp = fl.Pos; s.FillRoundedRect(Color2D.Rgba(200, 110, 220), fp.X, fp.Y, fl.Size.X, fl.Size.Y, 6); }

        // プレイヤー (無敵中は点滅)
        bool blink = sim.Invincible && ((int)(sim.InvincibleRemain * 12) & 1) == 0;
        if (!blink)
            s.FillRoundedRect(Color2D.Rgba(90, 170, 245), sim.PlayerPos.X, sim.PlayerPos.Y, sim.PlayerSize.X, sim.PlayerSize.Y, 3);

        // パーティクル
        ParticleBuffer b = _fx.Buffer;
        ParticleConfig cfg = _fx.Config;
        for (int i = 0; i < b.Count; i++)
        {
            float t01 = b.Age[i] / MathF.Max(1e-6f, b.LifeMax[i]);
            uint col = ParticleColor.Multiply(cfg.Color.Eval(t01), b.Tint[i]);
            s.FillCircle(col, b.PosX[i], b.PosY[i], b.Size[i] * 0.5f, 8);
        }
    }

    private void DrawOverlay(Scene2D s, Camera2D cam)
    {
        string? title = _flow.State switch
        {
            GameState.Paused => "ポーズ",
            GameState.GameOver => "ゲームオーバー",
            GameState.Clear => "クリア！",
            _ => null,
        };
        if (title is null) return;
        string hint = _flow.State == GameState.Paused ? "Esc で再開" : "Enter でリトライ";
        CavernHud.DrawPauseOverlay(s, (sc, t, x, y, h, c) => _font.AppendText(sc, t, x, y, h, c),
            CameraCenter(cam), _rig.EffectiveZoom, Width, Height, title, hint);
    }

    // Camera2D (screen = A*wx + C*wy + E, ...) の中心 world 座標を逆算 (A=D=zoom, C=B=0)
    private static Vector2 CameraCenter(Camera2D cam)
        => new((Width * 0.5f - cam.E) / cam.A, (Height * 0.5f - cam.F) / cam.D);

    public override Task OnUnloadAsync()
    {
        _fb?.Dispose();
        _atlas?.Dispose();
        _raster?.Dispose();
        return Task.CompletedTask;
    }
}
