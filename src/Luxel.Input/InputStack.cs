namespace Luxel.Input;

/// <summary>
/// InputContext のスタック。<see cref="Update"/> 時に上位から順に action を解決し、
/// 上位 context の action が使ったキーは下位に伝わらない (default consume)。
///
/// <c>Suspended</c> が true の context は Update されない (UI focus 中の gameplay context を suspend する等)。
/// </summary>
public sealed class InputStack
{
    private readonly List<Layer> _layers = new();
    // 累積 held/axes ─ bus の event は差分 (KeyDown/KeyUp)、Update は state を進める
    private readonly HashSet<KeyCode> _persistHeld = new();
    private readonly Dictionary<AxisCode, float> _persistAxes = new();

    private struct Layer { public InputContext Context; public bool Suspended; }

    /// <summary>context を上位に積む (最後に Push したものが最優先)。</summary>
    public void Push(InputContext ctx) => _layers.Add(new Layer { Context = ctx, Suspended = false });

    /// <summary>最上位を取り除く。</summary>
    public InputContext? Pop()
    {
        if (_layers.Count == 0) return null;
        var top = _layers[^1].Context;
        _layers.RemoveAt(_layers.Count - 1);
        return top;
    }

    /// <summary>指定 context を suspend する (Update から除外)。UI focus 検出などで使用。</summary>
    public void SetSuspended(InputContext ctx, bool suspended)
    {
        for (int i = 0; i < _layers.Count; i++)
            if (ReferenceEquals(_layers[i].Context, ctx))
            {
                var layer = _layers[i]; layer.Suspended = suspended; _layers[i] = layer;
                return;
            }
    }

    public IReadOnlyList<InputContext> Contexts
    {
        get { var list = new List<InputContext>(_layers.Count); foreach (var l in _layers) list.Add(l.Context); return list; }
    }

    /// <summary>
    /// bus に溜まった event を反映し、全 context の action を更新する。
    /// 上位 layer から順に action の使用キーを "consume" し、下位には残りだけを渡す。
    /// </summary>
    public void Update(InputBus bus)
    {
        // bus の差分イベントを累積 state に反映 (KeyDown で追加、KeyUp で除去)
        foreach (var e in bus.Events)
        {
            switch (e.Kind)
            {
                case InputEventKind.KeyDown: _persistHeld.Add(e.Key); break;
                case InputEventKind.KeyUp:   _persistHeld.Remove(e.Key); break;
                case InputEventKind.AxisChanged: _persistAxes[e.Axis] = e.Value; break;
            }
        }
        bus.Clear();   // 消費: 次フレームの差分だけを bus に

        var held = _persistHeld;
        var axes = _persistAxes;

        // 2) 上位から Update しつつ「使ったキー」を集めて下位から除外
        var consumedKeys = new HashSet<KeyCode>();
        var consumedAxes = new HashSet<AxisCode>();
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (layer.Suspended)
            {
                // suspended: action の IsActive を落とす (false 化) だけ
                foreach (var a in layer.Context.Actions) UpdateWithEmpty(a);
                continue;
            }

            // この layer 用の filtered set を作る
            var localHeld = FilterOut(held, consumedKeys);
            var localAxes = FilterAxes(axes, consumedAxes);
            foreach (var a in layer.Context.Actions)
            {
                a.Update(localHeld, localAxes);
                if (a.IsActive.Value)
                {
                    // active な action が使ってるキー/軸を consume に登録
                    switch (a)
                    {
                        case ButtonAction b: foreach (var k in b.Keys) consumedKeys.Add(k); break;
                        case Axis1DAction a1:
                            foreach (var (p, n) in a1.ButtonPairs) { consumedKeys.Add(p); consumedKeys.Add(n); }
                            foreach (var x in a1.Axes) consumedAxes.Add(x);
                            break;
                        case Axis2DAction a2:
                            foreach (var (u, d, l, r) in a2.ButtonQuads)
                                { consumedKeys.Add(u); consumedKeys.Add(d); consumedKeys.Add(l); consumedKeys.Add(r); }
                            foreach (var (ax, ay) in a2.AxisPairs) { consumedAxes.Add(ax); consumedAxes.Add(ay); }
                            break;
                    }
                }
            }
        }
    }

    private static HashSet<KeyCode> FilterOut(HashSet<KeyCode> src, HashSet<KeyCode> consumed)
    {
        if (consumed.Count == 0) return src;
        var r = new HashSet<KeyCode>(src.Count);
        foreach (var k in src) if (!consumed.Contains(k)) r.Add(k);
        return r;
    }

    private static Dictionary<AxisCode, float> FilterAxes(Dictionary<AxisCode, float> src, HashSet<AxisCode> consumed)
    {
        if (consumed.Count == 0) return src;
        var r = new Dictionary<AxisCode, float>(src.Count);
        foreach (var (k, v) in src) if (!consumed.Contains(k)) r[k] = v;
        return r;
    }

    private static void UpdateWithEmpty(InputAction a)
    {
        a.Update(new HashSet<KeyCode>(), new Dictionary<AxisCode, float>());
    }
}
