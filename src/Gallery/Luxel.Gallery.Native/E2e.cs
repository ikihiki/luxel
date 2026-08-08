using Luxel.DevTools;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// E2E ランナー (`-- vk e2e [--update] [--times] [フィルタ]`)。旧 snap の後継 —
/// **golden はストーリーの play (<c>ctx.Play</c>) 内の <c>d.Snap()</c> だけが生む**。
/// <list type="bullet">
/// <item>play を持たないストーリーは対象外 (golden なし)。初期絵の回帰だけ欲しければ
///   <c>ctx.Play(d =&gt; d.Snap())</c> の 1 行を書く</item>
/// <item>play ごとにストーリーを作り直して実行する (独立・hermetic)。名前は "パス#play名"</item>
/// <item>golden 名: <c>{Story}[.{Play}][.{Snap 名 | 連番}].{backend}.png</c> —
///   無名 play の最初の無名 Snap は旧 snap と同じファイル名 (移行互換)</item>
/// <item>決定性は旧 snap と同一 (固定 dt、ハイライト静定待ち、ピクセル完全一致)</item>
/// </list>
/// </summary>
public static class E2e
{
    private const int WarmupSteps = 8;          // 固定 1/60s × 8 (アニメ/caret を決定的に進める)
    private const float FixedDt = 1f / 60f;
    private const int MaxTinyAaPixels = 32;
    private const int MaxTinyAaChannelDelta = 5;

    private sealed class Counters
    {
        public int Snaps, SnapFailed, Updated;
        public readonly List<string> Produced = new();   // 生成/検証した golden ファイル名
    }

    public static int Run(GalleryHost host, IReadOnlyList<StoryInfo> stories, string backend, bool update,
        string? filter = null, bool times = false)
    {
        Stories.StrudelStory.HeadlessAudio = true;   // E2E は実 XAudio2 を触らない (決定的 + Vortice callback GC レース回避)
        string dir = GoldenDir();
        Directory.CreateDirectory(dir);
        Console.WriteLine($"goldens: {dir} ({(update ? "update" : "verify")}{(filter is null ? "" : $", filter '{filter}'")})");

        var sw = new System.Diagnostics.Stopwatch();
        var c = new Counters();
        int plays = 0, playFailed = 0, storyFailed = 0, noPlay = 0, skipped = 0;
        double msBuild = 0, msPlay = 0;

        foreach (StoryInfo story in stories)
        {
            if (story.RealWindowOnly) { skipped++; continue; }

            // play の有無はまず 1 回構築して調べる。構築/Tick/描画の失敗は story 単位で
            // 記録して次へ進み、1 ページの不具合で全 traversal を止めない。
            IReadOnlyList<StoryPlay>? registered;
            sw.Restart();
            try
            {
                host.SelectForE2e(story);
                Warmup(host);
                ResetPointer(host);
                registered = host.Context?.Plays;
            }
            catch (Exception error)
            {
                storyFailed++;
                Console.Error.WriteLine($"  STORY ERROR {story.Path}: {error.GetType().Name}: {error.Message}");
                continue;
            }
            finally
            {
                msBuild += sw.Elapsed.TotalMilliseconds;
            }
            if (registered is null or { Count: 0 }) { noPlay++; continue; }

            for (int pi = 0; pi < registered.Count; pi++)
            {
                string playName = registered[pi].Name;
                string testId = playName.Length > 0 ? $"{story.Path}#{playName}" : story.Path;
                if (filter is not null && !testId.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                if (pi > 0)   // play ごとに作り直し (独立実行)。最初の play は探索時の構築を使う
                {
                    sw.Restart();
                    try
                    {
                        host.SelectForE2e(story);
                        Warmup(host);
                        ResetPointer(host);
                    }
                    catch (Exception error)
                    {
                        storyFailed++;
                        Console.Error.WriteLine($"  STORY ERROR {testId}: {error.GetType().Name}: {error.Message}");
                        break;
                    }
                    finally
                    {
                        msBuild += sw.Elapsed.TotalMilliseconds;
                    }
                }
                StoryPlay play = host.Context!.Plays[pi];

                plays++;
                int snapIndex = 0;
                sw.Restart();
                try
                {
                    var driver = new PlayDriver(host.Host!,
                        step: n => { for (int i = 0; i < n; i++) host.Step(FixedDt); return Task.CompletedTask; },
                        snap: name => Checkpoint(host, dir, backend, story, playName, name, snapIndex++, update, c));
                    play.Body(driver).GetAwaiter().GetResult();
                }
                catch (PlayError e)
                {
                    playFailed++;
                    Console.Error.WriteLine($"  FAIL {testId}: {e.Message}");
                }
                catch (Exception e)
                {
                    playFailed++;
                    Console.Error.WriteLine($"  ERROR {testId}: {e.GetType().Name}: {e.Message}");
                }
                msPlay += sw.Elapsed.TotalMilliseconds;
            }
        }

        // 迷子 golden の検出 (フィルタなしの全実行時のみ意味がある)
        if (filter is null)
        {
            var produced = new HashSet<string>(c.Produced, StringComparer.OrdinalIgnoreCase);
            var stale = Directory.GetFiles(dir, $"*.{backend}.png")
                .Select(Path.GetFileName)
                .Where(f => f is not null && !f.EndsWith(".actual.png") && !produced.Contains(f!))
                .ToList();
            if (stale.Count > 0)
            {
                Console.WriteLine($"  STALE: どの play も生成しない golden が {stale.Count} 件 — 不要なら削除:");
                foreach (string? f in stale) Console.WriteLine($"    stale: {f}");
            }
        }

        if (times)
            Console.WriteLine($"--times: 構築+warmup {msBuild:0}ms / play 実行 (撮影込み) {msPlay:0}ms");

        string note = $" (story error={storyFailed}, play なし={noPlay}, 実窓専用 skip={skipped})";
        Console.WriteLine(update
            ? $"e2e: plays={plays} snaps={c.Snaps} 更新={c.Updated}{note}"
            : $"e2e: plays={plays} passed={plays - playFailed} failed={playFailed} " +
              $"(snap {c.Snaps} 枚中 diff {c.SnapFailed}){note}");
        return update || (playFailed == 0 && storyFailed == 0) ? 0 : 1;
    }

    /// <summary>1 play を実行する (dotnet test アダプタ用 — 検証のみ、golden は更新しない)。
    /// golden 差分/欠落/Expect 失敗は <see cref="PlayError"/>。ストーリーは呼び出しごとに作り直される。</summary>
    public static void RunPlay(GalleryHost host, StoryCatalog catalog, string path, int playIndex, string backend)
    {
        Stories.StrudelStory.HeadlessAudio = true;   // E2E は実 XAudio2 を触らない (上記 Run と同じ理由)
        StoryInfo story = catalog.Find(path) ?? throw new PlayError($"ストーリーがありません: {path}");
        string dir = GoldenDir();
        host.SelectForE2e(story);
        Warmup(host);
        ResetPointer(host);
        IReadOnlyList<StoryPlay> plays = host.Context?.Plays ?? [];
        if (playIndex >= plays.Count) throw new PlayError($"play #{playIndex} がありません (登録 {plays.Count})");
        var c = new Counters();
        int snapIndex = 0;
        var driver = new PlayDriver(host.Host!,
            step: n => { for (int i = 0; i < n; i++) host.Step(FixedDt); return Task.CompletedTask; },
            snap: name => Checkpoint(host, dir, backend, story, plays[playIndex].Name, name, snapIndex++, update: false, c));
        plays[playIndex].Body(driver).GetAwaiter().GetResult();
    }

    /// <summary>アニメ/ハイライトを静定させる (旧 snap と同じ決定性の作り)。</summary>
    private static void Warmup(GalleryHost host)
    {
        for (int i = 0; i < WarmupSteps; i++) host.Step(FixedDt);
        if (!Luxel.Controls.HighlightQueue.WaitIdle(15000))
            Console.Error.WriteLine("  WARN: ハイライト静定待ちタイムアウト");
        for (int i = 0; i < 2; i++) host.Step(0f);
    }

    private static void ResetPointer(GalleryHost host)
    {
        host.Host?.PointerMove(-1000, -1000);
        host.Step(0f);
    }

    /// <summary>d.Snap() の実体: 静定 → 撮影 → 比較/更新。失敗は PlayError (play 全体を落とす)。</summary>
    private static void Checkpoint(GalleryHost host, string dir, string backend,
        StoryInfo story, string playName, string snapName, int index, bool update, Counters c)
    {
        if (!Luxel.Controls.HighlightQueue.WaitIdle(15000))
            Console.Error.WriteLine($"  WARN {story.Path}: ハイライト静定待ちタイムアウト");
        for (int i = 0; i < 2; i++) host.Step(0f);

        (byte[] rgba, int w, int h)? snap = host.SnapshotRgba();
        if (snap is null) throw new PlayError("フレームが取得できません");

        string name = Sanitize(story.Path);
        if (playName.Length > 0) name += "." + Sanitize(playName);
        if (snapName.Length > 0) name += "." + Sanitize(snapName);
        else if (index > 0) name += "." + index;
        string file = Path.Combine(dir, $"{name}.{backend}.png");

        c.Snaps++;
        c.Produced.Add(Path.GetFileName(file));

        if (update)
        {
            Png.Write(file, snap.Value.w, snap.Value.h, snap.Value.rgba);
            c.Updated++;
            Console.WriteLine($"  UPDATE {Path.GetFileName(file)}");
            return;
        }
        if (!File.Exists(file))
        {
            c.SnapFailed++;
            throw new PlayError($"golden なし: {Path.GetFileName(file)} (--update で生成)");
        }
        if (PixelsEquivalent(File.ReadAllBytes(file), snap.Value, out string? diffNote))
        {
            if (diffNote is not null) Console.WriteLine($"  OK {Path.GetFileName(file)} ({diffNote})");
            return;
        }

        c.SnapFailed++;
        string actual = Path.Combine(dir, $"{name}.{backend}.actual.png");
        Png.Write(actual, snap.Value.w, snap.Value.h, snap.Value.rgba);
        throw new PlayError($"golden 差分: {Path.GetFileName(actual)}");
    }

    private static bool PixelsEquivalent(byte[] goldenPng, (byte[] rgba, int w, int h) snap, out string? diffNote)
    {
        diffNote = null;
        try
        {
            (byte[] rgba, int w, int h) = Png.Decode(goldenPng);
            if (w != snap.w || h != snap.h) return false;
            if (rgba.AsSpan().SequenceEqual(snap.rgba)) return true;

            int diffPixels = 0, maxDelta = 0;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                int prMax = 0;
                for (int c = 0; c < 4; c++)
                {
                    int d = Math.Abs(rgba[i + c] - snap.rgba[i + c]);
                    if (d > prMax) prMax = d;
                }
                if (prMax == 0) continue;
                diffPixels++;
                maxDelta = Math.Max(maxDelta, prMax);
                if (diffPixels > MaxTinyAaPixels || maxDelta > MaxTinyAaChannelDelta) return false;
            }
            diffNote = $"tiny AA diff: pixels={diffPixels}, maxΔ={maxDelta}";
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"  WARN golden デコード失敗: {e.Message}");
            return false;
        }
    }

    private static string Sanitize(string path)
    {
        var sb = new System.Text.StringBuilder(path.Length);
        foreach (char ch in path) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    /// <summary>goldens ディレクトリの解決 — cwd と実行ディレクトリからリポジトリルートを遡って探す
    /// (dotnet test は bin 配下が cwd になるため)。</summary>
    private static string GoldenDir()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (string? d = start; d is not null; d = Path.GetDirectoryName(d))
            {
                string candidate = Path.Combine(d, "src", "Gallery", "Luxel.Gallery", "goldens");
                if (Directory.Exists(candidate)) return candidate;
            }
        return Path.Combine(AppContext.BaseDirectory, "goldens");
    }
}
