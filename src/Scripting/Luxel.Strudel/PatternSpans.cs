namespace Luxel.Strudel;

/// <summary>再生中トークンの判定 — パターンをサイクル位置で点クエリし、鳴っているイベントの
/// ソース位置 (<see cref="SourceSpan"/>) を集める。エディタの「文字の囲み」(再生シーケンス強調) に使う。</summary>
public static class PatternSpans
{
    /// <summary>サイクル位置 <paramref name="now"/> に鳴っているイベントのソース位置 (重複除去・開始順)。</summary>
    public static IReadOnlyList<SourceSpan> ActiveAt<T>(this Pattern<T> p, Fraction now)
    {
        var set = new HashSet<SourceSpan>();
        foreach (Hap<T> h in p.Query(new TimeArc(now, now)))
            if (h.Span is { } s) set.Add(s);
        var list = set.ToList();
        list.Sort((a, b) => a.Start.CompareTo(b.Start));
        return list;
    }
}
