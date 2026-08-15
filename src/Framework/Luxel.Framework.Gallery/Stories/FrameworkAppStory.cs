using Luxel.Framework.Game;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.RenderSystem;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **Framework アプリの Storybook 実行** (最小例)。LuxelHostBuilder で組み立てた実アプリ
/// (DI + GameLoop + IGameScene) をストーリーの中で動かす。共通機構は <see cref="StoryAppView{TScene}"/>
/// (GPU はホスト借用、ペーシング/提示/入力 = Storybook 専用 Platform) を参照。
/// 本格的なゲームの例は Game/Breakout (BreakoutStory.cs)。
/// </summary>
[StoryMeta("Apps/Framework")]
public static class FrameworkAppStories
{
    [Story]
    public static Widget App(StoryContext ctx)
        => ctx.Snap(VStack(10)[
            new StoryAppView<StoryAppScene>(StoryAppScene.W, StoryAppScene.H, (s, bctx) =>
            {
                s.AddSingleton(bctx.Font);                  // アプリ UI 用フォント (ホスト共有)
                s.AddSingleton<Action<string>>(ctx.Log);    // ストーリーの Log パネルへ
            }),
            Muted("LuxelHostBuilder + GameLoop + IGameScene (real app) / GPU: host, Platform: Storybook")
        ]);

    /// <summary>デモアプリの Scene。GPU 資源 (GpuDeviceRasterizer2D/RetainedCanvas/framebuffer) は
    /// **最初のフレーム内で遅延生成** — フレームは埋め込みホストのスレッドで走るため、
    /// 起動スレッド (BackgroundService) からホストの GPU に触らない。</summary>
    public sealed class StoryAppScene : IGameScene, IStoryApp
    {
        public const uint W = 480, H = 300;

        private readonly GpuDevice _device;
        private readonly VectorFont _font;
        private readonly Action<string> _log;
        private readonly IRenderFeature _renderFeature;
        private GpuDeviceRasterizer2D? _raster;
        private RetainedCanvas? _canvas;
        private IRasterScene2D? _rasterScene;
        private UiHost? _ui;
        private GpuBuffer? _fb;
        private long _version, _seen;   // fb を描き直すたび進む → 表示側が Touch で再合成
        private readonly Signal<int> _count = new(0);

        public StoryAppScene(GpuDevice device, VectorFont font, Action<string> log)
        {
            _device = device;
            _font = font;
            _log = log;
            _renderFeature = new StoryAppRenderFeature(this);
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

        public ValueTask LoadAsync(GameSceneContext context, CancellationToken token)
            => ValueTask.CompletedTask;

        public void ConfigureRendering(
            RenderFeatureSetCatalog featureSets,
            RenderFeatureAssignmentBuilder assignments)
            => assignments.Register(RenderFeatureSets.RenderOutput, _renderFeature);

        public void FixedUpdate(in FixedUpdateContext context) { }

        public void Update(in UpdateContext context)
        {
            if (_ui is null)
            {
                _raster = new GpuDeviceRasterizer2D(_device);
                _canvas = new RetainedCanvas();
                _rasterScene = _raster.CreateScene(_canvas);
                _fb = _device.Malloc((ulong)W * H * 4, GpuMemoryKind.DeviceLocal);
                _ui = new UiHost(_canvas, _font, W, H, gpuRasterizer: _raster);
                _ui.SetRoot(BuildUi());
            }
            _ui.Tick(context.Time.DeltaSeconds);
        }

        public ValueTask UnloadAsync(GameSceneContext context, CancellationToken token)
        {
            _ui?.Dispose();
            _rasterScene?.Dispose();
            _canvas?.Dispose();
            _raster?.Dispose();
            _fb?.Dispose();
            _ui = null; _rasterScene = null; _canvas = null; _raster = null; _fb = null;
            return ValueTask.CompletedTask;
        }

        private sealed class StoryAppRenderFeature(StoryAppScene scene) : IRenderFeature
        {
            public void AddPasses(RenderFeatureContext context)
            {
                if (scene._canvas is null || scene._rasterScene is null || scene._fb is null) return;
                if (scene._version > 0 && !scene._canvas.HasPendingChanges) return;

                BufferHandle framebuffer = context.Graph.ImportBuffer(scene._fb, "story-app-framebuffer");
                context.Graph.AddPass("RenderStoryApp", PassQueue.Graphics)
                    .Write(framebuffer, ResourceUsage.CopyDest)
                    .Execute(pass =>
                    {
                        scene._rasterScene.Render(
                            Camera2D.Pixels,
                            new GpuRasterTarget2D(pass.Cmd, scene._fb, W, H));
                        scene._version++;
                    });
            }
        }

        private Widget BuildUi()
        {
            Func<string> countText = () => $"count = {_count.Value}";   // BindableString へは Func<string> 経由
            return Card(VStack(10)[
                Heading("Framework App"),
                Muted("IGameScene loop driven by the gallery tick"),
                HStack(8)[
                    Button(_ => { _count.Value++; _log($"count → {_count.Value}"); }, "Count +1"),
                    Text(countText)
                ]
            ]);
        }
    }
}
