namespace Luxel.Editor;

/// <summary>トランザクションの指定 — 変更 (複数編集を 1 つの ChangeSet に束ねる) と結果選択を宣言する。
/// <see cref="Selection"/> を省くと現在の選択を変更で写した位置になる。</summary>
public sealed class TransactionSpec
{
    /// <summary>この編集での変更列 (省略 = 変更なし)。複数編集はマルチカーソル置換などに使う。</summary>
    public IReadOnlyList<ChangeSpec>? Changes { get; init; }
    /// <summary>結果の選択 (省略 = 現在の選択を変更で写す)。</summary>
    public EditorSelection? Selection { get; init; }
    /// <summary>キャレットを可視域へスクロールするヒント (view が解釈、コアは保持のみ)。</summary>
    public bool ScrollIntoView { get; init; }
}

/// <summary>
/// エディタの**不変スナップショット** — 文書 + 選択。編集は <see cref="Update"/> が
/// <see cref="Transaction"/> を作り、その <see cref="Transaction.State"/> が新しい状態になる
/// (元の状態は変わらない)。装飾フィールドは S2 でここに足す。
/// </summary>
public sealed class EditorState
{
    /// <summary>文書。</summary>
    public TextDoc Doc { get; }
    /// <summary>選択 (複数レンジ)。</summary>
    public EditorSelection Selection { get; }

    internal EditorState(TextDoc doc, EditorSelection selection)
    {
        Doc = doc;
        Selection = selection.Clamp(doc.Length);
    }

    /// <summary>テキストと初期選択から状態を作る (選択省略 = 先頭キャレット)。</summary>
    public static EditorState Create(string text = "", EditorSelection? selection = null)
        => new(TextDoc.Of(text), selection ?? EditorSelection.Cursor(0));

    /// <summary>指定からトランザクションを作る (適用は <see cref="Transaction.State"/>)。</summary>
    public Transaction Update(TransactionSpec spec) => new(this, spec);

    /// <summary>[from, to) を <paramref name="insert"/> で置換するトランザクション (単発編集の便宜)。</summary>
    public Transaction Replace(int from, int to, string insert, EditorSelection? selection = null)
        => Update(new TransactionSpec { Changes = [new ChangeSpec(from, to, insert)], Selection = selection });
}

/// <summary>
/// 1 回の状態遷移 — 開始状態 + 変更セット + 結果選択。<see cref="State"/> が適用後の新しい
/// <see cref="EditorState"/> (遅延計算・キャッシュ)。undo 履歴はこの Transaction から作られる。
/// </summary>
public sealed class Transaction
{
    /// <summary>この遷移の開始状態。</summary>
    public EditorState StartState { get; }
    /// <summary>開始文書に対する変更セット。</summary>
    public ChangeSet Changes { get; }
    /// <summary>結果の選択。</summary>
    public EditorSelection Selection { get; }
    /// <summary>スクロールヒント。</summary>
    public bool ScrollIntoView { get; }

    private EditorState? _state;

    internal Transaction(EditorState start, TransactionSpec spec)
    {
        StartState = start;
        Changes = ChangeSet.Of(start.Doc.Length, spec.Changes ?? []);
        EditorSelection sel = spec.Selection ?? start.Selection.Map(Changes);
        Selection = sel.Clamp(Changes.NewLength);
        ScrollIntoView = spec.ScrollIntoView;
    }

    /// <summary>文書が変わるか (変更が空でない)。</summary>
    public bool DocChanged => !Changes.IsEmpty;

    /// <summary>適用後の新しい状態 (遅延生成・キャッシュ)。</summary>
    public EditorState State => _state ??= new EditorState(StartState.Doc.ApplyChange(Changes), Selection);
}
