using System.Numerics;
using Luxel.Controls;
using Luxel.Ecs;
using Luxel.Framework.Game;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **FixedUpdate 描画補間の可視化** — 表示レート (この例では固定ステップの 4 倍) で動く箱を
/// 「補間なし」と「補間あり」で並べる。位置は実物の <see cref="FixedTimestep"/> +
/// <see cref="InterpolatedTransform"/> で計算する (wall-clock 非依存 = snap 決定的)。
///
/// <list type="bullet">
/// <item><b>補間なし</b>: 各表示フレームで直近の固定ステップ位置へスナップ → 4 フレームに 1 回しか
///   動かず箱が団子になる (= カクつき)。</item>
/// <item><b>補間あり</b>: <c>lerp(prev, curr, Alpha)</c> で表示フレームごとに均等に進む (= なめらか)。
///   1 ステップ分の遅延と引き換えにフレーム間のガタつきが消える。</item>
/// </list>
/// </summary>
public static class FixedTimestepStories
{
    private const float CanvasW = 460, RowH = 92;
    private const float LeftMargin = 34, Baseline = RowH / 2, BoxSize = 16;
    private const float StopDist = 88;    // 固定ステップ 1 回で進む px
    private const int Ratio = 4;          // 表示レート / 固定レート (1 ステップ = 4 表示フレーム)
    private const int Warmup = Ratio, CaptureN = 16;

    [Story("Examples/Framework/DrawInterpolation", Height = 300, Order = 149)]
    public static Widget DrawInterpolation(StoryContext ctx)
    {
        // 実物の蓄積器で決定的にシミュレーション (固定ステップ = StopDist/step、表示 = その 4 倍レート)。
        var ft = new FixedTimestep(fixedDt: Ratio, maxStepsPerFrame: 8);
        var box = new InterpolatedTransform(Vector3.Zero, Quaternion.Identity, Vector3.One);
        int stepIdx = 0;
        var snapped = new List<float>();
        var interp = new List<float>();
        for (int f = 0; f < Warmup + CaptureN; f++)
        {
            int steps = ft.Advance(1.0);   // 表示 dt = 1、固定 dt = Ratio
            for (int s = 0; s < steps; s++)
            {
                stepIdx++;
                box.Push(new Vector3(stepIdx * StopDist, 0, 0), Quaternion.Identity, Vector3.One);
            }
            if (f >= Warmup)   // 助走 (箱が動き出すまで) を捨ててから採取
            {
                snapped.Add(box.CurrPosition.X);
                interp.Add(box.Sample(ft.Alpha).Translation.X);
            }
        }

        // 補間ありの初フレームを左端に合わせる共通オフセット (両行で共有 = 横位置が比較可能)。
        float offset = interp[0] - LeftMargin;
        float[] snapX = snapped.ConvertAll(v => v - offset).ToArray();
        float[] interpX = interp.ConvertAll(v => v - offset).ToArray();
        // 固定ステップの停止位置 (= スナップ箱が乗る目盛り)。両行で同じものを引く。
        var gridSet = new SortedSet<float>(snapX);
        float[] gridXs = new float[gridSet.Count];
        gridSet.CopyTo(gridXs);

        return ctx.Snap(VStack(6)[
            Muted("補間なし — 表示フレームが直近の固定ステップにスナップ (4 フレームに 1 回だけ動く = カクつく)"),
            Frame(Canvas2D(CanvasW, RowH, draw: s => DrawRow(s, snapX, gridXs, Color2D.Rgba(239, 68, 68, 70), Tw.Red500))),
            Muted("補間あり — lerp(prev, curr, Alpha) で毎フレーム均等に進む (なめらか)"),
            Frame(Canvas2D(CanvasW, RowH, draw: s => DrawRow(s, interpX, gridXs, Color2D.Rgba(59, 130, 246, 70), Tw.Blue500)))
        ]);
    }

    /// <summary>1 行分を描く: 固定ステップのグリッド (縦線) + 各表示フレームの箱 (半透明で重なると濃くなる) + 最新箱を強調。</summary>
    private static void DrawRow(Scene2D s, float[] xs, float[] gridXs, uint ghost, uint solid)
    {
        uint grid = Color2D.Rgba(148, 163, 184, 70);
        foreach (float gx in gridXs)
            s.FillRect(grid, gx, Baseline - 26, 1.5f, 52);

        foreach (float x in xs)
            s.FillRoundedRect(ghost, x - BoxSize / 2, Baseline - BoxSize / 2, BoxSize, BoxSize, 3);

        float last = xs[^1];
        s.FillRoundedRect(solid, last - BoxSize / 2, Baseline - BoxSize / 2, BoxSize, BoxSize, 3);
    }
}
