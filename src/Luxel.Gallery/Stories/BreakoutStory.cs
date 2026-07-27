using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Framework;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using static Luxel.Controls.Kit;
using Phase = Luxel.Framework.Phase;
using World = Luxel.Ecs.World;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **ショーケースゲーム: BREAKOUT** — エンジンの主要サブシステムを 1 つの実アプリで使う:
/// - <b>Framework</b>: LuxelHostBuilder + DI + GameLoop + GameScene (フェーズ駆動の実ゲームループ)
/// - <b>ECS</b>: ボール/ブロックはエンティティ。ロジックはフェーズ別の delegate system
///   (Update = 移動/衝突、LateUpdate = ノード同期/パーティクル) — <see cref="World.AddSystem(string, Action)"/>
/// - <b>保持型 2D</b>: 移動 = transform 書込のみ、ブロック破壊 = Visible=false (order 再構成のみ)、
///   パーティクル = 1 ノードの Content 差し替え (ReserveContent で in-place)、HUD テキスト = Content 差し替え
/// - <b>Storybook Platform</b>: GPU はホスト借用、ペーシング/提示/入力は <see cref="StoryAppView{TScene}"/>
/// 操作: マウス移動 = パドル、クリック = 開始/発射/リスタート。
/// </summary>
public static class BreakoutStories
{
    [Story("Apps/Game/Breakout", Width = 520, Height = 430, Order = 147)]
    public static Widget Breakout(StoryContext ctx)
        => ctx.Snap(VStack(8)[
            new StoryAppView<BreakoutScene>(BreakoutScene.FieldW, BreakoutScene.FieldH, (s, bctx) =>
            {
                s.AddSingleton(bctx.Font);
                s.AddSingleton<Action<string>>(ctx.Log);
            }),
            Muted("Mouse: paddle / Click: start & launch — real Framework app (ECS + retained 2D + phases)")
        ]);

    // ---- ECS コンポーネント (ゲーム状態はエンティティが持つ) ----

    /// <summary>ボール。Stuck = パドルに載っている (クリックで発射)。</summary>
    public struct Ball : IComponent { public float X, Y, VX, VY; public bool Stuck; }

    /// <summary>ブロック。Node = 対応する保持ノードの index (破壊 = Visible=false)。</summary>
    public struct Brick : IComponent { public float X, Y; public int Row, Node; public bool Alive; }

    public sealed class BreakoutScene : GameScene, IStoryApp
    {
        public const float FieldW = 480, FieldH = 340;
        private const float PadW = 70, PadH = 10, PadY = FieldH - 26;
        private const float BallR = 5f;
        private const int Cols = 10, Rows = 5;
        private const float BrickW = 44, BrickH = 15, BrickGap = 2;
        private const float BricksTop = 44;
        private const float BricksLeft = (FieldW - (Cols * (BrickW + BrickGap) - BrickGap)) / 2;
        private const float BallSpeed = 260f;

        private static readonly uint[] RowColors =
        [
            Color2D.Rgba(226, 88, 98), Color2D.Rgba(236, 152, 74), Color2D.Rgba(240, 202, 84),
            Color2D.Rgba(96, 194, 122), Color2D.Rgba(96, 144, 232),
        ];

        private readonly VectorFont _font;
        private readonly Action<string> _log;
        private readonly World _world = new();

        private enum GameState { Title, Playing, Over, Clear }
        private GameState _state = GameState.Title;
        private int _score, _lives;
        private float _paddleX = FieldW / 2;
        private float _dt;
        private bool _clickQueued;

        // 表示 (保持型キャンバス)
        private Rasterizer2D? _raster;
        private RetainedCanvas? _canvas;
        private GpuBuffer? _fb;
        private UiNode? _ballNode, _paddleNode, _scoreNode, _livesNode, _msgNode;
        private readonly List<UiNode> _brickNodes = new();
        private long _version, _seen;
        private int _shownScore = -1, _shownLives = -1;

        // パーティクル (破壊エフェクト — 標準 ParticleSystem + ParticleNode。色はブロック色を tint で per-particle 指定)
        private ParticleSystem? _particles;
        private ParticleNode? _pnode;
        private bool _hadParticles;

        public BreakoutScene(SceneLoopServices loop, VectorFont font, Action<string> log) : base(loop)
        {
            _font = font;
            _log = log;
            AddWorld(_world);
            // フェーズ別 system (delegate)。dt は OnUpdate が先に立てる (GameScene のフェーズ順)
            _world.AddSystem(Phase.Update.Name, StepBall);
            _world.AddSystem(Phase.Update.Name, CollideBricks);
            _world.AddSystem(Phase.LateUpdate.Name, StepParticles);
            _world.AddSystem(Phase.LateUpdate.Name, SyncNodes);
        }

        // ---- IStoryApp (Storybook Platform との接点) ----

        public uint FbIndex => _fb?.BindlessIndex ?? 0;
        public bool FbReady => _version > 0;
        public bool ConsumeRendered()
        {
            if (_seen == _version) return false;
            _seen = _version;
            return true;
        }

        public void PointerMove(float x, float y)
            => _paddleX = Math.Clamp(x, PadW / 2, FieldW - PadW / 2);
        public void PointerDown(float x, float y) => _clickQueued = true;
        public void PointerUp(float x, float y) { }
        public void Wheel(float x, float y, float d) { }

        // ---- フレーム (GameScene のフェーズフック) ----

        protected override void OnUpdate(UpdateContext ctx)
        {
            if (_canvas is null) InitGpu();
            _dt = MathF.Min(ctx.Time.DeltaSeconds, 1f / 30f);   // タブ切替等の巨大 dt でトンネリングしない

            if (_clickQueued)
            {
                _clickQueued = false;
                switch (_state)
                {
                    case GameState.Title or GameState.Over or GameState.Clear:
                        StartGame();
                        break;
                    case GameState.Playing:
                        LaunchBall();
                        break;
                }
            }
        }

        protected override void OnRender(RenderContext ctx)
        {
            if (_canvas is null || _fb is null) return;
            if (_version > 0 && !_canvas.HasPendingChanges) return;
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _canvas.Render(cmd, Camera2D.Pixels, (uint)FieldW, (uint)FieldH, _fb);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
            _version++;
        }

        public override Task OnUnloadAsync()
        {
            _canvas?.Dispose();
            _raster?.Dispose();
            _fb?.Dispose();
            _canvas = null; _raster = null; _fb = null;
            return Task.CompletedTask;
        }

        // ---- 初期化 (GPU 資源は最初のフレーム内で — 起動スレッドから触らない) ----

        private void InitGpu()
        {
            _raster = new Rasterizer2D(Device);
            _canvas = new RetainedCanvas(_raster);
            _fb = Device.Malloc((ulong)FieldW * (ulong)FieldH * 4, GpuMemoryKind.DeviceLocal);
            UiNode root = _canvas.Root;

            // フィールド (深い紺 + 枠)
            UiNode bg = _canvas.AddChild(root);
            var bgScene = new Scene2D();
            bgScene.FillRect(Color2D.White, 0, 0, FieldW, FieldH);
            bg.Content = bgScene;
            bg.Color = Color2D.Rgba(24, 24, 38);
            UiNode frame = _canvas.AddChild(root);
            var fr = new Scene2D();
            fr.StrokeRoundedRect(Color2D.White, 2, 1, 1, FieldW - 2, FieldH - 2, 6);
            frame.Content = fr;
            frame.Color = Color2D.Rgba(70, 74, 110);

            // ブロック (1 個 = 1 ノード + 1 エンティティ。破壊は Visible=false = order 再構成のみ)
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    float x = BricksLeft + c * (BrickW + BrickGap);
                    float y = BricksTop + r * (BrickH + BrickGap);
                    UiNode n = _canvas.AddChild(root);
                    var s = new Scene2D();
                    s.FillRoundedRect(Color2D.White, 0, 0, BrickW, BrickH, 3);
                    n.Content = s;
                    n.Color = RowColors[r];
                    n.Transform = Affine2D.Translate(x, y);
                    n.Z = 1;
                    _brickNodes.Add(n);
                    _world.CreateEntity(new Brick { X = x, Y = y, Row = r, Node = _brickNodes.Count - 1, Alive = false });
                }

            // パドル / ボール (移動 = transform 書込のみ)
            _paddleNode = _canvas.AddChild(root);
            var ps = new Scene2D();
            ps.FillRoundedRect(Color2D.White, -PadW / 2, 0, PadW, PadH, 5);
            _paddleNode.Content = ps;
            _paddleNode.Color = Color2D.Rgba(210, 214, 240);
            _paddleNode.Z = 2;

            _ballNode = _canvas.AddChild(root);
            var bs = new Scene2D();
            bs.FillCircle(Color2D.White, 0, 0, BallR, 24);
            _ballNode.Content = bs;
            _ballNode.Color = Color2D.Rgba(245, 245, 250);
            _ballNode.Z = 3;
            _world.CreateEntity(new Ball { X = FieldW / 2, Y = PadY - BallR, Stuck = true });

            // パーティクル: 標準 ParticleSystem。設定色 = 白→透明フェード、tint (ブロック色) を per-particle 乗算。
            // ParticleNode が ContentColors + ReserveContent (= in-place 差し替え) を面倒みる。
            var pcfg = new ParticleConfig(
                Life: 0.5f, Speed: ParticleValue.Range(40, 130), SpreadRadians: MathF.PI, BaseAngle: 0f,
                Gravity: 300, Drag: 0f, Size: 4f,
                Color: new ParticleColor(Color2D.Rgba(255, 255, 255, 255), Color2D.Rgba(255, 255, 255, 0)),
                Shape: ParticleShape.Quad);
            _particles = new ParticleSystem(pcfg, capacity: 128, seed: 12345);
            _pnode = new ParticleNode(_canvas, root, _particles);
            _pnode.Node.Z = 4;

            // HUD
            _scoreNode = _canvas.AddChild(root);
            _scoreNode.Color = Color2D.Rgba(235, 235, 245);
            _scoreNode.Z = 5;
            _livesNode = _canvas.AddChild(root);
            _livesNode.Color = Color2D.Rgba(226, 88, 98);
            _livesNode.Z = 5;
            _msgNode = _canvas.AddChild(root);
            _msgNode.Color = Color2D.Rgba(240, 240, 250);
            _msgNode.Z = 6;

            ShowMessage("BREAKOUT", "click to start");
            SetHud(score: 0, lives: 3);
        }

        // ---- ゲーム進行 ----

        private void StartGame()
        {
            _score = 0;
            _lives = 3;
            _state = GameState.Playing;
            _world.Query<Brick>().ForEachEntity((ref Brick b, Entity e) =>
            {
                b.Alive = true;
                _brickNodes[b.Node].Visible = true;
            });
            ResetBall();
            SetHud(_score, _lives);
            HideMessage();
            _log("game start");
        }

        private void ResetBall()
            => _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
            {
                b.Stuck = true;
                b.X = _paddleX;
                b.Y = PadY - BallR;
                b.VX = 0; b.VY = 0;
            });

        private void LaunchBall()
            => _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
            {
                if (!b.Stuck) return;
                b.Stuck = false;
                float a = (_paddleX / FieldW - 0.5f) * 1.6f - MathF.PI / 2;   // パドル位置で射出角
                b.VX = MathF.Cos(a) * BallSpeed;
                b.VY = MathF.Sin(a) * BallSpeed;
            });

        /// <summary>Update phase (ECS system): ボールの移動と壁/パドル反射、落下 = ライフ減。</summary>
        private void StepBall()
        {
            if (_state != GameState.Playing) return;
            bool lost = false;
            _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
            {
                if (b.Stuck) { b.X = _paddleX; b.Y = PadY - BallR; return; }
                b.X += b.VX * _dt;
                b.Y += b.VY * _dt;
                if (b.X < BallR + 2) { b.X = BallR + 2; b.VX = MathF.Abs(b.VX); }
                if (b.X > FieldW - BallR - 2) { b.X = FieldW - BallR - 2; b.VX = -MathF.Abs(b.VX); }
                if (b.Y < BallR + 2) { b.Y = BallR + 2; b.VY = MathF.Abs(b.VY); }

                // パドル反射 (当たった位置で角度が変わる)
                if (b.VY > 0 && b.Y + BallR >= PadY && b.Y + BallR <= PadY + PadH + 6
                    && MathF.Abs(b.X - _paddleX) <= PadW / 2 + BallR)
                {
                    float t = Math.Clamp((b.X - _paddleX) / (PadW / 2), -1f, 1f);
                    float a = t * 1.1f - MathF.PI / 2;
                    float sp = MathF.Sqrt(b.VX * b.VX + b.VY * b.VY) * 1.02f;   // 徐々に加速
                    b.VX = MathF.Cos(a) * sp;
                    b.VY = MathF.Sin(a) * sp;
                    b.Y = PadY - BallR;
                }

                if (b.Y > FieldH + BallR) lost = true;
            });
            if (lost) LoseLife();
        }

        /// <summary>Update phase (ECS system): ブロック衝突 — 破壊はノード Visible=false + パーティクル。</summary>
        private void CollideBricks()
        {
            if (_state != GameState.Playing) return;
            float ballX = 0, ballY = 0;
            bool moving = false;
            _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
            {
                ballX = b.X; ballY = b.Y; moving = !b.Stuck;
            });
            if (!moving) return;

            bool flipX = false, flipY = false;
            int remaining = 0;
            _world.Query<Brick>().ForEachEntity((ref Brick br, Entity e) =>
            {
                if (!br.Alive) return;
                remaining++;
                float nx = Math.Clamp(ballX, br.X, br.X + BrickW);
                float ny = Math.Clamp(ballY, br.Y, br.Y + BrickH);
                float dx = ballX - nx, dy = ballY - ny;
                if (dx * dx + dy * dy > BallR * BallR) return;

                br.Alive = false;
                remaining--;
                _brickNodes[br.Node].Visible = false;   // 破壊 = order 再構成のみ (ジオメトリ不変)
                _score += (Rows - br.Row) * 10;
                SpawnParticles(br.X + BrickW / 2, br.Y + BrickH / 2, RowColors[br.Row]);
                if (MathF.Abs(dx) > MathF.Abs(dy)) flipX = true; else flipY = true;
            });

            if (flipX || flipY)
                _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
                {
                    if (flipX) b.VX = -b.VX;
                    if (flipY) b.VY = -b.VY;
                });
            if (_score != _shownScore) SetHud(_score, _lives);
            if (remaining == 0 && _state == GameState.Playing)
            {
                _state = GameState.Clear;
                ShowMessage("CLEAR!", $"score {_score} — click to play again");
                _log($"clear! score={_score}");
            }
        }

        private void LoseLife()
        {
            _lives--;
            SetHud(_score, _lives);
            if (_lives <= 0)
            {
                _state = GameState.Over;
                ShowMessage("GAME OVER", $"score {_score} — click to retry");
                _log($"game over score={_score}");
            }
            else
            {
                ResetBall();
                _log($"miss — lives {_lives}");
            }
        }

        // ---- LateUpdate phase (ECS system): 表示同期 ----

        /// <summary>コンポーネント → ノード transform (移動 = transform 書込のみ)。</summary>
        private void SyncNodes()
        {
            if (_canvas is null) return;
            _world.Query<Ball>().ForEachEntity((ref Ball b, Entity e) =>
                _ballNode!.Transform = Affine2D.Translate(b.X, b.Y));
            _paddleNode!.Transform = Affine2D.Translate(_paddleX, PadY);
        }

        private void SpawnParticles(float x, float y, uint color)
            => _particles?.Emit(new Vector3(x, y, 0), 6, tint: color);   // tint = ブロック色

        /// <summary>パーティクル更新 + 1 ノードへの Content 差し替え (ParticleNode が in-place を面倒みる)。</summary>
        private void StepParticles()
        {
            if (_particles is null || _pnode is null) return;
            if (_particles.Alive == 0 && !_hadParticles) return;   // 空が続く間は再構築しない
            _particles.Update(_dt);
            _pnode.Sync();                                          // 空になった最後の 1 回も空シーンで消す
            _hadParticles = _particles.Alive > 0;
        }

        // ---- HUD ----

        private void SetHud(int score, int lives)
        {
            _shownScore = score;
            _shownLives = lives;
            var ss = new Scene2D();
            _font.AppendText(ss, $"SCORE {score:00000}", 12, 26, 15, Color2D.White);
            _scoreNode!.Content = ss;

            var ls = new Scene2D();
            for (int i = 0; i < lives; i++)
                ls.FillCircle(Color2D.White, FieldW - 16 - i * 16, 20, 5, 16);
            _livesNode!.Content = ls;
        }

        private void ShowMessage(string title, string sub)
        {
            var s = new Scene2D();
            float tw = _font.Measure(title, 34).width;
            _font.AppendText(s, title, (FieldW - tw) / 2, FieldH / 2 - 6, 34, Color2D.White);
            float sw = _font.Measure(sub, 14).width;
            _font.AppendText(s, sub, (FieldW - sw) / 2, FieldH / 2 + 22, 14, Color2D.White);
            _msgNode!.Content = s;
            _msgNode.Visible = true;
        }

        private void HideMessage()
        {
            if (_msgNode is not null) _msgNode.Visible = false;
        }
    }
}
