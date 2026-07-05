using Luxel.TwoD;

using Luxel.UI;

namespace Luxel.Controls;

/// <summary>子を中央(または指定整列)に配置するコンテナ。与えられた領域いっぱいに広がる。</summary>
[UiComponent]
public sealed partial class Center : Widget
{
    internal Widget? Child;

    /// <summary>生成 ctor からの初期化 (旧 ctor 本体): 自身は与えられた領域いっぱいに広がる。</summary>
    partial void OnConstruct() { HAlign.SetBase(Align.Stretch); VAlign.SetBase(Align.Stretch); }

    private void AddChild(Widget child) => Child = child;

    /// <summary>子要素の宣言: <c>Center()[ inner ]</c>。</summary>
    public Center this[Widget child]
    {
        get { AddChild(child); return this; }
    }

    public override IEnumerable<Widget> DebugChildren() => Child is null ? [] : [Child];

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        // 制約が無限の軸 (docs 埋め込み等の縦積み文脈) は「領域いっぱい」が定義できない —
        // 子の自然サイズへフォールバックする (0 に潰すと子が背景ごと消える)。
        bool infW = float.IsInfinity(c.MaxW), infH = float.IsInfinity(c.MaxH);
        if (Child is null)
        {
            Size = new Size(infW ? 0 : c.MaxW, infH ? 0 : c.MaxH);
            return;
        }
        if (infW || infH)
        {
            Size natural = Child.Layout(new Constraints(0, c.MaxW, 0, c.MaxH), ctx, parentUsesSize: true);
            Size = new Size(infW ? natural.Width : c.MaxW, infH ? natural.Height : c.MaxH);
        }
        else
            Size = new Size(c.MaxW, c.MaxH);
        LayoutHelper.PlaceInBox(Child, ctx, 0, 0, Size.Width, Size.Height, Align.Center, Align.Center);   // 強制中央
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        Child?.Realize(ctx, node, world);
    }
}

/// <summary>固定サイズの空き (区切り/余白)。描画なし。サイズは共通引数 (width/height)。</summary>
[UiComponent]
public sealed partial class Spacer : Widget
{
    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(ResolveW(c, ctx, 0), ResolveH(c, ctx, 0)));
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin) { /* 描画なし */ }
}
