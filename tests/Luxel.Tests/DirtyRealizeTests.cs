using Luxel.Graphics.TwoD;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>EX-M2c: dirty 伝播 — MarkNeedsRealize の集積・ParentWidget 経路・破棄判定。
/// 境界判定 (サイズ不変→自分 / 変化→親の OnChildNeedsRealize) は UiHost.FlushRealize が担い、
/// GPU (RetainedCanvas) が要るため実窓 E2E で検証する。</summary>
public class DirtyRealizeTests
{
    private static UiBuildContext Ctx() => new() { Canvas = null!, Font = null! };
    private static UiNode Node() => new(null!);

    /// <summary>ノードを作らないテスト用コンテナ (Realize テンプレートの検証のみ)。</summary>
    private sealed class Box : Widget
    {
        public readonly List<Box> Kids = new();
        protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(10, 10));
        protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        {
            foreach (Box k in Kids) k.Realize(ctx, parent, worldOrigin);
        }
    }

    [Fact]
    public void Realize_RecordsParentWidget_ForNestedChildren()
    {
        var root = new Box();
        var mid = new Box();
        var leaf = new Box();
        root.Kids.Add(mid);
        mid.Kids.Add(leaf);

        root.Realize(Ctx(), Node(), new Point(0, 0));

        Assert.Null(root.ParentWidget);              // ルートは親なし
        Assert.Same(root, mid.ParentWidget);         // 入れ子はテンプレートが自動記録
        Assert.Same(mid, leaf.ParentWidget);
    }

    [Fact]
    public void Realize_TopLevel_KeepsExplicitParentWidget()
    {
        // 手動ホスト (エディタの embed をイベントから実体化する等): 明示設定はトップレベル Realize で消えない
        var host = new Box();
        var embed = new Box { ParentWidget = host };

        embed.Realize(Ctx(), Node(), new Point(0, 0));

        Assert.Same(host, embed.ParentWidget);
    }

    [Fact]
    public void MarkNeedsRealize_CollectsIntoBuildContext_AndDedupes()
    {
        var ctx = Ctx();
        var w = new Box();
        w.Realize(ctx, Node(), new Point(0, 0));

        w.MarkNeedsRealize();
        w.MarkNeedsRealize();   // 1 フレーム内の多重変更は 1 回に畳まれる

        Assert.Single(ctx.Dirty);
        Assert.Same(w, ctx.Dirty[0]);
    }

    [Fact]
    public void MarkNeedsRealize_BeforeRealize_IsNoop()
    {
        var w = new Box();
        w.MarkNeedsRealize();   // 未実体化 — 例外なく無視される
        Assert.Null(w.Scope);
    }

    [Fact]
    public void ScopeIsDisposed_TracksParentDisposal()
    {
        var ctx = Ctx();
        var root = new Box();
        var kid = new Box();
        root.Kids.Add(kid);
        root.Realize(ctx, Node(), new Point(0, 0));

        Assert.False(kid.Scope!.IsDisposed);
        ctx.Root.Dispose();   // SetRoot 相当 — 配下スコープも破棄扱い (dirty 処理のスキップ判定)
        Assert.True(kid.Scope.IsDisposed);
    }
}
