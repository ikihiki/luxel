using System.Globalization;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// <see cref="Length"/> の入力コンボ: 数値 <see cref="TextField"/> + 単位 <see cref="Select"/> (px/%/em/vw/vh)
/// を 1 つのコントロールとして束ねる (Gallery の Width/Height 等 "length" prop エディタ)。
/// 双方向同期は ColorPicker と同じ「_sync フラグ + Signal の等値スキップ」で循環収束させる。
/// 空欄の間は値を書かない (最後の有効値を保持)。
/// </summary>
[UiComponent]
public sealed partial class LengthField : Widget
{
    private static readonly string[] Units = ["px", "%", "em", "vw", "vh"];

    private readonly Signal<Length> _value;
    private readonly Signal<string> _num = new("");
    private readonly Signal<int> _unit = new(0);
    private readonly TextField _tf;
    private readonly Select _sel;
    private bool _sync;
    private const float Gap = 0;   // 隙間なしで接合 (接合面は直角、境界は 1px 区切り線)

    [UiCtor]
    internal LengthField(Signal<Length> value)
    {
        _value = value;
        ReadValue(value.Peek());
        _tf = Kit.TextField(_num, width: 72);
        _tf.Pattern = @"^-?[0-9]*\.?[0-9]*$";
        _tf.BgCorners = RectCorners.Left;    // 右側 (接合面) は直角
        _sel = Kit.Select(Units, _unit);
        _sel.BgCorners = RectCorners.Right;  // 左側 (接合面) は直角
    }

    /// <summary>value → 表示 (数値文字列 + 単位 index)。</summary>
    private void ReadValue(Length v)
    {
        _num.Value = v.IsSet ? v.Value.ToString(CultureInfo.InvariantCulture) : "";
        _unit.Value = v.Unit switch
        {
            LengthUnit.Percent => 1,
            LengthUnit.Em => 2,
            LengthUnit.Vw => 3,
            LengthUnit.Vh => 4,
            _ => 0,
        };
    }

    private static LengthUnit UnitAt(int i) => i switch
    {
        1 => LengthUnit.Percent,
        2 => LengthUnit.Em,
        3 => LengthUnit.Vw,
        4 => LengthUnit.Vh,
        _ => LengthUnit.Px,
    };

    public override string DebugType => "LengthField";
    public override string? DebugDetail => _value.Peek().ToString();
    public override IEnumerable<Widget> DebugChildren() => [_tf, _sel];

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        Size ts = _tf.Layout(Constraints.LooseW(c.MaxW, c.MaxH), ctx, parentUsesSize: true);
        Size ss = _sel.Layout(Constraints.LooseW(MathF.Max(0, c.MaxW - ts.Width - Gap), c.MaxH), ctx, parentUsesSize: true);
        float h = MathF.Max(ts.Height, ss.Height);
        _tf.Offset = new Point(0, (h - ts.Height) / 2);
        _sel.Offset = new Point(ts.Width + Gap, (h - ss.Height) / 2);
        Size = c.Constrain(new Size(ts.Width + Gap + ss.Width, h));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => _tf.MaxIntrinsicWidth(height, ctx) + Gap + _sel.MaxIntrinsicWidth(height, ctx);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        _tf.Realize(ctx, node, world);
        _sel.Realize(ctx, node, world);

        // 接合部の 1px 区切り線 (数値と単位の境界を示す)
        UiNode div = ctx.Canvas.AddChild(node);
        div.Z = 2;
        var ds = new Scene2D();
        ds.FillRect(Color2D.White, _sel.Offset.X, 3, 1, Size.Height - 6);
        div.Content = ds;
        ctx.Effect(() => div.Color = ctx.Theme.Value.BorderColor);

        // 双方向同期 (循環は _sync + 等値スキップで収束)
        ctx.Effect(() =>
        {
            Length v = _value.Value;
            if (_sync) return;
            _sync = true;
            ReadValue(v);
            _sync = false;
        });
        ctx.Effect(() =>
        {
            string n = _num.Value;
            int u = _unit.Value;
            if (_sync) return;
            if (!float.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return;   // 空/途中入力は保留
            _sync = true;
            _value.Value = new Length(f, UnitAt(u));
            _sync = false;
        });
    }
}
