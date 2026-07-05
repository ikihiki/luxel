namespace Luxel.Document;

/// <summary>
/// docs 内リンクのデッドリンク検証 (純ロジック)。対象は <c>#アンカー</c> (同一文書の見出し) と
/// <c>story:</c> (存在判定は呼び出し側が渡す)。http(s) 等の外部リンクは検証しない。
/// Gallery の DocsIndex が起動時に全ページへかけ、壊れたリンクを警告する。
/// </summary>
public static class LinkCheck
{
    /// <summary>見出しアンカーのスラグ (小文字 + 空白→ハイフン)。エディタ側のアンカー解決と同一規則。</summary>
    public static string Slug(string heading)
        => heading.Trim().ToLowerInvariant().Replace(' ', '-').Replace('　', '-');

    /// <summary>壊れたリンクの URL 一覧を返す。<paramref name="storyExists"/> = null なら story: は検証しない。</summary>
    public static List<string> FindBroken(IReadOnlyList<Block> blocks, Func<string, bool>? storyExists = null)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (Block b in blocks)
            if (b.Kind == BlockKind.Heading)
                anchors.Add(Slug(b.Text));

        var broken = new List<string>();
        foreach (Block b in blocks)
            foreach (InlineRun r in b.Lines.SelectMany(l => l.Runs))
            {
                if (r.Style.Link is not string url || url.Length == 0) continue;
                if (url.StartsWith('#'))
                {
                    if (!anchors.Contains(Slug(url[1..]))) broken.Add(url);
                }
                else if (url.StartsWith("story:"))
                {
                    if (storyExists is not null && !storyExists(url["story:".Length..])) broken.Add(url);
                }
            }
        return broken;
    }
}
