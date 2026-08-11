namespace Luxel.UI;

/// <summary>アンカーに対する浮遊 UI の希望配置方向。</summary>
public enum PopupSide { Below, Above, Right, Left }

/// <summary>交差軸でのアンカーへの揃え (下/上配置なら左右、右/左配置なら上下)。</summary>
public enum PopupAlign { Start, Center, End }

/// <summary>
/// アンカー配置の指定 — 希望方向 + 揃え + 端での挙動。<see cref="PopupPlacer.Solve"/> がこれと
/// アンカー矩形・中身サイズ・viewport から実配置を解く。
/// </summary>
public sealed class AnchoredPlacement
{
    /// <summary>希望する配置方向。</summary>
    public PopupSide Side { get; init; } = PopupSide.Below;
    /// <summary>交差軸の揃え。</summary>
    public PopupAlign Align { get; init; } = PopupAlign.Start;
    /// <summary>入らなければ反対方向へフリップするか。</summary>
    public bool Flip { get; init; } = true;
    /// <summary>交差軸で画面内へずらすか。</summary>
    public bool Shift { get; init; } = true;
    /// <summary>アンカーとの隙間 px。</summary>
    public float Gap { get; init; } = 6;
    /// <summary>画面端との最小距離 px。</summary>
    public float Margin { get; init; }
    /// <summary>最大幅 px (0 = viewport 依存)。中身がこれを超えるなら呼び出し側がスクロールさせる。</summary>
    public float MaxWidth { get; init; }
    /// <summary>最大高さ px (0 = viewport 依存)。</summary>
    public float MaxHeight { get; init; }
}

/// <summary>配置の解 — 位置/サイズ矩形 + 実際に採用した方向 (フリップ後、アニメ/矢印に使う) + 収めたサイズ。</summary>
public readonly record struct PopupSolve(Rect Rect, PopupSide Side, Size Constrained);

/// <summary>
/// 浮遊 UI の配置ソルバ (純関数・canvas 非依存)。希望方向に置き、入らなければフリップ、交差軸でシフト、
/// viewport を超えるならサイズを詰める。全浮遊 UI (ドロップダウン/メニュー/補完/ツールチップ/IME 候補) が
/// これ 1 つを共有して画面端に一貫して反応する (ADR-0007)。
/// </summary>
public static class PopupPlacer
{
    /// <summary>アンカー矩形・中身サイズ・viewport から最適配置を解く。</summary>
    public static PopupSolve Solve(Rect anchor, Size content, Rect viewport, AnchoredPlacement p)
    {
        float uL = viewport.X + p.Margin, uR = viewport.X + viewport.Width - p.Margin;
        float uT = viewport.Y + p.Margin, uB = viewport.Y + viewport.Height - p.Margin;

        if (p.Side is PopupSide.Below or PopupSide.Above)
        {
            (float y, bool after, float h) = SolveMain(
                anchor.Y, anchor.Y + anchor.Height, uT, uB, content.Height, p.Gap, p.Side == PopupSide.Below, p.Flip, p.MaxHeight);
            (float x, float w) = SolveCross(
                anchor.X, anchor.Width, uL, uR, content.Width, p.Align, p.Shift, p.MaxWidth);
            return new PopupSolve(new Rect(x, y, w, h), after ? PopupSide.Below : PopupSide.Above, new Size(w, h));
        }
        else
        {
            (float x, bool after, float w) = SolveMain(
                anchor.X, anchor.X + anchor.Width, uL, uR, content.Width, p.Gap, p.Side == PopupSide.Right, p.Flip, p.MaxWidth);
            (float y, float h) = SolveCross(
                anchor.Y, anchor.Height, uT, uB, content.Height, p.Align, p.Shift, p.MaxHeight);
            return new PopupSolve(new Rect(x, y, w, h), after ? PopupSide.Right : PopupSide.Left, new Size(w, h));
        }
    }

    // 主軸: 希望側に置き、入らなければフリップ、どちらも入らなければ広い側へ寄せてサイズを詰める。
    private static (float pos, bool after, float extent) SolveMain(
        float aLo, float aHi, float uLo, float uHi, float content, float gap, bool preferAfter, bool flip, float maxExt)
    {
        if (maxExt > 0) content = MathF.Min(content, maxExt);
        float afterSpace = uHi - (aHi + gap);
        float beforeSpace = (aLo - gap) - uLo;

        bool after;
        if (!flip) after = preferAfter;                        // フリップ禁止 → 希望側のまま (サイズは詰まる)
        else if (preferAfter) after = content <= afterSpace ? true : (content <= beforeSpace ? false : afterSpace >= beforeSpace);
        else after = content <= beforeSpace ? false : (content <= afterSpace ? true : afterSpace > beforeSpace);

        float space = MathF.Max(0, after ? afterSpace : beforeSpace);
        float extent = MathF.Min(content, MathF.Max(1, space));
        float pos = after ? aHi + gap : aLo - gap - extent;
        pos = Clamp(pos, uLo, MathF.Max(uLo, uHi - extent));
        return (pos, after, extent);
    }

    // 交差軸: 揃えて配置し、画面内へシフト。viewport を超える幅/高さは詰める。
    private static (float pos, float extent) SolveCross(
        float aLo, float aExtent, float uLo, float uHi, float content, PopupAlign align, bool shift, float maxExt)
    {
        if (maxExt > 0) content = MathF.Min(content, maxExt);
        float extent = MathF.Min(content, MathF.Max(1, uHi - uLo));
        float pos = align switch
        {
            PopupAlign.Center => aLo + (aExtent - extent) / 2,
            PopupAlign.End => aLo + aExtent - extent,
            _ => aLo,
        };
        if (shift) pos = Clamp(pos, uLo, MathF.Max(uLo, uHi - extent));
        return (pos, extent);
    }

    private static float Clamp(float v, float lo, float hi) => MathF.Max(lo, MathF.Min(v, hi));
}
