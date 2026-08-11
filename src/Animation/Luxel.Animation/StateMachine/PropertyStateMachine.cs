namespace Luxel.Animation;

/// <summary>
/// プロパティ値の状態機械 (AS-M2、variants 型 — Framer Motion 相当)。クリップグラフ用
/// <see cref="StateMachine"/> と並ぶ第二の機械で、「状態 = プロパティ値の集合」を
/// <see cref="TransitionTable"/> (from / to / from→to × プロパティ) の設定でプロパティ毎に独立 tween する。
///
/// 意味論 (UI の不変条件を機械に内蔵):
/// - <see cref="Start(string)"/> は瞬時適用 (Realize 直後は静止値 — snap golden を揺らさない)
/// - 遷移途中の <see cref="Goto(string, IClock)"/> は各プロパティを**現在のアニメ値起点**で
///   再スタート (smooth interrupt) — 連打しても値はジャンプしない
/// - **動的状態**: <see cref="Goto(string, IClock, IReadOnlyDictionary{string, object})"/> は
///   値をその場で与える。状態名は from/to 解決キー、値は都度の目標 — 非有界な状態空間
///   (リスト選択行/スクロールオフセット等) を状態登録なしで表す。同名でも値が違えば retarget。
/// - 静定中の <see cref="Tick"/> は sink を呼ばない (アイドル書き込みゼロ)
/// - 目標状態に無いプロパティは Start 状態 (base) の値へ戻す (CSS の「ルールなし = 基底値」)
/// </summary>
public sealed class PropertyStateMachine
{
    private readonly TransitionTable _table;
    private readonly Dictionary<string, Dictionary<string, object>> _states = new();
    private readonly Dictionary<string, Channel> _channels = new();
    private Dictionary<string, object>? _base;
    private string _current = "";
    private bool _started;

    public PropertyStateMachine(TransitionTable table)
        => _table = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>現在の (遷移中は遷移先の) 状態名。</summary>
    public string Current => _current;

    /// <summary>いずれかのプロパティが遷移中か。</summary>
    public bool IsTransitioning
    {
        get
        {
            foreach (Channel c in _channels.Values)
                if (c.Active) return true;
            return false;
        }
    }

    /// <summary>登録状態を追加する (値は固定)。動的状態しか使わない場合は登録不要。</summary>
    public PropertyStateMachine AddState(string name, IReadOnlyDictionary<string, object> values)
    {
        _states[name] = new Dictionary<string, object>(values);
        return this;
    }

    /// <summary>初期状態を瞬時適用する。この状態の値が base (欠落プロパティの戻り先) になる。</summary>
    public void Start(string state)
        => Start(state, _states.TryGetValue(state, out var v) ? v
            : throw new InvalidOperationException($"未登録の状態: {state}"));

    /// <summary>初期状態を瞬時適用する (動的値)。</summary>
    public void Start(string state, IReadOnlyDictionary<string, object> values)
    {
        _started = true;
        _current = state;
        _base = new Dictionary<string, object>(values);
        foreach ((string prop, object v) in values) GetChannel(prop, v).Snap(v);
    }

    /// <summary>登録状態へ遷移する。未 Start なら Start (瞬時) に縮退。</summary>
    public void Goto(string state, IClock clock)
        => Goto(state, clock, _states.TryGetValue(state, out var v) ? v
            : throw new InvalidOperationException($"未登録の状態: {state}"));

    /// <summary>動的状態へ遷移する — 状態名は TransitionTable の from/to 解決キー、値は都度の目標。
    /// 各プロパティは現在のアニメ値起点で新目標へ (smooth interrupt)。同一目標のプロパティは触らない。</summary>
    public void Goto(string state, IClock clock, IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!_started) { Start(state, values); return; }
        string from = _current;
        _current = state;
        float now = clock.TimeSec;

        foreach ((string prop, object v) in values)
            GetChannel(prop, v).Retarget(v, _table.Resolve(from, state, prop), now);

        // 目標に無いプロパティ → base 値へ戻す (base にも無ければ現状維持)
        if (_base != null)
        {
            foreach ((string prop, Channel ch) in _channels)
            {
                if (values.ContainsKey(prop)) continue;
                if (_base.TryGetValue(prop, out object? bv))
                    ch.Retarget(bv, _table.Resolve(from, state, prop), now);
            }
        }
    }

    private readonly Dictionary<string, object> _single = new(1);

    /// <summary>単一プロパティの動的遷移 (辞書 alloc なしの糖衣 — setter ラッパ等の高頻度呼び出し用)。</summary>
    public void Goto(string state, IClock clock, string prop, object value)
    {
        _single.Clear();
        _single[prop] = value;
        Goto(state, clock, _single);
    }

    /// <summary>プロパティの書き出し先を束ねる。既に値があれば即座に 1 回呼ばれる。</summary>
    public void Bind<T>(string prop, Action<T> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Channel ch = _channels.TryGetValue(prop, out Channel? c) ? c : (_channels[prop] = new Channel<T>());
        ((Channel<T>)ch).AddSink(sink);
    }

    /// <summary>プロパティの現在値。</summary>
    public T Get<T>(string prop) => ((Channel<T>)_channels[prop]).Current;

    /// <summary>毎フレーム評価。静定中のプロパティには何もしない (sink 書き込みゼロ)。</summary>
    public void Tick(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        float now = clock.TimeSec;
        foreach (Channel c in _channels.Values) c.Tick(now);
    }

    private Channel GetChannel(string prop, object sample)
    {
        if (_channels.TryGetValue(prop, out Channel? c)) return c;
        Channel n = sample switch
        {
            float => new Channel<float>(),
            uint => new Channel<uint>(),
            System.Numerics.Vector2 => new Channel<System.Numerics.Vector2>(),
            System.Numerics.Vector3 => new Channel<System.Numerics.Vector3>(),
            System.Numerics.Vector4 => new Channel<System.Numerics.Vector4>(),
            System.Numerics.Quaternion => new Channel<System.Numerics.Quaternion>(),
            _ => new Channel<object>(),   // 不明型 = Step (t<0.5 で from、以上で to)
        };
        _channels[prop] = n;
        return n;
    }

    // ---- プロパティ毎の独立 tween チャネル ----

    private abstract class Channel
    {
        public abstract bool Active { get; }
        public abstract void Snap(object value);
        public abstract void Retarget(object target, TransitionSpec? spec, float now);
        public abstract void Tick(float now);
    }

    private sealed class Channel<T> : Channel
    {
        private T _current = default!, _target = default!;
        private ITween<T> _tween = default!;
        private ICurve _curve = OutCubicCurve.Instance;
        private float _start, _duration;
        private bool _active, _has;
        private Action<T>? _sink;

        public T Current => _current;
        public override bool Active => _active;

        public void AddSink(Action<T> sink)
        {
            _sink += sink;
            if (_has) sink(_current);   // 束ねた時点の値を即配信 (effect の初回実行相当)
        }

        public override void Snap(object value)
        {
            var v = (T)value;
            _current = _target = v;
            _active = false;
            _has = true;
            _sink?.Invoke(v);
        }

        public override void Retarget(object target, TransitionSpec? spec, float now)
        {
            var to = (T)target;
            if (_has && EqualityComparer<T>.Default.Equals(_target, to)) return;   // 同一目標 — 触らない
            if (!_has || spec is not { Duration: > 0f } s)
            {
                Snap(target);   // 設定なし/duration 0 = 瞬時
                return;
            }
            _tween = Transition.CreateTween(_current, to);   // 現在値起点 (smooth interrupt)
            _target = to;
            _curve = s.Curve ?? OutCubicCurve.Instance;
            _start = now + s.Delay;
            _duration = s.Duration;
            _active = true;
        }

        public override void Tick(float now)
        {
            if (!_active) return;
            float t = (now - _start) / _duration;
            if (t < 0f) return;   // delay 中は保持 (書き込みもしない)
            if (t >= 1f)
            {
                _current = _tween.Lerp(1f);
                _active = false;
            }
            else
            {
                _current = _tween.Lerp(_curve.Eval(t));
            }
            _sink?.Invoke(_current);
        }
    }
}
