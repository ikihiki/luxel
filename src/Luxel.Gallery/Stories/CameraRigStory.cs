using System.Numerics;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **CameraRig2D デモ** — タイル状背景 + プレイヤー矩形を <see cref="CameraRig2D"/> のカメラ越しに描く。
/// ① 開始 (プレイヤーがデッドゾーン内 → カメラ不動) ② 右へ移動 (デッドゾーンを出て追従 → 背景がスクロール)
/// ③ シェイク (固定シードで決定的)。実物の Rig を wall-clock 非依存に回して snap する。
/// </summary>
public static class CameraRigStories
{
    private const float CW = 460, RowH = 96, Grid = 60, Player = 18;
    private static readonly Vector2 Deadzone = new(140, 70);

    [Story("Demos/2D/CameraRig", Height = 340, Order = 118)]
    public static Widget CameraRig(StoryContext ctx)
    {
        return ctx.Snap(VStack(6)[
            Muted("① 開始 — プレイヤーがデッドゾーン内なのでカメラは動かない"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawStage(s, Stage.Start))),
            Muted("② 右へ移動 — デッドゾーンを出た分だけ追従し背景がスクロールする"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawStage(s, Stage.Follow))),
            Muted("③ シェイク — 固定シードで決定的に画面が揺れる"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawStage(s, Stage.Shake)))
        ]);
    }

    private enum Stage { Start, Follow, Shake }

    private static void DrawStage(Scene2D s, Stage stage)
    {
        var rig = new CameraRig2D { Deadzone = Deadzone, Smoothing = 0f, ZoomSmoothing = 0f, Zoom = 1f };
        var player = new Vector2(CW / 2, RowH / 2);   // 開始はワールド中央 = 画面中央
        rig.Target = player;
        rig.SnapToTarget();

        if (stage != Stage.Start)
        {
            player = new Vector2(CW / 2 + 150, RowH / 2);   // 右へ (デッドゾーン半径 70 を超える)
            rig.Target = player;
            rig.Update(1f / 60, CW, RowH);
        }
        if (stage == Stage.Shake)
        {
            rig.Shake(amplitude: 10f, duration: 1f, seed: 4242);
            for (int i = 0; i < 4; i++) rig.Update(1f / 60, CW, RowH);   // 揺れの途中で採取
        }

        Camera2D cam = rig.Camera(CW, RowH);

        s.FillRect(Color2D.Rgba(24, 28, 36), 0, 0, CW, RowH);   // 背景

        // タイル状グリッド (ワールド線をカメラで screen へ)
        uint grid = Color2D.Rgba(70, 80, 100);
        for (float wx = -Grid * 2; wx < CW + Grid * 4; wx += Grid)
        {
            Vector2 a = World(cam, wx, -Grid), b = World(cam, wx, RowH + Grid);
            s.StrokeLine(grid, 1f, a.X, a.Y, b.X, b.Y);
        }
        for (float wy = -Grid * 2; wy < RowH + Grid * 4; wy += Grid)
        {
            Vector2 a = World(cam, -Grid, wy), b = World(cam, CW + Grid * 4, wy);
            s.StrokeLine(grid, 1f, a.X, a.Y, b.X, b.Y);
        }

        // デッドゾーン (画面中央の枠) — カメラ相対なので常に画面中央
        uint dz = Color2D.Rgba(90, 200, 240);
        s.StrokeRoundedRect(dz, 1f, CW / 2 - Deadzone.X / 2, RowH / 2 - Deadzone.Y / 2, Deadzone.X, Deadzone.Y, 2);

        // プレイヤー (ワールド座標 → screen)
        Vector2 ps = World(cam, player.X, player.Y);
        s.FillRoundedRect(Color2D.Rgba(240, 190, 90), ps.X - Player / 2, ps.Y - Player / 2, Player, Player, 4);
    }

    /// <summary>ワールド座標をカメラ affine で screen へ (screen = A*x+C*y+E, B*x+D*y+F)。</summary>
    private static Vector2 World(Camera2D c, float wx, float wy)
        => new(c.A * wx + c.C * wy + c.E, c.B * wx + c.D * wy + c.F);
}
