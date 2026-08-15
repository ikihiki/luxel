using System.Text;
using System.Text.RegularExpressions;
using Luxel.Document;
using Luxel.Resources;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>Markdown 画像行を標準 <see cref="ImageBlock"/> へ解決するキー。</summary>
public readonly record struct MarkdownImageRef(string Src, string Alt);

/// <summary>GFM pipe table を標準 <see cref="TableBlock"/> へ解決するキー。</summary>
public readonly record struct MarkdownTableRef(int From, int To, string Source);

internal readonly record struct MarkdownTableSpan(int From, int To, MarkdownTableRef Ref);

/// <summary>Markdown の標準ブロック埋め込み（画像・GFMテーブル）の解析とWidget解決。</summary>
public static partial class MarkdownBlockEmbeds
{
    [GeneratedRegex("^\\s*!\\[([^\\]]*)\\]\\(([^)\\s]+)(?:\\s+\"[^\"]*\")?\\)\\s*$")]
    private static partial Regex ImageLineRegex();

    internal static bool TryImage(string line, out MarkdownImageRef image)
    {
        Match match = ImageLineRegex().Match(line);
        if (match.Success)
        {
            image = new MarkdownImageRef(match.Groups[2].Value, match.Groups[1].Value);
            return true;
        }
        image = default;
        return false;
    }

    internal static IReadOnlyList<MarkdownTableSpan> Tables(string markdown)
    {
        string[] lines = markdown.Split('\n');
        var starts = new int[lines.Length];
        int offset = 0;
        for (int i = 0; i < lines.Length; i++) { starts[i] = offset; offset += lines[i].Length + 1; }

        var spans = new List<MarkdownTableSpan>();
        for (int line = 0; line + 1 < lines.Length; line++)
        {
            if (!lines[line].Contains('|') || !IsSeparator(lines[line + 1])) continue;
            int last = line + 1;
            while (last + 1 < lines.Length && lines[last + 1].Contains('|') && lines[last + 1].Trim().Length > 0) last++;
            int from = starts[line];
            int to = starts[last] + lines[last].Length;
            string source = markdown[from..to];
            var reference = new MarkdownTableRef(from, to, source);
            spans.Add(new MarkdownTableSpan(from, to, reference));
            line = last;
        }
        return spans;
    }

    public static TablePayload ParseTable(string markdown)
    {
        string[] lines = markdown.Split('\n');
        if (lines.Length < 2 || !IsSeparator(lines[1])) return new TablePayload([]);
        string[] separators = Cells(lines[1]);
        var aligns = new TableAlign[separators.Length];
        for (int i = 0; i < separators.Length; i++)
        {
            string cell = separators[i].Trim();
            aligns[i] = (cell.StartsWith(':'), cell.EndsWith(':')) switch
            {
                (true, true) => TableAlign.Center,
                (true, false) => TableAlign.Left,
                (false, true) => TableAlign.Right,
                _ => TableAlign.None,
            };
        }
        var rows = new List<string[]> { Cells(lines[0]) };
        for (int i = 2; i < lines.Length; i++)
            if (lines[i].Trim().Length > 0) rows.Add(Cells(lines[i]));
        return new TablePayload(rows, aligns);
    }

    public static string SerializeTable(TablePayload payload)
    {
        if (payload.Rows.Count == 0 || payload.Columns == 0) return "";
        var output = new List<string> { Row(payload.Rows[0], payload.Columns) };
        var separators = new string[payload.Columns];
        for (int i = 0; i < separators.Length; i++)
            separators[i] = payload.Aligns[i] switch
            {
                TableAlign.Left => ":---",
                TableAlign.Center => ":---:",
                TableAlign.Right => "---:",
                _ => "---",
            };
        output.Add(Row(separators, payload.Columns));
        for (int i = 1; i < payload.Rows.Count; i++) output.Add(Row(payload.Rows[i], payload.Columns));
        return string.Join('\n', output);
    }

    internal static Widget? Resolve(TextEditorView editor, object key, ResourceSystem? resources, float maxWidth)
        => key switch
        {
            MarkdownTableRef table => Kit.TableBlock(ParseTable(table.Source), maxWidth,
                updated => editor.Replace(table.From, table.To, SerializeTable(updated))),
            MarkdownImageRef image when resources is not null => Kit.ImageBlock(
                new ImagePayload(image.Src, image.Alt), resources, maxWidth),
            _ => null,
        };

    private static bool IsSeparator(string line)
    {
        string[] cells = Cells(line);
        if (cells.Length == 0) return false;
        foreach (string raw in cells)
        {
            string cell = raw.Trim().Trim(':');
            if (cell.Length < 3 || cell.Any(c => c != '-')) return false;
        }
        return true;
    }

    private static string[] Cells(string line)
    {
        string value = line.Trim();
        if (value.StartsWith('|')) value = value[1..];
        if (value.EndsWith('|')) value = value[..^1];
        return value.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static string Row(IReadOnlyList<string> cells, int columns)
    {
        var builder = new StringBuilder("|");
        for (int i = 0; i < columns; i++)
            builder.Append(' ').Append(i < cells.Count ? cells[i] : "").Append(" |");
        return builder.ToString();
    }
}
