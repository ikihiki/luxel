using Luxel.Controls;

namespace Luxel.Gallery.Stories;

/// <summary>
/// ストーリー用 <see cref="IGpuScene"/> の共通下回り — RGBA8 レンダーターゲットと
/// 読み出しバッファ (GpuView がゼロコピー合成する bindless バッファ) の確保/破棄、
/// ワンショット描画の管理。派生は <see cref="OnInit"/> でリソースを <see cref="Track"/> し、
/// <see cref="OnRender"/> でコマンドを積む。
///
/// 規約 (snap の決定性と再実体化):
/// - ctor は引数保持のみ — GPU/リソース確保は全部 Init (DocsIndex が起動時に全ストーリーを
///   Build するため + SceneGuard による Dispose 後の再 Init に耐えるため)
/// - time は Render 引数の累積秒のみ (wall-clock 禁止)、周期アニメは <c>t % dur</c>
/// - D3D12 の CopyTextureToBuffer は行 256B 整列 — ターゲット幅は 64 の倍数にする
/// </summary>
internal abstract class GpuSceneBase : IGpuScene
{
    protected GpuDevice Device { get; private set; } = null!;
    protected GpuTexture Target { get; private set; } = null!;
    protected GpuBuffer OutBuffer { get; private set; } = null!;
    protected uint W { get; private set; }
    protected uint H { get; private set; }

    private readonly List<IDisposable> _resources = [];
    private bool _rendered;

    /// <summary>Dispose で逆順破棄するリソースを登録する。</summary>
    protected T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    /// <summary>false = カラーターゲット不要 (compute だけで OutBuffer へ書くシーン)。</summary>
    protected virtual bool NeedsColorTarget => true;

    public void Init(GpuDevice device, int width, int height)
    {
        Device = device;
        W = (uint)width;
        H = (uint)height;
        _rendered = false;   // 再 Init (Dispose 後の再実体化) ではバッファが新しい — 描き直す
        if (NeedsColorTarget) Target = Track(device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm));
        OutBuffer = Track(device.Malloc(W * H * 4, GpuMemoryKind.HostMapped));
        OnInit();
    }

    /// <summary>Target/OutBuffer 以外のリソース確保 (Track で登録すること)。</summary>
    protected abstract void OnInit();

    /// <summary>フレーム描画 (コマンド記録 + SubmitAndWait)。静的シーンは初回のみ呼ばれる。</summary>
    protected abstract void OnRender(float time);

    /// <summary>true = 毎フレーム OnRender (アニメ/knob 反映 — GpuView は animated: true で使う)。
    /// false = 初回のみ描画し、同じバッファを返し続ける。</summary>
    protected virtual bool RenderEveryFrame => false;

    public (int BindlessIndex, int StridePixels) Render(float time)
    {
        if (RenderEveryFrame || !_rendered)
        {
            _rendered = true;
            OnRender(time);
        }
        return ((int)OutBuffer.BindlessIndex, (int)W);
    }

    public void Dispose()
    {
        for (int i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
        _resources.Clear();
    }
}
