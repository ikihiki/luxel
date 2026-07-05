using System.Globalization;

namespace Luxel.Document;

/// <summary>
/// 文書編集モデル (UI 非依存・テスト可能)。**テキスト機構は行 (Line) 単位で動く** —
/// キャレット/選択/挿入削除 (グラフェム単位)/IME 合成 (キャレット行内)/スタイルトグル (run 分割/結合) は
/// フラットな行座標 (<see cref="DocPos"/> = 行 index + 行内オフセット) の上の一様な操作。
/// **ブロック (Block) は意味と構造の管理単位** — Enter/Backspace の意味論・型変換・undo 範囲は
/// ブロックを参照して決まる。
///
/// Enter の意味論: コード/引用 → 同一ブロック内の行追加 (グループ継続)、リスト項目 → 次項目
/// (空行なら解除して段落)、見出し → 後半は段落、コードは末尾空行で Enter するとブロックを抜ける。
/// 行頭 Backspace: コードの行 2 行目以降 = 行結合、引用の行 = 段落として切り出し (型解除)、
/// 1 行ブロックの非段落 = 型解除、段落 = 前ブロック末尾行と結合。
///
/// undo/redo は逆操作ジャーナル (影響**ブロック**範囲のスナップショット置換 — 行は所属ブロックごと保存)。
/// 連続タイプは 1 op に合体 (同一ブロック・キャレット連続・1 秒以内。キャレット移動/境界操作で切れる)。
/// IME 変換中は記録せず確定で 1 op。hybrid のソース展開/畳み込み (<see cref="HybridSwapLine"/> 等) は
/// ジャーナル外 — ブロック数が変わる場合はエントリの範囲を補正し、交差するときは履歴を破棄する。
/// </summary>
public sealed class DocumentEditor
{
    public RichDocument Doc { get; }
    public DocPos Caret { get; private set; }
    public DocPos Anchor { get; private set; }

    /// <summary>IME 編集中 (preedit)。キャレット行内のキャレット位置に挿入表示される。</summary>
    public string Composition { get; private set; } = "";
    public int CompTargetStart { get; private set; }
    public int CompTargetLen { get; private set; }

    /// <summary>ブロック/行の増減・差し替えで進む (表示側のノード再構成キー)。実体は
    /// <see cref="RichDocument.StructureVersion"/>。</summary>
    public int StructureVersion => Doc.StructureVersion;

    public DocumentEditor(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    public bool HasSelection => Caret != Anchor;
    public DocPos SelMin => Caret <= Anchor ? Caret : Anchor;
    public DocPos SelMax => Caret <= Anchor ? Anchor : Caret;
    /// <summary>キャレット行。</summary>
    public Line CaretLine => Doc.LineAt(Caret.Line);
    /// <summary>キャレット行の所属ブロック。</summary>
    public Block CaretBlock => Doc.BlockAt(Caret.Line);

    /// <summary>全文を置き換える (value signal 由来の外部更新)。プレーン段落列になり、undo 履歴は破棄。</summary>
    public void SetText(string text)
    {
        Doc.Blocks.Clear();
        foreach (string line in (text ?? "").Replace("\r", "").Split('\n'))
            Doc.Blocks.Add(new Block(BlockKind.Paragraph, line));
        Doc.Mutated();
        Caret = Anchor = Clamp(Caret);
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>文書を丸ごと差し替える (markdown 由来の外部更新)。undo 履歴は破棄。</summary>
    public void SetBlocks(IEnumerable<Block> blocks)
    {
        Doc.Blocks.Clear();
        Doc.Blocks.AddRange(blocks);
        if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
        Doc.Mutated();
        Caret = Anchor = Clamp(Caret);
        _undo.Clear();
        _redo.Clear();
    }

    // ---- 位置 ----

    private DocPos Clamp(DocPos p)
    {
        int l = Math.Clamp(p.Line, 0, Doc.LineCount - 1);
        return new DocPos(l, Math.Clamp(p.Offset, 0, Doc.LineAt(l).Length));
    }

    public void Select(DocPos anchor, DocPos caret)
    {
        Anchor = Clamp(anchor);
        Caret = Clamp(caret);
        _breakCoalesce = true;
    }

    public void PlaceCaret(DocPos p) => Select(p, p);

    public void MoveLeft(bool select)
    {
        DocPos p = Caret;
        if (p.Offset > 0) p = new DocPos(p.Line, PrevGrapheme(Doc.LineAt(p.Line).Text, p.Offset));
        else if (p.Line > 0) p = new DocPos(p.Line - 1, Doc.LineAt(p.Line - 1).Length);
        Caret = p;
        if (!select) Anchor = p;
        _breakCoalesce = true;
    }

    public void MoveRight(bool select)
    {
        DocPos p = Caret;
        if (p.Offset < Doc.LineAt(p.Line).Length) p = new DocPos(p.Line, NextGrapheme(Doc.LineAt(p.Line).Text, p.Offset));
        else if (p.Line < Doc.LineCount - 1) p = new DocPos(p.Line + 1, 0);
        Caret = p;
        if (!select) Anchor = p;
        _breakCoalesce = true;
    }

    public void Home(bool select)
    {
        Caret = new DocPos(Caret.Line, 0);
        if (!select) Anchor = Caret;
        _breakCoalesce = true;
    }

    public void End(bool select)
    {
        Caret = new DocPos(Caret.Line, Doc.LineAt(Caret.Line).Length);
        if (!select) Anchor = Caret;
        _breakCoalesce = true;
    }

    public void SelectAll()
    {
        Anchor = new DocPos(0, 0);
        Caret = new DocPos(Doc.LineCount - 1, Doc.LineAt(Doc.LineCount - 1).Length);
        _breakCoalesce = true;
    }

    // ---- undo/redo (逆操作ジャーナル: 影響ブロック範囲のスナップショット置換) ----

    private sealed class UndoEntry
    {
        public int Start;                       // 置換範囲の先頭 block index
        public Block[] Blocks = [];             // 適用時にこの列へ戻す (clone 保持 — 行ごと deep)
        public int LiveCount;                   // 現在文書側の範囲長 (適用時に置換される数)
        public DocPos Caret, Anchor;            // 適用後のキャレット状態
        public bool Typing;
        public DateTime At;
    }

    private readonly List<UndoEntry> _undo = new();
    private readonly List<UndoEntry> _redo = new();
    private bool _breakCoalesce;
    private const int MaxUndo = 200;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>変更を記録して実行する。範囲 [start, start+countBefore) が影響ブロック。</summary>
    private void Change(int start, int countBefore, bool typing, Action mutate)
    {
        var e = new UndoEntry
        {
            Start = start,
            Blocks = Doc.Blocks.Skip(start).Take(countBefore).Select(b => b.Clone()).ToArray(),
            Caret = Caret,
            Anchor = Anchor,
            Typing = typing,
            At = DateTime.UtcNow,
        };
        int total = Doc.Blocks.Count;
        mutate();
        e.LiveCount = countBefore + (Doc.Blocks.Count - total);
        _redo.Clear();

        // 連続タイプの合体: 直前もタイプ・同一ブロック・1 秒以内・間にキャレット移動なし
        if (typing && !_breakCoalesce && _undo.Count > 0)
        {
            UndoEntry p = _undo[^1];
            if (p.Typing && p.Start == e.Start && p.LiveCount == 1 && e.LiveCount == 1
                && (e.At - p.At) < TimeSpan.FromSeconds(1))
            {
                p.LiveCount = e.LiveCount;
                p.At = e.At;
                return;
            }
        }
        _breakCoalesce = false;
        _undo.Add(e);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        UndoEntry e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Apply(e));
        _breakCoalesce = true;
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        UndoEntry e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Apply(e));
        _breakCoalesce = true;
    }

    /// <summary>エントリを適用 (範囲を保存列へ置換 + キャレット復元) し、逆エントリを返す。</summary>
    private UndoEntry Apply(UndoEntry e)
    {
        var inverse = new UndoEntry
        {
            Start = e.Start,
            Blocks = Doc.Blocks.Skip(e.Start).Take(e.LiveCount).Select(b => b.Clone()).ToArray(),
            LiveCount = e.Blocks.Length,
            Caret = Caret,
            Anchor = Anchor,
        };
        Doc.Blocks.RemoveRange(e.Start, e.LiveCount);
        Doc.Blocks.InsertRange(e.Start, e.Blocks.Select(b => b.Clone()));
        if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
        Doc.Mutated();
        Caret = Clamp(e.Caret);
        Anchor = Clamp(e.Anchor);
        return inverse;
    }

    // ---- 編集 ----

    /// <summary>選択が重なるブロック範囲 (undo スナップショット用)。</summary>
    private (int start, int count) SelRange()
    {
        int b0 = Doc.Locate(SelMin.Line).Block;
        int b1 = Doc.Locate(SelMax.Line).Block;
        return (b0, b1 - b0 + 1);
    }

    /// <summary>キャレット位置へ挿入 (選択は置換)。\n を含むと行分割 (ブロック型の意味論に従う)。
    /// スタイルは直前文字を継承。Embed (原子) 上での入力は直後に段落を作ってそこへ入る。</summary>
    public void Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        (int start, int count) = SelRange();
        s = s.Replace("\r", "");
        bool typing = !s.Contains('\n') && !HasSelection && CaretBlock.Kind != BlockKind.Embed;
        Change(start, count, typing, () =>
        {
            DeleteSelectionCore();
            if (CaretBlock.Kind == BlockKind.Embed) EscapeEmbedForward();
            string[] parts = s.Split('\n');
            InsertText(CaretLine, Caret.Offset, parts[0]);
            Caret = new DocPos(Caret.Line, Caret.Offset + parts[0].Length);
            for (int i = 1; i < parts.Length; i++)
            {
                BreakLineAtCaret();
                InsertText(CaretLine, 0, parts[i]);
                Caret = new DocPos(Caret.Line, parts[i].Length);
            }
            Anchor = Caret;
        });
    }

    /// <summary>Enter。ブロック型の意味論に従う (クラス概要参照)。</summary>
    public void InsertNewline()
    {
        (int start, int count) = SelRange();
        Change(start, count, typing: false, () =>
        {
            DeleteSelectionCore();
            if (CaretBlock.Kind == BlockKind.Embed) { EscapeEmbedForward(); Anchor = Caret; return; }
            (int bi, int li) = Doc.Locate(Caret.Line);
            Block b = Doc.Blocks[bi];
            if (b.Kind == BlockKind.CodeBlock)
            {
                // 末尾の空行で Enter → コードを抜ける (空行を削って直後に段落)。それ以外は行追加。
                Line last = b.Lines[^1];
                if (li == b.Lines.Count - 1 && Caret.Offset == last.Length && last.Length == 0)
                {
                    if (b.Lines.Count > 1) b.Lines.RemoveAt(b.Lines.Count - 1);
                    Doc.Blocks.Insert(bi + 1, new Block(BlockKind.Paragraph));
                    Doc.Mutated();
                    Caret = new DocPos(Doc.FirstLineOf(bi + 1), 0);
                }
                else
                {
                    BreakLineAtCaret();
                }
            }
            else if (b.Kind is not BlockKind.Paragraph && Doc.LineAt(Caret.Line).Length == 0)
            {
                // 空行 (リスト項目/引用行/見出し) で Enter → 型解除: 行を段落として切り出す (分割しない)
                ExtractLineToParagraph(bi, li);
            }
            else
            {
                BreakLineAtCaret();
            }
            Anchor = Caret;
        });
    }

    public void Backspace()
    {
        if (HasSelection) { DeleteSelectionRecorded(); return; }
        (int bi, int li) = Doc.Locate(Caret.Line);
        Block b = Doc.Blocks[bi];
        if (b.Kind == BlockKind.Embed)
        {
            RemoveBlock(bi);   // 原子 — Backspace はブロックごと削除
        }
        else if (Caret.Offset > 0)
        {
            Change(bi, 1, typing: false, () =>
            {
                int prev = PrevGrapheme(CaretLine.Text, Caret.Offset);
                DeleteRange(CaretLine, prev, Caret.Offset);
                Caret = Anchor = new DocPos(Caret.Line, prev);
            });
        }
        else if (b.Kind == BlockKind.CodeBlock && li > 0)
        {
            // コード 2 行目以降の行頭 → 前の行と結合 (旧モデルの \n 削除に相当)
            Change(bi, 1, typing: false, () =>
            {
                int prevLen = b.Lines[li - 1].Length;
                MergeLineWithPrevious(b, li);
                Caret = Anchor = new DocPos(Caret.Line - 1, prevLen);
            });
        }
        else if (b.Kind == BlockKind.Quote)
        {
            // 引用行の行頭 → その行を段落として切り出す (型解除。マーカー行なら残りはトーンだけ残る)
            Change(bi, 1, typing: false, () => ExtractLineToParagraph(bi, li));
        }
        else if (b.Kind is not BlockKind.Paragraph)
        {
            // 行頭 Backspace はまず型解除 (リスト/見出し/コード先頭行 → 段落)
            Change(bi, 1, typing: false, () => ReleaseKind(bi));
        }
        else if (bi > 0 && Doc.Blocks[bi - 1].Kind == BlockKind.Embed)
        {
            RemoveBlock(bi - 1);   // Embed の直後で行頭 Backspace → Embed を削除 (結合しない)
        }
        else if (bi > 0)
        {
            Change(bi - 1, 2, typing: false, () =>
            {
                Line prevLine = Doc.Blocks[bi - 1].Lines[^1];
                int prevLen = prevLine.Length;
                MergeBlockIntoPrevious(bi);
                Caret = Anchor = new DocPos(Caret.Line - 1, prevLen);
            });
        }
    }

    public void DeleteForward()
    {
        if (HasSelection) { DeleteSelectionRecorded(); return; }
        (int bi, int li) = Doc.Locate(Caret.Line);
        Block b = Doc.Blocks[bi];
        if (b.Kind == BlockKind.Embed)
        {
            RemoveBlock(bi);
        }
        else if (Caret.Offset < CaretLine.Length)
        {
            Change(bi, 1, typing: false, () =>
                DeleteRange(CaretLine, Caret.Offset, NextGrapheme(CaretLine.Text, Caret.Offset)));
        }
        else if (li < b.Lines.Count - 1)
        {
            // ブロック内の次行と結合 (コード/引用の行末 Delete)
            Change(bi, 1, typing: false, () => MergeLineWithPrevious(b, li + 1));
        }
        else if (bi < Doc.Blocks.Count - 1 && Doc.Blocks[bi + 1].Kind == BlockKind.Embed)
        {
            RemoveBlock(bi + 1);   // 直後の Embed を削除 (結合しない)
        }
        else if (bi < Doc.Blocks.Count - 1)
        {
            Change(bi, 2, typing: false, () => MergeBlockIntoPrevious(bi + 1));
        }
    }

    /// <summary>ブロックを丸ごと削除する (Embed の原子削除)。undo 可。</summary>
    private void RemoveBlock(int index)
    {
        Change(index, 1, typing: false, () =>
        {
            Doc.Blocks.RemoveAt(index);
            if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
            Doc.Mutated();
            int target = Math.Max(0, Math.Min(index - 1, Doc.Blocks.Count - 1));
            int line = Doc.FirstLineOf(target + 1) - 1;   // target の末尾行
            Caret = Anchor = new DocPos(line, index > 0 ? Doc.LineAt(line).Length : 0);
        });
    }

    /// <summary>Embed 上のキャレットを「直後の段落」へ逃がす (なければ作る)。Insert/Enter 用。</summary>
    private void EscapeEmbedForward()
    {
        int bi = Doc.Locate(Caret.Line).Block;
        if (bi + 1 >= Doc.Blocks.Count || Doc.Blocks[bi + 1].Kind == BlockKind.Embed)
        {
            Doc.Blocks.Insert(bi + 1, new Block(BlockKind.Paragraph));
            Doc.Mutated();
        }
        Caret = new DocPos(Doc.FirstLineOf(bi + 1), 0);
    }

    private void DeleteSelectionRecorded()
    {
        (int start, int count) = SelRange();
        Change(start, count, typing: false, DeleteSelectionCore);
    }

    private void DeleteSelectionCore()
    {
        if (!HasSelection) return;
        DocPos a = SelMin, b = SelMax;
        (int ba, int la) = Doc.Locate(a.Line);
        (int bb, int lb) = Doc.Locate(b.Line);
        if (a.Line == b.Line)
        {
            if (Doc.Blocks[ba].Kind != BlockKind.Embed)
                DeleteRange(Doc.LineAt(a.Line), a.Offset, b.Offset);
            Caret = Anchor = a;
            return;
        }

        // 端の Embed は原子 — 部分削除でなく丸ごと削除対象
        bool firstEmbed = Doc.Blocks[ba].Kind == BlockKind.Embed;
        bool lastEmbed = Doc.Blocks[bb].Kind == BlockKind.Embed;
        Line first = Doc.LineAt(a.Line), last = Doc.LineAt(b.Line);
        if (!firstEmbed) DeleteRange(first, a.Offset, first.Length);   // 先頭行の後半
        if (!lastEmbed) DeleteRange(last, 0, b.Offset);                // 末尾行の前半

        if (ba == bb)
        {
            // 同一ブロック内 (コード/引用の複数行選択): 中間行を除去して端行を結合
            Block blk = Doc.Blocks[ba];
            for (int i = lb - 1; i > la; i--) blk.Lines.RemoveAt(i);
            MergeLineWithPrevious(blk, la + 1);
            Doc.Mutated();
            Caret = Anchor = Clamp(a);
            return;
        }

        // 末尾ブロック: 選択に入った行を除去 (残り行があればブロックは型ごと残る)
        Block tail = Doc.Blocks[bb];
        if (lastEmbed)
        {
            Doc.Blocks.RemoveAt(bb);
        }
        else
        {
            tail.Lines.RemoveRange(0, lb + 1);   // 選択に入った行 + 端行 (結合対象) を外す
            if (!firstEmbed)
            {
                foreach (InlineRun r in last.Runs.ToArray()) AppendRun(first, r);
                first.Bump();
            }
            else
            {
                tail.Lines.Insert(0, last);   // 先頭が Embed → 末尾行はブロックに残す
            }
            if (tail.Lines.Count == 0) Doc.Blocks.RemoveAt(bb);
            else tail.CalloutMarker = false;   // マーカー行 (Lines[0]) は選択に呑まれた
        }

        // 中間ブロック
        for (int i = bb - 1; i > ba; i--) Doc.Blocks.RemoveAt(i);

        // 先頭ブロック: 選択に入った行 (キャレット行の後ろ) を除去
        Block head = Doc.Blocks[ba];
        if (firstEmbed)
        {
            Doc.Blocks.RemoveAt(ba);
        }
        else
        {
            for (int i = head.Lines.Count - 1; i > la; i--) head.Lines.RemoveAt(i);
            first.Bump();
        }

        if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
        Doc.Mutated();
        Caret = Anchor = Clamp(a);
    }

    // ---- スタイルトグル (選択範囲。run の分割/結合) ----

    public void ToggleBold() => ToggleStyle(s => s.Bold, (s, v) => s with { Bold = v });
    public void ToggleItalic() => ToggleStyle(s => s.Italic, (s, v) => s with { Italic = v });
    public void ToggleCode() => ToggleStyle(s => s.Code, (s, v) => s with { Code = v });

    /// <summary>選択範囲の全文字が style を持つか (トグル方向とツールバー状態用)。選択なしは false。</summary>
    public bool SelectionHasStyle(Func<InlineStyle, bool> get)
    {
        if (!HasSelection) return false;
        bool all = true, any = false;
        ForEachSelectedSpan((l, s0, s1) =>
        {
            int pos = 0;
            foreach (InlineRun r in l.Runs)
            {
                int rs = pos, re = pos + r.Text.Length;
                pos = re;
                if (Math.Max(rs, s0) >= Math.Min(re, s1)) continue;
                any = true;
                if (!get(r.Style)) all = false;
            }
        });
        return any && all;
    }

    private void ToggleStyle(Func<InlineStyle, bool> get, Func<InlineStyle, bool, InlineStyle> set)
    {
        if (!HasSelection) return;
        bool value = !SelectionHasStyle(get);
        (int start, int count) = SelRange();
        Change(start, count, typing: false, () =>
            ForEachSelectedSpan((l, s0, s1) => ApplyStyle(l, s0, s1, st => set(st, value))));
    }

    /// <summary>選択が重なる各行の (line, 行内範囲) を列挙。CodeBlock/Divider/Embed の行は対象外。</summary>
    private void ForEachSelectedSpan(Action<Line, int, int> body)
    {
        DocPos a = SelMin, b = SelMax;
        for (int i = a.Line; i <= b.Line; i++)
        {
            if (Doc.BlockAt(i).Kind is BlockKind.CodeBlock or BlockKind.Divider or BlockKind.Embed) continue;
            Line ln = Doc.LineAt(i);
            int s0 = i == a.Line ? a.Offset : 0;
            int s1 = i == b.Line ? b.Offset : ln.Length;
            if (s1 > s0) body(ln, s0, s1);
        }
    }

    /// <summary>[start, end) の run スタイルを変換する (境界で run を分割、隣接同スタイルは結合)。</summary>
    internal static void ApplyStyle(Line l, int start, int end, Func<InlineStyle, InlineStyle> f)
    {
        if (end <= start) return;
        var result = new List<InlineRun>();
        int pos = 0;
        foreach (InlineRun r in l.Runs)
        {
            int rs = pos, re = pos + r.Text.Length;
            pos = re;
            int cutS = Math.Max(rs, start), cutE = Math.Min(re, end);
            if (cutS >= cutE) { result.Add(r); continue; }
            if (cutS > rs) result.Add(r with { Text = r.Text[..(cutS - rs)] });
            result.Add(new InlineRun(r.Text[(cutS - rs)..(cutE - rs)], f(r.Style)));
            if (cutE < re) result.Add(r with { Text = r.Text[(cutE - rs)..] });
        }
        l.Runs.Clear();
        foreach (InlineRun r in result)
        {
            if (r.Text.Length == 0) continue;
            if (l.Runs.Count > 0 && l.Runs[^1].Style == r.Style)
                l.Runs[^1] = l.Runs[^1] with { Text = l.Runs[^1].Text + r.Text };
            else l.Runs.Add(r);
        }
        l.Bump();
    }

    // ---- ブロック型変換 ----

    /// <summary>選択範囲 (なければキャレットブロック) の型を変換する。
    /// 全ブロックが既に同型ならトグルで段落へ戻す。CodeBlock へは範囲の全行を 1 ブロックに結合、
    /// CodeBlock から他型へは行毎に 1 ブロックへ分解する。Quote へ`は範囲の全行を 1 ブロックに束ねる。</summary>
    public void SetBlockKind(BlockKind kind, int headingLevel = 1, bool ordered = false)
    {
        (int start, int count) = SelRange();
        bool allSame = Doc.Blocks.Skip(start).Take(count).All(x =>
            x.Kind == kind
            && (kind != BlockKind.Heading || x.HeadingLevel == headingLevel)
            && (kind != BlockKind.ListItem || x.Ordered == ordered));
        BlockKind target = allSame ? BlockKind.Paragraph : kind;

        Change(start, count, typing: false, () =>
        {
            var result = new List<Block>();
            if (target is BlockKind.CodeBlock or BlockKind.Quote)
            {
                // 連続する非 Embed 区間の行を 1 ブロックへ束ねる (コードはインラインスタイルが落ちる)。
                // Embed は原子 — 位置を保って素通しし、グループを区切る
                Block? grouped = null;
                foreach (Block blk in Doc.Blocks.Skip(start).Take(count))
                {
                    if (blk.Kind == BlockKind.Embed) { result.Add(blk); grouped = null; continue; }
                    if (grouped is null)
                    {
                        grouped = new Block(target);
                        if (target == BlockKind.Quote) grouped.QuoteDepth = 1;
                        grouped.Lines.Clear();
                        result.Add(grouped);
                    }
                    foreach (Line l in blk.Lines)
                        grouped.Lines.Add(target == BlockKind.CodeBlock ? new Line(l.Text) : l.Clone());
                }
                foreach (Block g in result)
                    if (g.Kind == target && g.Lines.Count == 0) g.Lines.Add(new Line());
            }
            else
            {
                foreach (Block blk in Doc.Blocks.Skip(start).Take(count))
                {
                    if (blk.Kind == BlockKind.Embed)
                    {
                        result.Add(blk);   // Embed は原子 — 型変換の対象外
                    }
                    else if (blk.Lines.Count > 1)
                    {
                        foreach (Line l in blk.Lines)
                            result.Add(Styled(new Block(target) { QuoteDepth = target == BlockKind.Paragraph ? 0 : blk.QuoteDepth }, l));
                    }
                    else
                    {
                        blk.Kind = target;
                        blk.Callout = null;
                        blk.CalloutMarker = false;
                        if (target == BlockKind.Paragraph) blk.QuoteDepth = 0;   // 段落へのトグルは引用も抜ける
                        if (target == BlockKind.Heading) blk.HeadingLevel = headingLevel;
                        if (target == BlockKind.ListItem) blk.Ordered = ordered;
                        blk.Bump();
                        result.Add(blk);
                    }
                }
            }
            Doc.Blocks.RemoveRange(start, count);
            Doc.Blocks.InsertRange(start, result);
            Doc.Mutated();
            Caret = Clamp(Caret);
            Anchor = Clamp(Anchor);

            Block Styled(Block x, Line l)
            {
                x.Lines.Clear();
                x.Lines.Add(l.Clone());
                if (target == BlockKind.Heading) x.HeadingLevel = headingLevel;
                if (target == BlockKind.ListItem) x.Ordered = ordered;
                return x;
            }
        });
    }

    /// <summary>キャレットブロックを埋め込みブロックへ変換する (テキストは payload に取り込まれた前提で
    /// 破棄)。キャレットは直後の段落へ (なければ作る)。フォーマットの「行の確定 = ライブブロック化」
    /// (TryBlockCommit) 等から使う。undo 1 op。</summary>
    public void ConvertToEmbed(IBlockPayload payload)
    {
        int bi = Doc.Locate(Caret.Line).Block;
        Change(bi, 1, typing: false, () =>
        {
            Block b = Doc.Blocks[bi];
            b.Kind = BlockKind.Embed;
            b.Lines.Clear();
            b.Lines.Add(new Line());
            b.Payload = payload;
            b.Bump();
            Doc.Mutated();
            EscapeEmbedForward();
            Anchor = Caret;
        });
    }

    /// <summary>キャレットブロックの直後に埋め込みブロックを挿入し、その後ろの段落へキャレットを移す。</summary>
    public void InsertEmbed(IBlockPayload payload)
    {
        int bi = Doc.Locate(Caret.Line).Block;
        Change(bi, 1, typing: false, () =>
        {
            Doc.Blocks.Insert(bi + 1, new Block(BlockKind.Embed) { Payload = payload });
            if (bi + 2 >= Doc.Blocks.Count || Doc.Blocks[bi + 2].Kind == BlockKind.Embed)
                Doc.Blocks.Insert(bi + 2, new Block(BlockKind.Paragraph));
            Doc.Mutated();
            Caret = Anchor = new DocPos(Doc.FirstLineOf(bi + 2), 0);
        });
    }

    /// <summary>埋め込みブロックの payload を差し替える (内部編集の確定 — undo 1 op)。
    /// 埋め込み widget は Doc を直接触らず必ずこの経由で反映すること。</summary>
    public void ReplacePayload(int block, IBlockPayload payload)
    {
        if (block < 0 || block >= Doc.Blocks.Count || Doc.Blocks[block].Kind != BlockKind.Embed) return;
        Change(block, 1, typing: false, () =>
        {
            Doc.Blocks[block].Payload = payload;
            Doc.Blocks[block].Bump();
        });
    }

    /// <summary>キャレットブロックの直後に水平線を挿入し、その後ろへキャレットを移す (段落がなければ作る)。</summary>
    public void InsertDivider()
    {
        int bi = Doc.Locate(Caret.Line).Block;
        Change(bi, 1, typing: false, () =>
        {
            Doc.Blocks.Insert(bi + 1, new Block(BlockKind.Divider));
            if (bi + 2 >= Doc.Blocks.Count || Doc.Blocks[bi + 2].Kind == BlockKind.Divider)
                Doc.Blocks.Insert(bi + 2, new Block(BlockKind.Paragraph));
            Doc.Mutated();
            Caret = Anchor = new DocPos(Doc.FirstLineOf(bi + 2), 0);
        });
    }

    /// <summary>型解除 — **内側から一段ずつ**。属性の内側 (見出し/リスト/コード) を外すと
    /// 「同じ深さの引用テキスト」になり、引用は深さ −1、深さ 0 で段落に戻る。</summary>
    private void ReleaseKind(int bi)
    {
        Block b = Doc.Blocks[bi];
        if (b.Lines.Count == 1)
        {
            ReleaseOneLevel(b);
            b.Bump();
            return;
        }
        // 複数行 (コード先頭行の型解除): 行毎に「同じ深さの引用テキスト」(深さ 0 なら段落) へ分解
        var result = b.Lines.Select(l =>
        {
            Block p = ReleasedText(b.QuoteDepth);
            p.Lines[0] = l;
            return p;
        }).ToList();
        Doc.Blocks.RemoveAt(bi);
        Doc.Blocks.InsertRange(bi, result);
        Doc.Mutated();
        Caret = Clamp(Caret);
        Anchor = Clamp(Anchor);
    }

    /// <summary>ブロックの型を一段だけ解除する (行はそのまま)。</summary>
    private static void ReleaseOneLevel(Block b)
    {
        if (b.Kind == BlockKind.Quote)
        {
            if (b.QuoteDepth > 1) b.QuoteDepth--;   // 引用の入れ子を一段抜ける
            else { b.Kind = BlockKind.Paragraph; b.QuoteDepth = 0; }
        }
        else if (b.QuoteDepth > 0)
        {
            b.Kind = BlockKind.Quote;   // 見出し等を外す — 深さは維持 (引用の地のテキストへ)
        }
        else
        {
            b.Kind = BlockKind.Paragraph;
        }
        b.Callout = null;
        b.CalloutMarker = false;
    }

    /// <summary>「深さ qd の引用テキスト」ブロック (qd == 0 なら段落)。型解除の受け皿。</summary>
    private static Block ReleasedText(int qd)
        => qd > 0 ? new Block(BlockKind.Quote) { QuoteDepth = qd } : new Block(BlockKind.Paragraph);

    /// <summary>ブロック内の行 li を一段外へ切り出す (引用の型解除/エスケープ) — 深さ qd の引用行は
    /// qd−1 の引用テキスト (深さ 1 なら段落) になる。必要ならブロックを分割する。
    /// 切り出し後もキャレット行 index は変わらない。</summary>
    private void ExtractLineToParagraph(int bi, int li)
    {
        Block b = Doc.Blocks[bi];
        if (b.Lines.Count == 1) { ReleaseKind(bi); return; }

        Line line = b.Lines[li];
        Block released = ReleasedText(b.Kind == BlockKind.Quote ? Math.Max(1, b.QuoteDepth) - 1 : b.QuoteDepth);
        released.Lines[0] = line;
        b.Lines.RemoveAt(li);
        if (li == 0)
        {
            if (b.CalloutMarker) { b.CalloutMarker = false; b.Callout = null; }   // マーカーが抜けたらただの引用
            Doc.Blocks.Insert(bi, released);
        }
        else if (li == b.Lines.Count)   // 除去後の末尾 = 元の最終行だった
        {
            Doc.Blocks.Insert(bi + 1, released);
        }
        else
        {
            // 中間行: 後半を同型の新ブロックへ (Callout はトーン継続、マーカーは前半のみ)
            var suffix = new Block { Kind = b.Kind, Callout = b.Callout, CodeLang = b.CodeLang, QuoteDepth = b.QuoteDepth };
            suffix.Lines.Clear();
            while (b.Lines.Count > li) { suffix.Lines.Add(b.Lines[li]); b.Lines.RemoveAt(li); }
            Doc.Blocks.Insert(bi + 1, released);
            Doc.Blocks.Insert(bi + 2, suffix);
        }
        b.Bump();
        Doc.Mutated();
        Caret = Anchor = Clamp(Caret with { Offset = 0 });
    }

    /// <summary>範囲のプレーンテキスト (行間は \n)。クリップボード用。</summary>
    public string GetText(DocPos a, DocPos b)
    {
        if (b < a) (a, b) = (b, a);
        a = Clamp(a); b = Clamp(b);
        if (a.Line == b.Line) return Doc.LineAt(a.Line).Text[a.Offset..b.Offset];
        var sb = new System.Text.StringBuilder();
        sb.Append(Doc.LineAt(a.Line).Text[a.Offset..]);
        for (int i = a.Line + 1; i < b.Line; i++) sb.Append('\n').Append(Doc.LineAt(i).Text);
        sb.Append('\n').Append(Doc.LineAt(b.Line).Text[..b.Offset]);
        return sb.ToString();
    }

    // ---- hybrid 表示 / オートフォーマット支援 ----

    /// <summary>hybrid のソース展開: 行 line を所属ブロックから 1 行ブロック <paramref name="repl"/> として
    /// 切り出す (**ジャーナルに乗せない** — 表示状態の変換であって編集ではない)。所属ブロックが複数行なら
    /// 分割し、増えたブロック数だけジャーナルの範囲を補正する (交差するエントリがあれば履歴破棄)。
    /// <paramref name="caretOffset"/> 指定時はキャレットをその行内へ移す。</summary>
    public void HybridSwapLine(int line, Block repl, int? caretOffset = null)
    {
        (int bi, int li) = Doc.Locate(line);
        Block b = Doc.Blocks[bi];
        int before = Doc.Blocks.Count;
        if (b.Lines.Count == 1)
        {
            Doc.Blocks[bi] = repl;
        }
        else
        {
            // 分割: [前半 (同型)] repl [後半 (同型)] — マーカーは前半のみ
            var pieces = new List<Block>();
            if (li > 0)
            {
                var head = new Block { Kind = b.Kind, Callout = b.Callout, CalloutMarker = b.CalloutMarker, CodeLang = b.CodeLang, QuoteDepth = b.QuoteDepth };
                head.Lines.Clear();
                for (int i = 0; i < li; i++) head.Lines.Add(b.Lines[i]);
                pieces.Add(head);
            }
            pieces.Add(repl);
            if (li < b.Lines.Count - 1)
            {
                var tail = new Block { Kind = b.Kind, Callout = b.Callout, CodeLang = b.CodeLang, QuoteDepth = b.QuoteDepth };
                tail.Lines.Clear();
                for (int i = li + 1; i < b.Lines.Count; i++) tail.Lines.Add(b.Lines[i]);
                pieces.Add(tail);
            }
            Doc.Blocks.RemoveAt(bi);
            Doc.Blocks.InsertRange(bi, pieces);
            AdjustJournal(bi, 1, pieces.Count);
        }
        Doc.Mutated();
        if (caretOffset is int co && Caret.Line == line)
            Caret = Anchor = new DocPos(line, Math.Clamp(co, 0, repl.Lines[0].Length));
        else { Caret = Clamp(Caret); Anchor = Clamp(Anchor); }
    }

    /// <summary>hybrid の畳み込み: 行 line (ソース展開中の 1 行ブロック) を再パース結果
    /// <paramref name="parsed"/> で置き換え、隣接する同種引用と正規化マージする (ジャーナル外)。</summary>
    public void HybridRestoreLine(int line, Block parsed)
    {
        int bi = Doc.Locate(line).Block;
        Doc.Blocks[bi] = parsed;
        Doc.Mutated();
        NormalizeQuotesAround(bi);
        Caret = Clamp(Caret);
        Anchor = Clamp(Anchor);
    }

    /// <summary>bi の前後で隣接する Quote ブロック (同一 Callout、後続がマーカーでない) をマージする。
    /// ジャーナル外 — 減ったブロック数ぶん範囲を補正する。</summary>
    public void NormalizeQuotesAround(int bi)
    {
        // 後ろ方向 → 前方向の順で、bi を含む近傍だけ見る
        for (int i = Math.Min(bi + 1, Doc.Blocks.Count - 1); i > 0 && i >= bi; i--)
            TryMergeQuoteInto(i);
        if (bi < Doc.Blocks.Count && bi > 0) TryMergeQuoteInto(bi);
    }

    private void TryMergeQuoteInto(int i)
    {
        if (i <= 0 || i >= Doc.Blocks.Count) return;
        Block prev = Doc.Blocks[i - 1], cur = Doc.Blocks[i];
        if (prev.Kind != BlockKind.Quote || cur.Kind != BlockKind.Quote) return;
        if (prev.Callout != cur.Callout || cur.CalloutMarker) return;
        if (prev.QuoteDepth != cur.QuoteDepth) return;
        foreach (Line l in cur.Lines) prev.Lines.Add(l);
        Doc.Blocks.RemoveAt(i);
        AdjustJournal(i, 1, 0);
        Doc.Mutated();
        prev.Bump();
    }

    /// <summary>ジャーナル外の構造変更 ([index, index+removed) → inserted 個) にエントリ範囲を追従させる。
    /// 交差するエントリがあれば履歴ごと破棄する (安全側 — hybrid の再構成は稀)。</summary>
    private void AdjustJournal(int index, int removed, int inserted)
    {
        int delta = inserted - removed;
        if (delta == 0) return;
        bool Conflicts(UndoEntry e) => e.Start < index + removed && e.Start + Math.Max(e.LiveCount, e.Blocks.Length) > index;
        if (_undo.Any(Conflicts) || _redo.Any(Conflicts)) { _undo.Clear(); _redo.Clear(); return; }
        foreach (UndoEntry e in _undo) if (e.Start >= index + removed) e.Start += delta;
        foreach (UndoEntry e in _redo) if (e.Start >= index + removed) e.Start += delta;
    }

    /// <summary>行頭オートフォーマット: キャレット行の先頭 <paramref name="prefixLen"/> 文字
    /// (打ち終えた記法) を削って型変換する。引用へは隣接する引用ブロックとマージ (1 undo op)。</summary>
    public void ApplyAutoFormat(BlockKind kind, int prefixLen, int headingLevel = 1, bool ordered = false)
    {
        int bi = Doc.Locate(Caret.Line).Block;
        bool mergePrev = kind == BlockKind.Quote && bi > 0
            && Doc.Blocks[bi - 1] is { Kind: BlockKind.Quote, Callout: null, QuoteDepth: 1 };
        bool mergeNext = kind == BlockKind.Quote && bi + 1 < Doc.Blocks.Count
            && Doc.Blocks[bi + 1] is { Kind: BlockKind.Quote, Callout: null, CalloutMarker: false, QuoteDepth: 1 };
        int start = mergePrev ? bi - 1 : bi;
        int count = 1 + (mergePrev ? 1 : 0) + (mergeNext ? 1 : 0);

        Change(start, count, typing: false, () =>
        {
            Block b = Doc.Blocks[bi];
            Line line = b.Lines[0];
            DeleteRange(line, 0, prefixLen);
            b.Kind = kind;
            if (kind == BlockKind.Quote) b.QuoteDepth = Math.Max(1, b.QuoteDepth);
            if (kind == BlockKind.Heading) b.HeadingLevel = headingLevel;
            if (kind == BlockKind.ListItem) { b.Ordered = ordered; b.Depth = 0; }
            b.Bump();
            Doc.Mutated();
            Caret = Anchor = new DocPos(Caret.Line, Math.Max(0, Caret.Offset - prefixLen));
            if (mergeNext) TryMergeQuoteIntoRecorded(bi + 1);
            if (mergePrev) TryMergeQuoteIntoRecorded(bi);
        });

        // Change 内のマージ (スナップショット済み範囲) — ジャーナル補正は不要
        void TryMergeQuoteIntoRecorded(int i)
        {
            Block prev = Doc.Blocks[i - 1], cur = Doc.Blocks[i];
            if (prev.Kind != BlockKind.Quote || cur.Kind != BlockKind.Quote) return;
            foreach (Line l in cur.Lines) prev.Lines.Add(l);
            Doc.Blocks.RemoveAt(i);
            Doc.Mutated();
            prev.Bump();
            Caret = Clamp(Caret);
            Anchor = Clamp(Anchor);
        }
    }

    /// <summary>"```lang" 段落 → 空のコードブロック (フェンス開始のオートフォーマット)。1 undo op。</summary>
    public void ConvertToCodeFence(string lang)
    {
        int bi = Doc.Locate(Caret.Line).Block;
        Change(bi, 1, typing: false, () =>
        {
            Block b = Doc.Blocks[bi];
            b.Kind = BlockKind.CodeBlock;
            b.CodeLang = lang;
            b.Lines.Clear();
            b.Lines.Add(new Line());
            b.Bump();
            Doc.Mutated();
            Caret = Anchor = new DocPos(Caret.Line, 0);
        });
    }

    // ---- 行/ブロック操作 (run 保存) ----

    /// <summary>キャレット位置で行を分割する。コード/引用は同一ブロック内の行追加、
    /// それ以外はブロック分割 (後半の型は継承規則: リスト=次項目、見出し=段落)。</summary>
    private void BreakLineAtCaret()
    {
        (int bi, int li) = Doc.Locate(Caret.Line);
        Block b = Doc.Blocks[bi];
        if (b.Kind is BlockKind.CodeBlock or BlockKind.Quote)
        {
            var tail = new Line();
            MoveRunsAfter(b.Lines[li], Caret.Offset, tail);
            b.Lines.Insert(li + 1, tail);
        }
        else
        {
            // 後半の型: リスト = 次項目 (深さ維持)、それ以外は引用継続 (深さ維持) or 段落
            Block tailB = b.Kind switch
            {
                BlockKind.ListItem => new Block { Kind = BlockKind.ListItem, Ordered = b.Ordered, Depth = b.Depth, QuoteDepth = b.QuoteDepth },
                _ => ReleasedText(b.QuoteDepth),
            };
            MoveRunsAfter(b.Lines[li], Caret.Offset, tailB.Lines[0]);
            Doc.Blocks.Insert(bi + 1, tailB);
        }
        Doc.Mutated();
        Caret = new DocPos(Caret.Line + 1, 0);
    }

    /// <summary>ブロック内の行 li を前の行へ結合する。</summary>
    private void MergeLineWithPrevious(Block b, int li)
    {
        Line prev = b.Lines[li - 1], cur = b.Lines[li];
        foreach (InlineRun r in cur.Runs) AppendRun(prev, r);
        b.Lines.RemoveAt(li);
        Doc.Mutated();
        prev.Bump();
    }

    /// <summary>ブロック index の先頭行を前ブロックの末尾行へ結合する (残り行があれば同型で残す)。</summary>
    private void MergeBlockIntoPrevious(int index)
    {
        Block prev = Doc.Blocks[index - 1];
        Block cur = Doc.Blocks[index];
        Line dst = prev.Lines[^1];
        foreach (InlineRun r in cur.Lines[0].Runs) AppendRun(dst, r);
        cur.Lines.RemoveAt(0);
        if (cur.Lines.Count == 0) Doc.Blocks.RemoveAt(index);
        else if (cur.CalloutMarker) cur.CalloutMarker = false;   // マーカー行が吸われた
        Doc.Mutated();
        dst.Bump();
    }

    /// <summary>offset へテキストを挿入 (直前文字の run スタイルを継承。先頭ならプレーン/先頭 run のスタイル)。</summary>
    internal static void InsertText(Line l, int offset, string s)
    {
        if (s.Length == 0) return;
        int pos = 0;
        for (int i = 0; i < l.Runs.Count; i++)
        {
            InlineRun r = l.Runs[i];
            if (offset <= pos + r.Text.Length)
            {
                int local = offset - pos;
                // offset==pos (run 先頭) は直前 run のスタイル継承 → 前 run の末尾扱い
                if (local == 0 && i > 0)
                {
                    InlineRun p = l.Runs[i - 1];
                    l.Runs[i - 1] = p with { Text = p.Text + s };
                }
                else
                {
                    l.Runs[i] = r with { Text = r.Text[..local] + s + r.Text[local..] };
                }
                l.Bump();
                return;
            }
            pos += r.Text.Length;
        }
        // 末尾 (または空行)
        if (l.Runs.Count > 0)
        {
            InlineRun last = l.Runs[^1];
            l.Runs[^1] = last with { Text = last.Text + s };
        }
        else l.Runs.Add(new InlineRun(s));
        l.Bump();
    }

    /// <summary>[start, end) を削除 (run 跨ぎ対応、空 run は除去、隣接同スタイルは結合)。</summary>
    internal static void DeleteRange(Line l, int start, int end)
    {
        if (end <= start) return;
        var result = new List<InlineRun>();
        int pos = 0;
        foreach (InlineRun r in l.Runs)
        {
            int rs = pos, re = pos + r.Text.Length;
            pos = re;
            int cutS = Math.Max(rs, start), cutE = Math.Min(re, end);
            if (cutS >= cutE) { result.Add(r); continue; }
            string kept = r.Text[..(cutS - rs)] + r.Text[(cutE - rs)..];
            if (kept.Length > 0) result.Add(r with { Text = kept });
        }
        l.Runs.Clear();
        for (int i = 0; i < result.Count; i++)
        {
            if (l.Runs.Count > 0 && l.Runs[^1].Style == result[i].Style)
                l.Runs[^1] = l.Runs[^1] with { Text = l.Runs[^1].Text + result[i].Text };
            else l.Runs.Add(result[i]);
        }
        l.Bump();
    }

    private static void MoveRunsAfter(Line src, int offset, Line dst)
    {
        // run を保存して後半を移す (リッチ分割 — ED-M2 のプレーン化から昇格)
        int pos = 0;
        var moved = new List<InlineRun>();
        foreach (InlineRun r in src.Runs)
        {
            int rs = pos, re = pos + r.Text.Length;
            pos = re;
            if (re <= offset) continue;
            moved.Add(rs >= offset ? r : r with { Text = r.Text[(offset - rs)..] });
        }
        DeleteRange(src, offset, src.Length);
        foreach (InlineRun r in moved) AppendRun(dst, r);
    }

    private static void AppendRun(Line l, InlineRun r)
    {
        if (r.Text.Length == 0) return;
        if (l.Runs.Count > 0 && l.Runs[^1].Style == r.Style)
            l.Runs[^1] = l.Runs[^1] with { Text = l.Runs[^1].Text + r.Text };
        else l.Runs.Add(r);
    }

    // ---- IME (キャレット行内。TextEditor と同じ意味論) ----

    public void SetComposition(string text, int targetStart = 0, int targetLen = 0)
    {
        if (Composition.Length == 0 && HasSelection) DeleteSelectionRecorded();
        Composition = text ?? "";
        CompTargetStart = targetStart;
        CompTargetLen = targetLen;
        CaretLine.Bump();   // 表示テキストが変わる
    }

    public void CommitComposition(string final)
    {
        Composition = "";
        CompTargetStart = CompTargetLen = 0;
        CaretLine.Bump();
        Insert(final);
    }

    /// <summary>行の表示テキスト (キャレット行は preedit 挿入済み)。</summary>
    public string DisplayTextOf(int line)
    {
        Line l = Doc.LineAt(line);
        if (line != Caret.Line || Composition.Length == 0) return l.Text;
        string t = l.Text;
        return t[..Caret.Offset] + Composition + t[Caret.Offset..];
    }

    /// <summary>キャレット行表示内のキャレット位置 (preedit 末尾)。</summary>
    public int DisplayCaretOffset => Caret.Offset + Composition.Length;
    /// <summary>キャレット行表示内の preedit 範囲。</summary>
    public (int start, int len) CompositionDisplayRange => (Caret.Offset, Composition.Length);
    /// <summary>キャレット行表示内の変換対象節範囲。</summary>
    public (int start, int len) TargetDisplayRange => (Caret.Offset + CompTargetStart, CompTargetLen);

    // ---- ITextInput 相当 (TSF: 現在行 = 文書) ----

    public string CurrentBlockText => CaretLine.Text;

    public (int start, int length) SelectionInBlock
    {
        get
        {
            DocPos a = SelMin, b = SelMax;
            if (a.Line != Caret.Line || b.Line != Caret.Line) return (Caret.Offset, 0);
            return (a.Offset, b.Offset - a.Offset);
        }
    }

    public void SelectInBlock(int start, int end)
    {
        int len = CaretLine.Length;
        Anchor = new DocPos(Caret.Line, Math.Clamp(start, 0, len));
        Caret = new DocPos(Caret.Line, Math.Clamp(end, 0, len));
    }

    public void ReplaceInBlock(int start, int end, string s)
    {
        int len = CaretLine.Length;
        start = Math.Clamp(start, 0, len);
        end = Math.Clamp(end, start, len);
        Change(Doc.Locate(Caret.Line).Block, 1, typing: true, () =>
        {
            DeleteRange(CaretLine, start, end);
            InsertText(CaretLine, start, s ?? "");
            Caret = Anchor = new DocPos(Caret.Line, start + (s?.Length ?? 0));
        });
    }

    // ---- グラフェム境界 (StringInfo — .NET は ICU ベースで UAX#29 準拠) ----

    internal static int PrevGrapheme(string text, int index)
    {
        int prev = 0;
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            int end = e.ElementIndex + ((string)e.Current).Length;
            if (end >= index) return e.ElementIndex;
            prev = end;
        }
        return prev;
    }

    internal static int NextGrapheme(string text, int index)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            int end = e.ElementIndex + ((string)e.Current).Length;
            if (end > index) return end;
        }
        return text.Length;
    }
}
