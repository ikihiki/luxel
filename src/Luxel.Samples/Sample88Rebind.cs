using System.Numerics;
using System.Text.Json;
using Luxel;
using Luxel.Input;

namespace Luxel.Samples;

/// <summary>
/// Sample 88 (INPUT-M8): キーバインドのリマップ + JSON round trip を実演。
///
/// 1. 初期 binding = WASD → W 押下で Move.Value = (0,1)
/// 2. InputBindings JSON を <c>{"Move": {"kind":"axis2d", "quads":[["Up","Down","Left","Right"]]}}</c> に差替
/// 3. <see cref="InputBindingsApplier.Apply"/> で InputContext を更新
/// 4. W 押下 → 反応なし (WASD の binding が消えた)
/// 5. Up 押下 → Move.Value = (0,1) (arrow keys に切り替わった)
/// 6. JSON serialize/deserialize の round trip で bindings が保存/復元できることを検証
///
/// XInputSource も Windows 上で <c>xinput1_4.dll</c> が存在すれば動作 (実 controller は sample の対象外)。
/// </summary>
public static class Sample88Rebind
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 88: Input Binding Remap + JSON round trip ===");

        var bus = new InputBus();
        var fake = new FakeInputSource();
        var ctx = new InputContext("gameplay");
        var move = ctx.Add(new Axis2DAction("Move"));
        move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));   // 初期 = WASD
        var stack = new InputStack();
        stack.Push(ctx);

        Vector2 Step(FakeInputSource s, Action<FakeInputSource> setup)
        {
            setup(s);
            s.Poll(bus);
            stack.Update(bus);
            return move.Value.Value;
        }

        // === Stage 1: 初期 WASD binding ===
        Console.WriteLine("  [Stage 1] 初期 binding = WASD");
        Vector2 v1a = Step(fake, s => s.PressKey(KeyCode.W));
        Console.WriteLine($"    W press → move={v1a}");
        Vector2 v1b = Step(fake, s => { s.ReleaseKey(KeyCode.W); s.PressKey(KeyCode.Up); });
        Console.WriteLine($"    Up press → move={v1b}   (arrows 未 binding なので 0)");

        // === Stage 2: JSON で rebind to arrows ===
        Console.WriteLine("  [Stage 2] JSON rebind to arrows");
        var bindings = new InputBindings
        {
            Actions =
            {
                ["Move"] = new InputBindingEntry
                {
                    Kind = "axis2d",
                    Quads = { new[] { "Up", "Down", "Left", "Right" } },
                },
            },
        };
        InputBindingsApplier.Apply(bindings, ctx);
        // 既存の held を clear (release イベントを bus に流す)
        fake.ReleaseKey(KeyCode.Up);
        fake.Poll(bus); stack.Update(bus);

        Vector2 v2a = Step(fake, s => s.PressKey(KeyCode.W));
        Console.WriteLine($"    W press → move={v2a}   (WASD binding 剥がれた → 0)");
        Vector2 v2b = Step(fake, s => { s.ReleaseKey(KeyCode.W); s.PressKey(KeyCode.Up); });
        Console.WriteLine($"    Up press → move={v2b}");

        // === Stage 3: JSON round trip ===
        Console.WriteLine("  [Stage 3] JSON serialize/deserialize round trip");
        var opts = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(bindings, opts);
        Console.WriteLine($"    serialized JSON:\n{json}");
        var restored = JsonSerializer.Deserialize<InputBindings>(json, opts)!;
        bool jsonRoundtrip = restored.Actions.ContainsKey("Move")
                          && restored.Actions["Move"].Kind == "axis2d"
                          && restored.Actions["Move"].Quads.Count == 1
                          && restored.Actions["Move"].Quads[0].SequenceEqual(new[] { "Up", "Down", "Left", "Right" });

        // 検証
        bool initialWasdWorks = v1a == new Vector2(0, 1);
        bool initialArrowSilent = v1b == Vector2.Zero;
        bool remappedWasdSilent = v2a == Vector2.Zero;
        bool remappedArrowWorks = v2b == new Vector2(0, 1);
        Console.WriteLine($"  initial WASD works: {initialWasdWorks}, initial arrow silent: {initialArrowSilent}");
        Console.WriteLine($"  remapped WASD silent: {remappedWasdSilent}, remapped arrow works: {remappedArrowWorks}");
        Console.WriteLine($"  JSON round trip: {jsonRoundtrip}");

        bool ok = initialWasdWorks && initialArrowSilent && remappedWasdSilent && remappedArrowWorks && jsonRoundtrip;
        Console.WriteLine(ok ? "OK: rebind + JSON round trip が動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
