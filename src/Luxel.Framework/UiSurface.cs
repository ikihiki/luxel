using Luxel.RenderGraph;

namespace Luxel.Framework;

/// <summary>
/// 独立した描画対象 (UI パネル / モニタ / 低解像度 3D レイヤ等)。<see cref="GameScene"/> に
/// <see cref="GameScene.AddSurface"/> で登録すると、per-frame で <see cref="RateHz"/> に従って
/// <see cref="Draw"/> callback が RenderGraph 内の 1 pass として実行される。
///
/// <para><b>rate 制御 (Option A: 時間ベース throttle)</b>:
/// <list type="bullet">
/// <item><c>RateHz &gt; 0</c>: 目標更新頻度。<c>(now - lastRendered) &gt;= 1/RateHz</c> のとき描画</item>
/// <item><c>RateHz == 0</c>: 毎フレーム描画</item>
/// <item><c>RateHz &lt; 0</c>: <see cref="NeedsRedraw"/> が true のときだけ描画</item>
/// </list>
/// </para>
///
/// <para><b>Option B (表示 rate の整数分の 1) は本仕組みの上で実現</b>:
/// 表示が 60Hz なら <c>RateHz = 30</c> で毎 2 フレームに 1 回、<c>RateHz = 15</c> で毎 4 フレームに 1 回、
/// と時間軸で整数分の 1 に自然に落ちる (float 誤差 1e-6 で吸収)。</para>
///
/// <para><b>用途例</b>:
/// <list type="bullet">
/// <item>HUD: <c>RateHz = 60</c>, <see cref="Kind"/> = ScreenSpaceOverlay</item>
/// <item>コクピット計器 (10 Hz 更新で十分): <c>RateHz = 10</c>, Kind = WorldSpaceQuad</item>
/// <item>ダメージパネル (状態変化時のみ): <c>RateHz = -1</c>, dirty flag で駆動</item>
/// <item>3D 低解像度レイヤ (30Hz → upscale): <c>RateHz = 30</c>, Kind = ScreenSpaceOverlay (低解像度 Target)</item>
/// </list>
/// </para>
/// </summary>
public sealed class UiSurface : IDisposable
{
    public string Name { get; }
    public UiSurfaceKind Kind { get; }
    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public GpuTexture Target { get; }

    /// <summary>目標更新頻度 (Hz)。詳細は <see cref="UiSurface"/> のドキュメント参照。</summary>
    public float RateHz { get; set; } = 60f;

    /// <summary>次フレームで RateHz を無視して即時描画したいとき true にする。描画後は自動で false に戻る。</summary>
    public bool NeedsRedraw { get; set; } = true;

    /// <summary>各 rate 判定にヒットしたときに呼ばれる描画 callback。<see cref="Target"/> への描画を行う。</summary>
    public Action<SurfaceDrawContext>? Draw { get; set; }

    /// <summary>直前に描画された時刻 (秒、<see cref="FrameTime.TotalSeconds"/>)。未描画なら -1。</summary>
    public double LastRenderedTime { get; internal set; } = -1;
    /// <summary>累積描画フレーム数 (debug / test 用)。</summary>
    public long RenderedFrames { get; internal set; }

    private bool _disposed;

    public UiSurface(GpuDevice device, string name, uint width, uint height,
        UiSurfaceKind kind = UiSurfaceKind.ScreenSpaceOverlay,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        Name = name;
        Kind = kind;
        Width = width;
        Height = height;
        Format = format;
        Target = device.CreateRenderTarget(width, height, format);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Target.Dispose();
    }

    /// <summary>本 surface を今フレーム描画すべきかを判定 (<see cref="GameScene"/> が呼ぶ)。
    /// 判定は「理想スケジュール上、時刻 <see cref="FrameTime.TotalSeconds"/> までに何回描画されているべきか」を
    /// <c>floor(t * RateHz) + 1</c> で計算し、実描画数 <see cref="RenderedFrames"/> と比較 ─ 表示 rate の整数分の 1
    /// (Option B) が誤差なく落ちる仕組み。</summary>
    internal bool ShouldRender(FrameTime time)
    {
        if (NeedsRedraw) return true;
        if (RateHz < 0) return false;                // dirty-only モード
        if (RateHz == 0) return true;                // 毎フレーム (throttle 無し)
        long targetFrames = (long)(time.TotalSeconds * RateHz) + 1;
        return RenderedFrames < targetFrames;
    }
}

public enum UiSurfaceKind
{
    /// <summary>画面全体に重ねる HUD 等。合成は user (OnRender で ImportTexture して composite pass) が行う。</summary>
    ScreenSpaceOverlay,

    /// <summary>3D 内の quad にテクスチャとして貼るコクピット内モニタ等。material の bindless index に流す。</summary>
    WorldSpaceQuad,

    /// <summary>別カメラ視点で 3D + UI を描いた合成 (mini-map、リアビューミラー等)。</summary>
    CameraStacked,
}

/// <summary>UiSurface の Draw callback に渡される描画コンテキスト。</summary>
public readonly record struct SurfaceDrawContext(
    PassContext Pass,
    TextureHandle TargetHandle,
    UiSurface Surface,
    FrameTime Time)
{
    /// <summary>描画コマンドバッファ (ショートカット)。</summary>
    public GpuCommandBuffer Cmd => Pass.Cmd;
}
