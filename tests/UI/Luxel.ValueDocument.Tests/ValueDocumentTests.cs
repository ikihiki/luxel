using Luxel.ValueDocument;

namespace Luxel.ValueDocument.Tests;

public sealed class ValueDocumentTests
{
    [Fact]
    public void InvalidRawDraftDoesNotChangeAcceptedValueOrRevision()
    {
        ValueNode initial = Parse("{\"value\":1}");
        var document = new ValueDocument(initial);

        document.SetRawDraft("{\"value\":");
        ValueApplyResult result = document.ApplyRawDraft();

        Assert.Equal(ValueApplyStatus.ParseFailed, result.Status);
        Assert.Same(initial, document.AcceptedRoot);
        Assert.Equal(0, document.Revision);
        Assert.True(document.RawDraft!.IsDirty);
        Assert.Null(document.Candidate);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Severity == ValueDiagnosticSeverity.Error);
        Assert.False(document.History.CanUndo);
    }

    [Fact]
    public void ValidRawDraftCommitsExactlyOnceAndCreatesOneRevision()
    {
        int commits = 0;
        var document = new ValueDocument(Parse("1"), (candidate, context) =>
        {
            commits++;
            Assert.Equal(0, context.AcceptedRevision);
            Assert.Equal(0, context.BaseRevision);
            Assert.Equal(ValueTransactionOrigin.Raw, context.Origin);
            Assert.Equal("2", JsonValueCodec.Serialize(candidate));
            return ValueCommitResult.Accepted("version-2");
        });

        document.SetRawDraft("2");
        ValueApplyResult result = document.ApplyRawDraft();

        Assert.True(result.Success);
        Assert.Equal(1, commits);
        Assert.Equal(1, document.Revision);
        Assert.Equal("2", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.Equal("version-2", document.ExternalVersion);
        Assert.Equal(1, document.History.UndoDepth);
        Assert.False(document.RawDraft!.IsDirty);
        Assert.Null(document.Candidate);
    }

    [Fact]
    public void AdapterRejectionPreservesAcceptedValueRevisionAndHistory()
    {
        ValueNode initial = Parse("1");
        var rejection = new ValueDiagnostic("Domain rejected value.", ValueDiagnosticSeverity.Error, 0, 1, 1, "/", "adapter");
        var document = new ValueDocument(initial, (_, _) => ValueCommitResult.Rejected(rejection));

        document.SetRawDraft("2");
        ValueApplyResult result = document.ApplyRawDraft();

        Assert.Equal(ValueApplyStatus.AdapterRejected, result.Status);
        Assert.Same(initial, document.AcceptedRoot);
        Assert.Equal(0, document.Revision);
        Assert.False(document.History.CanUndo);
        Assert.NotNull(document.Candidate);
        Assert.Same(rejection, Assert.Single(document.Diagnostics));
        Assert.True(document.RawDraft!.IsDirty);
    }

    [Fact]
    public void RootReplacementUndoAndRedoPreserveSelectionMetadata()
    {
        ValueNode first = Parse("{\"a\":1}");
        ValueNode second = Parse("{\"b\":2}");
        var document = new ValueDocument(first);
        var afterSelection = new ValueSelection(second.Id, "/b");

        ValueApplyResult replace = document.ReplaceRoot(second, ValueTransactionOrigin.Tree, afterSelection);
        ValueApplyResult undo = document.Undo();
        ValueApplyResult redo = document.Redo();

        Assert.True(replace.Success);
        Assert.True(undo.Success);
        Assert.True(redo.Success);
        Assert.Same(second, document.AcceptedRoot);
        Assert.Equal(afterSelection, document.Selection);
        Assert.Equal(3, document.Revision);
        Assert.True(document.History.CanUndo);
        Assert.False(document.History.CanRedo);
        Assert.Equal(ValueTransactionOrigin.Tree, replace.Transaction!.Origin);
        Assert.Equal(string.Empty, replace.Transaction.BeforeSelection.Pointer);
        Assert.Equal("/b", replace.Transaction.AfterSelection.Pointer);
    }

    [Fact]
    public void ExternalRefreshCreatesConflictWhenDraftIsDirty()
    {
        ValueNode initial = Parse("1");
        ValueNode incoming = Parse("3");
        var document = new ValueDocument(initial, externalVersion: "v1");
        document.SetRawDraft("2");

        bool refreshed = document.RefreshExternal(incoming, "v2");

        Assert.False(refreshed);
        Assert.Same(initial, document.AcceptedRoot);
        Assert.Equal("v1", document.ExternalVersion);
        Assert.NotNull(document.ExternalConflict);
        Assert.Same(incoming, document.ExternalConflict!.IncomingRoot);
        Assert.Equal("v2", document.ExternalConflict.IncomingVersion);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Source == "external-conflict");
    }

    [Fact]
    public void DraftApplyRejectsStaleBaseRevision()
    {
        ValueNode initial = Parse("1");
        var document = new ValueDocument(initial);
        document.SetRawDraft("2");
        Assert.True(document.ReplaceRoot(Parse("3"), ValueTransactionOrigin.Tree).Success);

        ValueApplyResult result = document.ApplyRawDraft();

        Assert.Equal(ValueApplyStatus.StaleBaseRevision, result.Status);
        Assert.Equal("3", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, document.History.UndoDepth);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Source == "stale-base");
    }

    [Fact]
    public void ExplicitStaleStructuralTransactionIsRejectedBeforeAdapterCommit()
    {
        int commits = 0;
        var document = new ValueDocument(Parse("1"), (_, _) =>
        {
            commits++;
            return ValueCommitResult.Accepted();
        });
        Assert.True(document.ReplaceRoot(Parse("2"), ValueTransactionOrigin.Tree).Success);

        ValueApplyResult stale = document.ReplaceRoot(Parse("3"), ValueTransactionOrigin.Property, baseRevision: 0);

        Assert.Equal(ValueApplyStatus.StaleBaseRevision, stale.Status);
        Assert.Equal(1, commits);
        Assert.Equal("2", JsonValueCodec.Serialize(document.AcceptedRoot));
        Assert.Equal(1, document.Revision);
    }

    private static ValueNode Parse(string json)
    {
        JsonValueParseResult result = JsonValueCodec.Parse(json);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        return Assert.IsAssignableFrom<ValueNode>(result.Root);
    }
}
