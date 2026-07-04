using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// canvas 更新コストのマイクロベンチ (`-- vk|dx bench <story> [frames] [--type] [--click x y] [--wheel d]`)。
/// snap と同じ offscreen 駆動でストーリーを固定 dt で回し、<see cref="Luxel.TwoD.RetainedCanvas"/> の
/// 累積統計 (フル再構築回数/時間、アップロードバイト) を区間計測して出力する。
/// IC 計画 (docs/CANVAS_INCREMENTAL_PLAN.md) のベースライン計測と回帰ゲートに使う。
/// - `--type`: 毎フレーム 1 文字タイプ (エディタのタイプ連打シナリオ)。`--click x y` でフォーカス先を指定。
/// - `--wheel d`: 毎フレーム delta d のホイール (仮想化リストのスクロール再バインドシナリオ)。
/// </summary>
public static class Bench
{
    private const float FixedDt = 1f / 60f;
    private const int WarmupSteps = 8;

    public static int Run(GalleryHost host, string storyPath, int frames, bool type, (float x, float y)? click, float wheel = 0)
    {
        if (StoryRegistry.Find(storyPath) is null)
        {
            Console.Error.WriteLine($"bench: ストーリーが見つかりません: {storyPath}");
            return 1;
        }

        host.Select(storyPath);
        for (int i = 0; i < WarmupSteps; i++) host.Step(FixedDt);

        UiHost ui = host.Host!;
        (float cx, float cy) = click ?? (host.Size.w / 2f, host.Size.h / 2f);   // 既定: 中央 (テキスト系の本文)
        if (type || click is not null) { ui.PointerDown(cx, cy); ui.PointerUp(cx, cy); }
        host.Step(FixedDt);   // クリック反映

        var canvas = host.Canvas!;
        canvas.ResetStats();
        int gc0 = GC.CollectionCount(0);
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < frames; i++)
        {
            if (type) ui.Char("a");
            if (wheel != 0) ui.Wheel(cx, cy, wheel);
            host.Step(FixedDt);
        }

        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        int gc = GC.CollectionCount(0) - gc0;

        Console.WriteLine($"bench '{storyPath}' frames={frames}{(type ? " +type" : "")}{(wheel != 0 ? $" +wheel {wheel}" : "")}  " +
            $"scene: segments={canvas.SegmentCount} paths={canvas.PathCount}");
        Console.WriteLine($"  flushes           : {canvas.TotalFlushes} ({canvas.TotalFlushes * 100.0 / frames:0.#}% of frames)");
        Console.WriteLine($"  full rebuilds     : {canvas.TotalRebuilds} ({canvas.TotalRebuilds * 100.0 / Math.Max(1, canvas.TotalFlushes):0.#}% of flushes)");
        Console.WriteLine($"  rebuild cpu       : total {canvas.TotalRebuildMicros / 1000.0:0.##} ms, avg {canvas.TotalRebuildMicros / 1000.0 / Math.Max(1, canvas.TotalRebuilds):0.###} ms/rebuild");
        Console.WriteLine($"  upload            : total {canvas.TotalUploadBytes / 1024.0 / 1024.0:0.##} MB, avg {canvas.TotalUploadBytes / 1024.0 / Math.Max(1, canvas.TotalFlushes):0.#} KB/flush, {canvas.TotalUploadBytes * 60.0 / frames / 1024.0 / 1024.0:0.#} MB/s @60fps");
        Console.WriteLine($"  managed alloc     : {alloc / 1024.0 / 1024.0:0.##} MB ({alloc / 1024.0 / Math.Max(1, frames):0.#} KB/frame), gen0 GC ×{gc}");
        Console.WriteLine($"  wall              : {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalMilliseconds / frames:0.##} ms/frame incl. GPU+readback)");
        return 0;
    }
}
