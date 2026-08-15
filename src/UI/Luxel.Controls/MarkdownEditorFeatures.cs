using Luxel.Document;

namespace Luxel.Controls;

/// <summary>
/// Markdown が汎用 Editor UI へ渡すブロック境界・追加候補・選択操作。
/// ドラッグハンドルやメニュー自体は TextEditorView 側に置き、ここでは表示内容だけを定義する。
/// </summary>
public static class MarkdownEditorFeatures
{
    public static IEditorBlockProvider BlockProvider { get; } = new MarkdownBlockProvider();

    public static IReadOnlyList<EditorInsertItem> InsertItems { get; } =
    [
        new("paragraph", "Text", ""),
        new("heading-1", "Heading 1", "# "),
        new("heading-2", "Heading 2", "## "),
        new("heading-3", "Heading 3", "### "),
        new("bullet-list", "Bullet list", "- "),
        new("ordered-list", "Numbered list", "1. "),
        new("task-list", "Task list", "- [ ] "),
        new("quote", "Quote", "> "),
        new("code-block", "Code block", "```\n\n```", CaretBack: 4),
        new("horizontal-rule", "Divider", "---\n"),
        new("link", "Link", "[text](url)", CaretBack: 1),
        new("image", "Image", "![alt](url)", CaretBack: 1),
        new("table", "Table", "| Column | Column |\n| --- | --- |\n| Value | Value |"),
    ];

    public static IReadOnlyList<EditorSelectionAction> SelectionActions { get; } =
    [
        new("bold", "Bold", "**", "**"),
        new("italic", "Italic", "*", "*"),
        new("inline-code", "Code", "`", "`"),
        new("link", "Link", "[", "](url)"),
    ];

    private sealed class MarkdownBlockProvider : IEditorBlockProvider
    {
        public IReadOnlyList<EditorBlock> GetBlocks(TextDoc document)
        {
            var blocks = new List<EditorBlock>();
            int line = 0;
            while (line < document.LineCount)
            {
                string text = document.LineText(line);
                string trimmed = text.TrimStart();
                if (trimmed.Length == 0) { line++; continue; }

                int startLine = line;
                string kind;
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    kind = trimmed.StartsWith("```embed", StringComparison.Ordinal)
                        ? "markdown.embed"
                        : MarkdownBlockKinds.CodeBlock;
                    line++;
                    while (line < document.LineCount)
                    {
                        bool closing = document.LineText(line).TrimStart().StartsWith("```", StringComparison.Ordinal);
                        line++;
                        if (closing) break;
                    }
                }
                else
                {
                    kind = KindOf(trimmed);
                    line++;
                    if (kind == MarkdownBlockKinds.Paragraph)
                    {
                        while (line < document.LineCount)
                        {
                            string next = document.LineText(line).TrimStart();
                            if (next.Length == 0 || KindOf(next) != MarkdownBlockKinds.Paragraph) break;
                            line++;
                        }
                    }
                    else if (kind == MarkdownBlockKinds.Table)
                    {
                        while (line < document.LineCount && LooksLikeTableRow(document.LineText(line))) line++;
                    }
                }

                int from = document.LineStart(startLine);
                int to = document.LineEnd(Math.Max(startLine, line - 1));
                blocks.Add(new EditorBlock(from, to, kind));
            }
            return blocks;
        }

        private static string KindOf(string trimmed)
        {
            int hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 6 && hashes < trimmed.Length && trimmed[hashes] == ' ')
                return MarkdownBlockKinds.Heading(hashes);
            if (trimmed.StartsWith(">", StringComparison.Ordinal)) return MarkdownBlockKinds.Quote;
            if (IsHorizontalRule(trimmed)) return MarkdownBlockKinds.HorizontalRule;
            if (IsTask(trimmed)) return MarkdownBlockKinds.TaskList;
            if (trimmed.Length >= 2 && trimmed[1] == ' ' && trimmed[0] is '-' or '*' or '+')
                return MarkdownBlockKinds.BulletList;
            int digit = 0;
            while (digit < trimmed.Length && char.IsAsciiDigit(trimmed[digit])) digit++;
            if (digit > 0 && digit + 1 < trimmed.Length && trimmed[digit] == '.' && trimmed[digit + 1] == ' ')
                return MarkdownBlockKinds.OrderedList;
            if (LooksLikeTableRow(trimmed)) return MarkdownBlockKinds.Table;
            return MarkdownBlockKinds.Paragraph;
        }
    }

    internal static bool IsTask(string trimmed)
        => trimmed.Length >= 6 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' '
           && trimmed[2] == '[' && trimmed[4] == ']' && trimmed[5] == ' '
           && trimmed[3] is ' ' or 'x' or 'X';

    internal static bool IsHorizontalRule(string trimmed)
    {
        char marker = trimmed.FirstOrDefault(c => c != ' ');
        if (marker is not ('-' or '*' or '_')) return false;
        int count = 0;
        foreach (char c in trimmed)
        {
            if (c == marker) count++;
            else if (c != ' ') return false;
        }
        return count >= 3;
    }

    private static bool LooksLikeTableRow(string line)
        => line.Trim().Length >= 3 && line.Contains('|');
}
