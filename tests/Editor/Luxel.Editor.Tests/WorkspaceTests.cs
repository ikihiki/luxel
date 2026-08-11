using Luxel.UI;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: Workspace (ADR-0010 S(C1)) — 開閉・アクティブ・ダーティ集約・undo 委譲。GPU 不要。</summary>
public class WorkspaceTests
{
    private sealed class FakeDoc(string kind = "fake", string title = "doc") : IEditorDocument, IDisposable
    {
        public string Kind => kind;
        public string Title => title;
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo { get; set; }
        public bool CanRedo { get; set; }
        public int UndoCalls, RedoCalls;
        public bool Disposed;
        public string Content = "";

        public Widget CreateView() => throw new NotSupportedException("テストでは view を実体化しない");
        public void Undo() => UndoCalls++;
        public void Redo() => RedoCalls++;
        public string Serialize() => Content;
        public void LoadFrom(string content) => Content = content;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProvider(string kind = "fake") : IDocumentProvider
    {
        public string Kind => kind;
        public string DisplayName => kind;
        public IEditorDocument CreateNew() => new FakeDoc(kind, $"新規 {kind}");
    }

    [Fact]
    public void New_RegisteredKind_AddsAndActivates()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new FakeProvider());

        IEditorDocument doc = ws.New("fake");

        Assert.Single(ws.Documents);
        Assert.Same(doc, ws.Active.Value);
    }

    [Fact]
    public void New_UnknownKind_Throws()
    {
        var ws = new Workspace();
        Assert.Throws<ArgumentException>(() => ws.New("nope"));
    }

    [Fact]
    public void Open_WithContent_LoadsViaProvider()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new FakeProvider());

        IEditorDocument doc = ws.Open("fake", "hello");

        Assert.Equal("hello", doc.Serialize());
        Assert.Same(doc, ws.Active.Value);
    }

    [Fact]
    public void Open_SameDocTwice_NoDuplicate_Activates()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        var b = new FakeDoc();
        ws.Open(a);
        ws.Open(b);

        ws.Open(a);   // 再オープン = アクティブ化のみ

        Assert.Equal(2, ws.Documents.Count);
        Assert.Same(a, ws.Active.Value);
    }

    [Fact]
    public void Close_Active_ActivatesNeighborAtSameIndex()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        var b = new FakeDoc();
        var c = new FakeDoc();
        ws.Open(a); ws.Open(b); ws.Open(c);
        ws.Activate(b);

        Assert.True(ws.Close(b));

        Assert.Same(c, ws.Active.Value);   // 同じ位置の隣 (b の後ろにいた c)
        Assert.Equal(2, ws.Documents.Count);
    }

    [Fact]
    public void Close_ActiveAtEnd_ActivatesNewLast_ThenNull()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        var b = new FakeDoc();
        ws.Open(a); ws.Open(b);   // b がアクティブ (末尾)

        ws.Close(b);
        Assert.Same(a, ws.Active.Value);

        ws.Close(a);
        Assert.Null(ws.Active.Value);
        Assert.Empty(ws.Documents);
    }

    [Fact]
    public void Close_Inactive_KeepsActive()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        var b = new FakeDoc();
        ws.Open(a); ws.Open(b);   // b アクティブ

        ws.Close(a);

        Assert.Same(b, ws.Active.Value);
    }

    [Fact]
    public void Close_DisposesDoc_UnknownReturnsFalse()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        ws.Open(a);

        Assert.True(ws.Close(a));
        Assert.True(a.Disposed);
        Assert.False(ws.Close(a));   // 二重クローズは no-op
    }

    [Fact]
    public void AnyDirty_AggregatesAcrossDocs_AndFollowsOpenClose()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        var b = new FakeDoc();
        ws.Open(a); ws.Open(b);
        Assert.False(ws.AnyDirty.Value);

        b.Dirty.Value = true;
        Assert.True(ws.AnyDirty.Value);

        // ダーティ doc を閉じると集約から外れる
        ws.Close(b);
        Assert.False(ws.AnyDirty.Value);

        // 後から開いた doc のダーティにも追従する (動的購読)
        var c = new FakeDoc();
        ws.Open(c);
        c.Dirty.Value = true;
        Assert.True(ws.AnyDirty.Value);
    }

    [Fact]
    public void Documents_IsReactive_EffectRerunsOnOpenClose()
    {
        var ws = new Workspace();
        int seen = -1;
        using IDisposable eff = Reactive.Effect(() => seen = ws.Documents.Count);
        Assert.Equal(0, seen);

        var a = new FakeDoc();
        ws.Open(a);
        Assert.Equal(1, seen);

        ws.Close(a);
        Assert.Equal(0, seen);
    }

    [Fact]
    public void UndoRedo_DelegatesToActiveDoc()
    {
        var ws = new Workspace();
        var a = new FakeDoc { CanUndo = true };
        var b = new FakeDoc { CanRedo = true };
        ws.Open(a); ws.Open(b);   // b アクティブ

        Assert.False(ws.CanUndo);   // b は undo 不可
        Assert.True(ws.CanRedo);
        ws.Redo();
        Assert.Equal(1, b.RedoCalls);
        Assert.Equal(0, a.RedoCalls);

        ws.Activate(a);
        Assert.True(ws.CanUndo);
        ws.Undo();
        Assert.Equal(1, a.UndoCalls);
    }

    [Fact]
    public void UndoRedo_NoActive_IsNoop()
    {
        var ws = new Workspace();
        Assert.False(ws.CanUndo);
        Assert.False(ws.CanRedo);
        ws.Undo();   // 例外にならない
        ws.Redo();
    }

    [Fact]
    public void Activate_UnopenedDoc_Ignored()
    {
        var ws = new Workspace();
        var a = new FakeDoc();
        ws.Open(a);

        ws.Activate(new FakeDoc());   // 開いていない doc

        Assert.Same(a, ws.Active.Value);
    }

    [Fact]
    public void RegisterProvider_SameKind_Overwrites()
    {
        var ws = new Workspace();
        var p1 = new FakeProvider("text");
        var p2 = new FakeProvider("text");
        ws.RegisterProvider(p1);
        ws.RegisterProvider(p2);

        Assert.Single(ws.Providers);
        Assert.Same(p2, ws.ProviderFor("text"));
    }
}
