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
                                                     Luxel.Resources.ResourceSystem? resources)
    {
        var sw = Stopwatch.StartNew();
        var map = new Dictionary<string, DocsPage>();
        int broken = 0;
        foreach (StoryInfo s in stories)
        {
            try
            {
                var ctx = new StoryContext(resources);
                ctx.SetServices(GalleryServices.Provider);   // Scripting 等 DI ストーリーも build できるように
                Widget w = s.Build(ctx);
                if (FindDocEditor(w) is not { } doc) continue;
                IReadOnlyList<Luxel.Document.Block> blocks = doc.Editor.Doc.Blocks;
                var heads = new List<DocsHeading>();
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (blocks[i].Kind == Luxel.Document.BlockKind.Heading && blocks[i].HeadingLevel >= 2)
                        heads.Add(new DocsHeading(blocks[i].Text, blocks[i].HeadingLevel, i));
                    if (blocks[i].Kind != Luxel.Document.BlockKind.Embed)
                        text.AppendLine(blocks[i].Text);
                }
                map[s.Path] = new DocsPage(s.Path, text.ToString(), heads);

                // デッドリンク検証: #アンカー (同一ページ見出し) と story: (レジストリ) を確認
                foreach (string url in Luxel.Document.LinkCheck.FindBroken(
                             blocks, p => StoryRegistry.Find(p) is not null))
                {
                    broken++;
                    Console.Error.WriteLine($"[gallery] dead link in '{s.Path}': {url}");
                }
            }
            catch (Exception e)
            {
                // docs 抽出はベストエフォート — Build に前提が要るストーリーは検索対象外になるだけ
                Console.Error.WriteLine($"[gallery] docs index skip '{s.Path}': {e.Message}");
            }
        }
        Console.WriteLine($"[gallery] docs index: {map.Count} pages, {sw.ElapsedMilliseconds}ms"
                          + (broken > 0 ? $", dead links: {broken}" : ""));
        return map;
    }

    /// <summary>widget 木から docs エディタ (RichTextEditor) を探す。docs 以外のストーリーは通常 null。</summary>
    internal static RichTextEditor? FindDocEditor(Widget w)
    {
        if (w is RichTextEditor e) return e;
        foreach (Widget c in w.DebugChildren())
            if (FindDocEditor(c) is { } found) return found;
        return null;
    }
}
