namespace Luxel.Document;

/// <summary>ブロックの種別。将来枠 (Image/Table 等) はここへ追加する。</summary>
public enum BlockKind
{
    Paragraph = 0,
    Heading,     // HeadingLevel 1..3
    ListItem,    // Ordered / Depth
    Quote,
    CodeBlock,   // CodeLang。Text は複数行 (\n) を含みうる
    Divider,     // 内容なし
    Embed,       // Payload を持つ原子ブロック (テーブル/画像/ライブコードブロック等)。Runs は空
}

/// <summary>インラインの「意味」スタイル。見た目 (色/サイズ) はテーマとブロック型から導出する —
/// Markdown との round-trip を壊さないため、任意の色/サイズ指定は持たない (表示専用は RichTextView を使う)。</summary>
public readonly record struct InlineStyle(bool Bold = false, bool Italic = false, bool Code = false, string? Link = null,
                                          bool Math = false)
{
    public static readonly InlineStyle Plain = new();
    public bool IsPlain => !Bold && !Italic && !Code && Link is null && !Math;
}

/// <summary>連続した同一スタイルのテキスト片。</summary>
public sealed record InlineRun(string Text, InlineStyle Style)
{
    public InlineRun(string text) : this(text, InlineStyle.Plain) { }
}

/// <summary>文書の 1 ブロック (段落/見出し/リスト項目/引用/コードブロック/水平線)。
/// <see cref="Version"/> は編集で進む — 表示側のレイアウトキャッシュキー。</summary>
public sealed class Block
{
    public BlockKind Kind { get; set; } = BlockKind.Paragraph;
    /// <summary>Heading の 1..3。</summary>
    public int HeadingLevel { get; set; } = 1;
    /// <summary>ListItem: 番号付きか。</summary>
    public bool Ordered { get; set; }
    /// <summary>ListItem: ネスト深さ (0 起点)。</summary>
    public int Depth { get; set; }
    /// <summary>CodeBlock の言語 (表示ヒント)。</summary>
    public string CodeLang { get; set; } = "";
    /// <summary>Embed のデータ (Kind == Embed のとき有効)。immutable 運用 — 差し替えは
    /// DocumentEditor.ReplacePayload 経由 (undo ジャーナルに乗る)。</summary>
    public IBlockPayload? Payload { get; set; }

    /// <summary>コールアウト種別 (Kind == Quote のとき有効、GitHub alert 記法 <c>&gt; [!NOTE]</c> 由来)。
    /// "NOTE"/"TIP"/"IMPORTANT"/"WARNING"/"CAUTION"。null = ただの引用。</summary>
    public string? Callout { get; set; }

    /// <summary>コールアウトのマーカー行 (<c>&gt; [!NOTE]</c> そのもの — ラベルを表示し、
    /// シリアライズは記法へ戻す)。</summary>
    public bool CalloutMarker { get; set; }

    public List<InlineRun> Runs { get; } = new();
    public int Version { get; private set; }

    public Block() { }
    public Block(BlockKind kind, string text = "")
    {
        Kind = kind;
        if (text.Length > 0) Runs.Add(new InlineRun(text));
    }

    public string Text => string.Concat(Runs.Select(r => r.Text));
    public int Length => Runs.Sum(r => r.Text.Length);

    /// <summary>編集で呼ぶ (表示キャッシュの無効化)。</summary>
    public void Bump() => Version++;

    public Block Clone()
    {
        var b = new Block
        {
            Kind = Kind, HeadingLevel = HeadingLevel, Ordered = Ordered, Depth = Depth, CodeLang = CodeLang,
            Payload = Payload?.Clone(), Callout = Callout, CalloutMarker = CalloutMarker,
        };
        b.Runs.AddRange(Runs);
        return b;
    }
}

/// <summary>リッチ文書 = ブロック列。空文書でも段落 1 つは必ず持つ。</summary>
public sealed class RichDocument
{
    public List<Block> Blocks { get; } = new();

    public RichDocument() => Blocks.Add(new Block(BlockKind.Paragraph));

    public static RichDocument FromBlocks(IEnumerable<Block> blocks)
    {
        var d = new RichDocument();
        d.Blocks.Clear();
        d.Blocks.AddRange(blocks);
        if (d.Blocks.Count == 0) d.Blocks.Add(new Block(BlockKind.Paragraph));
        return d;
    }

    /// <summary>プレーンテキスト全体 (ブロック間は \n)。</summary>
    public string PlainText => string.Join("\n", Blocks.Select(b => b.Text));
}

/// <summary>文書内位置 (ブロック index + ブロック内 char オフセット)。操作単位はグラフェム (エディタ側で吸着)。</summary>
public readonly record struct DocPos(int Block, int Offset) : IComparable<DocPos>
{
    public int CompareTo(DocPos other)
        => Block != other.Block ? Block.CompareTo(other.Block) : Offset.CompareTo(other.Offset);
    public static bool operator <(DocPos a, DocPos b) => a.CompareTo(b) < 0;
    public static bool operator >(DocPos a, DocPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocPos a, DocPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocPos a, DocPos b) => a.CompareTo(b) >= 0;
}

/// <summary>選択範囲 (Anchor = 固定端, Caret = 可動端)。</summary>
public readonly record struct DocRange(DocPos Anchor, DocPos Caret)
{
    public bool IsCollapsed => Anchor == Caret;
    public DocPos Min => Anchor <= Caret ? Anchor : Caret;
    public DocPos Max => Anchor <= Caret ? Caret : Anchor;
}
