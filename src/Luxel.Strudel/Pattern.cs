namespace Luxel.Strudel;

/// <summary>
/// パターン = **時間区間 → イベント列の純関数** (Tidal/Strudel のクエリモデル)。
/// 状態を持たず、同じ区間には常に同じ答えを返す — だからスケジューラは好きな窓幅で先読みでき、
/// テストは「サイクル 0..2 を問うたらこのイベント列」のゴールデンで書ける。
/// 時間変形 (Fast/Rev/…) は「クエリを逆写像 → 結果を順写像」の対で実装する。
/// </summary>
public sealed class Pattern<T>(Func<TimeArc, IEnumerable<Hap<T>>> query)
{
    /// <summary>区間内のイベント断片を列挙する (時間順は保証しない — 呼び出し側で必要ならソート)。</summary>
    public IEnumerable<Hap<T>> Query(TimeArc span) => query(span);

    /// <summary>サイクル [from, from+count) のイベントを Part.Begin 順で返す (テスト/表示用)。</summary>
    public List<Hap<T>> QueryCycles(long from = 0, long count = 1)
    {
        var list = Query(new TimeArc(new Fraction(from), new Fraction(from + count))).ToList();
        list.Sort(static (a, b) => a.Part.Begin.CompareTo(b.Part.Begin));
        return list;
    }

    // ---- 値変換 ----

    public Pattern<TU> Select<TU>(Func<T, TU> f)
        => new(span => Query(span).Select(h => h.WithValue(f(h.Value))));

    public Pattern<T> Where(Func<Hap<T>, bool> pred)
        => new(span => Query(span).Where(pred));

    // ---- 時間変形の基本対 ----

    /// <summary>クエリ区間へ写像を適用 (結果は動かさない)。</summary>
    public Pattern<T> WithQueryTime(Func<Fraction, Fraction> f)
        => new(span => Query(span.WithTime(f)));

    /// <summary>結果イベントの時間へ写像を適用。</summary>
    public Pattern<T> WithHapTime(Func<Fraction, Fraction> f)
        => new(span => Query(span).Select(h => h.WithTime(f)));

    /// <summary>k 倍速 (1 サイクルに k サイクル分)。</summary>
    public Pattern<T> Fast(Fraction k)
        => k.Num <= 0 ? Pat.Silence<T>()
         : WithQueryTime(t => t * k).WithHapTime(t => t / k);

    /// <summary>1/k 倍速。</summary>
    public Pattern<T> Slow(Fraction k) => Fast(Fraction.One / k);

    /// <summary>k サイクル前へ (イベントが早く来る)。</summary>
    public Pattern<T> Early(Fraction k)
        => WithQueryTime(t => t + k).WithHapTime(t => t - k);

    /// <summary>k サイクル後ろへ。</summary>
    public Pattern<T> Late(Fraction k) => Early(-k);

    /// <summary>各サイクル内で逆再生 (サイクル c の鏡映 t → 2c+1-t)。</summary>
    public Pattern<T> Rev()
        => new(span => RevQuery(span));

    private IEnumerable<Hap<T>> RevQuery(TimeArc span)
    {
        foreach (TimeArc s in span.SpanCycles())
        {
            Fraction m = s.Begin.Sam + s.Begin.NextSam;   // 2c+1
            foreach (Hap<T> h in Query(s.WithTime(t => m - t)))
                yield return h.WithTime(t => m - t);
        }
    }

    /// <summary>サイクル i を i/n だけ前へずらす (n サイクルで一周する回転)。</summary>
    public Pattern<T> Iter(int n)
        => n <= 1 ? this : new(span => IterQuery(span, n));

    private IEnumerable<Hap<T>> IterQuery(TimeArc span, int n)
    {
        foreach (TimeArc s in span.SpanCycles())
        {
            long cyc = s.Begin.Floor;
            var k = new Fraction(((cyc % n) + n) % n, n);
            foreach (Hap<T> h in Early(k).Query(s)) yield return h;
        }
    }

    /// <summary>n サイクルに 1 回 (cycle % n == 0 のサイクル) だけ f を適用する。</summary>
    public Pattern<T> Every(int n, Func<Pattern<T>, Pattern<T>> f)
    {
        if (n <= 0) return this;
        Pattern<T> alt = f(this);   // 変形は一度だけ構築 (クエリ毎に作らない)
        return new(span => EveryQuery(span, n, alt));
    }

    private IEnumerable<Hap<T>> EveryQuery(TimeArc span, int n, Pattern<T> alt)
    {
        foreach (TimeArc s in span.SpanCycles())
        {
            long cyc = s.Begin.Floor;
            Pattern<T> p = ((cyc % n) + n) % n == 0 ? alt : this;
            foreach (Hap<T> h in p.Query(s)) yield return h;
        }
    }

    /// <summary>自身 + t ずらして f を適用したコピーの重ね (echo/ハモり)。</summary>
    public Pattern<T> Off(Fraction t, Func<Pattern<T>, Pattern<T>> f)
        => Pat.Stack(this, f(Late(t)));

    /// <summary>確率 amount で発音を間引く (時間ハッシュ乱数 — 決定的)。</summary>
    public Pattern<T> Degrade(double amount = 0.5)
        => Where(h => Pat.TimeHash((h.Whole ?? h.Part).Begin) >= amount);

    /// <summary>サイクル内の [b, e) 区間へ圧縮する (残りは無音)。b/e は [0,1]。
    /// TimeCat (重み付き並び) の部品 — スロットに 1 サイクル分を押し込む。</summary>
    public Pattern<T> CompressSpan(Fraction b, Fraction e)
    {
        if (b < Fraction.Zero || e > Fraction.One || b >= e) return Pat.Silence<T>();
        Fraction w = e - b;
        return new(span => CompressQuery(span, b, w));
    }

    private IEnumerable<Hap<T>> CompressQuery(TimeArc span, Fraction b, Fraction w)
    {
        foreach (TimeArc s in span.SpanCycles())
        {
            Fraction c = s.Begin.Sam;
            var slot = new TimeArc(c + b, c + b + w);
            TimeArc? hit = s.Intersect(slot);
            if (hit is not TimeArc q) continue;
            // 外側 [c+b, c+b+w) ↔ 内側 [c, c+1)
            Fraction ToInner(Fraction t) => c + (t - c - b) / w;
            Fraction ToOuter(Fraction t) => c + b + (t - c) * w;
            foreach (Hap<T> h in Query(q.WithTime(ToInner)))
                yield return h.WithTime(ToOuter);
        }
    }

    /// <summary>左の構造で右の値を取り込む (control 合成の基本形)。左の各イベントに対し
    /// その Part で右をクエリし、重なった値を combine で畳む。</summary>
    public Pattern<T> OpLeft<TU>(Pattern<TU> right, Func<T, TU, T> combine)
        => new(span => OpLeftQuery(span, right, combine));

    private IEnumerable<Hap<T>> OpLeftQuery<TU>(TimeArc span, Pattern<TU> right, Func<T, TU, T> combine)
    {
        foreach (Hap<T> h in Query(span))
            foreach (Hap<TU> r in right.Query(h.Part))
            {
                TimeArc? part = h.Part.Intersect(r.Part);
                if (part is TimeArc p)
                    yield return new Hap<T>(h.Whole, p, combine(h.Value, r.Value));
            }
    }
}

/// <summary>パターンのコンストラクタ群 (Tidal のトップレベル関数相当)。</summary>
public static class Pat
{
    /// <summary>無音。</summary>
    public static Pattern<T> Silence<T>() => new(static _ => []);

    /// <summary>毎サイクル 1 回、サイクル全長のイベント。</summary>
    public static Pattern<T> Pure<T>(T v)
        => new(span => PureQuery(v, span));

    private static IEnumerable<Hap<T>> PureQuery<T>(T v, TimeArc span)
    {
        foreach (TimeArc s in span.SpanCycles())
        {
            Fraction sam = s.Begin.Sam;
            yield return new Hap<T>(new TimeArc(sam, s.Begin.NextSam), s, v);
        }
    }

    /// <summary>重ねる (同時再生)。</summary>
    public static Pattern<T> Stack<T>(params IReadOnlyList<Pattern<T>> ps)
        => ps.Count == 0 ? Silence<T>()
         : new(span => ps.SelectMany(p => p.Query(span)));

    /// <summary>1 サイクルに 1 パターンずつ順番に (slowcat)。各パターンは自分の番が来るたび
    /// 自分の「次のサイクル」を進める (Tidal と同じ)。</summary>
    public static Pattern<T> Cat<T>(params IReadOnlyList<Pattern<T>> ps)
        => ps.Count == 0 ? Silence<T>()
         : ps.Count == 1 ? ps[0]
         : new(span => CatQuery(ps, span));

    private static IEnumerable<Hap<T>> CatQuery<T>(IReadOnlyList<Pattern<T>> ps, TimeArc span)
    {
        int n = ps.Count;
        foreach (TimeArc s in span.SpanCycles())
        {
            long cyc = s.Begin.Floor;
            int i = (int)(((cyc % n) + n) % n);
            // 内側パターンはサイクル floor(cyc/n) を再生する
            long inner = cyc >= 0 ? cyc / n : (cyc - n + 1) / n;
            Fraction offset = new Fraction(cyc) - new Fraction(inner);
            foreach (Hap<T> h in ps[i].Query(s.WithTime(t => t - offset)))
                yield return h.WithTime(t => t + offset);
        }
    }

    /// <summary>1 サイクルに全パターンを均等詰め (fastcat = 標準のシーケンス)。</summary>
    public static Pattern<T> FastCat<T>(params IReadOnlyList<Pattern<T>> ps)
        => ps.Count == 0 ? Silence<T>() : Cat(ps).Fast(ps.Count);

    /// <summary>重み付きシーケンス (ミニ記法の @)。1 サイクルを重み比で分割し、
    /// 各パターンの 1 サイクル分をスロットへ圧縮する。</summary>
    public static Pattern<T> TimeCat<T>(IReadOnlyList<(Fraction Weight, Pattern<T> P)> parts)
    {
        if (parts.Count == 0) return Silence<T>();
        Fraction total = Fraction.Zero;
        foreach ((Fraction w, _) in parts) total += w;
        if (total.Num <= 0) return Silence<T>();
        var layers = new List<Pattern<T>>(parts.Count);
        Fraction pos = Fraction.Zero;
        foreach ((Fraction w, Pattern<T> p) in parts)
        {
            layers.Add(p.CompressSpan(pos / total, (pos + w) / total));
            pos += w;
        }
        return Stack(layers);
    }

    /// <summary>時間の決定的ハッシュ → [0,1) (Degrade/確率 ? 用)。既約分数の (Num, Den) を
    /// splitmix64 で混ぜる — 同じ時刻は常に同じ値 (snap/テストの決定性)。</summary>
    public static double TimeHash(Fraction t)
    {
        ulong z = (ulong)t.Num * 0x9E3779B97F4A7C15UL ^ ((ulong)t.Den << 32);
        z ^= z >> 30; z *= 0xBF58476D1CE4E5B9UL;
        z ^= z >> 27; z *= 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }
}
