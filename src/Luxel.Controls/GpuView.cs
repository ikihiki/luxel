using Luxel.TwoD;
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
    private readonly float _w, _h;
    private readonly IGpuScene _scene;
    private readonly bool _animated;
    private bool _alive;   // シーンが Init 済みで未 Dispose か (スコープ破棄で false へ)
    private float _t;
    private int _idx = -1, _stride;
    private UiNode? _node;

    [UiCtor]
    internal GpuView(float width, float height, IGpuScene scene, bool animated = true)
    {
        _w = MathF.Max(1, width);
        _h = MathF.Max(1, height);
        _scene = scene;
        _animated = animated;
    }

    public override string? DebugDetail => $"{(int)_w}x{(int)_h}{(_animated ? " animated" : "")}";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(_w, _h));

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _w;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _node = CreateRoot(ctx, parent, worldOrigin);

        // シーンの寿命は realize スコープ毎のガードで管理する: スコープ破棄 (ストーリー切替の
        // SetRoot / リサイズの再実体化) で Dispose し、再実体化されたら Init し直す —
        // SurfaceView.SetContent はリサイズ時に旧ルートを一度再実体化するため、once 所有だと
        // 破棄済みシーンを Render してしまう。
        if (!_alive)
        {
            _scene.Init(ctx.Canvas.Rasterizer.Device, (int)_w, (int)_h);
            _idx = -1;   // バッファは作り直されている — Content を必ず貼り直す
            _alive = true;
        }
        ctx.Own(new SceneGuard(this));

        Apply(_scene.Render(_t));
        if (_animated)
            ctx.AddAnimation(dt =>
            {
                _t += dt;
                Apply(_scene.Render(_t));
                return false;
            });

        void Apply((int BindlessIndex, int StridePixels) r)
        {
            if (r.BindlessIndex != _idx || r.StridePixels != _stride)
            {
                (_idx, _stride) = r;
                _node!.Content = new Scene2D().ImageRect(
                    (uint)_idx, (uint)_stride, (uint)_w, (uint)_h, 0, 0, Size.Width, Size.Height);
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
            view._scene.Dispose();
        }
    }
}
