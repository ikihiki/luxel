namespace Luxel.Document;

/// <summary>
/// 文書フォーマット抽象 — **パーサーが文章をすべて管理する**。
/// テキスト表現との往復・記法の知識・行の確定 (hybrid の離脱時再パース)・入力オートフォーマットは
/// すべてこの実装の責務で、エディタ (RichTextEditor/DocumentEditor) は「ブロック列の編集と表示」だけを持つ。
/// 既定は <see cref="MarkdownFormat"/>。独自フォーマット (例: ライブコーディング用 — markdown で
/// ある必要はない) は Parse が直接 Embed ブロック列を作ればよい。
/// </summary>
public interface IDocumentFormat
{
    RichDocument Parse(string source);
    string Serialize(RichDocument doc);
    /// <summary>選択範囲 [min, max) をこのフォーマットで書き出す (コピー用)。</summary>
    string SerializeRange(RichDocument doc, DocPos min, DocPos max);

    /// <summary>hybrid (アクティブ行のソース編集) に対応するか。
    /// 「1 表示行 = ソース 1 行」の行指向で往復できるフォーマットのみ true。</summary>
    bool SupportsHybrid { get; }
    /// <summary>1 行のソース → ブロック (hybrid の離脱時再パース。結果は 1 行ブロック)。</summary>
    Block ParseLine(string line);
    /// <summary>ブロック内 1 行のソース (hybrid の進入時展開 — 引用等は行に記法を付ける)。</summary>
    string SerializeLine(Block b, int line);
    /// <summary>行頭記法の長さ (hybrid のソース展開時の offset 近似写像)。</summary>
    int LinePrefixLen(Block b, int line);

    /// <summary>入力オートフォーマット (行頭記法の確定など)。<paramref name="inserted"/> は直前に
    /// 挿入された文字列。変換したら true。不要なフォーマットは常に false。</summary>
    bool TryAutoFormat(DocumentEditor ed, string inserted);
    /// <summary>Enter 時のブロック確定 (フェンス開始など)。確定したら true (エディタは改行を挿入しない)。</summary>
    bool TryBlockCommit(DocumentEditor ed);
}

/// <summary>markdown 文書フォーマット (Markdig パース + 自前正規形シリアライズ = 既存 <see cref="Markdown"/>)。
/// 行頭記法のオートフォーマット ("# "/"- "/"1. "/"> " + 空白) とフェンス確定 ("```lang" + Enter) を持つ。</summary>
public sealed class MarkdownFormat : IDocumentFormat
{
    /// <summary>既定インスタンス (リゾルバなし)。フェンスリゾルバを使うアプリは自前インスタンスを作ること
    /// (Default は共有なので変更しない)。</summary>
    public static readonly MarkdownFormat Default = new();

    /// <summary>フェンス → embed の判定チェーン (パース時に先勝ち)。空 = フェンスは常に CodeBlock。</summary>
    public List<IFenceResolver> FenceResolvers { get; } = new();

    public RichDocument Parse(string source)
        => Markdown.Parse(source, FenceResolvers.Count > 0 ? FenceResolvers : null);
    public string Serialize(RichDocument doc) => Markdown.Serialize(doc);
    public string SerializeRange(RichDocument doc, DocPos min, DocPos max) => Markdown.SerializeRange(doc, min, max);

    public bool SupportsHybrid => true;
    public Block ParseLine(string line) => Parse(line).Blocks[0];
    public string SerializeLine(Block b, int line) => Markdown.SerializeLine(b, line);
    public int LinePrefixLen(Block b, int line) => Markdown.LinePrefixLen(b, line);

    public bool TryAutoFormat(DocumentEditor ed, string inserted)
    {
        if (inserted != " ") return false;
        if (ed.CaretBlock.Kind != BlockKind.Paragraph) return false;
        string head = ed.CaretLine.Text[..ed.Caret.Offset];   // 行頭〜キャレット (打ったばかりの空白を含む)
        switch (head)
        {
            case "# ": ed.ApplyAutoFormat(BlockKind.Heading, 2, headingLevel: 1); return true;
            case "## ": ed.ApplyAutoFormat(BlockKind.Heading, 3, headingLevel: 2); return true;
            case "### ": ed.ApplyAutoFormat(BlockKind.Heading, 4, headingLevel: 3); return true;
            case "- ": ed.ApplyAutoFormat(BlockKind.ListItem, 2); return true;
            case "> ": ed.ApplyAutoFormat(BlockKind.Quote, 2); return true;
            default:
                if (System.Text.RegularExpressions.Regex.IsMatch(head, @"^\d+\. $"))
                { ed.ApplyAutoFormat(BlockKind.ListItem, head.Length, ordered: true); return true; }
                return false;
        }
    }

    public bool TryBlockCommit(DocumentEditor ed)
    {
        if (ed.CaretBlock.Kind != BlockKind.Paragraph || ed.Caret.Offset != ed.CaretLine.Length) return false;
        var m = System.Text.RegularExpressions.Regex.Match(ed.CaretLine.Text, @"^```([A-Za-z0-9+#.-]*)$");
        if (!m.Success) return false;
        ed.ConvertToCodeFence(m.Groups[1].Value);
        return true;
    }
}

/// <summary>プレーンテキストフォーマット (全行 = 段落、記法なし)。フォーマット差し替えの最小実証と
/// 「整形なしのブロックエディタ」用。</summary>
public sealed class PlainTextFormat : IDocumentFormat
{
    public static readonly PlainTextFormat Default = new();

    public RichDocument Parse(string source)
        => RichDocument.FromBlocks(
            (source ?? "").Replace("\r", "").Split('\n').Select(l => new Block(BlockKind.Paragraph, l)));

    public string Serialize(RichDocument doc) => doc.PlainText;

    public string SerializeRange(RichDocument doc, DocPos min, DocPos max)
    {
        if (max < min) (min, max) = (max, min);
        var ed = new DocumentEditor(doc);
        return ed.GetText(min, max);
    }

    public bool SupportsHybrid => true;   // ソース = 表示 (展開しても変わらない)
    public Block ParseLine(string line) => new(BlockKind.Paragraph, line);
    public string SerializeLine(Block b, int line) => b.Lines[line].Text;
    public int LinePrefixLen(Block b, int line) => 0;

    public bool TryAutoFormat(DocumentEditor ed, string inserted) => false;
    public bool TryBlockCommit(DocumentEditor ed) => false;
}
