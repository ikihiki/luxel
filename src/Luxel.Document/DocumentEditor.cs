using System.Globalization;

namespace Luxel.Document;

/// <summary>
/// 文書編集モデル (UI 非依存・テスト可能)。<see cref="RichDocument"/> に対する
/// キャレット/選択/挿入削除 (グラフェム単位)/Enter 分割/Backspace 結合/IME 合成 (キャレットブロック内)、
/// スタイルトグル (run 分割/結合)/ブロック型変換/undo・redo を扱う。
/// 挿入・削除は run (スタイル) を保存する — 挿入は直前文字のスタイルを継承。
/// 構造変化 (ブロック増減) は <see cref="StructureVersion"/>、テキスト変化は各 Block.Version が進む —
/// 表示側の部分更新キー。
///
/// Enter の意味論: リスト項目 → 次項目 (空項目なら解除して段落)、引用 → 引用継続、見出し → 後半は段落、
/// コードブロック → リテラル改行 (末尾空行で Enter するとブロックを抜ける)。
/// 行頭 Backspace: 段落以外はまず型解除 (段落化)、段落は前ブロックと結合。
///
/// undo/redo は逆操作ジャーナル (影響ブロック範囲のスナップショット置換)。連続タイプは 1 op に合体
/// (同一ブロック・キャレット連続・1 秒以内。キャレット移動/境界操作で切れる)。IME 変換中は記録せず確定で 1 op。
/// </summary>
public sealed class DocumentEditor
{
    public RichDocument Doc { get; }
    public DocPos Caret { get; private set; }
    public DocPos Anchor { get; private set; }

    /// <summary>IME 編集中 (preedit)。キャレットブロック内のキャレット位置に挿入表示される。</summary>
    public string Composition { get; private set; } = "";
    public int CompTargetStart { get; private set; }
    public int CompTargetLen { get; private set; }

    /// <summary>ブロックの増減・差し替えで進む (表示側のノード再構成キー)。</summary>
    public int StructureVersion { get; private set; }

    public DocumentEditor(RichDocument? doc = null) => Doc = doc ?? new RichDocument();

    public bool HasSelection => Caret != Anchor;
    public DocPos SelMin => Caret <= Anchor ? Caret : Anchor;
    public DocPos SelMax => Caret <= Anchor ? Anchor : Caret;
    public Block CaretBlock => Doc.Blocks[Caret.Block];

    /// <summary>全文を置き換える (value signal 由来の外部更新)。プレーン段落列になり、undo 履歴は破棄。</summary>
    public void SetText(string text)
    {
        Doc.Blocks.Clear();
        foreach (string line in (text ?? "").Replace("\r", "").Split('\n'))
            Doc.Blocks.Add(new Block(BlockKind.Paragraph, line));
        StructureVersion++;
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
        StructureVersion++;
        Caret = Anchor = Clamp(Caret);
        _undo.Clear();
        _redo.Clear();
    }

    // ---- 位置 ----

    private DocPos Clamp(DocPos p)
    {
        int b = Math.Clamp(p.Block, 0, Doc.Blocks.Count - 1);
        return new DocPos(b, Math.Clamp(p.Offset, 0, Doc.Blocks[b].Length));
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
        if (p.Offset > 0) p = new DocPos(p.Block, PrevGrapheme(Doc.Blocks[p.Block].Text, p.Offset));
        else if (p.Block > 0) p = new DocPos(p.Block - 1, Doc.Blocks[p.Block - 1].Length);
        Caret = p;
        if (!select) Anchor = p;
        _breakCoalesce = true;
    }

    public void MoveRight(bool select)
    {
        DocPos p = Caret;
        if (p.Offset < Doc.Blocks[p.Block].Length) p = new DocPos(p.Block, NextGrapheme(Doc.Blocks[p.Block].Text, p.Offset));
        else if (p.Block < Doc.Blocks.Count - 1) p = new DocPos(p.Block + 1, 0);
        Caret = p;
        if (!select) Anchor = p;
        _breakCoalesce = true;
    }

    public void Home(bool select)
    {
        Caret = new DocPos(Caret.Block, 0);
        if (!select) Anchor = Caret;
        _breakCoalesce = true;
    }

    public void End(bool select)
    {
        Caret = new DocPos(Caret.Block, Doc.Blocks[Caret.Block].Length);
        if (!select) Anchor = Caret;
        _breakCoalesce = true;
    }

    public void SelectAll()
    {
        Anchor = new DocPos(0, 0);
        Caret = new DocPos(Doc.Blocks.Count - 1, Doc.Blocks[^1].Length);
        _breakCoalesce = true;
    }

    // ---- undo/redo (逆操作ジャーナル: 影響ブロック範囲のスナップショット置換) ----

    private sealed class UndoEntry
    {
        public int Start;                       // 置換範囲の先頭 block index
        public Block[] Blocks = [];             // 適用時にこの列へ戻す (clone 保持)
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
            Caret = Caret, Anchor = Anchor,
            Typing = typing, At = DateTime.UtcNow,
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
            Caret = Caret, Anchor = Anchor,
        };
        Doc.Blocks.RemoveRange(e.Start, e.LiveCount);
        Doc.Blocks.InsertRange(e.Start, e.Blocks.Select(b => b.Clone()));
        if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
        StructureVersion++;
        Caret = Clamp(e.Caret);
        Anchor = Clamp(e.Anchor);
        return inverse;
    }

    // ---- 編集 ----

    private (int start, int count) SelRange()
        => (SelMin.Block, SelMax.Block - SelMin.Block + 1);

    /// <summary>キャレット位置へ挿入 (選択は置換)。\n を含むとブロック分割。スタイルは直前文字を継承。
    /// Embed (原子) 上での入力は直後に段落を作ってそこへ入る。</summary>
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
            InsertText(Caret.Block, Caret.Offset, parts[0]);
            Caret = new DocPos(Caret.Block, Caret.Offset + parts[0].Length);
            for (int i = 1; i < parts.Length; i++)
            {
                SplitBlockAtCaret();
                InsertText(Caret.Block, 0, parts[i]);
                Caret = new DocPos(Caret.Block, parts[i].Length);
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
            Block b = CaretBlock;
            if (b.Kind == BlockKind.CodeBlock)
            {
                // 末尾の空行で Enter → コードを抜ける (空行を削って直後に段落)。それ以外はリテラル改行。
                if (Caret.Offset == b.Length && (b.Length == 0 || b.Text.EndsWith('\n')))
                {
                    if (b.Length > 0) DeleteRange(b, b.Length - 1, b.Length);
                    Doc.Blocks.Insert(Caret.Block + 1, new Block(BlockKind.Paragraph));
                    StructureVersion++;
                    Caret = new DocPos(Caret.Block + 1, 0);
                }
                else
                {
                    InsertText(b, Caret.Offset, "\n");
                    Caret = new DocPos(Caret.Block, Caret.Offset + 1);
                }
            }
            else if (b.Kind is not BlockKind.Paragraph && b.Length == 0)
            {
                // 空のリスト項目/引用/見出しで Enter → 型解除 (分割しない)
                MakeParagraph(b);
            }
            else
            {
                SplitBlockAtCaret();
            }
            Anchor = Caret;
        });
    }

    public void Backspace()
    {
        if (HasSelection) { DeleteSelectionRecorded(); return; }
        if (CaretBlock.Kind == BlockKind.Embed)
        {
            RemoveBlock(Caret.Block);   // 原子 — Backspace はブロックごと削除
        }
        else if (Caret.Offset > 0)
        {
            Change(Caret.Block, 1, typing: false, () =>
            {
                int prev = PrevGrapheme(CaretBlock.Text, Caret.Offset);
                DeleteRange(Caret.Block, prev, Caret.Offset);
                Caret = Anchor = new DocPos(Caret.Block, prev);
            });
        }
        else if (CaretBlock.Kind is not BlockKind.Paragraph)
        {
            // 行頭 Backspace はまず型解除 (リスト/引用/見出し/コード → 段落)
            Change(Caret.Block, 1, typing: false, () => MakeParagraph(CaretBlock));
        }
        else if (Caret.Block > 0 && Doc.Blocks[Caret.Block - 1].Kind == BlockKind.Embed)
        {
            RemoveBlock(Caret.Block - 1);   // Embed の直後で行頭 Backspace → Embed を削除 (結合しない)
        }
        else if (Caret.Block > 0)
        {
            Change(Caret.Block - 1, 2, typing: false, () =>
            {
                int prevLen = Doc.Blocks[Caret.Block - 1].Length;
                MergeWithPrevious(Caret.Block);
                Caret = Anchor = new DocPos(Caret.Block - 1, prevLen);
            });
        }
    }

    public void DeleteForward()
    {
        if (HasSelection) { DeleteSelectionRecorded(); return; }
        if (CaretBlock.Kind == BlockKind.Embed)
        {
            RemoveBlock(Caret.Block);
        }
        else if (Caret.Offset < CaretBlock.Length)
        {
            Change(Caret.Block, 1, typing: false, () =>
                DeleteRange(Caret.Block, Caret.Offset, NextGrapheme(CaretBlock.Text, Caret.Offset)));
        }
        else if (Caret.Block < Doc.Blocks.Count - 1 && Doc.Blocks[Caret.Block + 1].Kind == BlockKind.Embed)
        {
            RemoveBlock(Caret.Block + 1);   // 直後の Embed を削除 (結合しない)
        }
        else if (Caret.Block < Doc.Blocks.Count - 1)
        {
            Change(Caret.Block, 2, typing: false, () => MergeWithPrevious(Caret.Block + 1));
        }
    }

    /// <summary>ブロックを丸ごと削除する (Embed の原子削除)。undo 可。</summary>
    private void RemoveBlock(int index)
    {
        Change(index, 1, typing: false, () =>
        {
            Doc.Blocks.RemoveAt(index);
            if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
            StructureVersion++;
            int target = Math.Max(0, Math.Min(index - 1, Doc.Blocks.Count - 1));
            Caret = Anchor = new DocPos(target, index > 0 ? Doc.Blocks[target].Length : 0);
        });
    }

    /// <summary>Embed 上のキャレットを「直後の段落」へ逃がす (なければ作る)。Insert/Enter 用。</summary>
    private void EscapeEmbedForward()
    {
        int bi = Caret.Block;
        if (bi + 1 >= Doc.Blocks.Count || Doc.Blocks[bi + 1].Kind == BlockKind.Embed)
        {
            Doc.Blocks.Insert(bi + 1, new Block(BlockKind.Paragraph));
            StructureVersion++;
        }
        Caret = new DocPos(bi + 1, 0);
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
        if (a.Block == b.Block)
        {
            if (Doc.Blocks[a.Block].Kind != BlockKind.Embed)
                DeleteRange(a.Block, a.Offset, b.Offset);
            Caret = Anchor = a;
            return;
        }
        // 端の Embed は原子 — 部分削除でなく丸ごと削除対象
        bool firstEmbed = Doc.Blocks[a.Block].Kind == BlockKind.Embed;
        bool lastEmbed = Doc.Blocks[b.Block].Kind == BlockKind.Embed;
        if (!firstEmbed) DeleteRange(a.Block, a.Offset, Doc.Blocks[a.Block].Length);   // 先頭ブロックの後半
        if (!lastEmbed) DeleteRange(b.Block, 0, b.Offset);                             // 末尾ブロックの前半
        for (int i = b.Block - 1; i > a.Block; i--) Doc.Blocks.RemoveAt(i);            // 中間ブロック
        if (lastEmbed) Doc.Blocks.RemoveAt(a.Block + 1);
        if (firstEmbed) Doc.Blocks.RemoveAt(a.Block);
        StructureVersion++;
        if (Doc.Blocks.Count == 0) Doc.Blocks.Add(new Block(BlockKind.Paragraph));
        if (!firstEmbed && !lastEmbed) MergeWithPrevious(a.Block + 1);
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
        ForEachSelectedSpan((b, s0, s1) =>
        {
            int pos = 0;
            foreach (InlineRun r in b.Runs)
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
            ForEachSelectedSpan((b, s0, s1) => ApplyStyle(b, s0, s1, st => set(st, value))));
    }

    /// <summary>選択が重なる各ブロックの (block, ブロック内範囲) を列挙。CodeBlock/Divider/Embed は対象外。</summary>
    private void ForEachSelectedSpan(Action<Block, int, int> body)
    {
        DocPos a = SelMin, b = SelMax;
        for (int i = a.Block; i <= b.Block; i++)
        {
            Block blk = Doc.Blocks[i];
            if (blk.Kind is BlockKind.CodeBlock or BlockKind.Divider or BlockKind.Embed) continue;
            int s0 = i == a.Block ? a.Offset : 0;
            int s1 = i == b.Block ? b.Offset : blk.Length;
            if (s1 > s0) body(blk, s0, s1);
        }
    }

    /// <summary>[start, end) の run スタイルを変換する (境界で run を分割、隣接同スタイルは結合)。</summary>
    internal static void ApplyStyle(Block b, int start, int end, Func<InlineStyle, InlineStyle> f)
    {
        if (end <= start) return;
        var result = new List<InlineRun>();
        int pos = 0;
        foreach (InlineRun r in b.Runs)
        {
            int rs = pos, re = pos + r.Text.Length;
            pos = re;
            int cutS = Math.Max(rs, start), cutE = Math.Min(re, end);
            if (cutS >= cutE) { result.Add(r); continue; }
            if (cutS > rs) result.Add(r with { Text = r.Text[..(cutS - rs)] });
            result.Add(new InlineRun(r.Text[(cutS - rs)..(cutE - rs)], f(r.Style)));
            if (cutE < re) result.Add(r with { Text = r.Text[(cutE - rs)..] });
        }
        b.Runs.Clear();
        foreach (InlineRun r in result)
        {
            if (r.Text.Length == 0) continue;
            if (b.Runs.Count > 0 && b.Runs[^1].Style == r.Style)
                b.Runs[^1] = b.Runs[^1] with { Text = b.Runs[^1].Text + r.Text };
            else b.Runs.Add(r);
        }
        b.Bump();
    }

    // ---- ブロック型変換 ----

    /// <summary>選択範囲 (なければキャレットブロック) の型を変換する。
    /// 全ブロックが既に同型ならトグルで段落へ戻す。CodeBlock へは範囲を 1 ブロックに結合、
    /// CodeBlock から他型へは行 (\n) 毎に分解する。</summary>
    public void SetBlockKind(BlockKind kind, int headingLevel = 1, bool ordered = false)
    {
        DocPos a = SelMin, sb = SelMax;
        int start = a.Block, count = sb.Block - start + 1;
        bool allSame = Doc.Blocks.Skip(start).Take(count).All(x =>
            x.Kind == kind
            && (kind != BlockKind.Heading || x.HeadingLevel == headingLevel)
            && (kind != BlockKind.ListItem || x.Ordered == ordered));
        BlockKind target = allSame ? BlockKind.Paragraph : kind;

        Change(start, count, typing: false, () =>
        {
            var result = new List<Block>();
            if (target == BlockKind.CodeBlock)
            {
                // 範囲を 1 つのコードブロックへ結合 (インラインスタイルは落ちる)。Embed は変換対象外 = 素通し
                string text = string.Join("\n",
                    Doc.Blocks.Skip(start).Take(count).Where(x => x.Kind != BlockKind.Embed).Select(x => x.Text));
                result.Add(new Block(BlockKind.CodeBlock, text));
                result.AddRange(Doc.Blocks.Skip(start).Take(count).Where(x => x.Kind == BlockKind.Embed));
            }
            else
            {
                foreach (Block blk in Doc.Blocks.Skip(start).Take(count))
                {
                    if (blk.Kind == BlockKind.Embed)
                    {
                        result.Add(blk);   // Embed は原子 — 型変換の対象外
                    }
                    else if (blk.Kind == BlockKind.CodeBlock)
                    {
                        foreach (string line in blk.Text.Split('\n'))
                            result.Add(Styled(new Block(target, line)));
                    }
                    else
                    {
                        blk.Kind = target;
                        if (target == BlockKind.Heading) blk.HeadingLevel = headingLevel;
                        if (target == BlockKind.ListItem) blk.Ordered = ordered;
                        blk.Bump();
                        result.Add(blk);
                    }
                }
            }
            Doc.Blocks.RemoveRange(start, count);
            Doc.Blocks.InsertRange(start, result);
            StructureVersion++;
            Caret = Clamp(Caret);
            Anchor = Clamp(Anchor);

            Block Styled(Block x)
            {
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
        Change(Caret.Block, 1, typing: false, () =>
        {
            Block b = CaretBlock;
            b.Kind = BlockKind.Embed;
            b.Runs.Clear();
            b.Payload = payload;
            b.Bump();
            EscapeEmbedForward();
            Anchor = Caret;
        });
    }

    /// <summary>キャレットブロックの直後に埋め込みブロックを挿入し、その後ろの段落へキャレットを移す。</summary>
    public void InsertEmbed(IBlockPayload payload)
    {
        int b = Caret.Block;
        Change(b, 1, typing: false, () =>
        {
            Doc.Blocks.Insert(b + 1, new Block(BlockKind.Embed) { Payload = payload });
            if (b + 2 >= Doc.Blocks.Count || Doc.Blocks[b + 2].Kind == BlockKind.Embed)
                Doc.Blocks.Insert(b + 2, new Block(BlockKind.Paragraph));
            StructureVersion++;
            Caret = Anchor = new DocPos(b + 2, 0);
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
        int b = Caret.Block;
        Change(b, 1, typing: false, () =>
        {
            Doc.Blocks.Insert(b + 1, new Block(BlockKind.Divider));
            if (b + 2 >= Doc.Blocks.Count || Doc.Blocks[b + 2].Kind == BlockKind.Divider)
                Doc.Blocks.Insert(b + 2, new Block(BlockKind.Paragraph));
            StructureVersion++;
            Caret = Anchor = new DocPos(b + 2, 0);
        });
    }

    private void MakeParagraph(Block b)
    {
        b.Kind = BlockKind.Paragraph;
        b.Bump();
    }

    /// <summary>範囲のプレーンテキスト (ブロック間は \n)。クリップボード用。</summary>
    public string GetText(DocPos a, DocPos b)
    {
        if (b < a) (a, b) = (b, a);
        a = Clamp(a); b = Clamp(b);
        if (a.Block == b.Block) return Doc.Blocks[a.Block].Text[a.Offset..b.Offset];
        var sb = new System.Text.StringBuilder();
        sb.Append(Doc.Blocks[a.Block].Text[a.Offset..]);
        for (int i = a.Block + 1; i < b.Block; i++) sb.Append('\n').Append(Doc.Blocks[i].Text);
        sb.Append('\n').Append(Doc.Blocks[b.Block].Text[..b.Offset]);
        return sb.ToString();
    }

    // ---- hybrid 表示 / オートフォーマット支援 ----

    /// <summary>ブロックを **ジャーナルに乗せず** 1:1 置換する (hybrid のソース展開/畳み込み用 —
    /// 表示状態の変換であって編集ではないため undo 対象にしない。1:1 なので既存エントリの範囲整合は保たれる)。
    /// <paramref name="caretOffset"/> 指定時はキャレットをそのブロック内へ移す。</summary>
    public void SwapBlock(int index, Block b, int? caretOffset = null)
    {
        Doc.Blocks[index] = b;
        StructureVersion++;
        if (caretOffset is int co && Caret.Block == index)
            Caret = Anchor = new DocPos(index, Math.Clamp(co, 0, b.Length));
        else { Caret = Clamp(Caret); Anchor = Clamp(Anchor); }
    }

    /// <summary>行頭オートフォーマット: キャレットブロックの先頭 <paramref name="prefixLen"/> 文字
    /// (打ち終えた記法) を削って型変換する。1 undo op。</summary>
    public void ApplyAutoFormat(BlockKind kind, int prefixLen, int headingLevel = 1, bool ordered = false)
    {
        Change(Caret.Block, 1, typing: false, () =>
        {
            Block b = CaretBlock;
            DeleteRange(b, 0, prefixLen);
            b.Kind = kind;
            if (kind == BlockKind.Heading) b.HeadingLevel = headingLevel;
            if (kind == BlockKind.ListItem) { b.Ordered = ordered; b.Depth = 0; }
            b.Bump();
            Caret = Anchor = new DocPos(Caret.Block, Math.Max(0, Caret.Offset - prefixLen));
        });
    }

    /// <summary>"```lang" 段落 → 空のコードブロック (フェンス開始のオートフォーマット)。1 undo op。</summary>
    public void ConvertToCodeFence(string lang)
    {
        Change(Caret.Block, 1, typing: false, () =>
        {
            Block b = CaretBlock;
            DeleteRange(b, 0, b.Length);
            b.Kind = BlockKind.CodeBlock;
            b.CodeLang = lang;
            b.Bump();
            Caret = Anchor = new DocPos(Caret.Block, 0);
        });
    }

    // ---- ブロック操作 (run 保存) ----

    /// <summary>キャレット位置でブロックを分割し、キャレットを新ブロック先頭へ。
    /// 後半ブロックの型は継承規則に従う (リスト=次項目、引用=継続、見出し=段落)。</summary>
    private void SplitBlockAtCaret()
    {
        Block src = CaretBlock;
        Block tail = src.Kind switch
        {
            BlockKind.ListItem => new Block { Kind = BlockKind.ListItem, Ordered = src.Ordered, Depth = src.Depth },
            BlockKind.Quote => new Block(BlockKind.Quote),
            _ => new Block(BlockKind.Paragraph),
        };
        MoveRunsAfter(src, Caret.Offset, tail);
        Doc.Blocks.Insert(Caret.Block + 1, tail);
        StructureVersion++;
        src.Bump();
        Caret = new DocPos(Caret.Block + 1, 0);
    }

    /// <summary>ブロック index を前のブロックへ結合する。</summary>
    private void MergeWithPrevious(int index)
    {
        Block prev = Doc.Blocks[index - 1];
        Block cur = Doc.Blocks[index];
        foreach (InlineRun r in cur.Runs) AppendRun(prev, r);
        Doc.Blocks.RemoveAt(index);
        StructureVersion++;
        prev.Bump();
    }

    /// <summary>offset へテキストを挿入 (直前文字の run スタイルを継承。先頭ならプレーン/先頭 run のスタイル)。</summary>
    internal static void InsertText(Block b, int offset, string s)
    {
        if (s.Length == 0) return;
        int pos = 0;
        for (int i = 0; i < b.Runs.Count; i++)
        {
            InlineRun r = b.Runs[i];
            if (offset <= pos + r.Text.Length)
            {
                int local = offset - pos;
                // offset==pos (run 先頭) は直前 run のスタイル継承 → 前 run の末尾扱い
                if (local == 0 && i > 0)
                {
                    InlineRun p = b.Runs[i - 1];
                    b.Runs[i - 1] = p with { Text = p.Text + s };
                }
                else
                {
                    b.Runs[i] = r with { Text = r.Text[..local] + s + r.Text[local..] };
                }
                b.Bump();
                return;
            }
            pos += r.Text.Length;
        }
        // 末尾 (または空ブロック)
        if (b.Runs.Count > 0)
        {
            InlineRun last = b.Runs[^1];
            b.Runs[^1] = last with { Text = last.Text + s };
        }
        else b.Runs.Add(new InlineRun(s));
        b.Bump();
    }

    private void InsertText(int block, int offset, string s) => InsertText(Doc.Blocks[block], offset, s);

    /// <summary>[start, end) を削除 (run 跨ぎ対応、空 run は除去、隣接同スタイルは結合)。</summary>
    internal static void DeleteRange(Block b, int start, int end)
    {
        if (end <= start) return;
        var result = new List<InlineRun>();
        int pos = 0;
        foreach (InlineRun r in b.Runs)
        {
            int rs = pos, re = pos + r.Text.Length;
            pos = re;
            int cutS = Math.Max(rs, start), cutE = Math.Min(re, end);
            if (cutS >= cutE) { result.Add(r); continue; }
            string kept = r.Text[..(cutS - rs)] + r.Text[(cutE - rs)..];
            if (kept.Length > 0) result.Add(r with { Text = kept });
        }
        b.Runs.Clear();
        for (int i = 0; i < result.Count; i++)
        {
            if (b.Runs.Count > 0 && b.Runs[^1].Style == result[i].Style)
                b.Runs[^1] = b.Runs[^1] with { Text = b.Runs[^1].Text + result[i].Text };
            else b.Runs.Add(result[i]);
        }
        b.Bump();
    }

    private void DeleteRange(int block, int start, int end) => DeleteRange(Doc.Blocks[block], start, end);

    private static void MoveRunsAfter(Block src, int offset, Block dst)
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

    private static void AppendRun(Block b, InlineRun r)
    {
        if (r.Text.Length == 0) return;
        if (b.Runs.Count > 0 && b.Runs[^1].Style == r.Style)
            b.Runs[^1] = b.Runs[^1] with { Text = b.Runs[^1].Text + r.Text };
        else b.Runs.Add(r);
    }

    // ---- IME (キャレットブロック内。TextEditor と同じ意味論) ----

    public void SetComposition(string text, int targetStart = 0, int targetLen = 0)
    {
        if (Composition.Length == 0 && HasSelection) DeleteSelectionRecorded();
        Composition = text ?? "";
        CompTargetStart = targetStart;
        CompTargetLen = targetLen;
        CaretBlock.Bump();   // 表示テキストが変わる
    }

    public void CommitComposition(string final)
    {
        Composition = "";
        CompTargetStart = CompTargetLen = 0;
        CaretBlock.Bump();
        Insert(final);
    }

    /// <summary>ブロックの表示テキスト (キャレットブロックは preedit 挿入済み)。</summary>
    public string DisplayTextOf(int block)
    {
        Block b = Doc.Blocks[block];
        if (block != Caret.Block || Composition.Length == 0) return b.Text;
        string t = b.Text;
        return t[..Caret.Offset] + Composition + t[Caret.Offset..];
    }

    /// <summary>キャレットブロック表示内のキャレット位置 (preedit 末尾)。</summary>
    public int DisplayCaretOffset => Caret.Offset + Composition.Length;
    /// <summary>キャレットブロック表示内の preedit 範囲。</summary>
    public (int start, int len) CompositionDisplayRange => (Caret.Offset, Composition.Length);
    /// <summary>キャレットブロック表示内の変換対象節範囲。</summary>
    public (int start, int len) TargetDisplayRange => (Caret.Offset + CompTargetStart, CompTargetLen);

    // ---- ITextInput 相当 (TSF: 現在ブロック = 文書) ----

    public string CurrentBlockText => CaretBlock.Text;

    public (int start, int length) SelectionInBlock
    {
        get
        {
            DocPos a = SelMin, b = SelMax;
            if (a.Block != Caret.Block || b.Block != Caret.Block) return (Caret.Offset, 0);
            return (a.Offset, b.Offset - a.Offset);
        }
    }

    public void SelectInBlock(int start, int end)
    {
        int len = CaretBlock.Length;
        Anchor = new DocPos(Caret.Block, Math.Clamp(start, 0, len));
        Caret = new DocPos(Caret.Block, Math.Clamp(end, 0, len));
    }

    public void ReplaceInBlock(int start, int end, string s)
    {
        int len = CaretBlock.Length;
        start = Math.Clamp(start, 0, len);
        end = Math.Clamp(end, start, len);
        Change(Caret.Block, 1, typing: true, () =>
        {
            DeleteRange(Caret.Block, start, end);
            InsertText(Caret.Block, start, s ?? "");
            Caret = Anchor = new DocPos(Caret.Block, start + (s?.Length ?? 0));
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
