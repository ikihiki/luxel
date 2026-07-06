using System.Numerics;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **DebugDraw (gizmo コア)** — 即時モードのデバッグ描画を最前面オーバーレイに流し込むデモ。
/// コライダー箱 (矩形)・トリガー半径 (円)・速度ベクトル (線)・ラベル (テキスト) を
/// <see cref="DebugDraw"/> のカテゴリ "demo" で描く。無効カテゴリ ("disabled") の呼び出しは
/// 何も出ない (OFF 時ゼロ描画)。2D なのでワールド=スクリーン恒等、3D はここに viewProj を渡す。
/// </summary>
public static class DebugDrawStories
{
    private static readonly Lazy<VectorFont> Font = new(() => Luxel.Gallery.GalleryFonts.Load(Luxel.Gallery.GalleryFonts.Regular));
    private const float W = 460, H = 260;

    [Story("Demos/Framework/Gizmos", Height = 300, Order = 150)]
    public static Widget Gizmos(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(W, H, draw: s =>
    {
        s.FillRect(Color2D.Rgba(24, 28, 36), 0, 0, W, H);   // 暗い背景 (ゲーム画面のつもり)

        // 即時コマンドを毎フレーム作り直す (決定的 = wall-clock 非依存)
        DebugDraw.Reset();
        DebugDraw.Enable("demo");

        uint green = Color2D.Rgba(80, 220, 120);
        uint cyan = Color2D.Rgba(90, 200, 240);
        uint amber = Color2D.Rgba(240, 190, 90);

        DebugDraw.Rect(60, 70, 150, 90, green, 2f, "demo");                 // コライダー箱
        DebugDraw.Circle(330, 130, 55, cyan, 2f, 32, "demo");              // トリガー半径
        DebugDraw.Line(new Vector2(135, 115), new Vector2(275, 70), amber, 2.5f, "demo");  // 速度ベクトル
        DebugDraw.Text(60, 58, "Enemy #3", green, 16f, "demo");            // ラベル
        DebugDraw.Text(288, 65, "trigger", cyan, 14f, "demo");

        // 無効カテゴリは出ない (golden に現れないこと自体が OFF 時ゼロ描画の証跡)
        DebugDraw.Line(new Vector2(0, 0), new Vector2(W, H), Color2D.Red, 4f, "disabled");

        DebugDraw.Flush(s, w => new Vector2(w.X, w.Y),
            (sc, t, x, y, h, c) => Font.Value.AppendText(sc, t, x, y, h, c));
    })));
}
