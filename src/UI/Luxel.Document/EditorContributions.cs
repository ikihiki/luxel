namespace Luxel.Document;

/// <summary>文書が Editor の汎用ブロック UI（ドラッグ、追加、削除）へ公開するブロック範囲。</summary>
public readonly record struct EditorBlock(int From, int To, string Kind, bool CanMove = true, bool CanDelete = true);

/// <summary>文書形式ごとのブロック境界を Editor へ提供する。UI や描画には依存しない。</summary>
public interface IEditorBlockProvider
{
    IReadOnlyList<EditorBlock> GetBlocks(TextDoc document);
}

/// <summary>ブロック追加ボタンやスラッシュメニューに表示する、文書形式側の挿入候補。</summary>
public sealed record EditorInsertItem(string Id, string Label, string InsertText, int CaretBack = 0, string? Detail = null);

/// <summary>選択ツールバーに表示する、文書形式側の囲み操作。</summary>
public sealed record EditorSelectionAction(
    string Id,
    string Label,
    string Prefix,
    string Suffix,
    string Placeholder = "text");

/// <summary>Editor の汎用 UI が挿入候補と選択操作を実行するための純関数。</summary>
public static class EditorContributionCommands
{
    public static Transaction Insert(EditorState state, EditorInsertItem item)
    {
        IReadOnlyList<SelectionRange> ranges = state.Selection.Ranges;
        var changes = new ChangeSpec[ranges.Count];
        var carets = new SelectionRange[ranges.Count];
        int delta = 0;
        int caretInInsert = Math.Clamp(item.InsertText.Length - item.CaretBack, 0, item.InsertText.Length);

        for (int i = 0; i < ranges.Count; i++)
        {
            SelectionRange range = ranges[i];
            changes[i] = new ChangeSpec(range.From, range.To, item.InsertText);
            carets[i] = SelectionRange.Cursor(range.From + delta + caretInInsert);
            delta += item.InsertText.Length - (range.To - range.From);
        }

        return state.Update(new TransactionSpec
        {
            Changes = changes,
            Selection = EditorSelection.Of(carets, state.Selection.MainIndex),
            ScrollIntoView = true,
        });
    }

    public static Transaction Apply(EditorState state, EditorSelectionAction action)
    {
        IReadOnlyList<SelectionRange> ranges = state.Selection.Ranges;
        var changes = new ChangeSpec[ranges.Count];
        var selections = new SelectionRange[ranges.Count];
        int delta = 0;

        for (int i = 0; i < ranges.Count; i++)
        {
            SelectionRange range = ranges[i];
            string content = range.Empty
                ? action.Placeholder
                : state.Doc.Text[range.From..range.To];
            string replacement = action.Prefix + content + action.Suffix;
            changes[i] = new ChangeSpec(range.From, range.To, replacement);

            int from = range.From + delta + action.Prefix.Length;
            selections[i] = new SelectionRange(from, from + content.Length);
            delta += replacement.Length - (range.To - range.From);
        }

        return state.Update(new TransactionSpec
        {
            Changes = changes,
            Selection = EditorSelection.Of(selections, state.Selection.MainIndex),
            ScrollIntoView = true,
        });
    }
}
