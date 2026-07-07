using System.Numerics;
using Luxel;
using Luxel.Audio;
using Luxel.Framework;
using Luxel.Input;
using Luxel.Particles;
using Luxel.Settings;
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
    private readonly IFileStore _store;
    private readonly IAudioBackend _audioBackend;
    private readonly IKeyCapture _capture;
    private bool _rebinding;
    private CavernAudio? _audio;
    private CavernSave? _save;

    /// <summary>タイトルの「おわる」で立つ — Program がウィンドウを閉じる合図。</summary>
    public bool QuitRequested { get; private set; }

    private Rasterizer2D _raster = null!;
    private GpuBuffer _fb = null!;
    private GpuBuffer _atlas = null!;
    private int _paddedW;
    private bool _init;

    private CavernLevelLoader _levels = null!;
    private GameFlow _flow = null!;
    private readonly CameraRig2D _rig = new();
    private ParticleSystem _fx = null!;

    private Axis1DAction _move = null!, _navV = null!;
    private ButtonAction _jump = null!, _pause = null!, _confirm = null!, _continue = null!, _settingsBtn = null!;
    private bool _prevJump, _prevPause, _prevConfirm, _prevMenuJump, _prevContinue, _prevSettingsBtn;
    private float _prevNavV, _prevAdjust;

    private CavernSettings? _settings;
    private int _settingsRow;   // 0=Master, 1=Music, 2=Sfx

    private readonly CavernDevOverlay _dev = new();
    private ButtonAction _devToggle = null!;
    private bool _prevDevToggle;
    private float _fps = 60f;

    private static readonly uint TorchTint = Color2D.Rgba(255, 160, 60), DustTint = Color2D.Rgba(200, 180, 150);
    private static readonly uint CoinTint = Color2D.Rgba(250, 225, 70), DefeatTint = Color2D.Rgba(230, 90, 90);

    /// <summary>提示対象のフレームバッファ (Program が Present する)。</summary>
    public GpuBuffer Framebuffer => _fb;
    public uint StridePixels => (uint)_paddedW;

    public CavernRealtimeScene(SceneLoopServices loop, VectorFont font, IFileStore store, IAudioBackend audioBackend, IKeyCapture keyCapture) : base(loop)
    {
        _loop = loop;
        _font = font;
        _store = store;
        _audioBackend = audioBackend;
        _capture = keyCapture;
    }

    private void Init()
    {
        // レベルは ResourceSystem 経由で読む (埋め込み .tmj)。GameFlow がこのローダで sim を作る。
        _levels = new CavernLevelLoader();
        _flow = new GameFlow(_levels);

        _paddedW = Align(Width, 64);
        _raster = new Rasterizer2D(Device);
        _fb = Device.Malloc((ulong)(_paddedW * Height * 4), GpuMemoryKind.HostMapped);
        BakeAtlas();

        _move = new Axis1DAction("move");     // バインドは CavernBindings.Apply が設定 (プライマリ + 矢印セカンダリ)
        _jump = new ButtonAction("jump");
        _pause = new ButtonAction("pause", KeyCode.Escape);
        _confirm = new ButtonAction("confirm", KeyCode.Enter);
        _continue = new ButtonAction("continue", KeyCode.C);
        _settingsBtn = new ButtonAction("settings", KeyCode.S);
        _devToggle = new ButtonAction("dev", KeyCode.F1);
        _navV = new Axis1DAction("navV");
        _navV.ButtonPairs.Add((KeyCode.Down, KeyCode.Up));   // Down=+1 (下へ), Up=-1 (上へ)
        var ctx = new InputContext("gameplay");
        ctx.Add(_move); ctx.Add(_jump); ctx.Add(_pause); ctx.Add(_confirm); ctx.Add(_continue);
        ctx.Add(_settingsBtn); ctx.Add(_navV); ctx.Add(_devToggle);
        _loop.InputStack?.Push(ctx);

        _fx = new ParticleSystem(new ParticleConfig(
            Life: ParticleValue.Range(0.4f, 0.8f), Speed: ParticleValue.Range(20, 60),
            SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: -40, Drag: 0.6f,
            Size: 3f, Color: new ParticleColor(Color2D.Rgba(255, 255, 255, 255), Color2D.Rgba(255, 255, 255, 0)),
            Shape: ParticleShape.Circle), capacity: 600, seed: 0xCA5E);

        // 設定 (音量 + キーバインド) を %APPDATA% から読み込み、キーバインドを入力アクションへ反映。
        _settings = new CavernSettings(_store);
        CavernBindings.Apply(_move, _jump, _settings);

        // オーディオ配線 (BGM + イベント SE)。Mixer は Framework の UseAudio が用意する共有インスタンス。
        if (_loop.Mixer is { } mixer)
        {
            _audio = new CavernAudio(_audioBackend, mixer);
            _audio.BindSettings(_settings);
        }

        // タイトルで起動 — セーブがあれば「つづきから」を出す (%APPDATA% は Program が PhysicalFileStore で渡す)。
        _save = CavernPersistence.TryLoad(_store);
        _flow.HasSave = _save is not null;
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

    /// <summary>選択中の音量を増減 (0..1 でクランプ)。AutoSave が %APPDATA% へ即書き戻す。</summary>
    private void AdjustVolume(int row, float delta)
    {
        if (_settings is null) return;
        var sig = row switch { 0 => _settings.MasterVolume, 1 => _settings.MusicVolume, _ => _settings.SfxVolume };
        sig.Value = Math.Clamp(sig.Value + delta, 0f, 1f);
    }

    /// <summary>最初から始める (「はじめる」/ GameOver のセーブ無しリトライ)。</summary>
    private void StartNewGame() { _flow.StartNew(); OnSimStarted(); }

    /// <summary>セーブから再開する (「つづきから」/ GameOver のチェックポイント復活)。</summary>
    private void ContinueGame()
    {
        if (_save is not { } save) { StartNewGame(); return; }
        _flow.Continue(save);
        OnSimStarted();
    }

    /// <summary>sim 差し替え後のビュー初期化 (アトラス束縛・カメラ据付・パーティクル掃除)。</summary>
    private void OnSimStarted()
    {
        CavernSim sim = _flow.Sim!;
        sim.Map.TileSet.Atlas.Bind(_atlas.BindlessIndex, AtlasW, AtlasH);
        _rig.Zoom = 3f;
        _rig.Deadzone = new Vector2(120, 80);
        _rig.WorldBounds = new RectF(0, 0, CavernLevel.Width * CavernLevel.Tile, CavernLevel.Height * CavernLevel.Tile);
        _rig.Smoothing = 0.12f;
        _rig.Target = sim.PlayerCenter;
        _rig.SnapToTarget();
        _fx.Clear();
        _audio?.ResetForNewGame();
        _audio?.PlayBgm();
    }

    protected override void OnUpdate(UpdateContext ctx)
    {
        if (!_init) { Init(); _init = true; }

        // DevTools オーバーレイ (F1): gizmo + 統計。状態に依らず切り替え可能。
        float dt = ctx.Time.DeltaSeconds;
        if (dt > 1e-4f) _fps += (1f / dt - _fps) * 0.1f;   // 指数移動平均で滑らかに
        bool dev = _devToggle.Value.Value, devEdge = dev && !_prevDevToggle; _prevDevToggle = dev;
        if (devEdge) _dev.Toggle();
        _dev.PublishStats(_flow.Sim, _flow.State, _fx.Alive, _fps);

        // メニュー系の押下エッジ (ゲームプレイの _prevJump とは別トラッキング)。
        bool esc = _pause.Value.Value, escEdge = esc && !_prevPause; _prevPause = esc;
        bool enter = _confirm.Value.Value, enterEdge = enter && !_prevConfirm; _prevConfirm = enter;
        bool mjump = _jump.Value.Value, mjumpEdge = mjump && !_prevMenuJump; _prevMenuJump = mjump;
        bool cont = _continue.Value.Value, contEdge = cont && !_prevContinue; _prevContinue = cont;
        bool sets = _settingsBtn.Value.Value, setsEdge = sets && !_prevSettingsBtn; _prevSettingsBtn = sets;

        switch (_flow.State)
        {
            case GameState.Title:
                if (enterEdge || mjumpEdge) StartNewGame();            // はじめる
                else if (contEdge && _flow.HasSave) ContinueGame();    // つづきから
                else if (setsEdge) { _settingsRow = 0; _flow.ToSettings(); }     // せってい
                else if (escEdge) { _audio?.StopBgm(); QuitRequested = true; }   // おわる
                break;
            case GameState.Settings:
                if (_rebinding)
                {
                    // リバインド中: 次に押された生キーを割り当て (Esc はキャンセル)
                    if (_capture.TakePressed() is { } key)
                    {
                        if (key != KeyCode.Escape && _settings is { } st)
                            CavernBindings.Rebind(st, (CavernBind)(_settingsRow - 3), key);
                        _rebinding = false;
                        if (_settings is not null) CavernBindings.Apply(_move, _jump, _settings);
                    }
                    break;   // リバインド中は他操作を無視
                }
                float nav = _navV.Value.Value;
                if (nav != 0f && _prevNavV == 0f) _settingsRow = Math.Clamp(_settingsRow + (nav > 0f ? 1 : -1), 0, 5);
                _prevNavV = nav;
                float adj = _move.Value.Value;
                if (_settingsRow < 3 && adj != 0f && _prevAdjust == 0f) AdjustVolume(_settingsRow, adj > 0f ? 0.05f : -0.05f);
                _prevAdjust = adj;
                if (_settingsRow >= 3 && enterEdge) { _capture.TakePressed(); _rebinding = true; }   // Enter 自身を捨ててリバインド開始
                if (escEdge) _flow.ToTitle();   // もどる (AutoSave 済み)
                break;
            case GameState.Playing:
            case GameState.Paused:
                if (escEdge) _flow.TogglePause();
                break;
            case GameState.GameOver:
                if (enterEdge) { if (_flow.HasSave) ContinueGame(); else StartNewGame(); }   // 復活 or 最初から
                break;
            case GameState.Clear:
                if (enterEdge) { _audio?.StopBgm(); _flow.ToTitle(); _flow.HasSave = _save is not null; }   // タイトルへ
                break;
        }

        _audio?.Tick();   // BGM の音量 Signal を voice へ反映 (SE 側の Tick は Framework)
    }

    protected override void OnFixedUpdate(FixedUpdateContext ctx)
    {
        if (!_init) return;
        float dt = ctx.FixedDeltaSeconds;

        float move = _move.Value.Value;
        bool jh = _jump.Value.Value;
        bool jp = jh && !_prevJump;
        _prevJump = jh;

        GameState before = _flow.State;
        _flow.Step(dt, move, jp);
        CavernSim? sim = _flow.Sim;

        // オーディオ: このステップの出来事を SE として鳴らす (Playing→Clear の遷移ステップも含めるため before で判定)。
        if (before == GameState.Playing && sim is not null) _audio?.React(sim);

        // 永続化: チェックポイント通過でオートセーブ、クリアでセーブ消去。
        if (sim is not null)
        {
            if (sim.CheckpointThisStep)
            {
                _save = sim.Export();
                CavernPersistence.Save(_store, _save);
                _flow.HasSave = true;
            }
            if (before == GameState.Playing && _flow.State == GameState.Clear)
            {
                CavernPersistence.Clear(_store);
                _save = null;
                _flow.HasSave = false;
            }
        }

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
        foreach (Vector2 t in _levels.Torches) _fx.Emit(new Vector3(t, 0), 1, TorchTint);
        _fx.Update(dt);
    }

    protected override void OnRender(RenderContext ctx)
    {
        if (!_init) return;
        var s = new Scene2D();
        Camera2D cam;
        if (_flow.State == GameState.Settings)
        {
            cam = Camera2D.Pixels;
            DrawSettings(s);
        }
        else if (_flow.State == GameState.Title || _flow.Sim is not { } sim)
        {
            cam = Camera2D.Pixels;   // スクリーン空間 (world == px) でタイトルを描く
            DrawTitle(s);
        }
        else
        {
            cam = _rig.Camera(Width, Height);
            DrawWorld(s, sim, cam);
            CavernHud.Draw(s, (sc, t, x, y, h, c) => _font.AppendText(sc, t, x, y, h, c), sim, CameraCenter(cam), _rig.EffectiveZoom, Width, Height);
            DrawOverlay(s, cam);

            if (_dev.Enabled)   // DevTools: gizmo をワールド空間で溜めて最前面へ Flush + 統計パネル
            {
                Vector2 c = CameraCenter(cam);
                float zoom = _rig.EffectiveZoom, vw = Width / zoom, vh = Height / zoom;
                _dev.EmitGizmos(sim, _rig, new RectF(c.X - vw * 0.5f, c.Y - vh * 0.5f, vw, vh), _fx, _levels.Torches);
                DebugDraw.Flush(s, w => new Vector2(w.X, w.Y), (sc, t, x, y, h, cc) => _font.AppendText(sc, t, x, y, h, cc));
                _dev.DrawStatsPanel(s, (sc, t, x, y, h, cc) => _font.AppendText(sc, t, x, y, h, cc),
                    c, zoom, Width, Height, sim, _flow.State, _fx.Alive, _fps);
            }
        }

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

    /// <summary>タイトル画面 (スクリーン空間、<see cref="Camera2D.Pixels"/> 前提)。中央にタイトル + メニュー。</summary>
    private void DrawTitle(Scene2D s)
    {
        s.FillRect(Color2D.Rgba(14, 16, 26), 0, 0, Width, Height);
        float cx = Width * 0.5f;
        // AppendText は左寄せなので、おおよその字幅 (height*0.28) で中央寄せ。
        void Line(string t, float y, float size, uint col) => _font.AppendText(s, t, cx - t.Length * size * 0.28f, y, size, col);

        Line(TitleScreen.GameTitle, 170, 52, Color2D.Rgba(245, 246, 252));
        Line(TitleScreen.Subtitle, 222, 15, Color2D.Rgba(150, 160, 182));

        float y = 300;
        Line("Space / Enter : はじめる", y, 20, Color2D.Rgba(214, 220, 236)); y += 32;
        if (_flow.HasSave) { Line("C : つづきから", y, 20, Color2D.Rgba(150, 220, 170)); y += 32; }
        Line("S : せってい", y, 20, Color2D.Rgba(190, 200, 224)); y += 32;
        Line("Esc : おわる", y, 20, Color2D.Rgba(160, 170, 190));
    }

    /// <summary>設定画面 (音量 + キーバインド。スクリーン空間、<see cref="Camera2D.Pixels"/> 前提)。↑↓ 選択・←→ 音量・Enter 割当。</summary>
    private void DrawSettings(Scene2D s)
    {
        s.FillRect(Color2D.Rgba(14, 16, 26), 0, 0, Width, Height);
        float cx = Width * 0.5f;
        _font.AppendText(s, "せってい", cx - 4 * 40 * 0.28f, 120, 40, Color2D.Rgba(245, 246, 252));

        uint sel_ = Color2D.Rgba(250, 230, 130), normal_ = Color2D.Rgba(210, 216, 230);
        float y = 190, bx = cx + 30, bw = 180, bh = 14;

        // 音量 (行 0-2)
        string[] volNames = { "マスター音量", "BGM 音量", "SE 音量" };
        var vols = new[] { _settings?.MasterVolume.Value ?? 0f, _settings?.MusicVolume.Value ?? 0f, _settings?.SfxVolume.Value ?? 0f };
        for (int i = 0; i < 3; i++)
        {
            bool sel = i == _settingsRow;
            uint col = sel ? sel_ : normal_;
            _font.AppendText(s, (sel ? "▶ " : "   ") + volNames[i], cx - 240, y, 20, col);
            s.FillRoundedRect(Color2D.Rgba(50, 54, 66), bx, y - 13, bw, bh, 4);
            s.FillRoundedRect(sel ? Color2D.Rgba(120, 200, 150) : Color2D.Rgba(90, 150, 200),
                bx, y - 13, bw * Math.Clamp(vols[i], 0f, 1f), bh, 4);
            _font.AppendText(s, $"{(int)MathF.Round(vols[i] * 100)}", bx + bw + 14, y, 18, col);
            y += 38;
        }

        // キーバインド (行 3-5)
        for (int i = 0; i < 3; i++)
        {
            int row = 3 + i;
            bool sel = row == _settingsRow;
            uint col = sel ? sel_ : normal_;
            var bind = (CavernBind)i;
            _font.AppendText(s, (sel ? "▶ " : "   ") + CavernBindings.Label(bind), cx - 240, y, 20, col);
            string keyName = (_rebinding && sel) ? "＿＿" : (_settings is { } ss ? CavernBindings.Current(ss, bind).ToString() : "");
            _font.AppendText(s, $"[{keyName}]", bx, y, 20, sel ? sel_ : Color2D.Rgba(170, 200, 230));
            y += 38;
        }

        string hint = _rebinding
            ? "キーをおしてください (Esc でキャンセル)"
            : "↑↓ せんたく / ←→ おんりょう / Enter わりあて / Esc もどる";
        _font.AppendText(s, hint, cx - hint.Length * 13 * 0.28f, 470, 13, Color2D.Rgba(150, 160, 182));
    }

    // Camera2D (screen = A*wx + C*wy + E, ...) の中心 world 座標を逆算 (A=D=zoom, C=B=0)
    private static Vector2 CameraCenter(Camera2D cam)
        => new((Width * 0.5f - cam.E) / cam.A, (Height * 0.5f - cam.F) / cam.D);

    public override Task OnUnloadAsync()
    {
        _audio?.Dispose();
        _levels?.Dispose();
        _fb?.Dispose();
        _atlas?.Dispose();
        _raster?.Dispose();
        return Task.CompletedTask;
    }
}
