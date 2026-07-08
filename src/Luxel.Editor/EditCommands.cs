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
