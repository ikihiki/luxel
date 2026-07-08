namespace Luxel.Editor;

/// <summary>選択レンジ 1 本 — <see cref="Anchor"/> (固定端) と <see cref="Head"/> (キャレット端)。
/// 空 (Anchor==Head) はただのキャレット。方向を保つため From/To ではなく anchor/head で持つ。</summary>
public readonly record struct SelectionRange(int Anchor, int Head)
{
    /// <summary>キャレット位置に置く空レンジ。</summary>
    public static SelectionRange Cursor(int pos) => new(pos, pos);

    /// <summary>小さい方の端。</summary>
    public int From => Math.Min(Anchor, Head);
    /// <summary>大きい方の端。</summary>
    public int To => Math.Max(Anchor, Head);
    /// <summary>空 (キャレット) か。</summary>
    public bool Empty => Anchor == Head;

    /// <summary>両端を変更セットで写す (assoc は既定=左寄せ)。</summary>
    public SelectionRange Map(ChangeSet cs) => new(cs.MapPos(Anchor), cs.MapPos(Head));

    /// <summary>両端を [0, len] にクランプ。</summary>
    public SelectionRange Clamp(int len)
        => new(Math.Clamp(Anchor, 0, len), Math.Clamp(Head, 0, len));
}

/// <summary>
/// エディタの選択状態 — **複数レンジ**の集合 + main index。マルチカーソルが native で、単一カーソルは
/// レンジ 1 本の特殊ケース。<see cref="Of"/> は From 昇順にソートし、重なり/重複キャレットをマージして
/// 正規化する (main は元の main を含むレンジへ引き継ぐ)。編集追従は <see cref="Map"/> が全レンジを写す。
/// </summary>
public sealed class EditorSelection
{
    /// <summary>正規化済みのレンジ列 (From 昇順、非重複、1 本以上)。</summary>
    public IReadOnlyList<SelectionRange> Ranges { get; }
    /// <summary>主レンジのインデックス (キャレット追従・スクロールの基準)。</summary>
    public int MainIndex { get; }
    /// <summary>主レンジ。</summary>
    public SelectionRange Main => Ranges[MainIndex];

    private EditorSelection(IReadOnlyList<SelectionRange> ranges, int mainIndex)
    {
        Ranges = ranges;
        MainIndex = mainIndex;
    }

    /// <summary>キャレット 1 個。</summary>
    public static EditorSelection Cursor(int pos) => new([SelectionRange.Cursor(pos)], 0);

    /// <summary>単一レンジ。</summary>
    public static EditorSelection Single(int anchor, int head) => new([new SelectionRange(anchor, head)], 0);

    /// <summary>複数レンジから正規化して作る。<paramref name="mainIndex"/> は入力配列内の主レンジ。</summary>
    public static EditorSelection Of(IReadOnlyList<SelectionRange> ranges, int mainIndex = -1)
    {
        if (ranges.Count == 0) return Cursor(0);
        if (mainIndex < 0 || mainIndex >= ranges.Count) mainIndex = ranges.Count - 1;

        // 主レンジの識別のため、ソート前に元インデックスを付ける
        var tagged = ranges.Select((r, i) => (r, main: i == mainIndex)).ToList();
        tagged.Sort((a, b) => a.r.From != b.r.From ? a.r.From.CompareTo(b.r.From) : a.r.To.CompareTo(b.r.To));

        var merged = new List<SelectionRange>();
        int mainOut = 0;
        foreach ((SelectionRange r, bool main) in tagged)
        {
            if (merged.Count > 0 && Overlaps(merged[^1], r))
            {
                merged[^1] = Union(merged[^1], r);
                if (main) mainOut = merged.Count - 1;
            }
            else
            {
                merged.Add(r);
                if (main) mainOut = merged.Count - 1;
            }
        }
        return new EditorSelection(merged, mainOut);
    }

    // 重なり: 範囲が交差する、または同位置の重複キャレット
    private static bool Overlaps(SelectionRange a, SelectionRange b)
        => b.From < a.To || (b.From == a.To && a.Empty && b.Empty);

    // 和集合 (方向は先行レンジの向きを保つ)
    private static SelectionRange Union(SelectionRange a, SelectionRange b)
    {
        int from = Math.Min(a.From, b.From), to = Math.Max(a.To, b.To);
        return a.Anchor <= a.Head ? new SelectionRange(from, to) : new SelectionRange(to, from);
    }

    /// <summary>全レンジを変更セットで写して正規化した新しい選択を返す。</summary>
    public EditorSelection Map(ChangeSet cs)
    {
        var mapped = new SelectionRange[Ranges.Count];
        for (int i = 0; i < Ranges.Count; i++) mapped[i] = Ranges[i].Map(cs);
        return Of(mapped, MainIndex);
    }

    /// <summary>全レンジを [0, len] にクランプして正規化。</summary>
    public EditorSelection Clamp(int len)
    {
        var c = new SelectionRange[Ranges.Count];
        for (int i = 0; i < Ranges.Count; i++) c[i] = Ranges[i].Clamp(len);
        return Of(c, MainIndex);
    }

    /// <summary>レンジを 1 本足す (既定で新しいものを main に)。正規化で重なればマージされる。</summary>
    public EditorSelection AddRange(SelectionRange r, bool asMain = true)
    {
        var next = new List<SelectionRange>(Ranges) { r };
        return Of(next, asMain ? next.Count - 1 : MainIndex);
    }
}
