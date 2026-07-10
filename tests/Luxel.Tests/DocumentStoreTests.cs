using Luxel.UI;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: DocumentStore (ADR-0010 S(C2)) — open/save/saveAs/外部変更検知。GPU 不要。</summary>
public class DocumentStoreTests
{
    private sealed class TextDocStub(string kind = "text") : IEditorDocument
    {
        public string Kind => kind;
        public string Title => "stub";
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public string Content = "";

        public Widget CreateView() => throw new NotSupportedException();
        public void Undo() { }
        public void Redo() { }
        public string Serialize() => Content;
        public void LoadFrom(string content) => Content = content;
    }

    private sealed class StubProvider : IDocumentProvider
    {
        public string Kind => "text";
        public string DisplayName => "テキスト";
        public IEditorDocument CreateNew() => new TextDocStub();
    }

    private static (Workspace ws, MemoryFileStorage fs, DocumentStore store) Make()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new StubProvider());
        var fs = new MemoryFileStorage();
        return (ws, fs, new DocumentStore(ws, fs));
    }

    [Fact]
    public void Open_ReadsContent_BindsPath_ClearsDirty()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("notes/a.md", "hello");

        IEditorDocument doc = store.Open("text", "notes/a.md");

        Assert.Equal("hello", doc.Serialize());
        Assert.False(doc.Dirty.Value);
        Assert.Equal("notes/a.md", store.BindingOf(doc)!.Path);
        Assert.Same(doc, store.DocAt("notes/a.md"));
        Assert.Same(doc, ws.Active.Value);
    }

    [Fact]
    public void Open_MissingFile_Throws()
    {
        (_, _, DocumentStore store) = Make();
        Assert.Throws<FileNotFoundException>(() => store.Open("text", "nope.md"));
    }

    [Fact]
    public void Open_SamePathTwice_ReturnsExistingAndActivates()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "1");
        fs.Write("b.md", "2");
        IEditorDocument a = store.Open("text", "a.md");
        store.Open("text", "b.md");

        IEditorDocument again = store.Open("text", "a.md");

        Assert.Same(a, again);
        Assert.Same(a, ws.Active.Value);
        Assert.Equal(2, ws.Documents.Count);
    }

    [Fact]
    public void Save_Writes_ClearsDirty_OwnEchoNotExternalChange()
    {
        (_, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "v1");
        var doc = (TextDocStub)store.Open("text", "a.md");

        doc.Content = "v2";
        doc.Dirty.Value = true;
        store.Save(doc);

        Assert.Equal("v2", fs.Read("a.md"));
        Assert.False(doc.Dirty.Value);
        // MemoryFileStorage の Write は watch を同期発火する — 自書込は外部変更にならない
        Assert.False(store.BindingOf(doc)!.ExternalChange.Value);
    }

    [Fact]
    public void Save_Unbound_Throws()
    {
        (Workspace ws, _, DocumentStore store) = Make();
        IEditorDocument doc = ws.New("text");
        Assert.Throws<InvalidOperationException>(() => store.Save(doc));
    }

    [Fact]
    public void ExternalWrite_RaisesExternalChange_ReloadTakesIt()
    {
        (_, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "v1");
        var doc = (TextDocStub)store.Open("text", "a.md");

        fs.Write("a.md", "外部編集");   // エディタ外の書込

        DocumentBinding b = store.BindingOf(doc)!;
        Assert.True(b.ExternalChange.Value);

        store.Reload(doc);
        Assert.Equal("外部編集", doc.Content);
        Assert.False(b.ExternalChange.Value);
        Assert.False(doc.Dirty.Value);
    }

    [Fact]
    public void SaveAs_BindsNewDoc_AndRebinds()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        var doc = (TextDocStub)ws.New("text");   // path 未結線の新規 doc
        doc.Content = "new";

        store.SaveAs(doc, "new.md");
        Assert.Equal("new", fs.Read("new.md"));
        Assert.Equal("new.md", store.BindingOf(doc)!.Path);
        Assert.False(doc.Dirty.Value);

        // 別 path へ結び直し — 旧 path の外部変更はもう検知しない
        store.SaveAs(doc, "moved.md");
        Assert.Equal("moved.md", store.BindingOf(doc)!.Path);
        fs.Write("new.md", "他人の編集");
        Assert.False(store.BindingOf(doc)!.ExternalChange.Value);
    }

    [Fact]
    public void SaveAs_PathOpenByOtherDoc_Throws()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "1");
        store.Open("text", "a.md");
        IEditorDocument other = ws.New("text");

        Assert.Throws<InvalidOperationException>(() => store.SaveAs(other, "a.md"));
    }

    [Fact]
    public void CloseInWorkspace_PrunesBinding_StopsWatch()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "1");
        IEditorDocument doc = store.Open("text", "a.md");
        DocumentBinding b = store.BindingOf(doc)!;

        ws.Close(doc);   // Workspace 側で閉じる → store が追従して結び付きを外す

        Assert.Null(store.BindingOf(doc));
        Assert.Null(store.DocAt("a.md"));
        fs.Write("a.md", "after close");   // watch は解除済み — 発火しない
        Assert.False(b.ExternalChange.Value);
    }
}
