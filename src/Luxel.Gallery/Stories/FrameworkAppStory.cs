using Luxel.Framework;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **Framework アプリの Storybook 実行** (最小例)。LuxelHostBuilder で組み立てた実アプリ
/// (DI + GameLoop + GameScene) をストーリーの中で動かす。共通機構は <see cref="StoryAppView{TScene}"/>
/// (GPU はホスト借用、ペーシング/提示/入力 = Storybook 専用 Platform) を参照。
/// 本格的なゲームの例は Game/Breakout (BreakoutStory.cs)。
/// </summary>
public static class FrameworkAppStories
{
    [Story("Apps/Framework/App", Width = 520, Height = 400, Order = 146)]
    public static Widget App(StoryContext ctx)
        => ctx.Snap(VStack(10)[
            new StoryAppView<StoryAppScene>(StoryAppScene.W, StoryAppScene.H, (s, bctx) =>
            {
                s.AddSingleton(bctx.Font);                  // アプリ UI 用フォント (ホスト共有)
                s.AddSingleton<Action<string>>(ctx.Log);    // ストーリーの Log パネルへ
            }),
            Muted("LuxelHostBuilder + GameLoop + GameScene (real app) / GPU: host, Platform: Storybook")
        ]);

    /// <summary>デモアプリの Scene。GPU 資源 (GpuDeviceRasterizer2D/RetainedCanvas/framebuffer) は
    /// **最初のフレーム内で遅延生成** — フレームは埋め込みホストのスレッドで走るため、
    /// 起動スレッド (BackgroundService) からホストの GPU に触らない。</summary>
    public sealed class StoryAppScene : GameScene, IStoryApp
    {
        public const uint W = 480, H = 300;

        private readonly VectorFont _font;
        private readonly Action<string> _log;
        private GpuDeviceRasterizer2D? _raster;
        private RetainedCanvas? _canvas;
        private IRasterScene2D? _rasterScene;
        private UiHost? _ui;
        private GpuBuffer? _fb;
        private long _version, _seen;   // fb を描き直すたび進む → 表示側が Touch で再合成
        private readonly Signal<int> _count = new(0);

        public StoryAppScene(SceneLoopServices loop, VectorFont font, Action<string> log) : base(loop)
        {
            _font = font;
            _log = log;
        }

        public uint FbIndex => _fb?.BindlessIndex ?? 0;
        public bool FbReady => _version > 0;
        public bool ConsumeRendered()
        {
            if (_seen == _version) return false;
            _seen = _version;
            return true;
        }

        // Storybook Platform の入力転送先 (ローカル座標 = アプリのクライアント座標)
        public void PointerMove(float x, float y) => _ui?.PointerMove(x, y);
        public void PointerDown(float x, float y) => _ui?.PointerDown(x, y);
        public void PointerUp(float x, float y) => _ui?.PointerUp(x, y);
        public void Wheel(float x, float y, float d) => _ui?.Wheel(x, y, d);

        protected override void OnUpdate(UpdateContext ctx)
        {
            if (_ui is null)
            {
                _raster = new GpuDeviceRasterizer2D(Device);
                _canvas = new RetainedCanvas();
                _rasterScene = _raster.CreateScene(_canvas);
                _fb = Device.Malloc((ulong)W * H * 4, GpuMemoryKind.DeviceLocal);
                _ui = new UiHost(_canvas, _font, W, H, gpuRasterizer: _raster);
                _ui.SetRoot(BuildUi());
            }
            _ui.Tick(ctx.Time.DeltaSeconds);
        }

        protected override void OnRender(RenderContext ctx)
        {
            if (_canvas is null || _rasterScene is null || _fb is null) return;
            if (_version > 0 && !_canvas.HasPendingChanges) return;   // 変化なし = 再描画不要
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, _fb, W, H));
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
            _version++;
        }

        public override Task OnUnloadAsync()
        {
            _ui?.Dispose();
            _rasterScene?.Dispose();
            _canvas?.Dispose();
            _raster?.Dispose();
            _fb?.Dispose();
            _ui = null; _rasterScene = null; _canvas = null; _raster = null; _fb = null;
            return Task.CompletedTask;
        }

        private Widget BuildUi()
        {
            Func<string> countText = () => $"count = {_count.Value}";   // BindableString へは Func<string> 経由
            return Card(VStack(10)[
                Heading("Framework App"),
                Muted("GameScene loop driven by the gallery tick"),
                HStack(8)[
                    Button(_ => { _count.Value++; _log($"count → {_count.Value}"); }, "Count +1"),
                    Text(countText)
                ]
            ]);
        }
    }
}
