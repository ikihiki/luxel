namespace Luxel.Editor;

/// <summary>
/// 編集/移動コマンド — いずれも純関数 <c>(EditorState) → Transaction</c> で、canvas 不要で単体テストできる。
/// **マルチカーソル対応**: 全選択レンジに対して 1 つの <see cref="ChangeSet"/> を作り、結果選択も各レンジぶん
/// 計算する (単一カーソルは n=1)。縦移動だけはジオメトリ (goal-x) が要るので view 側 (EditorGeometry.MoveVertical)。
/// </summary>
public static class EditCommands
{
    /// <summary>各選択レンジを <paramref name="text"/> で置換して挿入する (タイプ/貼り付け)。</summary>
    public static Transaction InsertText(EditorState s, string text)
    {
        IReadOnlyList<SelectionRange> ranges = s.Selection.Ranges;
        var specs = new ChangeSpec[ranges.Count];
        for (int i = 0; i < ranges.Count; i++) specs[i] = new ChangeSpec(ranges[i].From, ranges[i].To, text);
        ChangeSet cs = ChangeSet.Of(s.Doc.Length, specs);

        var carets = new SelectionRange[ranges.Count];
        for (int i = 0; i < ranges.Count; i++) carets[i] = SelectionRange.Cursor(cs.MapPos(ranges[i].From, +1));
        return s.Update(new TransactionSpec
        {
            Changes = specs,
            Selection = EditorSelection.Of(carets, s.Selection.MainIndex),
            ScrollIntoView = true,
        });
    }

    /// <summary>改行を挿入。</summary>
    public static Transaction InsertNewline(EditorState s) => InsertText(s, "\n");

    /// <summary>Backspace — 選択があれば削除、無ければキャレット直前の 1 グラフェムを削除。</summary>
    public static Transaction DeleteBackward(EditorState s) => Delete(s, forward: false);

    /// <summary>Delete — 選択があれば削除、無ければキャレット直後の 1 グラフェムを削除。</summary>
    public static Transaction DeleteForward(EditorState s) => Delete(s, forward: true);

    private static Transaction Delete(EditorState s, bool forward)
    {
        string t = s.Doc.Text;
        IReadOnlyList<SelectionRange> ranges = s.Selection.Ranges;
        var specs = new List<ChangeSpec>(ranges.Count);
        var delStart = new int[ranges.Count];
        for (int i = 0; i < ranges.Count; i++)
        {
            SelectionRange r = ranges[i];
            if (!r.Empty) { specs.Add(new ChangeSpec(r.From, r.To, "")); delStart[i] = r.From; }
            else if (!forward && r.From > 0) { int a = StepLeft(t, r.From); specs.Add(new ChangeSpec(a, r.From, "")); delStart[i] = a; }
            else if (forward && r.From < t.Length) { int b = StepRight(t, r.From); specs.Add(new ChangeSpec(r.From, b, "")); delStart[i] = r.From; }
            else delStart[i] = r.From;   // 端で削除なし
        }
        ChangeSet cs = ChangeSet.Of(s.Doc.Length, specs);
        var carets = new SelectionRange[ranges.Count];
        for (int i = 0; i < ranges.Count; i++) carets[i] = SelectionRange.Cursor(cs.MapPos(delStart[i]));
        return s.Update(new TransactionSpec
        {
            Changes = specs,
            Selection = EditorSelection.Of(carets, s.Selection.MainIndex),
            ScrollIntoView = true,
        });
    }

    /// <summary>← 左へ (選択中は選択端を縮める / extend で伸ばす)。</summary>
    public static Transaction MoveLeft(EditorState s, bool select) => MoveHorizontal(s, -1, select);
    /// <summary>→ 右へ。</summary>
    public static Transaction MoveRight(EditorState s, bool select) => MoveHorizontal(s, +1, select);

    private static Transaction MoveHorizontal(EditorState s, int dir, bool select)
    {
        string t = s.Doc.Text;
        IReadOnlyList<SelectionRange> ranges = s.Selection.Ranges;
        var next = new SelectionRange[ranges.Count];
        for (int i = 0; i < ranges.Count; i++)
        {
            SelectionRange r = ranges[i];
            if (select)
            {
                int head = dir < 0 ? StepLeft(t, r.Head) : StepRight(t, r.Head);
                next[i] = new SelectionRange(r.Anchor, head);
            }
            else if (!r.Empty)
                next[i] = SelectionRange.Cursor(dir < 0 ? r.From : r.To);   // 選択を潰す
            else
                next[i] = SelectionRange.Cursor(dir < 0 ? StepLeft(t, r.Head) : StepRight(t, r.Head));
        }
        return Reselect(s, next);
    }

    /// <summary>Home — 行頭へ (行内の各キャレット)。</summary>
    public static Transaction MoveLineStart(EditorState s, bool select) => MoveLineEdge(s, home: true, select);
    /// <summary>End — 行末へ。</summary>
    public static Transaction MoveLineEnd(EditorState s, bool select) => MoveLineEdge(s, home: false, select);

    private static Transaction MoveLineEdge(EditorState s, bool home, bool select)
    {
        TextDoc doc = s.Doc;
        IReadOnlyList<SelectionRange> ranges = s.Selection.Ranges;
        var next = new SelectionRange[ranges.Count];
        for (int i = 0; i < ranges.Count; i++)
        {
            SelectionRange r = ranges[i];
            int line = doc.LineOf(r.Head);
            int pos = home ? doc.LineStart(line) : doc.LineEnd(line);
            next[i] = select ? new SelectionRange(r.Anchor, pos) : SelectionRange.Cursor(pos);
        }
        return Reselect(s, next);
    }

    /// <summary>全選択。</summary>
    public static Transaction SelectAll(EditorState s)
        => Reselect(s, [new SelectionRange(0, s.Doc.Length)]);

    /// <summary>選択を差し替える (クリック/ドラッグ/縦移動結果を反映)。</summary>
    public static Transaction SetSelection(EditorState s, EditorSelection selection)
        => s.Update(new TransactionSpec { Selection = selection, ScrollIntoView = true });

    private static Transaction Reselect(EditorState s, IReadOnlyList<SelectionRange> ranges)
        => s.Update(new TransactionSpec { Selection = EditorSelection.Of(ranges, s.Selection.MainIndex), ScrollIntoView = true });

    // ---- 行操作 (主選択が跨ぐ行範囲に作用) ----

    private static (int la, int lb) MainLineSpan(EditorState s)
    {
        SelectionRange m = s.Selection.Main;
        return (s.Doc.LineOf(m.From), s.Doc.LineOf(m.To));
    }

    /// <summary>主選択の行範囲を 1 つ上の行と入れ替える (Alt+↑)。</summary>
    public static Transaction MoveLineUp(EditorState s)
    {
        TextDoc doc = s.Doc;
        (int la, int lb) = MainLineSpan(s);
        if (la == 0) return Reselect(s, s.Selection.Ranges);   // no-op
        int prevStart = doc.LineStart(la - 1);
        int blockStart = doc.LineStart(la), blockEnd = doc.LineEnd(lb);
        string prev = doc.LineText(la - 1);
        string block = doc.Slice(blockStart, blockEnd);
        int shift = prev.Length + 1;
        SelectionRange m = s.Selection.Main;
        return s.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(prevStart, blockEnd, block + "\n" + prev)],
            Selection = EditorSelection.Single(m.Anchor - shift, m.Head - shift),
            ScrollIntoView = true,
        });
    }

    /// <summary>主選択の行範囲を 1 つ下の行と入れ替える (Alt+↓)。</summary>
    public static Transaction MoveLineDown(EditorState s)
    {
        TextDoc doc = s.Doc;
        (int la, int lb) = MainLineSpan(s);
        if (lb >= doc.LineCount - 1) return Reselect(s, s.Selection.Ranges);
        int blockStart = doc.LineStart(la), blockEnd = doc.LineEnd(lb);
        int nextEnd = doc.LineEnd(lb + 1);
        string next = doc.LineText(lb + 1);
        string block = doc.Slice(blockStart, blockEnd);
        int shift = next.Length + 1;
        SelectionRange m = s.Selection.Main;
        return s.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(blockStart, nextEnd, next + "\n" + block)],
            Selection = EditorSelection.Single(m.Anchor + shift, m.Head + shift),
            ScrollIntoView = true,
        });
    }

    /// <summary>主選択の行範囲を直下に複製し、選択を複製側へ移す (Shift+Alt+↓)。</summary>
    public static Transaction DuplicateLine(EditorState s)
    {
        TextDoc doc = s.Doc;
        (int la, int lb) = MainLineSpan(s);
        int blockStart = doc.LineStart(la), blockEnd = doc.LineEnd(lb);
        string block = doc.Slice(blockStart, blockEnd);
        int shift = block.Length + 1;
        SelectionRange m = s.Selection.Main;
        return s.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(blockEnd, blockEnd, "\n" + block)],
            Selection = EditorSelection.Single(m.Anchor + shift, m.Head + shift),
            ScrollIntoView = true,
        });
    }

    /// <summary>主選択の行範囲の行コメントをトグルする (Ctrl+/)。全行がコメント済みなら外す、
    /// でなければ各行のインデント直後に <paramref name="prefix"/> を挿入する。</summary>
    public static Transaction ToggleLineComment(EditorState s, string prefix = "// ")
    {
        TextDoc doc = s.Doc;
        (int la, int lb) = MainLineSpan(s);
        string trimmed = prefix.TrimEnd();

        // 非空行が全てコメント済みか
        bool allCommented = true;
        bool any = false;
        for (int i = la; i <= lb; i++)
        {
            string t = doc.LineText(i);
            int ind = Indent(t);
            if (ind >= t.Length) continue;   // 空行は無視
            any = true;
            if (!t.AsSpan(ind).StartsWith(trimmed)) { allCommented = false; break; }
        }
        if (!any) allCommented = false;

        var specs = new List<ChangeSpec>();
        for (int i = la; i <= lb; i++)
        {
            string t = doc.LineText(i);
            int ind = Indent(t);
            int at = doc.LineStart(i) + ind;
            if (allCommented)
            {
                if (ind >= t.Length || !t.AsSpan(ind).StartsWith(trimmed)) continue;
                int rm = trimmed.Length;
                if (ind + rm < t.Length && t[ind + rm] == ' ') rm++;   // 続く空白 1 も
                specs.Add(new ChangeSpec(at, at + rm, ""));
            }
            else
            {
                if (ind >= t.Length) continue;   // 空行はコメントしない
                specs.Add(new ChangeSpec(at, at, prefix));
            }
        }
        if (specs.Count == 0) return Reselect(s, s.Selection.Ranges);
        ChangeSet cs = ChangeSet.Of(doc.Length, specs);
        SelectionRange m = s.Selection.Main;
        return s.Update(new TransactionSpec
        {
            Changes = specs,
            Selection = EditorSelection.Single(cs.MapPos(m.Anchor), cs.MapPos(m.Head)),
            ScrollIntoView = true,
        });
    }

    private static int Indent(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    // ---- マルチカーソル ----

    /// <summary>Ctrl+D — 主選択が空ならその語を選択、選択済みなら同一テキストの次の出現を
    /// 追加選択する (全件済みならそのまま)。native 複数レンジなので新レンジを足すだけ。</summary>
    public static Transaction SelectNextOccurrence(EditorState s)
    {
        string text = s.Doc.Text;
        SelectionRange main = s.Selection.Main;

        if (main.Empty)
        {
            (int ws, int we) = WordAt(text, main.Head);
            if (ws == we) return Reselect(s, s.Selection.Ranges);
            var ranges = s.Selection.Ranges.ToList();
            ranges[s.Selection.MainIndex] = new SelectionRange(ws, we);
            return SelectExplicit(s, ranges, s.Selection.MainIndex);
        }

        string needle = text[main.From..main.To];
        if (needle.Length == 0) return Reselect(s, s.Selection.Ranges);
        var have = new HashSet<(int, int)>(s.Selection.Ranges.Select(r => (r.From, r.To)));
        var matches = TextSearch.FindAll(text, needle);

        (int From, int To)? pick = null;
        foreach ((int f, int t) in matches) if (f >= main.To && !have.Contains((f, t))) { pick = (f, t); break; }
        if (pick is null) foreach ((int f, int t) in matches) if (!have.Contains((f, t))) { pick = (f, t); break; }   // ラップ
        if (pick is null) return Reselect(s, s.Selection.Ranges);   // 全件選択済み

        var next = s.Selection.Ranges.ToList();
        next.Add(new SelectionRange(pick.Value.From, pick.Value.To));
        return SelectExplicit(s, next, next.Count - 1);
    }

    /// <summary>Escape — セカンダリカーソルを解除して主レンジのみにする。</summary>
    public static Transaction ClearSecondaryCursors(EditorState s)
        => s.Update(new TransactionSpec { Selection = EditorSelection.Single(s.Selection.Main.Anchor, s.Selection.Main.Head) });

    private static Transaction SelectExplicit(EditorState s, IReadOnlyList<SelectionRange> ranges, int mainIndex)
        => s.Update(new TransactionSpec { Selection = EditorSelection.Of(ranges, mainIndex), ScrollIntoView = true });

    /// <summary>位置を含む語の範囲 [start, end) (識別子文字。語外なら start==end)。</summary>
    internal static (int Start, int End) WordAt(string t, int pos)
    {
        static bool IsW(char c) => char.IsLetterOrDigit(c) || c == '_';
        int a = Math.Clamp(pos, 0, t.Length), b = a;
        while (a > 0 && IsW(t[a - 1])) a--;
        while (b < t.Length && IsW(t[b])) b++;
        return (a, b);
    }

    // ---- グラフェム境界 (サロゲートペアを割らない。結合文字の完全対応は将来) ----

    internal static int StepLeft(string t, int i)
    {
        if (i <= 0) return 0;
        i--;
        if (i > 0 && char.IsLowSurrogate(t[i]) && char.IsHighSurrogate(t[i - 1])) i--;
        return i;
    }

    internal static int StepRight(string t, int i)
    {
        if (i >= t.Length) return t.Length;
        if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1])) return i + 2;
        return i + 1;
    }
}
