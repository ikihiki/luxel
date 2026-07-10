using Luxel.Document;

namespace Luxel.Tests;

/// <summary>エディタ新スタック S1 コア (ToDo 22 / ADR-0006) の単体テスト —
/// TextDoc の行索引、ChangeSet の Apply/MapPos/Invert/Compose、EditorSelection の正規化、
/// EditorState/Transaction、History の undo/redo。canvas 不要 (純データ)。</summary>
public class EditorCoreTests
{
    // ---- TextDoc ----

    [Fact]
    public void TextDoc_LineIndex()
    {
        var d = TextDoc.Of("ab\ncde\n\nf");
        Assert.Equal(4, d.LineCount);              // "ab" / "cde" / "" / "f"
        Assert.Equal("ab", d.LineText(0));
        Assert.Equal("cde", d.LineText(1));
        Assert.Equal("", d.LineText(2));
        Assert.Equal("f", d.LineText(3));

        Assert.Equal(0, d.LineStart(0));
        Assert.Equal(3, d.LineStart(1));           // "ab\n" = 3 文字
        Assert.Equal(2, d.LineEnd(0));             // '\n' は含まない
        Assert.Equal(9, d.Length);
        Assert.Equal(9, d.LineEnd(3));             // 最終行は文書末
    }

    [Fact]
    public void TextDoc_OffsetCoordRoundTrip()
    {
        var d = TextDoc.Of("ab\ncde");
        Assert.Equal(0, d.LineOf(0));
        Assert.Equal(0, d.LineOf(2));              // '\n' の直前はまだ行 0
        Assert.Equal(1, d.LineOf(3));              // '\n' の直後は行 1
        Assert.Equal(1, d.LineOf(6));              // 文書末

        Assert.Equal((1, 1), d.CoordAt(4));        // "ab\nc|de" → 行1 桁1
        Assert.Equal(4, d.OffsetAt(1, 1));
        Assert.Equal(6, d.OffsetAt(1, 99));        // 行末にクランプ
        Assert.Equal(3, d.OffsetAt(1, 0));
    }

    [Fact]
    public void TextDoc_ReplaceIsImmutable()
    {
        var a = TextDoc.Of("hello");
        var b = a.Replace(0, 1, "J");
        Assert.Equal("hello", a.Text);             // 元は不変
        Assert.Equal("Jello", b.Text);
    }

    // ---- ChangeSet: Apply ----

    [Fact]
    public void ChangeSet_Apply_InsertDeleteReplace()
    {
        Assert.Equal("abXcdef", ChangeSet.Of(6, [new ChangeSpec(2, 2, "X")]).Apply("abcdef"));
        Assert.Equal("abef", ChangeSet.Of(6, [new ChangeSpec(2, 4, "")]).Apply("abcdef"));
        Assert.Equal("abZZef", ChangeSet.Of(6, [new ChangeSpec(2, 4, "ZZ")]).Apply("abcdef"));
    }

    [Fact]
    public void ChangeSet_Apply_MultipleEdits_OneSet()
    {
        // 2 箇所を 1 つの ChangeSet で (マルチカーソル置換の基礎)。順序が乱れていても From でソート。
        var cs = ChangeSet.Of(6, [new ChangeSpec(4, 5, "Y"), new ChangeSpec(1, 2, "X")]);
        Assert.Equal("aXcdYf", cs.Apply("abcdef"));
    }

    [Fact]
    public void ChangeSet_Of_RejectsOverlap()
    {
        Assert.Throws<ArgumentException>(() =>
            ChangeSet.Of(6, [new ChangeSpec(1, 4, "X"), new ChangeSpec(2, 5, "Y")]));
    }

    [Fact]
    public void ChangeSet_Apply_WrongLengthThrows()
    {
        Assert.Throws<ArgumentException>(() => ChangeSet.Of(6, [new ChangeSpec(0, 0, "x")]).Apply("short"));
    }

    // ---- ChangeSet: MapPos ----

    [Fact]
    public void MapPos_Insertion_Assoc()
    {
        // "abcdef" の位置 3 に "X" を挿入 → "abcXdef"
        var cs = ChangeSet.Of(6, [new ChangeSpec(3, 3, "X")]);
        Assert.Equal(2, cs.MapPos(2));                 // 挿入より前は不変
        Assert.Equal(3, cs.MapPos(3, assoc: -1));      // 挿入点・左寄せ → X の前
        Assert.Equal(4, cs.MapPos(3, assoc: +1));      // 挿入点・右寄せ → X の後
        Assert.Equal(5, cs.MapPos(4));                 // 挿入より後は +1
        Assert.Equal(7, cs.MapPos(6));                 // 文書末
    }

    [Fact]
    public void MapPos_Deletion_CollapsesInside()
    {
        // "abcdef" の [2,4) を削除 → "abef"
        var cs = ChangeSet.Of(6, [new ChangeSpec(2, 4, "")]);
        Assert.Equal(2, cs.MapPos(2));                 // 削除開始
        Assert.Equal(2, cs.MapPos(3));                 // 削除範囲内 → 潰れる
        Assert.Equal(2, cs.MapPos(4));                 // 削除終端
        Assert.Equal(3, cs.MapPos(5));                 // 後続は詰まる
    }

    // ---- ChangeSet: Invert ----

    [Fact]
    public void Invert_RoundTrips()
    {
        foreach (var spec in new[]
        {
            new ChangeSpec(3, 3, "XYZ"),   // 挿入
            new ChangeSpec(1, 4, ""),      // 削除
            new ChangeSpec(2, 5, "QQ"),    // 置換
        })
        {
            const string old = "abcdef";
            var cs = ChangeSet.Of(old.Length, [spec]);
            string mid = cs.Apply(old);
            var inv = cs.Invert(old);
            Assert.Equal(old, inv.Apply(mid));         // 逆適用で元に戻る
        }
    }

    // ---- ChangeSet: Compose (fuzz) ----

    [Fact]
    public void Compose_Fuzz_MatchesSequential()
    {
        var rng = new Random(20260708);
        for (int iter = 0; iter < 500; iter++)
        {
            string doc = RandomText(rng, rng.Next(0, 12));
            ChangeSet a = RandomChange(rng, doc.Length);
            string mid = a.Apply(doc);
            ChangeSet b = RandomChange(rng, mid.Length);
            string expected = b.Apply(mid);

            // Compose の真の不変条件は「合成の Apply = 逐次適用の結果」。これが変更セットの構造的正しさ。
            // (MapPos は挿入の由来がフラット化で失われるため、置換境界での右寄せは逐次と一致しないことがある —
            //  CM6 も同様の性質。実プロダクトは選択を合成セットに通さない [単発 ChangeSet を通す・畳み undo は
            //  Apply を使う] ので影響しない。単発 ChangeSet の MapPos 挙動は別途明示テストで担保。)
            ChangeSet composed = a.Compose(b);
            Assert.Equal(doc.Length, composed.OldLength);
            Assert.Equal(expected.Length, composed.NewLength);
            Assert.Equal(expected, composed.Apply(doc));
        }
    }

    private static string RandomText(Random rng, int len)
    {
        const string alpha = "abc\n";
        return string.Concat(Enumerable.Range(0, len).Select(_ => alpha[rng.Next(alpha.Length)]));
    }

    // 文書長に対しランダムな (据え置き / 削除 / 挿入) を並べた変更セット
    private static ChangeSet RandomChange(Random rng, int docLen)
    {
        var edits = new List<ChangeSpec>();
        int pos = 0;
        while (pos <= docLen)
        {
            int keep = rng.Next(0, Math.Max(1, docLen - pos) + 1);
            pos += keep;
            if (pos > docLen) break;
            int del = rng.Next(0, Math.Min(3, docLen - pos) + 1);
            string ins = rng.Next(3) == 0 ? RandomText(rng, rng.Next(1, 3)) : "";
            if (del > 0 || ins.Length > 0) { edits.Add(new ChangeSpec(pos, pos + del, ins)); pos += del; }
            if (rng.Next(3) == 0) break;
        }
        return ChangeSet.Of(docLen, edits);
    }

    // ---- EditorSelection ----

    [Fact]
    public void Selection_Normalize_SortsAndMerges()
    {
        // 順不同・重なりありのレンジ → From 昇順・マージ
        var sel = EditorSelection.Of(
        [
            new SelectionRange(8, 10),
            new SelectionRange(0, 3),
            new SelectionRange(2, 5),   // [0,3) と重なる → [0,5)
        ], mainIndex: 2);

        Assert.Equal(2, sel.Ranges.Count);
        Assert.Equal(0, sel.Ranges[0].From);
        Assert.Equal(5, sel.Ranges[0].To);
        Assert.Equal(8, sel.Ranges[1].From);
        // main はマージ後の [0,5) を指す (元 main = [2,5) がそこへ吸収)
        Assert.Equal(0, sel.MainIndex);
    }

    [Fact]
    public void Selection_Normalize_DedupesCursors()
    {
        var sel = EditorSelection.Of([SelectionRange.Cursor(4), SelectionRange.Cursor(4), SelectionRange.Cursor(1)]);
        Assert.Equal(2, sel.Ranges.Count);              // 重複キャレットは 1 つに
        Assert.Equal(1, sel.Ranges[0].From);
        Assert.Equal(4, sel.Ranges[1].From);
    }

    [Fact]
    public void Selection_PreservesDirection()
    {
        var sel = EditorSelection.Single(5, 2);         // 逆向き (head < anchor)
        Assert.Equal(5, sel.Main.Anchor);
        Assert.Equal(2, sel.Main.Head);
        Assert.Equal(2, sel.Main.From);
        Assert.Equal(5, sel.Main.To);
        Assert.False(sel.Main.Empty);
    }

    [Fact]
    public void Selection_MapThroughChange()
    {
        // "abcdef" 先頭に "XX" 挿入 → 全レンジが +2
        var cs = ChangeSet.Of(6, [new ChangeSpec(0, 0, "XX")]);
        var sel = EditorSelection.Of([SelectionRange.Cursor(1), new SelectionRange(3, 5)]).Map(cs);
        Assert.Equal(3, sel.Ranges[0].From);
        Assert.Equal(5, sel.Ranges[1].From);
        Assert.Equal(7, sel.Ranges[1].To);
    }

    // ---- EditorState / Transaction ----

    [Fact]
    public void State_Replace_ProducesNewState()
    {
        var s0 = EditorState.Create("hello", EditorSelection.Cursor(5));
        Transaction tr = s0.Replace(5, 5, "!", EditorSelection.Cursor(6));
        Assert.True(tr.DocChanged);
        Assert.Equal("hello", s0.Doc.Text);             // 元は不変
        Assert.Equal("hello!", tr.State.Doc.Text);
        Assert.Equal(6, tr.State.Selection.Main.Head);
    }

    [Fact]
    public void State_DefaultSelection_MapsThroughChange()
    {
        // 選択を明示しなければ現在選択を変更で写す
        var s0 = EditorState.Create("abcdef", EditorSelection.Cursor(5));
        Transaction tr = s0.Update(new TransactionSpec { Changes = [new ChangeSpec(0, 0, "XX")] });
        Assert.Equal(7, tr.State.Selection.Main.Head);  // 5 → 7
    }

    [Fact]
    public void State_MultiCursorInsert_OneTransaction()
    {
        // 3 箇所へ 1 トランザクションで挿入 (マルチカーソル打鍵の核)
        var s0 = EditorState.Create("a.b.c");
        var tr = s0.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(1, 1, "!"), new ChangeSpec(3, 3, "!")],
        });
        Assert.Equal("a!.b!.c", tr.State.Doc.Text);
    }

    // ---- History ----

    [Fact]
    public void History_UndoRedo()
    {
        var s0 = EditorState.Create("hello", EditorSelection.Cursor(5));
        var h = new History();

        Transaction t1 = s0.Replace(5, 5, " world", EditorSelection.Cursor(11));
        h.Record(t1);
        EditorState s1 = t1.State;
        Assert.Equal("hello world", s1.Doc.Text);
        Assert.True(h.CanUndo);

        EditorState back = h.Undo(s1);
        Assert.Equal("hello", back.Doc.Text);
        Assert.Equal(5, back.Selection.Main.Head);      // 前の選択が戻る
        Assert.True(h.CanRedo);

        EditorState fwd = h.Redo(back);
        Assert.Equal("hello world", fwd.Doc.Text);
        Assert.Equal(11, fwd.Selection.Main.Head);
    }

    [Fact]
    public void History_MultiCursorEditIsOneUndo()
    {
        var s0 = EditorState.Create("a.b.c");
        var h = new History();
        Transaction t = s0.Update(new TransactionSpec { Changes = [new ChangeSpec(1, 1, "!"), new ChangeSpec(3, 3, "!")] });
        h.Record(t);
        Assert.Equal(1, h.UndoDepth);
        EditorState back = h.Undo(t.State);
        Assert.Equal("a.b.c", back.Doc.Text);           // 1 undo で両方戻る
    }

    [Fact]
    public void History_Coalesce_MergesConsecutiveTyping()
    {
        var s0 = EditorState.Create("", EditorSelection.Cursor(0));
        var h = new History();

        Transaction t1 = s0.Replace(0, 0, "a", EditorSelection.Cursor(1));
        h.Record(t1);
        Transaction t2 = t1.State.Replace(1, 1, "b", EditorSelection.Cursor(2));
        h.Record(t2, coalesce: true);
        Transaction t3 = t2.State.Replace(2, 2, "c", EditorSelection.Cursor(3));
        h.Record(t3, coalesce: true);

        Assert.Equal(1, h.UndoDepth);                   // 3 打鍵が 1 undo
        EditorState back = h.Undo(t3.State);
        Assert.Equal("", back.Doc.Text);                // 一括で戻る
        Assert.Equal(0, back.Selection.Main.Head);      // 先頭の選択へ
        EditorState fwd = h.Redo(back);
        Assert.Equal("abc", fwd.Doc.Text);
    }

    [Fact]
    public void History_RecordClearsRedo()
    {
        var s0 = EditorState.Create("x");
        var h = new History();
        Transaction t1 = s0.Replace(1, 1, "y");
        h.Record(t1);
        EditorState back = h.Undo(t1.State);
        Assert.True(h.CanRedo);
        Transaction t2 = back.Replace(1, 1, "z");
        h.Record(t2);
        Assert.False(h.CanRedo);                        // 新しい編集で redo 破棄
    }
}
