namespace Luxel.ValueDocument;

public enum ValueTransactionOrigin
{
    Raw,
    Tree,
    Property,
    External,
    Undo,
    Redo,
}

public sealed record ValueSelection(NodeId? NodeId = null, string? Pointer = null)
{
    public static ValueSelection Root(ValueNode root) => new(root.Id, string.Empty);
}

public sealed record RawValueDraft(string Text, long Revision, long BaseRevision, bool IsDirty);

public sealed record ParsedValueCandidate(
    ValueNode Root,
    long DraftRevision,
    long BaseRevision,
    IReadOnlyList<ValueDiagnostic> Diagnostics);

public sealed record ValueCommitContext(
    long AcceptedRevision,
    long BaseRevision,
    ValueTransactionOrigin Origin,
    string? ExternalVersion);

public sealed record ValueCommitResult(bool Success, ValueDiagnostic? Diagnostic = null, string? ExternalVersion = null)
{
    public static ValueCommitResult Accepted(string? externalVersion = null) => new(true, null, externalVersion);
    public static ValueCommitResult Rejected(ValueDiagnostic diagnostic) => new(false, diagnostic);
}

public delegate ValueCommitResult ValueCommitCallback(ValueNode candidate, ValueCommitContext context);

public sealed record ValueTransaction(
    long BaseRevision,
    ValueTransactionOrigin Origin,
    ValueNode BeforeRoot,
    ValueNode AfterRoot,
    ValueSelection BeforeSelection,
    ValueSelection AfterSelection);

public sealed class ValueHistory
{
    private readonly List<ValueTransaction> _undo = [];
    private readonly List<ValueTransaction> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoDepth => _undo.Count;
    public int RedoDepth => _redo.Count;

    internal void Record(ValueTransaction transaction)
    {
        _undo.Add(transaction);
        _redo.Clear();
    }

    internal ValueTransaction? PeekUndo() => _undo.Count == 0 ? null : _undo[^1];
    internal ValueTransaction? PeekRedo() => _redo.Count == 0 ? null : _redo[^1];

    internal void CommitUndo()
    {
        ValueTransaction entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
    }

    internal void CommitRedo()
    {
        ValueTransaction entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public enum ValueApplyStatus
{
    Accepted,
    ParseFailed,
    AdapterRejected,
    StaleBaseRevision,
    NoDraft,
    NothingToUndo,
    NothingToRedo,
}

public sealed record ValueApplyResult(ValueApplyStatus Status, ValueTransaction? Transaction = null)
{
    public bool Success => Status == ValueApplyStatus.Accepted;
}

public sealed record ExternalValueConflict(
    ValueNode IncomingRoot,
    string? IncomingVersion,
    long DraftBaseRevision,
    long AcceptedRevision);

public sealed class ValueDocument
{
    private static readonly ValueCommitCallback AcceptAll = (_, _) => ValueCommitResult.Accepted();
    private readonly ValueCommitCallback _commit;
    private IReadOnlyList<ValueDiagnostic> _diagnostics = Array.Empty<ValueDiagnostic>();
    private long _nextDraftRevision;

    public ValueDocument(ValueNode root, ValueCommitCallback? commit = null, string? externalVersion = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        AcceptedRoot = root;
        Selection = ValueSelection.Root(root);
        _commit = commit ?? AcceptAll;
        ExternalVersion = externalVersion;
        RawDraft = new RawValueDraft(JsonValueCodec.Serialize(root), 0, 0, false);
    }

    public ValueNode AcceptedRoot { get; private set; }
    public long Revision { get; private set; }
    public ValueSelection Selection { get; private set; }
    public RawValueDraft? RawDraft { get; private set; }
    public ParsedValueCandidate? Candidate { get; private set; }
    public IReadOnlyList<ValueDiagnostic> Diagnostics => _diagnostics;
    public ValueHistory History { get; } = new();
    public string? ExternalVersion { get; private set; }
    public ExternalValueConflict? ExternalConflict { get; private set; }

    public RawValueDraft SetRawDraft(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        long baseRevision = RawDraft?.IsDirty == true ? RawDraft.BaseRevision : Revision;
        bool dirty = !string.Equals(text, JsonValueCodec.Serialize(AcceptedRoot), StringComparison.Ordinal);
        if (!dirty) baseRevision = Revision;
        RawDraft = new RawValueDraft(text, checked(++_nextDraftRevision), baseRevision, dirty);
        Candidate = null;
        _diagnostics = Array.Empty<ValueDiagnostic>();
        ExternalConflict = null;
        return RawDraft;
    }

    public ValueApplyResult ApplyRawDraft(ValueSelection? selection = null)
    {
        if (RawDraft is null) return new ValueApplyResult(ValueApplyStatus.NoDraft);

        JsonValueParseResult parsed = JsonValueCodec.Parse(RawDraft.Text);
        _diagnostics = parsed.Diagnostics;
        if (!parsed.Success)
        {
            Candidate = null;
            return new ValueApplyResult(ValueApplyStatus.ParseFailed);
        }

        Candidate = new ParsedValueCandidate(parsed.Root!, RawDraft.Revision, RawDraft.BaseRevision, parsed.Diagnostics);
        if (RawDraft.BaseRevision != Revision)
        {
            _diagnostics = [CreateStateDiagnostic(
                $"The draft is based on revision {RawDraft.BaseRevision}, but the accepted revision is {Revision}.",
                "stale-base")];
            return new ValueApplyResult(ValueApplyStatus.StaleBaseRevision);
        }

        return CommitReplacement(parsed.Root!, ValueTransactionOrigin.Raw, RawDraft.BaseRevision, selection, recordHistory: true);
    }

    public ValueApplyResult ReplaceRoot(
        ValueNode root,
        ValueTransactionOrigin origin,
        ValueSelection? selection = null,
        long? baseRevision = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        long expectedRevision = baseRevision ?? Revision;
        if (expectedRevision != Revision)
        {
            _diagnostics = [CreateStateDiagnostic(
                $"The transaction is based on revision {expectedRevision}, but the accepted revision is {Revision}.",
                "stale-base")];
            return new ValueApplyResult(ValueApplyStatus.StaleBaseRevision);
        }
        Candidate = null;
        return CommitReplacement(root, origin, expectedRevision, selection, recordHistory: true);
    }

    public ValueApplyResult Undo()
    {
        ValueTransaction? entry = History.PeekUndo();
        if (entry is null) return new ValueApplyResult(ValueApplyStatus.NothingToUndo);
        ValueApplyResult result = CommitReplacement(
            entry.BeforeRoot,
            ValueTransactionOrigin.Undo,
            Revision,
            entry.BeforeSelection,
            recordHistory: false);
        if (result.Success) History.CommitUndo();
        return result;
    }

    public ValueApplyResult Redo()
    {
        ValueTransaction? entry = History.PeekRedo();
        if (entry is null) return new ValueApplyResult(ValueApplyStatus.NothingToRedo);
        ValueApplyResult result = CommitReplacement(
            entry.AfterRoot,
            ValueTransactionOrigin.Redo,
            Revision,
            entry.AfterSelection,
            recordHistory: false);
        if (result.Success) History.CommitRedo();
        return result;
    }

    public bool RefreshExternal(ValueNode root, string? externalVersion)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (RawDraft?.IsDirty == true)
        {
            ExternalConflict = new ExternalValueConflict(root, externalVersion, RawDraft.BaseRevision, Revision);
            _diagnostics = [CreateStateDiagnostic(
                "An external value arrived while the raw draft has uncommitted changes.",
                "external-conflict")];
            return false;
        }

        AcceptedRoot = root;
        Revision = checked(Revision + 1);
        Selection = ValueSelection.Root(root);
        ExternalVersion = externalVersion;
        ExternalConflict = null;
        Candidate = null;
        History.Clear();
        RawDraft = new RawValueDraft(JsonValueCodec.Serialize(root), checked(++_nextDraftRevision), Revision, false);
        _diagnostics = Array.Empty<ValueDiagnostic>();
        return true;
    }

    public void DiscardRawDraft()
    {
        RawDraft = new RawValueDraft(JsonValueCodec.Serialize(AcceptedRoot), checked(++_nextDraftRevision), Revision, false);
        Candidate = null;
        ExternalConflict = null;
        _diagnostics = Array.Empty<ValueDiagnostic>();
    }

    private ValueApplyResult CommitReplacement(
        ValueNode root,
        ValueTransactionOrigin origin,
        long baseRevision,
        ValueSelection? selection,
        bool recordHistory)
    {
        ValueCommitResult commitResult;
        try
        {
            commitResult = _commit(root, new ValueCommitContext(Revision, baseRevision, origin, ExternalVersion));
        }
        catch (Exception exception)
        {
            commitResult = ValueCommitResult.Rejected(new ValueDiagnostic(
                exception.Message,
                ValueDiagnosticSeverity.Error,
                0,
                1,
                1,
                null,
                "adapter"));
        }

        if (!commitResult.Success)
        {
            _diagnostics = [commitResult.Diagnostic ?? CreateStateDiagnostic("The backing adapter rejected the candidate.", "adapter")];
            return new ValueApplyResult(ValueApplyStatus.AdapterRejected);
        }

        ValueSelection afterSelection = selection ?? ValueSelection.Root(root);
        var transaction = new ValueTransaction(baseRevision, origin, AcceptedRoot, root, Selection, afterSelection);
        AcceptedRoot = root;
        Selection = afterSelection;
        Revision = checked(Revision + 1);
        if (recordHistory) History.Record(transaction);
        if (commitResult.ExternalVersion is not null) ExternalVersion = commitResult.ExternalVersion;
        Candidate = null;
        ExternalConflict = null;
        _diagnostics = Array.Empty<ValueDiagnostic>();

        if (origin == ValueTransactionOrigin.Raw || RawDraft?.IsDirty != true)
        {
            string text = origin == ValueTransactionOrigin.Raw && RawDraft is not null
                ? RawDraft.Text
                : JsonValueCodec.Serialize(root);
            RawDraft = new RawValueDraft(text, checked(++_nextDraftRevision), Revision, false);
        }
        return new ValueApplyResult(ValueApplyStatus.Accepted, transaction);
    }

    private static ValueDiagnostic CreateStateDiagnostic(string message, string source)
        => new(message, ValueDiagnosticSeverity.Error, 0, 1, 1, null, source);
}
