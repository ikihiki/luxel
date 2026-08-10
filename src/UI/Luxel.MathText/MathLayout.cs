namespace Luxel.MathText;

/// <summary>数式ボックス: 幅/高さ/ベースライン (上端からの距離)。</summary>
public readonly record struct MathBox(float W, float H, float Base)
{
    /// <summary>ベースラインより下の高さ。</summary>
    public float Desc => H - Base;   // ベースラインより下
}

/// <summary>
/// 数式のボックスレイアウト (TeX の簡易版)。計測と描画プリミティブは**関数注入** —
/// 純ロジックとしてテストでき、widget 側が VectorFont/Scene2D の closure を渡す。
/// script は 0.7 倍、分数線・根号・括弧はストローク描画 (グリフに依存しない)。
/// </summary>
public sealed class MathLayoutEngine(
    Func<string, float, (float W, float H)> measure,
    Func<float, float> ascent)
{
    private const float ScriptScale = 0.7f;
    private const float SupRaise = 0.42f, SubDrop = 0.22f;   // px 比
    private const float Axis = 0.30f;                        // 数式軸 (分数線/行列中心) のベースライン上比
    private const float Rule = 1.4f;

    /// <summary>ノードのボックス (幅/高さ/ベースライン) を再帰的に計測する。</summary>
    public MathBox Measure(MathNode n, float px) => n switch
    {
        MathSymbol s => MeasureSymbol(s, px),
        MathRow r => MeasureRow(r, px),
        MathScript sc => MeasureScript(sc, px),
        MathFrac f => MeasureFrac(f, px),
        MathSqrt q => MeasureSqrt(q, px),
        MathVec v => MeasureVec(v, px),
        MathMatrix m => MeasureMatrix(m, px),
        _ => new MathBox(0, 0, 0),
    };

    private MathBox MeasureSymbol(MathSymbol s, float px)
    {
        if (s.Text.Length == 0) return new MathBox(0, 0, 0);
        (float w, float h) = measure(s.Text, px);
        return new MathBox(w, h, ascent(px));
    }

    private MathBox MeasureRow(MathRow r, float px)
    {
        float w = 0, up = 0, down = 0;
        foreach (MathNode c in r.Items)
        {
            MathBox b = Measure(c, px);
            w += b.W;
            up = MathF.Max(up, b.Base);
            down = MathF.Max(down, b.Desc);
        }
        return new MathBox(w, up + down, up);
    }

    private MathBox MeasureScript(MathScript sc, float px)
    {
        MathBox b = Measure(sc.Base, px);
        float spx = px * ScriptScale;
        MathBox sup = sc.Sup is null ? default : Measure(sc.Sup, spx);
        MathBox sub = sc.Sub is null ? default : Measure(sc.Sub, spx);
        float up = MathF.Max(b.Base, sc.Sup is null ? 0 : SupRaise * px + sup.Base);
        float down = MathF.Max(b.Desc, sc.Sub is null ? 0 : SubDrop * px + sub.Desc);
        return new MathBox(b.W + MathF.Max(sup.W, sub.W) + px * 0.05f, up + down, up);
    }

    private MathBox MeasureFrac(MathFrac f, float px)
    {
        MathBox num = Measure(f.Num, px), den = Measure(f.Den, px);
        float gap = px * 0.12f, axis = Axis * px;
        float up = axis + Rule / 2 + gap + num.H;
        float down = -axis + Rule / 2 + gap + den.H;
        return new MathBox(MathF.Max(num.W, den.W) + px * 0.35f, up + down, up);
    }

    private MathBox MeasureSqrt(MathSqrt q, float px)
    {
        MathBox b = Measure(q.Body, px);
        float hook = px * 0.55f, top = px * 0.16f;
        return new MathBox(b.W + hook + px * 0.15f, b.H + top, b.Base + top);
    }

    private MathBox MeasureVec(MathVec v, float px)
    {
        MathBox b = Measure(v.Body, px);
        float top = px * 0.22f;
        return new MathBox(b.W, b.H + top, b.Base + top);
    }

    private MathBox MeasureMatrix(MathMatrix m, float px)
    {
        (float[] colW, float[] rowUp, float[] rowDown) = MatrixCells(m, px);
        float colGap = px * 0.7f, rowGap = px * 0.35f, bracketW = m.Bracket is null ? 0 : px * 0.35f;
        float w = colW.Sum() + colGap * Math.Max(0, colW.Length - 1) + bracketW * 2;
        float h = 0;
        for (int r = 0; r < rowUp.Length; r++) h += rowUp[r] + rowDown[r];
        h += rowGap * Math.Max(0, rowUp.Length - 1);
        return new MathBox(w, h, h / 2 + Axis * px);   // 数式軸で中央合わせ
    }

    private (float[] ColW, float[] RowUp, float[] RowDown) MatrixCells(MathMatrix m, float px)
    {
        int cols = m.Rows.Count == 0 ? 0 : m.Rows.Max(r => r.Count);
        var colW = new float[cols];
        var rowUp = new float[m.Rows.Count];
        var rowDown = new float[m.Rows.Count];
        for (int r = 0; r < m.Rows.Count; r++)
            for (int c = 0; c < m.Rows[r].Count; c++)
            {
                MathBox b = Measure(m.Rows[r][c], px);
                colW[c] = MathF.Max(colW[c], b.W);
                rowUp[r] = MathF.Max(rowUp[r], b.Base);
                rowDown[r] = MathF.Max(rowDown[r], b.Desc);
            }
        return (colW, rowUp, rowDown);
    }

    // ---- 描画 (x = 左端、baseY = ベースライン)。text/line は widget が注入 ----

    /// <summary>ノードを描画する (x = 左端、baseY = ベースライン)。
    /// text は (文字列, x, baseY, px)、line は (x1, y1, x2, y2, 線幅) を受ける注入プリミティブ。</summary>
    public void Draw(MathNode n, float x, float baseY, float px,
        Action<string, float, float, float> text, Action<float, float, float, float, float> line)
    {
        switch (n)
        {
            case MathSymbol s:
                if (s.Text.Length > 0) text(s.Text, x, baseY, px);
                break;
            case MathRow r:
                {
                    float cx = x;
                    foreach (MathNode c in r.Items)
                    {
                        Draw(c, cx, baseY, px, text, line);
                        cx += Measure(c, px).W;
                    }
                    break;
                }
            case MathScript sc:
                {
                    MathBox b = Measure(sc.Base, px);
                    Draw(sc.Base, x, baseY, px, text, line);
                    float spx = px * ScriptScale, sx = x + b.W + px * 0.05f;
                    if (sc.Sup is not null) Draw(sc.Sup, sx, baseY - SupRaise * px, spx, text, line);
                    if (sc.Sub is not null) Draw(sc.Sub, sx, baseY + SubDrop * px, spx, text, line);
                    break;
                }
            case MathFrac f:
                {
                    MathBox box = Measure(f, px);
                    MathBox num = Measure(f.Num, px), den = Measure(f.Den, px);
                    float gap = px * 0.12f, axisY = baseY - Axis * px;
                    Draw(f.Num, x + (box.W - num.W) / 2, axisY - Rule / 2 - gap - num.Desc, px, text, line);
                    Draw(f.Den, x + (box.W - den.W) / 2, axisY + Rule / 2 + gap + den.Base, px, text, line);
                    line(x, axisY, x + box.W, axisY, Rule);
                    break;
                }
            case MathSqrt q:
                {
                    MathBox b = Measure(q.Body, px);
                    MathBox box = Measure(q, px);
                    float hook = px * 0.55f;
                    float top = baseY - box.Base;
                    // 根号: フック (√ 形) + 上線
                    line(x, top + box.H * 0.6f, x + hook * 0.45f, top + box.H - 1, Rule);
                    line(x + hook * 0.45f, top + box.H - 1, x + hook, top + 1, Rule);
                    line(x + hook, top + 1, x + box.W, top + 1, Rule);
                    Draw(q.Body, x + hook + px * 0.1f, baseY, px, text, line);
                    break;
                }
            case MathVec v:
                {
                    MathBox b = Measure(v.Body, px);
                    Draw(v.Body, x, baseY, px, text, line);
                    float y = baseY - b.Base - px * 0.08f;
                    line(x, y, x + b.W, y, Rule * 0.9f);
                    line(x + b.W - px * 0.16f, y - px * 0.09f, x + b.W, y, Rule * 0.9f);
                    line(x + b.W - px * 0.16f, y + px * 0.09f, x + b.W, y, Rule * 0.9f);
                    break;
                }
            case MathMatrix m:
                {
                    MathBox box = Measure(m, px);
                    (float[] colW, float[] rowUp, float[] rowDown) = MatrixCells(m, px);
                    float colGap = px * 0.7f, rowGap = px * 0.35f, bracketW = m.Bracket is null ? 0 : px * 0.35f;
                    float top = baseY - box.Base;
                    float cy = top;
                    for (int r = 0; r < m.Rows.Count; r++)
                    {
                        float rowBase = cy + rowUp[r];
                        float cx = x + bracketW;
                        for (int c = 0; c < m.Rows[r].Count; c++)
                        {
                            MathBox cell = Measure(m.Rows[r][c], px);
                            Draw(m.Rows[r][c], cx + (colW[c] - cell.W) / 2, rowBase, px, text, line);
                            cx += colW[c] + colGap;
                        }
                        cy += rowUp[r] + rowDown[r] + rowGap;
                    }
                    if (m.Bracket is char br)
                    {
                        float h = box.H, serif = px * 0.22f;
                        // '[' は直線 + セリフ、'(' も v1 は同形 (わずかに内傾) で近似
                        float lean = br == '(' ? px * 0.08f : 0;
                        line(x + lean, top, x, top + h / 2, Rule);
                        line(x, top + h / 2, x + lean, top + h, Rule);
                        if (br == '[') { line(x, top, x + serif, top, Rule); line(x, top + h, x + serif, top + h, Rule); }
                        float rx = x + box.W;
                        line(rx - lean, top, rx, top + h / 2, Rule);
                        line(rx, top + h / 2, rx - lean, top + h, Rule);
                        if (br == '[') { line(rx, top, rx - serif, top, Rule); line(rx, top + h, rx - serif, top + h, Rule); }
                    }
                    break;
                }
        }
    }
}
