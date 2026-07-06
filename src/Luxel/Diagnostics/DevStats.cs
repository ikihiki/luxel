using System.Collections.Concurrent;
using System.Globalization;

namespace Luxel.Diagnostics;

/// <summary>
/// ゲームが 1 行で観測に載せる汎用 key-value watch。score / 残弾 / HP / 状態機械の現在状態や
/// パーティクル Alive 数など、printf デバッグの受け皿。値は <see cref="Set(string,string)"/> で
/// いつでも上書きし、フレームループが <see cref="Flush"/> で 30f 毎にまとめて <see cref="EngineDiagnostics.Custom"/> へ emit する。
/// <para><b>ゼロコスト規律</b>: DevTools が繋がっていない (購読者ゼロ) ときは <see cref="Set(string,string)"/> は
/// <see cref="EngineDiagnostics.IsEnabled"/> の bool 判定だけで即 return し、辞書書き込みも割り当ても行わない。</para>
/// </summary>
public static class DevStats
{
    // key → 文字列化済み値。値は文字列で持つ (boxing を残さない / emit 時に再変換不要)。
    private static readonly ConcurrentDictionary<string, string> Values = new();

    /// <summary>文字列値を観測に載せる (購読者が居なければ何もしない)。</summary>
    public static void Set(string key, string value)
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Custom)) return;
        Values[key] = value;
    }

    /// <summary>数値を観測に載せる (小数 3 桁、invariant)。</summary>
    public static void Set(string key, double value) => Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));
    /// <summary>整数を観測に載せる。</summary>
    public static void Set(string key, long value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    /// <summary>整数を観測に載せる。</summary>
    public static void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    /// <summary>真偽値を観測に載せる。</summary>
    public static void Set(string key, bool value) => Set(key, value ? "true" : "false");

    /// <summary>現在の全 key-value を key 昇順で取得する (テスト/emit 用の決定的スナップショット)。</summary>
    public static DiagStat[] Snapshot()
    {
        var stats = new DiagStat[Values.Count];
        int i = 0;
        foreach (var kv in Values) stats[i++] = new DiagStat(kv.Key, kv.Value);
        Array.Sort(stats, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return stats;
    }

    /// <summary>溜まった key-value を <see cref="EngineDiagnostics.Custom"/> へ emit する (フレームループが 30f 毎に呼ぶ)。</summary>
    public static void Flush()
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Custom)) return;
        EngineDiagnostics.Emit(EngineDiagnostics.Custom, new DiagCustom(Snapshot()));
    }

    /// <summary>全 key を消す (シーン切替時など。テストの分離にも使う)。</summary>
    public static void Clear() => Values.Clear();
}
