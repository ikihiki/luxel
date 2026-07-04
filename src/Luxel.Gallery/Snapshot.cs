using Luxel.Samples;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// スナップショット回帰 (`-- vk snap [--update]`)。全ストーリーを決定的に描画して
/// golden PNG と比較する。`--update` で golden を書き換える。
/// アニメ (Spinner 等) は固定 dt で N ステップ進めるので決定的。
/// </summary>
public static class Snapshot
{
    private const int WarmupSteps = 8;          // 固定 1/60s × 8 (アニメ/caret を決定的に進める)
    private const float FixedDt = 1f / 60f;

    public static int Run(GalleryHost host, IReadOnlyList<StoryInfo> stories, string backend, bool update)
    {
        string dir = GoldenDir();
        Directory.CreateDirectory(dir);
        Console.WriteLine($"goldens: {dir} ({(update ? "update" : "verify")})");

        int failed = 0, updated = 0, passed = 0;
        foreach (StoryInfo story in stories)
        {
            host.Select(story.Path);
            for (int i = 0; i < WarmupSteps; i++) host.Step(FixedDt);
            // ハイライト (別スレッド解析) の静定待ち → 到着ドレイン分を dt=0 で追加 Step
            // (アニメ時間を進めない — アニメ系 golden を変えずに色付けだけ確定させる)
            if (!Luxel.Controls.HighlightQueue.WaitIdle(15000))
                Console.Error.WriteLine($"  WARN {story.Path}: ハイライト静定待ちタイムアウト");
            for (int i = 0; i < 2; i++) host.Step(0f);
            (byte[] rgba, int w, int h)? snap = host.SnapshotRgba();
            if (snap is null) { Console.Error.WriteLine($"  SKIP {story.Path} (no frame)"); continue; }

            // golden はバックエンド別 (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)
            string file = Path.Combine(dir, Sanitize(story.Path) + $".{backend}.png");
            byte[] png = EncodePng(snap.Value.w, snap.Value.h, snap.Value.rgba);

            if (update)
            {
                File.WriteAllBytes(file, png);
                updated++;
                Console.WriteLine($"  UPDATE {story.Path}");
                continue;
            }

            if (!File.Exists(file))
            {
                failed++;
                Console.Error.WriteLine($"  MISSING {story.Path} (golden なし — --update で生成)");
                continue;
            }

            byte[] golden = File.ReadAllBytes(file);
            if (golden.AsSpan().SequenceEqual(png)) { passed++; continue; }

            failed++;
            string actual = Path.Combine(dir, Sanitize(story.Path) + $".{backend}.actual.png");
            File.WriteAllBytes(actual, png);
            Console.Error.WriteLine($"  DIFF {story.Path} → {Path.GetFileName(actual)}");
        }

        Console.WriteLine(update
            ? $"snap: {updated} 件の golden を更新"
            : $"snap: passed={passed} failed={failed} / {stories.Count}");
        return update || failed == 0 ? 0 : 1;
    }

    private static byte[] EncodePng(int w, int h, byte[] rgba)
    {
        string tmp = Path.GetTempFileName();
        try
        {
            PngWriter.WriteRgba(tmp, w, h, rgba);
            return File.ReadAllBytes(tmp);
        }
        finally { File.Delete(tmp); }
    }

    private static string Sanitize(string path)
    {
        var sb = new System.Text.StringBuilder(path.Length);
        foreach (char c in path) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    private static string GoldenDir()
    {
        // リポジトリ内 (src/Luxel.Gallery/goldens) を優先、見つからなければ実行ディレクトリ
        string cwd = Environment.CurrentDirectory;
        string repoPath = Path.Combine(cwd, "src", "Luxel.Gallery");
        if (Directory.Exists(repoPath)) return Path.Combine(repoPath, "goldens");
        return Path.Combine(AppContext.BaseDirectory, "goldens");
    }
}
