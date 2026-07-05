using System.Numerics;
using Luxel.UI;

namespace Luxel.Input;

/// <summary>
/// アクションの基底。Update() で bus の生 event を解決して <see cref="IsActive"/> と rising/falling edge を出す。
/// 型ごとの Value ({bool, float, Vector2}) を持つ 3 種 (<see cref="ButtonAction"/> / <see cref="Axis1DAction"/> / <see cref="Axis2DAction"/>) を派生。
/// </summary>
public abstract class InputAction
{
    public string Name { get; }
    /// <summary>「値がゼロでない/押されている」を示す boolean。<see cref="Triggered"/> / <see cref="Released"/> はここの遷移で発火。</summary>
    public Signal<bool> IsActive { get; } = new Signal<bool>(false);
    public event Action? Triggered;
    public event Action? Released;

    protected InputAction(string name) { Name = name; }

    internal abstract void Update(HashSet<KeyCode> held, Dictionary<AxisCode, float> axes);

    protected void SetActive(bool active)
    {
        if (IsActive.Value == active) return;
        IsActive.Value = active;
        if (active) Triggered?.Invoke();
        else Released?.Invoke();
    }
}

/// <summary>ボタン (bool) アクション。バインドされた任意の <see cref="KeyCode"/> が押されていれば active。</summary>
public sealed class ButtonAction : InputAction
{
    public Signal<bool> Value => IsActive;
    /// <summary>OR で合成される複数バインド (例: Space または GamepadA)。</summary>
    public List<KeyCode> Keys { get; } = new();

    public ButtonAction(string name, params KeyCode[] keys) : base(name)
    {
        Keys.AddRange(keys);
    }

    internal override void Update(HashSet<KeyCode> held, Dictionary<AxisCode, float> axes)
    {
        bool any = false;
        foreach (var k in Keys) if (held.Contains(k)) { any = true; break; }
        SetActive(any);
    }
}

/// <summary>1D 軸 (float) アクション。WASD の左右 → -1..1 のような ボタン対 + 生 axis を統合。</summary>
public sealed class Axis1DAction : InputAction
{
    public Signal<float> Value { get; } = new Signal<float>(0f);
    /// <summary>Positive - Negative でボタン対から軸を作る (WASD 相当)。複数対も OR 統合。</summary>
    public List<(KeyCode Positive, KeyCode Negative)> ButtonPairs { get; } = new();
    /// <summary>生 axis を直接バインド (GamepadLeftStickX 等、正規化前提)。</summary>
    public List<AxisCode> Axes { get; } = new();

    public Axis1DAction(string name) : base(name) { }

    internal override void Update(HashSet<KeyCode> held, Dictionary<AxisCode, float> axes)
    {
        float v = 0f;
        foreach (var (pos, neg) in ButtonPairs)
        {
            float p = held.Contains(pos) ? 1f : 0f;
            float n = held.Contains(neg) ? 1f : 0f;
            v += p - n;
        }
        foreach (var a in Axes)
            if (axes.TryGetValue(a, out float av)) v += av;
        v = Math.Clamp(v, -1f, 1f);
        Value.Value = v;
        SetActive(MathF.Abs(v) > 0.01f);
    }
}

/// <summary>2D 軸 (Vector2) アクション。上下左右のボタン 4 個 or アナログスティックの XY 対。</summary>
public sealed class Axis2DAction : InputAction
{
    public Signal<Vector2> Value { get; } = new Signal<Vector2>(Vector2.Zero);
    /// <summary>4 方向ボタン (例: WASD, 矢印) を統合 (X = Right - Left, Y = Up - Down)。</summary>
    public List<(KeyCode Up, KeyCode Down, KeyCode Left, KeyCode Right)> ButtonQuads { get; } = new();
    /// <summary>(X 軸, Y 軸) の対 (例: GamepadLeftStickX/Y)。</summary>
    public List<(AxisCode X, AxisCode Y)> AxisPairs { get; } = new();

    public Axis2DAction(string name) : base(name) { }

    internal override void Update(HashSet<KeyCode> held, Dictionary<AxisCode, float> axes)
    {
        Vector2 v = Vector2.Zero;
        foreach (var (u, d, l, r) in ButtonQuads)
        {
            float dx = (held.Contains(r) ? 1f : 0f) - (held.Contains(l) ? 1f : 0f);
            float dy = (held.Contains(u) ? 1f : 0f) - (held.Contains(d) ? 1f : 0f);
            v += new Vector2(dx, dy);
        }
        foreach (var (ax, ay) in AxisPairs)
        {
            float x = axes.TryGetValue(ax, out float xv) ? xv : 0f;
            float y = axes.TryGetValue(ay, out float yv) ? yv : 0f;
            v += new Vector2(x, y);
        }
        if (v.LengthSquared() > 1f) v = Vector2.Normalize(v);
        Value.Value = v;
        SetActive(v.LengthSquared() > 0.0001f);
    }
}
