using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// 下部ステータス帯 (ADR-0014)。左右のセグメント (任意 widget — ライブ値は
/// <c>Text(color/文言を signal 束縛)</c> 等で widget 側が配線する) を 1 行に並べる。
/// 地は SurfaceAlt + 上辺ヘアライン。
/// </summary>
[UiComponent]
public sealed partial class StatusBar : CompositeControl
{
    public const float BarH = 26f;

    /// <summary>左寄せセグメント。</summary>
    [UiParam] private readonly Bindable<Widget[]> _left = new([]);
    /// <summary>右寄せセグメント。</summary>
    [UiParam] private readonly Bindable<Widget[]> _right = new([]);

    protected override Widget Build()
    {
        Widget leftRow = HStack(12)[Left.Get()];
        leftRow.GridColumn(0);
        leftRow.VAlign.SetBase(Align.Center);
        Widget rightRow = HStack(12)[Right.Get()];
        rightRow.GridColumn(1);
        rightRow.VAlign.SetBase(Align.Center);

        Grid row = Grid(columns: [GridLength.Star(), GridLength.Auto], rows: [GridLength.Px(BarH - 1)])[leftRow, rightRow];
        row.HAlign.SetBase(Align.Stretch);
        Widget hairline = Box(background: (Func<uint>)(() => UiTheme.T.BorderColor), height: 1, hAlign: Align.Stretch);

        return Border(background: (Func<uint>)(() => UiTheme.T.SurfaceAlt),
                      padding: new Thickness(10, 0, 10, 0), hAlign: Align.Stretch)[
            VStack()[hairline, row]];
    }
}
