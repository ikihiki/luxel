using Luxel;
using Luxel.Scene.UI;

namespace Luxel.Samples;

/// <summary>
/// Sample 74: <see cref="PlaybackState"/> の動作確認。time tick / loop / 反転再生。
/// UI Widget は host 不要な console-only demo (Signal だけ動かす)。
/// </summary>
public static class Sample74PlaybackState
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 74: PlaybackState demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var state = new PlaybackState();
        state.Duration.Value = 2.0f;
        state.IsPlaying.Value = true;
        state.Speed.Value = 1.0f;
        state.Looped.Value = true;

        // 3 回 tick: 0.0 → 1.0 → 2.0 → 0.5 (loop)
        state.Tick(1.0f);
        Console.WriteLine($"  after tick 1.0s: {state.CurrentTime}");
        state.Tick(1.0f);
        Console.WriteLine($"  after tick 1.0s: {state.CurrentTime} (loop)");
        state.Tick(0.5f);
        Console.WriteLine($"  after tick 0.5s: {state.CurrentTime}");

        // Stop で reset
        state.IsPlaying.Value = false;
        state.CurrentTime.Value = 0;
        Console.WriteLine($"  stopped: playing={state.IsPlaying}, t={state.CurrentTime}");

        // Speed 0.5x
        state.IsPlaying.Value = true;
        state.Speed.Value = 0.5f;
        state.Tick(1.0f);
        Console.WriteLine($"  with 0.5x speed, tick 1.0s: {state.CurrentTime} (expect 0.5s)");

        bool ok = Math.Abs(state.CurrentTime.Value - 0.5f) < 1e-3;
        Console.WriteLine(ok ? "OK: SC-UI-M1 (PlaybackState tick + loop + speed) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
