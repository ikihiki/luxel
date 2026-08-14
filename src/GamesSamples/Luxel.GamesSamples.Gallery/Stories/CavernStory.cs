using System.Numerics;
using LuxelCavern.Core;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Scripting;
using Luxel.Graphics.TwoD;
using Luxel.Framework.Game;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.RenderSystem;
using Microsoft.Extensions.DependencyInjection;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>
/// **capstone ① 「Luxel Cavern」** (タスク 19 ステージ B) — タイルマップ + プレイヤー物理 + カメラ追従 +
/// 収集/扉/トゲ/巡回敵/飛行敵 (<see cref="CavernSim"/>) + **パーティクル演出** (松明の炎 + 着地砂埃 / コイン /
/// 撃破バースト、sim のイベントから発火・per-particle tint)。sim + fx を固定 dt で決定的に事前実行し追従カメラで描く。
/// HUD (日本語)・実時間 exe・Audio・セーブは後段。手続きアトラス (外部アセット不要)。
/// </summary>
public static class CavernStories
{
    private const int Steps = 78;

    // 敵 AI を .csx で書くドッグフーディング (ScriptSystem、タスク 01/19)。既定巡回と同じ挙動を .csx で表現し、
    // 実ゲームビルドで Roslyn コンパイル経路が通ることを golden で担保する (挙動は既定と同一なので diff 0)。
    private static readonly ScriptProfile AiProfile = new("cavern.ai",
        [
            typeof(object).Assembly, typeof(Enumerable).Assembly,
            typeof(Vector2).Assembly, typeof(RectF).Assembly, typeof(GpuDevice).Assembly, typeof(Walker).Assembly,
        ],
        ["System", "LuxelCavern.Core"],
        typeof(object));

    private const string PatrolAiCsx =
        "(Action<Walker, CavernSim, float>)((w, s, dt) => { w.Pos.X += w.VelX * dt; " +
        "if (w.Pos.X <= w.MinX) { w.Pos.X = w.MinX; w.VelX = MathF.Abs(w.VelX); } " +
        "else if (w.Pos.X + w.Size.X >= w.MaxX) { w.Pos.X = w.MaxX - w.Size.X; w.VelX = -MathF.Abs(w.VelX); } })";

    [Story("Game/Cavern", Height = 300, Order = 145)]
    public static Widget Cavern(StoryContext ctx, ScriptHostRegistry scripts)
    {
        // 敵 AI を .csx からコンパイル (ScriptSystem のドッグフーディング — 実ゲームの敵ロジックを csx で書く)
        ScriptResult r = scripts.GetOrAdd(AiProfile).Run(PatrolAiCsx, new object());
        var ai = r.ReturnValue as Action<Walker, CavernSim, float>;
        return ctx.Snap(Frame(new StoryAppView<CavernScene>(CavernScene.W, CavernScene.H,
            (services, _) => services.AddSingleton(new CavernAi(ai)))));
    }

    private static readonly Lazy<VectorFont> Font = new(() => Luxel.Gallery.GalleryFonts.Load(Luxel.Gallery.GalleryFonts.Regular));

    private sealed record CavernAi(Action<Walker, CavernSim, float>? Callback);

    private sealed class CavernScene(GpuDevice device, CavernAi ai) : StorySceneBase(device)
    {
        public const uint W = 384, H = 256;
        private const int Tile = CavernLevel.Tile;
        private readonly Action<Walker, CavernSim, float>? _ai = ai.Callback;
        private GpuDeviceRasterizer2D _raster = null!;
        private GpuBuffer _atlasBuf = null!;
        private RetainedCanvas _canvas = null!;
        private IRasterScene2D _rasterScene = null!;
        private Vector2 _cameraCenter;
        private GpuBuffer? _fb;
        private bool _initialized;

        public override uint FbIndex => _fb?.BindlessIndex ?? 0;
        public override void PointerMove(float x, float y) { }
        public override void PointerDown(float x, float y) { }
        public override void PointerUp(float x, float y) { }
        public override void Wheel(float x, float y, float delta) { }

        public override void Update(in UpdateContext context)
        {
            if (_initialized) return;
            _initialized = true;
            InitializeGpu();
        }

        private void InitializeGpu()
        {
            _raster = new GpuDeviceRasterizer2D(Device);

            const int aw = 32, ah = 32;
            _atlasBuf = Device.Malloc(aw * ah * 4, GpuMemoryKind.HostMapped);
            Span<byte> px = _atlasBuf.Span<byte>(aw * ah * 4);
            (int Ox, int Oy, byte R, byte G, byte B)[] cells =
            [
                (0, 0, 70, 175, 85), (16, 0, 140, 92, 52), (0, 16, 120, 122, 135), (16, 16, 210, 70, 70),
            ];
            foreach (var (ox, oy, r, g, b) in cells)
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        int i = ((oy + y) * aw + ox + x) * 4;
                        bool edge = x == 0 || y == 0 || x == Tile - 1 || y == Tile - 1;
                        px[i] = (byte)(edge ? r * 3 / 4 : r);
                        px[i + 1] = (byte)(edge ? g * 3 / 4 : g);
                        px[i + 2] = (byte)(edge ? b * 3 / 4 : b);
                        px[i + 3] = 255;
                    }

            using var levels = new CavernLevelLoader();   // レベルは ResourceSystem 経由で読む (埋め込み .tmj)
            CavernSim sim = levels.CreateSim();
            Vector2[] torches = levels.Torches;
            sim.Map.TileSet.Atlas.Bind(_atlasBuf.BindlessIndex, aw, ah);
            if (_ai is not null && sim.Enemies.Count > 0) sim.Enemies[0].Ai = _ai;   // .csx 製の敵 AI



            // パーティクル演出 (放射スパーク、per-particle tint で炎/砂埃/コイン/撃破を出し分け)
            var fx = new ParticleSystem(new ParticleConfig(
                Life: ParticleValue.Range(0.4f, 0.8f), Speed: ParticleValue.Range(20, 60),
                SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: -40, Drag: 0.6f,
                Size: 3f, Color: new ParticleColor(Color2D.Rgba(255, 255, 255, 255), Color2D.Rgba(255, 255, 255, 0)),
                Shape: ParticleShape.Circle), capacity: 400, seed: 0xCA5E);
            uint torchTint = Color2D.Rgba(255, 160, 60), dustTint = Color2D.Rgba(200, 180, 150);
            uint coinTint = Color2D.Rgba(250, 225, 70), defeatTint = Color2D.Rgba(230, 90, 90);

            // プレイヤー物理 + fx を固定 dt で事前実行
            for (int f = 0; f < Steps; f++)
            {
                sim.Step(1f / 60, f >= 12 ? 1f : 0f, jumpPressed: false);
                foreach (Vector2 t in torches) fx.Emit(new Vector3(t, 0), 2, torchTint);
                if (sim.LandedThisStep)
                    fx.Emit(new Vector3(sim.PlayerPos.X + sim.PlayerSize.X * 0.5f, sim.PlayerPos.Y + sim.PlayerSize.Y, 0), 8, dustTint);
                foreach (Vector2 c in sim.PickupsThisStep) fx.Emit(new Vector3(c, 0), 10, coinTint);
                foreach (Vector2 d in sim.DefeatsThisStep) fx.Emit(new Vector3(d, 0), 14, defeatTint);
                fx.Update(1f / 60);
            }
            _cameraCenter = sim.PlayerCenter;

            _canvas = new RetainedCanvas();
            _rasterScene = _raster.CreateScene(_canvas);
            _fb = Device.Malloc((ulong)W * H * 4, GpuMemoryKind.DeviceLocal);

            UiNode sky = _canvas.AddChild(_canvas.Root);
            sky.Content = new Scene2D().FillRect(Color2D.White, 0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile);
            sky.Color = Color2D.Rgba(120, 170, 220);

            var layer = new TileMapLayer(_canvas, _canvas.Root, sim.Map);
            layer.Update(new RectF(0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile));

            UiNode ents = _canvas.AddChild(_canvas.Root);
            ents.ContentColors = true;
            var es = new Scene2D();
            uint door = sim.DoorOpen ? Color2D.Rgba(90, 200, 120) : Color2D.Rgba(120, 80, 50);
            es.FillRoundedRect(door, sim.DoorPos.X, sim.DoorPos.Y, sim.DoorSize.X, sim.DoorSize.Y, 3);
            foreach (Checkpoint c in sim.Checkpoints)   // 旗ポール (通過済=緑 / 未=灰)
            {
                float cx = c.Pos.X + c.Size.X * 0.5f;
                es.FillRoundedRect(Color2D.Rgba(180, 180, 190), cx - 1, c.Pos.Y, 2, c.Size.Y, 0);
                es.FillRoundedRect(c.Reached ? Color2D.Rgba(90, 200, 120) : Color2D.Rgba(150, 150, 160), cx + 1, c.Pos.Y, 11, 7, 1);
            }
            foreach (Pickup p in sim.Pickups)
            {
                if (p.Collected) continue;
                uint c = p.IsKey ? Color2D.Rgba(240, 210, 90) : Color2D.Rgba(250, 225, 70);
                es.FillCircle(c, p.Pos.X + p.Size * 0.5f, p.Pos.Y + p.Size * 0.5f, p.Size * 0.5f, 12);
            }
            foreach (Walker w in sim.Enemies)
                if (w.Alive) es.FillRoundedRect(Color2D.Rgba(220, 80, 90), w.Pos.X, w.Pos.Y, w.Size.X, w.Size.Y, 2);
            foreach (Flyer fl in sim.Flyers)
                if (fl.Alive) { Vector2 fp = fl.Pos; es.FillRoundedRect(Color2D.Rgba(200, 110, 220), fp.X, fp.Y, fl.Size.X, fl.Size.Y, 6); }
            es.FillRoundedRect(Color2D.Rgba(90, 170, 245), sim.PlayerPos.X, sim.PlayerPos.Y, sim.PlayerSize.X, sim.PlayerSize.Y, 3);
            ents.Content = es;

            // パーティクル (エンティティの上)
            new ParticleNode(_canvas, _canvas.Root, fx, circleSegments: 8).Sync();

            // HUD (HP/コイン/鍵。カメラにアンカーしてスクリーン左上へ、最前面)
            UiNode hud = _canvas.AddChild(_canvas.Root);
            hud.ContentColors = true;
            var hs = new Scene2D();
            CavernHud.Draw(hs, (sc, t, x, y, h, c) => Font.Value.AppendText(sc, t, x, y, h, c),
                sim, _cameraCenter, 2f, (int)W, (int)H);
            hud.Content = hs;
        }

        protected override void AddRenderPasses(RenderFeatureContext context)
        {
            if (_fb is null || FbReady) return;
            BufferHandle output = context.Graph.ImportBuffer(_fb, "cavern-framebuffer");
            context.Graph.AddPass("Cavern2D", PassQueue.Graphics)
                .Write(output)
                .Execute(pctx =>
                {
                    Camera2D cam = Camera2D.Create(2f, _cameraCenter, W, H);
                    _rasterScene.Render(cam, new GpuRasterTarget2D(pctx.Cmd, _fb, W, H));
                    MarkRendered();
                });
        }

        protected override ValueTask DisposeAsync()
        {
            _rasterScene?.Dispose();
            _canvas?.Dispose();
            _atlasBuf?.Dispose();
            _raster?.Dispose();
            _fb?.Dispose();
            _fb = null;
            return ValueTask.CompletedTask;
        }
    }
}
