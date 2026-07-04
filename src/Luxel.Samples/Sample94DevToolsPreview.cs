using System.Numerics;
using Friflo.Engine.ECS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Ecs;
using Luxel.AssetRuntime;
using Luxel.Audio;
using Luxel.Framework;
using Luxel.Input;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using Phase = Luxel.Framework.Phase;
using World = Luxel.Ecs.World;
using static Luxel.Controls.Kit;

namespace Luxel.Samples;

/// <summary>
/// Sample 94: DevTools UI の目視確認用シーン。ECS component 値 (Phase 2-A) /
/// per-system timing (Phase 2-B) / UiSurface panel (Phase 2-C) を全て触れる構成:
/// <list type="bullet">
/// <item>World に cube entity 1 個 (LocalTransform / GlobalTransform / Color3D)</item>
/// <item>Update phase に <see cref="RotateSystem"/> (毎フレーム LocalTransform を更新)</item>
/// <item>PreRender phase に <see cref="TransformPropagateSystem"/></item>
/// <item>UiSurface 3 個 (60Hz HUD / 30Hz radar / dirty-only damage)</item>
/// </list>
/// 使い方: <c>dotnet run --project src/Luxel.Samples -- vk 94 [port] [seconds]</c> (既定 5173 / 60s)
/// </summary>
public static class Sample94DevToolsPreview
{
    public static int Run(Func<GpuDevice> createDevice, int port, int seconds, bool nativeApp = false)
    {
        Console.WriteLine($"=== Sample 94: DevTools preview (port={port}, {seconds}s{(nativeApp ? ", native app" : "")}) ===");

        var host = LuxelHostBuilder.Create()
            .UseGpu(createDevice)
            .ConfigureServices(s =>
            {
                s.AddSingleton<FakeInputSource>();
                s.AddSingleton<IInputSource>(sp => sp.GetRequiredService<FakeInputSource>());
                s.AddSingleton<MainScene>();
            })
            .AddScene<MainScene>()
            .ConfigureServices(s => s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3)))
            .Build();

        // EngineCommands は Host が DI 登録済み。Scene が engine.pause/resume/step を register する。
        var cmds = host.Services.GetRequiredService<EngineCommands>();
        using var listener = new DevToolsListener(cmds);
        using var server = new DebugServer(listener, port: port);
        server.Start();
        Console.WriteLine($"DevTools URL: {server.Url}");

        // ネイティブ DevTools ウィンドウ (別スレッド + 自前デバイス)。E2E は port+1。
        using DevToolsApp? devtools = nativeApp
            ? DevToolsApp.Launch(createDevice, listener, cmds, e2ePort: port + 1)
            : null;

        // 実 UI を 3 つ構築して UiRegistry に登録 → DevTools が /trees で拾う
        try
        {
            BuildRealUis(
                host.Services.GetRequiredService<Luxel.UI.UiRegistry>(),
                host.Services.GetRequiredService<GpuDevice>());
        }
        catch (Exception ex) { Console.WriteLine($"UI construction skipped: {ex.Message}"); }

        // DevTools Resources パネルの検証用に、代表的なリソース状態をダミー emit する
        // (Sample 94 は ResourceSystem を使わないので、実データが無いと空になる)
        EngineDiagnostics.Emit(EngineDiagnostics.Resources, new DiagResources(new[]
        {
            new DiagResourceNode("AssetImage:demo/hud.png", "Luxel.Assets.AssetImage", "demo/hud.png",       "Ready",   1, "ImageDecodeStep",  "Cpu", Array.Empty<string>()),
            new DiagResourceNode("GpuTexture:demo/hud.png", "Luxel.AssetsGpu.GpuTexture", "demo/hud.png",    "Ready",   1, "TextureUploadStep","Gpu", new[]{"AssetImage:demo/hud.png"}),
            new DiagResourceNode("AudioClip:sfx/beep.wav",  "Luxel.Audio.AudioClip",  "sfx/beep.wav",       "Loading", 1, "AudioClipStep",    "Io",  Array.Empty<string>()),
            new DiagResourceNode("AssetImage:missing.png",  "Luxel.Assets.AssetImage", "missing.png",       "Failed",  0, "ImageDecodeStep",  "Cpu", Array.Empty<string>()),
        }));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try { host.RunAsync(cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }

        Console.WriteLine("DevTools preview: shutting down");
        return 0;
    }

    /// <summary>
    /// 実 UiHost を 3 つ (画面 HUD / コクピット gauges / world-space monitor) 構築し UiRegistry に登録。
    /// DevTools UI Tree タブで tree セレクタから切替可能、Widget プロパティは Bindable&lt;T&gt; フィールドから
    /// reflection で自動抽出され、編集値は <see cref="Bindable{T}.SetOverride"/> で反映される。
    /// </summary>
    /// <summary>UiHost + それ用の readback GpuBuffer + サイズを 1 セットで保持し、UiFrame emit に必要な情報を束ねる。</summary>
    internal sealed record UiPreview(string Name, UiHost Host, RetainedCanvas Canvas, GpuBuffer Readback, int Width, int Height);
    internal static readonly List<UiPreview> UiPreviews = new();

    private static void BuildRealUis(UiRegistry registry, GpuDevice device)
    {
        VectorFont font;
        try { font = VectorFont.LoadSystem(); }
        catch { return; }   // font ロード失敗環境はスキップ

        (UiHost host, RetainedCanvas canvas) MakeHost(float w, float h)
        {
            var raster = new Rasterizer2D(device);
            var canvas = new RetainedCanvas(raster);
            return (new UiHost(canvas, font, w, h), canvas);
        }
        void Reg(string name, UiHost host, RetainedCanvas canvas, int w, int h)
        {
            registry.Register(name, host);
            var readback = device.Malloc((ulong)(w * h * 4), GpuMemoryKind.HostMapped);
            UiPreviews.Add(new UiPreview(name, host, canvas, readback, w, h));
        }

        // ---- Screen HUD (320×180) ----
        var (hud, hudCanvas) = MakeHost(320f, 180f);
        hud.SetRoot(Border(background: Tw.Slate800, padding: new Thickness(8))
            [ VStack(spacing: 4)
                [ Text("Player HUD", fontSize: 16, color: Tw.Slate100),
                  Text("HP 87/100",  fontSize: 12, color: Tw.Amber400),
                  Text("Score 12480", fontSize: 12, color: Tw.Amber400) ]
            ]);
        Reg("HUD (screen 320×180)", hud, hudCanvas, 320, 180);

        // ---- Cockpit gauges (256×128) ----
        var (cockpit, cockpitCanvas) = MakeHost(256f, 128f);
        cockpit.SetRoot(Border(background: Tw.Slate900, padding: new Thickness(6))
            [ HStack(spacing: 8)
                [ Border(background: Tw.Amber500, width: 60, height: 60, rounded: 30),
                  VStack(spacing: 2)
                    [ Text("SPEED",    fontSize: 10, color: Tw.Slate400),
                      Text("214 km/h", fontSize: 18, color: Tw.Amber400),
                      Text("FUEL 62%", fontSize: 12, color: Tw.Cyan400) ] ]
            ]);
        Reg("Cockpit gauges (256×128, world-space)", cockpit, cockpitCanvas, 256, 128);

        // ---- Radar minimap (128×128) ----
        var (radar, radarCanvas) = MakeHost(128f, 128f);
        radar.SetRoot(Border(background: Tw.Slate900, padding: new Thickness(4))
            [ VStack(spacing: 2)
                [ Text("RADAR", fontSize: 11, color: Tw.Amber400),
                  Border(background: Tw.Slate700, width: 100, height: 100, rounded: 50) ]
            ]);
        Reg("Radar minimap (128×128)", radar, radarCanvas, 128, 128);
    }

    /// <summary>登録済 UiPreview を順に render + readback → DiagUiFrame として emit。
    /// Scene の Render phase から毎フレーム呼ぶ (heavy なので購読者チェックで早期 return)。</summary>
    private static void EmitUiPreviewFrames(GpuDevice device)
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.UiFrame)) return;
        if (UiPreviews.Count == 0) return;

        for (int i = 0; i < UiPreviews.Count; i++)
        {
            var p = UiPreviews[i];
            using (var cmd = device.MainQueue.StartCommandRecording())
            {
                p.Canvas.Render(cmd, Camera2D.Pixels, (uint)p.Width, (uint)p.Height, p.Readback);
                cmd.Finish();
                device.MainQueue.SubmitAndWait(cmd);
            }
            byte[] rgba = p.Readback.Span<byte>(p.Width * p.Height * 4).ToArray();
            EngineDiagnostics.Emit(EngineDiagnostics.UiFrame, new DiagUiFrame(i, p.Name, p.Width, p.Height, rgba));
        }
    }

    /// <summary>Cube を毎フレーム回転させる System。Perf タブで Update phase 配下に現れる。</summary>
    private sealed class RotateSystem : Friflo.Engine.ECS.Systems.QuerySystem<LocalTransform>
    {
        protected override void OnUpdate()
        {
            float dt = Tick.deltaTime;
            Query.ForEachEntity((ref LocalTransform t, Entity _) =>
            {
                t.Matrix *= Matrix4x4.CreateRotationY(dt * 0.5f);
            });
        }
    }

    public sealed class MainScene : GameScene
    {
        private readonly World _world;
        private readonly InputStack _stack;
        private readonly FakeInputSource _fake;
        private readonly Axis2DAction _move;
        private readonly ButtonAction _fire;
        public UiSurface Hud { get; }
        public UiSurface Radar { get; }
        public UiSurface Damage { get; }

        public MainScene(SceneLoopServices loop, World world, InputStack stack, FakeInputSource fake, AudioRegistry audio) : base(loop)
        {
            _world = world; _stack = stack; _fake = fake;

            // Bus 階層 (Master → SFX / Music) を audio registry に登録
            var master = new AudioBus("Master");
            var sfx = new AudioBus("SFX", parent: master);
            var music = new AudioBus("Music", parent: master);
            music.Volume.Value = 0.7f;
            sfx.Volume.Value = 0.85f;
            audio.RegisterBus(master);
            audio.RegisterBus(sfx);
            audio.RegisterBus(music);

            var gameplay = new InputContext("gameplay");
            _move = gameplay.Add(new Axis2DAction("Move"));
            _move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
            _fire = gameplay.Add(new ButtonAction("Fire", KeyCode.Space));
            _stack.Push(gameplay);

            AddWorld(_world);
            _world.AddSystem(Phase.Update.Name, new RotateSystem());
            _world.AddSystem(Phase.PreRender.Name, () => TransformPropagateSystem.Run(_world));

            _world.CreateEntity(
                new LocalTransform(Matrix4x4.Identity),
                new Color3D(new Vector4(0.30f, 0.70f, 0.95f, 1f)),
                new MeshRef(0));

            Hud    = new UiSurface(Device, "hud",    320, 180) { RateHz = 60 };
            Radar  = new UiSurface(Device, "radar",  128, 128, UiSurfaceKind.WorldSpaceQuad) { RateHz = 30 };
            Damage = new UiSurface(Device, "damage", 128,  64) { RateHz = -1, NeedsRedraw = false };

            Hud.Draw    = c => c.Cmd.BeginRendering(Hud.Target,    null, 0.10f, 0.10f, 0.15f, 1f).EndRendering();
            Radar.Draw  = c => c.Cmd.BeginRendering(Radar.Target,  null, 0.20f, 0.30f, 0.20f, 1f).EndRendering();
            Damage.Draw = c => c.Cmd.BeginRendering(Damage.Target, null, 0.40f, 0.10f, 0.10f, 1f).EndRendering();

            AddSurface(Hud); AddSurface(Radar); AddSurface(Damage);
        }

        // 起動時から周期的に WASD + Space を仮想入力して Input panel が動いて見えるようにする。
        // 3 秒周期で W → D → S → A → (all off, Space) を切替える。
        private KeyCode _lastKey = KeyCode.None;
        private int _phaseIdx = -1;
        protected override void OnEarlyUpdate(EarlyUpdateContext ctx)
        {
            int p = (int)(ctx.Time.TotalSeconds / 0.6) % 5;
            if (p == _phaseIdx) return;
            _phaseIdx = p;
            if (_lastKey != KeyCode.None) _fake.ReleaseKey(_lastKey);
            _lastKey = p switch
            {
                0 => KeyCode.W, 1 => KeyCode.D, 2 => KeyCode.S, 3 => KeyCode.A,
                _ => KeyCode.Space,
            };
            _fake.PressKey(_lastKey);
        }

        // 各 UiHost の canvas を毎 30 フレームに 1 回 render + readback して DevTools に emit。
        // (RG-Render 前に走らせるため PostRender ではなく PreRender で。ただし主 3D Render とは別 command。)
        private int _uiFrameCounter = 0;
        protected override void OnPreRender(PreRenderContext ctx)
        {
            if (++_uiFrameCounter < 30) return;
            _uiFrameCounter = 0;
            EmitUiPreviewFrames(Device);
        }
    }
}
