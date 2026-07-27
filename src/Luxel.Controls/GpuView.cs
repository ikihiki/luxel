using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 自前の GPU レンダリング (3D 等) を offscreen で行うシーン。結果は RGBA8 の bindless バッファで、
/// <see cref="GpuView"/> が image プリミティブとして UI キャンバスへ**ゼロコピー合成**する。
/// <list type="bullet">
/// <item><see cref="Init"/>: PSO/バッファ/レンダーターゲットの確保。**Dispose 後に再度呼ばれることが
///   ある** (リサイズ等の再実体化) — 全リソースを作り直せる実装にすること。リソースが要るシーンは
///   ctor で <c>ctx.Resources</c> を受け取り、ここで <c>Ready.Wait</c> してよい (初回ロードの
///   publish は Pump 不要)。</item>
/// <item><see cref="Render"/>: 1 フレーム描画して結果バッファの bindless index を返す。
///   <paramref name="time"/> は累積秒 (Tick 由来) — **wall-clock を混ぜない** (snap の決定性)。</item>
/// <item>D3D12 の CopyTextureToBuffer は行 256B 整列 — ターゲット幅は 64 の倍数を推奨。</item>
/// </list>
/// </summary>
public interface IGpuScene : IDisposable
{
    void Init(GpuDevice device, int width, int height);

    /// <summary>1 フレーム描画して (結果バッファの bindless index, 行ピッチ px) を返す。</summary>
    (int BindlessIndex, int StridePixels) Render(float time);
}

/// <summary>
/// <see cref="IGpuScene"/> の描画結果を表示する widget — 3D/GPU 描画結果をストーリー/docs に載せる器。
/// <code>
/// GpuView(320, 240, new TriangleScene())                    // 毎フレーム Render (アニメ)
/// GpuView(320, 240, new TexturedScene(ctx.Resources), animated: false)   // 1 回だけ Render (静的)
/// </code>
/// 結果は image プリミティブの 1 ノード (CPU 読み戻しなし) — 同一 device/queue なので submit 順で
/// 同期される。シーンの寿命は realize スコープが所有 (ストーリー切替 = SetRoot 破棄で Dispose)。
/// </summary>
[UiComponent]
public sealed partial class GpuView : Widget
{
    /// <summary>表示幅 (px、最小 1)。ターゲット幅は 64 の倍数を推奨 (D3D12 の行整列)。</summary>
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    /// <summary>表示高さ (px、最小 1)。</summary>
    [UiParam] private readonly Bindable<float> _viewHeight = new();
    /// <summary>描画シーン (寿命は realize スコープが所有 — SetRoot 破棄で Dispose)。</summary>
    [UiParam] private readonly Bindable<IGpuScene> _scene = new();
    /// <summary>true = 毎フレーム Render (アニメ)、false = 1 回だけ (静的)。</summary>
    [UiParam] private readonly Bindable<bool> _animated = true;

    private bool _alive;   // シーンが Init 済みで未 Dispose か (スコープ破棄で false へ)
    private float _t;
    private int _idx = -1, _stride;
    private UiNode? _node;

    private float W1 => MathF.Max(1, ViewWidth.Get());
    private float H1 => MathF.Max(1, ViewHeight.Get());

    public override string? DebugDetail => $"{(int)W1}x{(int)H1}{(Animated.Get() ? " animated" : "")}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(W1, H1));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W1;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        IGpuScene scene = Scene.Get();
        float w = W1, h = H1;
        _node = CreateRoot(ctx, parent, worldOrigin);

        // シーンの寿命は realize スコープ毎のガードで管理する: スコープ破棄 (ストーリー切替の
        // SetRoot / リサイズの再実体化) で Dispose し、再実体化されたら Init し直す —
        // SurfaceView.SetContent はリサイズ時に旧ルートを一度再実体化するため、once 所有だと
        // 破棄済みシーンを Render してしまう。
        if (!_alive)
        {
            scene.Init(ctx.Canvas.Rasterizer.Device, (int)w, (int)h);
            _idx = -1;   // バッファは作り直されている — Content を必ず貼り直す
            _alive = true;
        }
        ctx.Own(new SceneGuard(this));

        Apply(scene.Render(_t));
        if (Animated.Get())
            ctx.AddAnimation(dt =>
            {
                _t += dt;
                Apply(scene.Render(_t));
                return false;
            });

        void Apply((int BindlessIndex, int StridePixels) r)
        {
            if (r.BindlessIndex != _idx || r.StridePixels != _stride)
            {
                (_idx, _stride) = r;
                _node!.Content = new Scene2D().ImageRect(
                    (uint)_idx, (uint)_stride, (uint)w, (uint)h, 0, 0, Size.Width, Size.Height);
            }
            else
                _node!.Touch();   // 同じバッファの中身だけ更新 → 再合成を促す
        }
    }

    /// <summary>realize スコープ破棄 → シーン破棄 (再実体化されたら Init し直す)。</summary>
    private sealed class SceneGuard(GpuView view) : IDisposable
    {
        public void Dispose()
        {
            if (!view._alive) return;   // 二重破棄防止 (リサイズで旧スコープが後から破棄される等)
            view._alive = false;
            view.Scene.Get()?.Dispose();
        }
    }
}
