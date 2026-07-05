namespace Luxel.Document;

/// <summary>ブロックの種別。将来枠 (Image/Table 等) はここへ追加する。</summary>
public enum BlockKind
{
    Paragraph = 0,
    Heading,     // HeadingLevel 1..3
    ListItem,    // Ordered / Depth
    Quote,
    CodeBlock,   // CodeLang。Lines が複数行
    Divider,     // 内容なし (空 1 行 — キャレットは乗れる)
    Embed,       // Payload を持つ原子ブロック (テーブル/画像/ライブコードブロック等)。Lines は空 1 行
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

/// <summary>文書の 1 表示行 = 編集単位。テキスト (run 列) を持ち、<see cref="Version"/> は編集で進む
/// (表示側のレイアウトキャッシュキー)。行がどのブロックに属するかは <see cref="Block.Lines"/> が持つ —
/// エディタ (DocumentEditor) はフラットな行列 (<see cref="RichDocument.LineAt"/>) の上で動き、
/// ブロック構造は意味・装飾・ハイライトの管理単位。</summary>
public sealed class Line
{
    public List<InlineRun> Runs { get; } = new();
    public int Version { get; private set; }

    public Line() { }
    public Line(string text)
    {
        if (text.Length > 0) Runs.Add(new InlineRun(text));
    }

    public string Text => string.Concat(Runs.Select(r => r.Text));
    public int Length => Runs.Sum(r => r.Text.Length);

    /// <summary>編集で呼ぶ (表示キャッシュの無効化)。</summary>
    public void Bump() => Version++;

    public Line Clone()
    {
        var l = new Line();
        l.Runs.AddRange(Runs);
        return l;
    }
}

/// <summary>文書の 1 ブロック = 行のグループ + 意味属性 (見出しレベル/リスト/引用/コード言語/embed payload)。
/// 常に <see cref="Lines"/> ≥ 1 (Divider/Embed は空 1 行 — キャレット意味論を単純に保つ)。
/// 複数行を持つのは Quote と CodeBlock のみ (装飾とハイライトの単位が行グループであるため)。
/// <see cref="Version"/> は属性/payload の変更で進む (行テキストは <see cref="Line.Version"/>)。</summary>
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

    /// <summary>引用の深さ (0 = 引用外)。**全種別に直交して乗る** — markdown の引用はコンテナで、
    /// 見出し/リスト/コードにも重なる (<c>&gt; # x</c>) し入れ子にもなる (<c>&gt; &gt; x</c>)。
    /// 階層はツリーでなくこの属性で平坦に表現する (編集機構はフラットな行列のままにする設計判断)。
    /// Kind == Quote (引用の地のテキスト行) は常に ≥ 1。シリアライズは行頭 <c>"&gt; " × depth</c>。</summary>
    public int QuoteDepth { get; set; }

    /// <summary>コールアウト種別 (QuoteDepth ≥ 1 のとき有効、GitHub alert 記法 <c>&gt; [!NOTE]</c> 由来)。
    /// "NOTE"/"TIP"/"IMPORTANT"/"WARNING"/"CAUTION"。null = ただの引用。</summary>
    public string? Callout { get; set; }

    /// <summary><see cref="Lines"/>[0] がコールアウトのマーカー行 (<c>&gt; [!KIND]</c> — ラベルを表示し、
    /// シリアライズは記法へ戻す)。編集でマーカー行が抜かれた分割後ブロックは false (トーン色だけ残る)。</summary>
    public bool CalloutMarker { get; set; }

    /// <summary>ブロックの行列 (常に ≥ 1)。</summary>
    public List<Line> Lines { get; } = new() { new Line() };

    public int Version { get; private set; }

    public Block() { }
    public Block(BlockKind kind, string text = "")
    {
        Kind = kind;
        if (text.Length > 0)
        {
            // \n 入りテキストは行に分解 (CodeBlock の複数行初期化)
            string[] parts = text.Split('\n');
            Lines[0] = new Line(parts[0]);
            for (int i = 1; i < parts.Length; i++) Lines.Add(new Line(parts[i]));
        }
    }

    /// <summary>ブロック全文 (行間は \n)。ハイライトのキャッシュキー等、行グループを 1 テキストとして
    /// 扱う場面用。1 行ブロックでは行テキストと一致する。</summary>
    public string Text => Lines.Count == 1 ? Lines[0].Text : string.Join("\n", Lines.Select(l => l.Text));
    /// <summary>全行の合計文字数 (\n は数えない)。</summary>
    public int Length => Lines.Sum(l => l.Length);

    /// <summary>属性/payload の変更で呼ぶ (表示キャッシュの無効化)。全行も進める —
    /// 属性はブロック内全行の表示に効く (インデント/色/フォント)。</summary>
    public void Bump()
    {
        Version++;
        foreach (Line l in Lines) l.Bump();
    }

    public Block Clone()
    {
        var b = new Block
        {
            Kind = Kind, HeadingLevel = HeadingLevel, Ordered = Ordered, Depth = Depth, CodeLang = CodeLang,
            Payload = Payload?.Clone(), QuoteDepth = QuoteDepth, Callout = Callout, CalloutMarker = CalloutMarker,
        };
        b.Lines.Clear();
        b.Lines.AddRange(Lines.Select(l => l.Clone()));
        return b;
    }
}

/// <summary>リッチ文書 = ブロック列 (各ブロックは行 ≥ 1)。空文書でも段落 1 つは必ず持つ。
/// エディタが使う**フラットな行ビュー** (<see cref="LineAt"/>/<see cref="Locate"/>) を提供する —
/// 構造変更 (<see cref="StructureVersion"/> を進める操作) で行インデックスは自動再構築。</summary>
public sealed class RichDocument
{
    public List<Block> Blocks { get; } = new();

    /// <summary>ブロック/行の増減・差し替えで進む (表示側のノード再構成キー + 行インデックス無効化)。
    /// 構造を変えた側 (DocumentEditor) が <see cref="Mutated"/> を呼ぶ。</summary>
    public int StructureVersion { get; private set; }

    public RichDocument() => Blocks.Add(new Block(BlockKind.Paragraph));

    public static RichDocument FromBlocks(IEnumerable<Block> blocks)
    {
        var d = new RichDocument();
        d.Blocks.Clear();
        d.Blocks.AddRange(blocks);
        if (d.Blocks.Count == 0) d.Blocks.Add(new Block(BlockKind.Paragraph));
        d.Mutated();
        return d;
    }

    /// <summary>構造変更 (ブロック/行の増減・差し替え) を通知する。</summary>
    public void Mutated()
    {
        StructureVersion++;
        _indexStamp = -1;   // 行インデックス無効化
    }

    // ---- フラット行ビュー (行インデックス ⇄ (block, 行内 index) の対応キャッシュ) ----

    private int _indexStamp = -1;
    private int[] _firstLine = [];   // block index → 先頭のフラット行 index (末尾に総行数の番兵)

    private void EnsureIndex()
    {
        if (_indexStamp == StructureVersion) return;
        if (_firstLine.Length != Blocks.Count + 1) _firstLine = new int[Blocks.Count + 1];
        int li = 0;
        for (int b = 0; b < Blocks.Count; b++)
        {
            _firstLine[b] = li;
            li += Blocks[b].Lines.Count;
        }
        _firstLine[Blocks.Count] = li;
        _indexStamp = StructureVersion;
    }

    /// <summary>総行数。</summary>
    public int LineCount
    {
        get { EnsureIndex(); return _firstLine[Blocks.Count]; }
    }

    /// <summary>フラット行 index → 行。</summary>
    public Line LineAt(int line)
    {
        (int b, int l) = Locate(line);
        return Blocks[b].Lines[l];
    }

    /// <summary>フラット行 index → 所属ブロック。</summary>
    public Block BlockAt(int line) => Blocks[Locate(line).Block];

    /// <summary>フラット行 index → (block index, ブロック内行 index)。範囲外はクランプ。</summary>
    public (int Block, int Line) Locate(int line)
    {
        EnsureIndex();
        line = Math.Clamp(line, 0, LineCount - 1);
        // 二分探索: _firstLine[b] <= line < _firstLine[b+1]
        int lo = 0, hi = Blocks.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_firstLine[mid] <= line) lo = mid;
            else hi = mid - 1;
        }
        return (lo, line - _firstLine[lo]);
    }

    /// <summary>block index → 先頭のフラット行 index。</summary>
    public int FirstLineOf(int block)
    {
        EnsureIndex();
        return _firstLine[Math.Clamp(block, 0, Blocks.Count)];
    }

    /// <summary>プレーンテキスト全体 (行間は \n)。</summary>
    public string PlainText => string.Join("\n", Blocks.SelectMany(b => b.Lines).Select(l => l.Text));
}

/// <summary>文書内位置 (フラット行 index + 行内 char オフセット)。操作単位はグラフェム (エディタ側で吸着)。
/// エディタのテキスト機構は行だけを見る — ブロック境界は構造操作 (Enter/Backspace の意味論) でのみ意識する。</summary>
public readonly record struct DocPos(int Line, int Offset) : IComparable<DocPos>
{
    public int CompareTo(DocPos other)
        => Line != other.Line ? Line.CompareTo(other.Line) : Offset.CompareTo(other.Offset);
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
