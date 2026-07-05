using Luxel.Audio.Sequencing;

namespace Luxel.Strudel;

/// <summary>
/// サイクルクロック + 先読みクエリ。スロット (d1/d2… 相当) に置かれたパターンを
/// 固定幅の時間窓でクエリし、onset を持つイベントを <see cref="ScheduledEvent"/> にして
/// 全 sink へ配送する。**窓 1 つ = ミキサのチャンク 1 つ** — 呼び出し側 (REPL の Tick) が
/// 「ミキサのキューが減ったら <see cref="RenderWindow"/>」で駆動する。
/// <list type="bullet">
/// <item>サイクル位置は <see cref="Fraction"/> で厳密に進む — float 蓄積の位相ずれなし</item>
/// <item>評価 = <see cref="SetPattern"/> によるホットスワップ。クロックは止まらず、
///   次の窓 (≤ 先読み分, 数百 ms) から新パターンが鳴る</item>
/// <item><see cref="Hush"/> = 全スロット破棄 + sink へ伝播</item>
/// </list>
/// </summary>
public sealed class StrudelScheduler(double chunkSeconds = 0.1)
{
    private readonly List<IEventSink> _sinks = new();
    private readonly SortedDictionary<int, Pattern<ControlMap>> _slots = new();
    private readonly List<ScheduledEvent> _buf = new();

    private Fraction _cyclePos = Fraction.Zero;    // クエリ済みのサイクル位置
    private long _window;                           // 発行済みの窓数 (秒 = _window * chunk)
    private Fraction _chunk = FromDouble(chunkSeconds);
    private Fraction _cps = new(1, 2);              // 既定 0.5 cps (= 120 BPM 4 つ打ち相当)

    /// <summary>テンポ (cycles per second)。次の窓から効く。</summary>
    public double Cps
    {
        get => _cps.ToDouble();
        set => _cps = FromDouble(value <= 0 ? 0.5 : value);
    }

    /// <summary>現在のサイクル位置 (クエリ済み地点 — 表示は再生位置と先読み分ずれる)。</summary>
    public double CyclePos => _cyclePos.ToDouble();

    /// <summary>発行済み窓の末尾時刻 (秒)。</summary>
    public double RenderedSeconds => _window * _chunk.ToDouble();

    public void AddSink(IEventSink sink) => _sinks.Add(sink);

    /// <summary>スロットへパターンを置く/差し替える (null = スロット停止)。</summary>
    public void SetPattern(int slot, Pattern<ControlMap>? pattern)
    {
        if (pattern is null) _slots.Remove(slot);
        else _slots[slot] = pattern;
    }

    /// <summary>全停止。スロットを空にし sink へ Hush を伝える (クロックは進み続ける)。</summary>
    public void Hush()
    {
        _slots.Clear();
        foreach (IEventSink s in _sinks) s.Hush();
    }

    /// <summary>窓 1 つ分クエリして配送する。ミキサはこの呼び出しでチャンク 1 つを焼く。</summary>
    public void RenderWindow()
    {
        // 時刻は Fraction で厳密に組んでから double へ落とす — 窓数 × 0.1 の蓄積誤差を出さない
        Fraction wsF = _chunk * new Fraction(_window);
        double ws = wsF.ToDouble();
        double we = (_chunk * new Fraction(_window + 1)).ToDouble();
        Fraction cycLen = _chunk * _cps;
        Fraction c0 = _cyclePos, c1 = _cyclePos + cycLen;

        _buf.Clear();
        foreach (Pattern<ControlMap> p in _slots.Values)
        {
            foreach (Hap<ControlMap> h in p.Query(new TimeArc(c0, c1)))
            {
                if (!h.HasOnset) continue;   // 断片は前の窓で発音済み
                double t = (wsF + (h.Part.Begin - c0) / _cps).ToDouble();
                _buf.Add(new ScheduledEvent(t, (h.Duration / _cps).ToDouble(), h.Value));
            }
        }
        _buf.Sort(static (a, b) => a.Time.CompareTo(b.Time));

        ScheduledEvent[] events = _buf.ToArray();
        foreach (IEventSink s in _sinks) s.Schedule(events, ws, we);

        _cyclePos = c1;
        _window++;
    }

    /// <summary>double → 有理数 (分母 10000 上限で丸め)。cps/チャンク幅の内部表現用。</summary>
    private static Fraction FromDouble(double v)
        => new((long)Math.Round(v * 10000), 10000);
}
