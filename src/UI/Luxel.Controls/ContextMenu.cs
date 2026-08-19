using System.Runtime.CompilerServices;
using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// ポインタ位置に出す軽量コンテキストメニュー (AP-M2)。オーバーレイ機構 (SetRoot 時のみ実体化) と違い、
/// **任意のタイミングで canvas 直下へ実体化する** — 再実体化済みの widget や embed 内エディタからも開ける。
/// 全面ディスミスヒットを先に張り、メニュー行のヒットが後登録 (= 前面勝ち) になる。
/// 項目クリック / 外側クリックで閉じる。UiBuildContext につき同時に 1 個。
/// </summary>
public static class ContextMenu
{
    private sealed class State
    {
        public required Signal<bool> Open;
        public required OverlayEntry Entry;
    }

    private static readonly ConditionalWeakTable<UiBuildContext, State> _open = new();

    /// <summary>メニューを canvas 座標 (x, y) に開く。既に開いていれば閉じてから。</summary>
    public static void Open(UiBuildContext ctx, float x, float y, params (string Label, Action Action)[] items)
    {
        Theme t = ctx.Theme.Peek();
        var rows = new Widget[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            (string label, Action act) = items[i];
            rows[i] = Kit.MenuRow(label, _ => { Close(ctx); act(); }, hAlign: Align.Stretch);
        }
        OpenWidget(ctx, x, y, Kit.Border(background: t.Surface, padding: new Thickness(4))[Kit.VStack()[rows]]);
    }

    /// <summary>任意 widget をメニューとして canvas 座標 (x, y) に開く (MenuBar のドロップダウンや
    /// コマンドパレットの基盤)。閉じは項目側が <see cref="Close"/> を呼ぶ / 外側クリック。
    /// maxW/maxH はレイアウト制約。</summary>
    public static void OpenWidget(UiBuildContext ctx, float x, float y, Widget menu,
                                  float maxW = 320, float maxH = 600)
    {
        Close(ctx);

        // 吸収ホストで包む — 内側の CompositeControl (パレット等) が Rebuild したとき、その場で
        // 再実体化する。ライフサイクル/外側クリック/Escape/ビューポート制約は共通 overlay に委譲する。
        var host = new MenuHost { Menu = menu, HitLayer = 3000 };
        var open = new Signal<bool>(true);
        var entry = new OverlayEntry
        {
            Open = open,
            Content = host,
            Anchor = () => new Rect(x, y, 0, 0),
            Anchored = new AnchoredPlacement
            {
                Side = PopupSide.Below,
                Align = PopupAlign.Start,
                Gap = 0,
                Margin = 4,
                MaxWidth = maxW,
                MaxHeight = maxH,
            },
        };
        ctx.RegisterOverlay(entry);
        _open.Add(ctx, new State { Open = open, Entry = entry });
    }

    /// <summary>メニュー widget のホスト。子 (メニュー内容) の再実体化要求をその場で吸収する —
    /// 高さが変わる絞り込み (コマンドパレット) でもメニューは生き残る。</summary>
    private sealed class MenuHost : Widget
    {
        public required Widget Menu;
        private UiBuildContext? _rctx;
        private UiNode? _node;
        private LayoutContext? _lc;
        private Constraints _cons;

        protected override void PerformLayout(Constraints c, LayoutContext ctx)
        {
            _cons = c;
            _lc = ctx;
            Menu.Layout(c, ctx, parentUsesSize: true);
            Menu.Offset = default;
            Size = Menu.Size;
        }

        public override IEnumerable<Widget> DebugChildren() => [Menu];

        protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        {
            _rctx = ctx;
            _node = CreateRoot(ctx, parent, worldOrigin);
            Menu.Realize(ctx, _node, WorldPos);
        }

        protected override bool OnChildNeedsRealize(Widget child)
        {
            if (!ReferenceEquals(child, Menu) || _rctx is null || _node is null || _lc is null) return false;
            Menu.Scope?.Release();
            Menu.Layout(_cons, _lc, parentUsesSize: true);
            Menu.Offset = default;
            Size = Menu.Size;
            Menu.Realize(_rctx, _node, WorldPos);
            return true;
        }
    }

    /// <summary>メニューが開いているか。</summary>
    public static bool IsOpen(UiBuildContext ctx) => _open.TryGetValue(ctx, out State? state) && state.Open.Peek();

    /// <summary>テキスト編集の標準メニュー (切り取り/コピー/貼り付け/すべて選択)。
    /// 動作は既存のキーボード配線 (<see cref="FocusTarget.OnKey"/> の Ctrl+X/C/V/A) を再利用する。</summary>
    public static void OpenForEditor(UiBuildContext ctx, UiNode node, float lx, float ly, FocusTarget f)
    {
        System.Numerics.Vector2 w = node.ComputeWorldNow().Apply(new System.Numerics.Vector2(lx, ly));
        Open(ctx, w.X, w.Y,
            ("切り取り", () => { f.OnKey?.Invoke(new KeyEvent(Key.X, false, true)); }
        ),
            ("コピー", () => { f.OnKey?.Invoke(new KeyEvent(Key.C, false, true)); }
        ),
            ("貼り付け", () => { f.OnKey?.Invoke(new KeyEvent(Key.V, false, true)); }
        ),
            ("すべて選択", () => { f.OnKey?.Invoke(new KeyEvent(Key.A, false, true)); }
        ));
    }

    /// <summary>読み取り専用テキストの標準メニュー (コピー/すべて選択)。
    /// 動作はキーボード配線 (Ctrl+C/A) を再利用する — ReadOnly の docs 等が使う。</summary>
    public static void OpenForReadOnlyText(UiBuildContext ctx, UiNode node, float lx, float ly, FocusTarget f)
    {
        System.Numerics.Vector2 w = node.ComputeWorldNow().Apply(new System.Numerics.Vector2(lx, ly));
        Open(ctx, w.X, w.Y,
            ("コピー", () => { f.OnKey?.Invoke(new KeyEvent(Key.C, false, true)); }
        ),
            ("すべて選択", () => { f.OnKey?.Invoke(new KeyEvent(Key.A, false, true)); }
        ));
    }

    /// <summary>開いているメニューを閉じる (無ければ no-op)。</summary>
    public static void Close(UiBuildContext ctx)
    {
        if (!_open.TryGetValue(ctx, out State? s)) return;
        _open.Remove(ctx);
        s.Open.Value = false;
        ctx.UnregisterOverlay(s.Entry);
    }
}
