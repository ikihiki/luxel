using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 数値系列の小型グラフ (折れ線 or 棒)。値は <see cref="SetValues"/> で**実体化後も差し替え可能** —
/// グラフノードの Content だけ差し替えて Invalidate する (ListView と同じ流儀。fps/メモリ等の履歴向け)。
/// SetValues 毎に Effect は作らない — 色は Realize 時の単一 Effect が流す (ノード色がシーン色を上書きする
/// 網膜モードの性質を利用し、シーンは白で描く)。
/// </summary>
[UiComponent]
public sealed partial class Sparkline : Widget
{
    /// <summary>グラフ幅 (px、最小 8)。</summary>
    [UiParam] private readonly Bindable<float> _width = new();
    /// <summary>グラフ高 (px、最小 8)。</summary>
    [UiParam] private readonly Bindable<float> _height = new();

    private float[] _values = [];
    private float? _min, _max;   // null = データから自動レンジ

    /// <summary>折れ線ではなく棒グラフで描く。</summary>
    [UiParam] private readonly Bindable<bool> _bars = new();

    /// <summary>線/棒の色。未設定 → テーマ Primary。</summary>
    [UiParam] private readonly Bindable<uint> _lineColor = new();
    /// <summary>下地の色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] private readonly Bindable<uint> _background = new();

    // 旧 ctor のクランプは読み出し側で適用
    private float W => MathF.Max(8, Width.Get());
    private float H => MathF.Max(8, Height.Get());

    public override string? DebugDetail => $"{_values.Length} 点{(Bars.Get() ? " (棒)" : "")}";

    // 実体化状態
    private UiBuildContext? _ctx;
    private UiNode? _bg, _fill, _line;

    /// <summary>系列を差し替える (実体化後も可)。min/max 省略時はデータから自動レンジ。</summary>
    public void SetValues(ReadOnlySpan<float> values, float? min = null, float? max = null)
    {
        _values = values.ToArray();
        _min = min;
        _max = max;
        if (_ctx is null) return;   // 実体化前 — Realize が反映する
        RebuildGraph();   // Content 差し替え = canvas の in-place 部分更新 (点数不変なら再構築なし)
    }

    private float _wPx;   // PerformLayout で解決 (% / em / vw 対応)

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _wPx = ResolveW(c, ctx, W);
        Size = c.Constrain(new Size(_wPx, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, W);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        float w = _wPx;
        _bg = ctx.Canvas.AddChild(node);
        var bs = new Scene2D();
        bs.FillRoundedRect(Color2D.White, 0, 0, w, H, 3);
        _bg.Content = bs;

        _fill = ctx.Canvas.AddChild(node);
        _fill.Z = 1;
        _line = ctx.Canvas.AddChild(node);
        _line.Z = 2;

        ctx.Effect(() =>
        {
            _bg!.Color = Background.Or(ctx.Theme.Value.SurfaceAlt);
            uint c = LineColor.Or(ctx.Theme.Value.Primary);
            _line!.Color = c;
            _fill!.Color = (c & 0x00FFFFFF) | 0x40000000;   // 面は同色 25% 透過
        });

        RebuildGraph();
    }

    /// <summary>グラフ形状を作り直す (色は Effect 任せ — シーンは白)。</summary>
    private void RebuildGraph()
    {
        if (_fill is null || _line is null) return;
        float w = _wPx, h = H;
        int n = _values.Length;

        float lo = _min ?? (n > 0 ? _values.Min() : 0);
        float hi = _max ?? (n > 0 ? _values.Max() : 1);
        if (hi - lo < 1e-6f) { hi = lo + 0.5f; lo -= 0.5f; }   // 平坦な系列は中央に

        const float padY = 3f;
        float Y(float v) => h - padY - (v - lo) / (hi - lo) * (h - padY * 2);

        var fill = new Scene2D();
        var line = new Scene2D();
        if (n >= 1 && Bars.Get())
        {
            float bw = MathF.Max(1, w / n - 1);
            for (int i = 0; i < n; i++)
            {
                float x = n > 1 ? i * (w - bw) / (n - 1) : 0;
                float y = Y(_values[i]);
                fill.FillRect(Color2D.White, x, y, bw, h - padY - y);
            }
        }
        else if (n >= 2)
        {
            var pts = new System.Numerics.Vector2[n];
            for (int i = 0; i < n; i++)
                pts[i] = new System.Numerics.Vector2(i * w / (n - 1), Y(_values[i]));
            line.StrokePolyline(Color2D.White, 1.5f, pts);

            fill.BeginFill(Color2D.White).MoveTo(pts[0].X, h - padY);
            foreach (var p in pts) fill.LineTo(p.X, p.Y);
            fill.LineTo(pts[^1].X, h - padY).Close().End();
        }
        _fill.Content = fill;
        _line.Content = line;
    }
}
