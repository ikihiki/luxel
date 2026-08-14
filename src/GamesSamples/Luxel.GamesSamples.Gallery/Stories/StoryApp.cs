using Luxel.Framework.Game;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Luxel.Gallery.Stories;

/// <summary>
/// Storybook 専用 Platform (共通部)。実アプリ (LuxelHostBuilder + GameLoop + GameScene) を
/// ストーリー内で動かすための 3 点セット:
/// - ペーシング: <see cref="StoryFramePacer"/> — ギャラリーの描画ティックに同期。TCS の**同期継続**で
///   アプリの 1 フレーム (GPU Submit 込み) がギャラリースレッド上で実行される (GPU キュー共有が安全)
/// - 提示: アプリが framebuffer (GpuBuffer) に描き、<see cref="StoryAppView{TScene}"/> が
///   image ノード (<see cref="Scene2D.ImageRect"/>) で合成 (SurfaceView と同型)
/// - 入力: ストーリー領域の pointer/ホイールをアプリへローカル座標のまま転送
/// GPU はホストのものを借用 (<see cref="LuxelHostBuilder.UseGpuDevice"/>)。
/// </summary>
public interface IStoryApp
{
    /// <summary>アプリの framebuffer (RGBA8 GpuBuffer) の bindless index。</summary>
    uint FbIndex { get; }
    /// <summary>1 回でも描画されたか (image ノードを張ってよいか)。</summary>
    bool FbReady { get; }
    /// <summary>前回の問い合わせ以降に fb が描き直されたか (表示側の Touch 判定 — 消費式)。</summary>
    bool ConsumeRendered();

    void PointerMove(float x, float y);
    void PointerDown(float x, float y);
    void PointerUp(float x, float y);
    void Wheel(float x, float y, float delta);
}

/// <summary>ギャラリーの描画ティックにアプリのフレームを同期させる waiter
/// (<see cref="LuxelHostBuilder.UseFrameWaiter"/> に渡す)。</summary>
internal sealed class StoryFramePacer
{
    private TaskCompletionSource? _tcs;
    private bool _cancelHooked;

    public Task WaitAsync(CancellationToken token)
    {
        if (!_cancelHooked)
        {
            _cancelHooked = true;   // 同一 token が毎フレーム来る — 登録は 1 回 (蓄積リーク回避)
            token.Register(() => Interlocked.Exchange(ref _tcs, null)?.TrySetCanceled(token));
        }
        var tcs = new TaskCompletionSource();
        Volatile.Write(ref _tcs, tcs);
        if (token.IsCancellationRequested) tcs.TrySetCanceled(token);
        return tcs.Task;
    }

    /// <summary>ギャラリーの 1 ティック = アプリの 1 フレーム (この呼び出しの中で同期実行)。</summary>
    public void Tick() => Interlocked.Exchange(ref _tcs, null)?.TrySetResult();
}

internal sealed class StorySceneBootstrap(IGameScene scene) : IGameSceneBootstrap
{
    public ValueTask BootstrapAsync(IGameSceneSystem scenes, CancellationToken token)
    {
        scenes.Enqueue(new GameSceneCommand.Push(GameSceneId.New(), scene));
        return ValueTask.CompletedTask;
    }
}

/// <summary>アプリを所有し、framebuffer を合成表示して入力を転送する widget。
/// ストーリー破棄で <see cref="Dispose"/> → host.StopAsync → scene unload が GPU 資源を返す。
/// TScene は GPU 資源を**最初のフレーム内で遅延生成**すること (起動スレッドからホスト GPU に触らない)。</summary>
internal sealed class StoryAppView<TScene> : Widget, IDisposable
    where TScene : class, IGameScene, IStoryApp
{
    private readonly float _w, _h;
    private readonly Action<IServiceCollection, UiBuildContext>? _services;
    private IHost? _host;
    private TScene? _scene;
    private StoryFramePacer? _pacer;
    private bool _imageSet;

    /// <summary><paramref name="services"/> でアプリの DI に追加登録する (フォント/ログ窓口等)。</summary>
    public StoryAppView(float width, float height, Action<IServiceCollection, UiBuildContext>? services = null)
    {
        _w = width;
        _h = height;
        _services = services;
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(_w, _h));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _w;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        if (_host is null)
        {
            _pacer = new StoryFramePacer();
            GpuDevice device = ctx.RequireGpuRasterizer().Device;   // GPU はホストのものを借用
            _host = LuxelHostBuilder.Create()
                .UseGpuDevice(device)
                .UseFrameWaiter(_pacer.WaitAsync)
                .ConfigureServices(s =>
                {
                    s.AddSingleton<TScene>();   // scene system と表示側が同一インスタンスへアクセスする
                    s.AddSingleton<IGameSceneBootstrap>(sp => new StorySceneBootstrap(sp.GetRequiredService<TScene>()));
                    _services?.Invoke(s, ctx);
                })
                .UseStandardCadences()
                .AddGameLoop<GameLoop>()
                .Build();
            _scene = _host.Services.GetRequiredService<TScene>();
            _host.Start();   // GameLoop は最初のフレームを pacer 待ちで開始 — GPU には触らない
            ctx.Own(this);
        }

        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, Size.Width, Size.Height);
        _imageSet = false;

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            onClickPos: e => { _scene!.PointerDown(e.X, e.Y); _scene.PointerUp(e.X, e.Y); },   // プログラム的 Click (E2E/テスト)
            onMovePos: e => _scene!.PointerMove(e.X, e.Y),
            onDragStart: e => _scene!.PointerDown(e.X, e.Y),
            onDrag: e => _scene!.PointerMove(e.X, e.Y),
            onDragEnd: e => _scene!.PointerUp(e.X, e.Y));
        ctx.AddScroll(node, new Rect(0, 0, Size.Width, Size.Height),
            onScrollPos: (lx, ly, d) => _scene!.Wheel(lx, ly, d));

        UiNode n = node;
        ctx.AddAnimation(dt =>
        {
            _pacer!.Tick();   // ← ここでアプリの 1 フレームがこのスレッド上で同期実行される
            if (!_imageSet && _scene!.FbReady)
            {
                n.Content = new Scene2D().ImageRect(_scene.FbIndex, (uint)_w, (uint)_w, (uint)_h, 0, 0, _w, _h);
                _imageSet = true;
            }
            if (_scene!.ConsumeRendered()) n.Touch();   // fb が変わった = image ノードを再合成
            return false;   // 常駐
        });
    }

    public void Dispose()
    {
        try { _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch { /* 停止失敗はアプリ側の問題 — ホストの破棄は続行 */ }
        _host?.Dispose();
        _host = null;
    }
}
