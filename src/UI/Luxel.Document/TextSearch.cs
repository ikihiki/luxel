namespace Luxel.Document;

/// <summary>プレーンテキスト検索 — マッチ位置の収集 (canvas 不要・純関数)。ハイライトやナビゲーションは
/// view がこの結果を装飾・選択に落とす。</summary>
public static class TextSearch
{
    /// <summary>全マッチ <c>[From, To)</c> を文書順に返す (空クエリ = 空)。</summary>
    public static IReadOnlyList<(int From, int To)> FindAll(string text, string query, bool ignoreCase = false)
    {
        var matches = new List<(int, int)>();
        if (string.IsNullOrEmpty(query)) return matches;
        StringComparison cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int i = 0;
        while ((i = text.IndexOf(query, i, cmp)) >= 0)
        {
            matches.Add((i, i + query.Length));
            i += query.Length;
        }
        return matches;
    }
}
