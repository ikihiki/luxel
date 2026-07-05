using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Resources;

namespace Luxel.Input;

/// <summary>
/// アクション名 → バインド定義の JSON 表現 (INPUT-M6)。設定ファイルとして永続化し、
/// Resources 経由で hot-reload できる。<see cref="InputBindingsApplier.Apply"/> で <see cref="InputContext"/> に反映。
///
/// <code>
/// {
///   "Move":  { "kind": "axis2d", "quads": [["W","S","A","D"]] },
///   "Jump":  { "kind": "button", "keys": ["Space","GamepadA"] },
///   "LookX": { "kind": "axis1d", "axes": ["MouseX"] }
/// }
/// </code>
/// </summary>
public sealed class InputBindings
{
    [JsonPropertyName("actions")]
    public Dictionary<string, InputBindingEntry> Actions { get; set; } = new();
}

public sealed class InputBindingEntry
{
    /// <summary>"button" / "axis1d" / "axis2d"</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "button";

    /// <summary>ButtonAction 用: 押されているとき active になる KeyCode 群 (OR)。</summary>
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new();

    /// <summary>Axis1DAction 用: Positive/Negative の対 (WASD 右左 → +1/-1)。</summary>
    [JsonPropertyName("pairs")]
    public List<string[]> Pairs { get; set; } = new();

    /// <summary>Axis2DAction 用: Up/Down/Left/Right の 4 個組 (WASD 相当)。</summary>
    [JsonPropertyName("quads")]
    public List<string[]> Quads { get; set; } = new();

    /// <summary>Axis1D/Axis2D の 生 axis 名 (GamepadLeftStickX 等)。Axis2D では 2 個組で対解釈。</summary>
    [JsonPropertyName("axes")]
    public List<string> Axes { get; set; } = new();
}

/// <summary>InputBindings JSON → InputContext への反映ヘルパ。</summary>
public static class InputBindingsApplier
{
    public static void Apply(InputBindings bindings, InputContext ctx)
    {
        foreach (var action in ctx.Actions)
        {
            if (!bindings.Actions.TryGetValue(action.Name, out var entry)) continue;
            switch (action)
            {
                case ButtonAction b:
                    b.Keys.Clear();
                    foreach (var k in entry.Keys)
                        if (Enum.TryParse<KeyCode>(k, ignoreCase: true, out var kc)) b.Keys.Add(kc);
                    break;
                case Axis1DAction a1:
                    a1.ButtonPairs.Clear();
                    foreach (var pair in entry.Pairs)
                    {
                        if (pair.Length >= 2
                            && Enum.TryParse<KeyCode>(pair[0], true, out var pos)
                            && Enum.TryParse<KeyCode>(pair[1], true, out var neg))
                            a1.ButtonPairs.Add((pos, neg));
                    }
                    a1.Axes.Clear();
                    foreach (var a in entry.Axes)
                        if (Enum.TryParse<AxisCode>(a, true, out var ac)) a1.Axes.Add(ac);
                    break;
                case Axis2DAction a2:
                    a2.ButtonQuads.Clear();
                    foreach (var q in entry.Quads)
                    {
                        if (q.Length >= 4
                            && Enum.TryParse<KeyCode>(q[0], true, out var u)
                            && Enum.TryParse<KeyCode>(q[1], true, out var d)
                            && Enum.TryParse<KeyCode>(q[2], true, out var l)
                            && Enum.TryParse<KeyCode>(q[3], true, out var r))
                            a2.ButtonQuads.Add((u, d, l, r));
                    }
                    a2.AxisPairs.Clear();
                    for (int i = 0; i + 1 < entry.Axes.Count; i += 2)
                        if (Enum.TryParse<AxisCode>(entry.Axes[i], true, out var ax)
                            && Enum.TryParse<AxisCode>(entry.Axes[i + 1], true, out var ay))
                            a2.AxisPairs.Add((ax, ay));
                    break;
            }
        }
    }
}

/// <summary>byte[] JSON → InputBindings の Resources ステップ (fragment 不要、拡張子 .keys/.json)。</summary>
public sealed class InputBindingsStep : IResourceStep<byte[], InputBindings>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => new[] { ".keys", ".json" };

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public Task<InputBindings> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        string text = System.Text.Encoding.UTF8.GetString(input);
        var b = JsonSerializer.Deserialize<InputBindings>(text, _opts) ?? new InputBindings();
        return Task.FromResult(b);
    }
}
