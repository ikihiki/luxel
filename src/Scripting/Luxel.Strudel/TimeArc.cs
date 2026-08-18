namespace Luxel.Strudel;

/// <summary>
/// 時間区間 [Begin, End) (Tidal の Arc / Strudel の TimeSpan)。単位はサイクル。
/// Begin == End の零幅区間は「点クエリ」(その瞬間に鳴っているものを問う) を表す。
/// </summary>
public readonly struct TimeArc(Fraction begin, Fraction end)
{
    public Fraction Begin { get; } = begin;
    public Fraction End { get; } = end;

    public Fraction Length => End - Begin;

    /// <summary>交差。重ならなければ null。零幅同士/零幅と区間の接触は「点」として残す
    /// (点クエリの伝播に必要)。</summary>
    public TimeArc? Intersect(in TimeArc o)
    {
        Fraction b = Fraction.Max(Begin, o.Begin);
        Fraction e = Fraction.Min(End, o.End);
        if (b > e) return null;
        if (b == e && Length != Fraction.Zero && o.Length != Fraction.Zero) return null;   // 端の接触は交差なし
        return new TimeArc(b, e);
    }

    /// <summary>サイクル境界 (整数) で分割して列挙する。零幅区間はそのまま 1 つ返す。
    /// パターンのクエリは「サイクル単位の観測」が基本形 — Pure/Cat/Rev 等はこれで分けてから処理する。</summary>
    public IEnumerable<TimeArc> SpanCycles()
    {
        if (Begin == End) { yield return this; yield break; }
        Fraction b = Begin;
        while (b < End)
        {
            Fraction e = Fraction.Min(b.NextSam, End);
            yield return new TimeArc(b, e);
            b = e;
        }
    }

    /// <summary>両端に時間写像を適用する (順序が逆転したら入れ替える — Rev の鏡映用)。</summary>
    public TimeArc WithTime(Func<Fraction, Fraction> f)
    {
        Fraction b = f(Begin), e = f(End);
        return b <= e ? new TimeArc(b, e) : new TimeArc(e, b);
    }

    public override string ToString() => $"[{Begin}, {End})";
}

/// <summary>
/// パターンイベント (Tidal/Strudel の Hap)。
/// <c>Whole</c> = イベント本来の全区間 (null = 連続値のサンプル — v1 では未使用)、
/// <c>Part</c> = このクエリで見えている断片 (Whole ⊇ Part)。
/// クエリ窓がイベントを跨ぐと同じ Whole の断片が複数回返る — 発音は <c>HasOnset</c>
/// (断片が頭を含む) のときだけ行う。これが「窓を跨いでも二重発火しない」仕組みの核。
/// </summary>
/// <summary>ミニ記法アトムのソース位置 (ソース文字列内の開始オフセット + 長さ)。
/// パース時にアトムへ刻まれ、クエリ変形 (fast/slow/rev/cat/…) を通り抜けて Hap に残る。
/// スケジューラ/REPL が「いまどのトークンが鳴っているか」を判定して文字の囲みを描くのに使う。</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public readonly struct Hap<T>(TimeArc? whole, TimeArc part, T value, SourceSpan? span = null)
{
    public TimeArc? Whole { get; } = whole;
    public TimeArc Part { get; } = part;
    public T Value { get; } = value;
    /// <summary>このイベントを生んだミニ記法アトムのソース位置 (無ければ null)。</summary>
    public SourceSpan? Span { get; } = span;

    /// <summary>この断片がイベントの頭 (発音点) を含むか。</summary>
    public bool HasOnset => Whole is { } w && w.Begin == Part.Begin;

    /// <summary>発音の長さ (Whole 基準。Whole なしは Part)。</summary>
    public Fraction Duration => (Whole ?? Part).Length;

    public Hap<TU> WithValue<TU>(TU v) => new(Whole, Part, v, Span);

    /// <summary>Whole/Part の両方に時間写像を適用する (ソース位置は保つ)。</summary>
    public Hap<T> WithTime(Func<Fraction, Fraction> f)
        => new(Whole?.WithTime(f), Part.WithTime(f), Value, Span);

    public override string ToString() => $"{Part} (whole {Whole?.ToString() ?? "-"}): {Value}";
}
