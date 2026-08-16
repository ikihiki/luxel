using System.Diagnostics;
using Luxel.Controls;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>docs ページの見出し 1 つ (TOC 用)。<see cref="Block"/> はブロック index — ページを
/// 開いた実エディタの <c>ScrollTo</c> にそのまま渡せる (Build は決定的なので index が一致する)。</summary>
public sealed record DocsHeading(string Text, int Level, int Block);

/// <summary>docs ページ 1 つの検索素材: 全文 (ブロックテキスト連結) + 見出し一覧。</summary>
public sealed record DocsPage(string Path, string Text, IReadOnlyList<DocsHeading> Headings);

/// <summary>
/// Docs 全文インデックス (SR)。全ストーリーを使い捨て <see cref="StoryContext"/> で **Build だけ**
/// して (実体化しない — GPU/Effect は走らず安価)、widget 木から RichTextEditor を見つけて
/// 本文と見出しを回収する。docs を持たないストーリーはスキップ。
/// </summary>
public static class DocsIndex
{
    /// <summary>全ストーリーから path → DocsPage の辞書を作る (起動時に 1 回)。</summary>
    public static Dictionary<string, DocsPage> Build(IReadOnlyList<StoryInfo> stories,
                                                     Luxel.Resources.ResourceSystem? resources,
                                                     StoryCatalog? catalog = null)
    {
        var sw = Stopwatch.StartNew();
        var map = new Dictionary<string, DocsPage>();
        int broken = 0;
        foreach (StoryInfo s in stories)
        {
            try
            {
                using var ctx = new StoryContext(resources);
                ctx.SetServices(GalleryServices.Provider);   // Scripting 等 DI ストーリーも build できるように
                StoryResult result = s.Build(ctx);
                string? src = result.Kind == StoryResultKind.Markdown
                    ? StoryMarkdownRenderer.EffectiveMarkdown(s, result.Markdown)
                    : FindSemanticDocument(result.Widget)?.DocumentSource;
                if (src is null) continue;

                // Semantic markdownから見出し/リンクを取る (realize 不要)。
                // Block はソースオフセット (TextEditorView.ScrollToSource でそのまま使える)。
                var heads = MarkdownDecorations.Headings(src)
                    .Where(h => h.Level >= 2)
                    .Select(h => new DocsHeading(h.Text, h.Level, h.Offset))
                    .ToList();
                map[s.Path] = new DocsPage(s.Path, src, heads);
                foreach (MarkdownLink l in MarkdownDecorations.Links(src))
                    if (LinkBroken(l.Url, src, catalog))
                    { broken++; Console.Error.WriteLine($"[gallery] dead link in '{s.Path}': {l.Url}"); }
            }
            catch (Exception e)
            {
                // docs 抽出はベストエフォート — Build に前提が要るストーリーは検索対象外になるだけ
                Console.Error.WriteLine($"[gallery] docs index skip '{s.Path}': {e.Message}");
            }
        }
        // Keep startup diagnostics, while tests use ValidateLinks(map) as a failing CI gate.
        broken = ValidateLinks(map, catalog).Count;
        Console.WriteLine($"[gallery] docs index: {map.Count} pages, {sw.ElapsedMilliseconds}ms"
                          + (broken > 0 ? $", dead links: {broken}" : ""));
        return map;
    }

    /// <summary>docs index内のinternal story/heading linkを検証し、CIで扱えるerror一覧を返す。</summary>
    public static IReadOnlyList<string> ValidateLinks(IReadOnlyDictionary<string, DocsPage> pages,
                                                       StoryCatalog? catalog = null)
        => pages.Values.OrderBy(page => page.Path, StringComparer.Ordinal)
            .SelectMany(page => ValidateLinks(page.Path, page.Text, catalog)).ToArray();

    public static IReadOnlyList<string> ValidateLinks(string path, string source,
                                                       StoryCatalog? catalog = null)
    {
        var errors = new List<string>();
        foreach (MarkdownLink link in MarkdownDecorations.Links(source))
            if (LinkBroken(link.Url, source, catalog)) errors.Add($"{path}: {link.Url}");
        return errors;
    }

    /// <summary>widget 木からnative realization不要のsemantic documentを探す。</summary>
    internal static ISemanticDocument? FindSemanticDocument(Widget? w)
    {
        if (w is null) return null;
        if (w is ISemanticDocument { DocumentSource: not null } document) return document;
        foreach (Widget c in w.DebugChildren())
            if (FindSemanticDocument(c) is { } found) return found;
        return null;
    }

    internal static TextEditorView? FindMarkdownDoc(Widget w)
    {
        if (w is TextEditorView { DocSource: not null } editor) return editor;
        foreach (Widget c in w.DebugChildren())
            if (FindMarkdownDoc(c) is { } found) return found;
        return null;
    }

    // 新スタック docs のリンク検証: story: (レジストリ) と #アンカー (見出し slug)。外部リンクは検証しない。
    private static bool LinkBroken(string url, string src, StoryCatalog? catalog = null)
    {
        if (url.StartsWith("story:"))
        {
            string path = url["story:".Length..];
            return (catalog?.Find(path) ?? StoryRegistry.Find(path)) is null;
        }
        if (url.StartsWith("#")) return !MarkdownDecorations.Headings(src).Any(h => MarkdownDoc.Slug(h.Text) == url[1..]);
        return false;
    }
}
