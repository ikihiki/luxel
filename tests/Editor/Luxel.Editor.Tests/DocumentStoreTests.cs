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
    public void Save_SynchronousOwnWriteNeverSignalsExternalChange()
    {
        (_, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "v1");
        var doc = (TextDocStub)store.Open("text", "a.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        bool observedExternalChange = false;
        using IDisposable effect = Reactive.Effect(() => observedExternalChange |= binding.ExternalChange.Value);
        doc.Content = "v2";
        doc.Dirty.Value = true;

        store.Save(doc);

        Assert.False(observedExternalChange);
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
        DocumentBinding binding = store.BindingOf(doc)!;

        // 別 path へ結び直し — binding identity は保持し、旧 path の外部変更はもう検知しない
        store.SaveAs(doc, "moved.md");
        Assert.Same(binding, store.BindingOf(doc));
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
    public void Save_WriteFailureRetainsDirtyAndPreviousSavedBaseline()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("a.md", "saved");
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)store.Open("atomic", "a.md");
        doc.Edit("failed");
        storage.FailWrites = true;

        Assert.Throws<IOException>(() => store.Save(doc));

        Assert.True(doc.Dirty.Value);
        Assert.Equal("saved", storage.Inner.Read("a.md"));
        doc.Edit("saved");
        Assert.False(doc.Dirty.Value);
        doc.Edit("failed");
        Assert.True(doc.Dirty.Value);
    }

    [Fact]
    public void SaveAs_WriteFailureRetainsOldBindingAndDirtyState()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("old.md", "saved");
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)store.Open("atomic", "old.md");
        doc.Edit("changed");
        DocumentBinding oldBinding = store.BindingOf(doc)!;
        storage.FailWrites = true;

        Assert.Throws<IOException>(() => store.SaveAs(doc, "new.md"));

        Assert.Same(oldBinding, store.BindingOf(doc));
        Assert.Equal("old.md", store.BindingOf(doc)!.Path);
        Assert.Same(doc, store.DocAt("old.md"));
        Assert.Null(store.DocAt("new.md"));
        Assert.True(doc.Dirty.Value);
        Assert.False(storage.Inner.Exists("new.md"));
    }

    [Fact]
    public void SaveAs_CurrentPathNeverSignalsExternalChange()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("a.md", "saved");
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)store.Open("atomic", "a.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        bool observedExternalChange = false;
        using IDisposable effect = Reactive.Effect(() => observedExternalChange |= binding.ExternalChange.Value);
        doc.Edit("changed");

        store.SaveAs(doc, "a.md");

        Assert.False(observedExternalChange);
        Assert.Same(binding, store.BindingOf(doc));
        Assert.False(doc.Dirty.Value);
    }

    [Fact]
    public void SaveAs_WatcherFailureDoesNotWriteOrReplaceOldBinding()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("old.md", "saved");
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)store.Open("atomic", "old.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        doc.Edit("changed");
        storage.FailWatches = true;

        Assert.Throws<IOException>(() => store.SaveAs(doc, "new.md"));

        Assert.False(storage.Inner.Exists("new.md"));
        Assert.Same(binding, store.BindingOf(doc));
        Assert.True(doc.Dirty.Value);
    }

    [Fact]
    public void RebindAfterAssetMovePreservesBindingIdentityAndSwitchesWatcher()
    {
        (Workspace _, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("old.md", "saved");
        IEditorDocument doc = store.Open("text", "old.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        fs.Move("old.md", "new.md");

        DocumentRebindResult result = store.Rebind(doc, "new.md");

        Assert.Same(binding, result.Binding);
        Assert.Equal("old.md", result.PreviousPath);
        Assert.Equal("new.md", binding.Path);
        Assert.Null(store.DocAt("old.md"));
        Assert.Same(doc, store.DocAt("new.md"));
        fs.Write("old.md", "stale watcher");
        Assert.False(binding.ExternalChange.Value);
        fs.Write("new.md", "external");
        Assert.True(binding.ExternalChange.Value);
    }

    [Fact]
    public void RebindAfterAssetMovePreservesExternalChangeConflict()
    {
        (Workspace _, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("old.md", "saved");
        IEditorDocument doc = store.Open("text", "old.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        fs.Write("old.md", "external");
        Assert.True(binding.ExternalChange.Peek());
        fs.Move("old.md", "new.md");

        store.Rebind(doc, "new.md");

        Assert.Equal("new.md", binding.Path);
        Assert.True(binding.ExternalChange.Peek());
        Assert.Equal("saved", doc.Serialize());
    }

    [Fact]
    public void RebindCollisionLeavesBothBindingsAndWatchersUnchanged()
    {
        (_, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "a");
        fs.Write("b.md", "b");
        IEditorDocument a = store.Open("text", "a.md");
        IEditorDocument b = store.Open("text", "b.md");
        DocumentBinding aBinding = store.BindingOf(a)!;
        DocumentBinding bBinding = store.BindingOf(b)!;

        Assert.Throws<InvalidOperationException>(() => store.Rebind(a, "b.md"));

        Assert.Same(aBinding, store.BindingOf(a));
        Assert.Same(bBinding, store.BindingOf(b));
        Assert.Same(a, store.DocAt("a.md"));
        Assert.Same(b, store.DocAt("b.md"));
        fs.Write("a.md", "external-a");
        Assert.True(aBinding.ExternalChange.Value);
        Assert.False(bBinding.ExternalChange.Value);
    }

    [Fact]
    public void RebindWatcherFailureIsAtomic()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("old.md", "old");
        storage.Inner.Write("new.md", "new");
        using var store = new DocumentStore(ws, storage);
        IEditorDocument doc = store.Open("atomic", "old.md");
        DocumentBinding binding = store.BindingOf(doc)!;
        storage.FailWatches = true;

        Assert.Throws<IOException>(() => store.Rebind(doc, "new.md"));

        Assert.Same(binding, store.BindingOf(doc));
        Assert.Equal("old.md", binding.Path);
        Assert.Same(doc, store.DocAt("old.md"));
        Assert.Null(store.DocAt("new.md"));
        storage.FailWatches = false;
        storage.Inner.Write("old.md", "external");
        Assert.True(binding.ExternalChange.Value);
    }

    [Fact]
    public void ExplicitUnbindDisposesWatcherAndAllowsPathReuse()
    {
        (Workspace ws, MemoryFileStorage fs, DocumentStore store) = Make();
        fs.Write("a.md", "a");
        IEditorDocument first = store.Open("text", "a.md");
        DocumentBinding binding = store.BindingOf(first)!;

        DocumentUnbindResult result = Assert.IsType<DocumentUnbindResult>(store.Unbind(first));
        Assert.Equal("a.md", result.Path);
        Assert.Null(store.BindingOf(first));
        fs.Write("a.md", "after unbind");
        Assert.False(binding.ExternalChange.Value);

        IEditorDocument second = ws.New("text");
        DocumentRebindResult rebound = store.Rebind(second, "a.md");
        Assert.Same(second, store.DocAt("a.md"));
        Assert.Equal("a.md", rebound.Path);
    }

    [Fact]
    public void SaveAs_WritesDestinationBeforeWatcherSetup()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new WatchRequiresExistingFileStorage();
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)ws.New("atomic");
        doc.Edit("saved");

        store.SaveAs(doc, "new/folder/document.md");

        Assert.Equal("saved", storage.Inner.Read("new/folder/document.md"));
        Assert.Equal("new/folder/document.md", store.BindingOf(doc)!.Path);
        Assert.False(doc.Dirty.Value);
    }

    [Fact]
    public void SaveAs_VerifiesDestinationAfterWatcherSetup()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new ChangeDuringWatchStorage();
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)ws.New("atomic");
        doc.Edit("saved");

        store.SaveAs(doc, "new.md");

        Assert.Equal("external", storage.Inner.Read("new.md"));
        Assert.True(store.BindingOf(doc)!.ExternalChange.Peek());
    }

    [Fact]
    public void SaveAs_WatcherFailureRestoresExistingDestination()
    {
        var ws = new Workspace();
        ws.RegisterProvider(new AtomicProvider());
        var storage = new FailingStorage();
        storage.Inner.Write("old.md", "old saved");
        storage.Inner.Write("destination.md", "destination original");
        using var store = new DocumentStore(ws, storage);
        var doc = (AtomicDocument)store.Open("atomic", "old.md");
        doc.Edit("changed");
        storage.FailWatches = true;

        Assert.Throws<IOException>(() => store.SaveAs(doc, "destination.md"));

        Assert.Equal("destination original", storage.Inner.Read("destination.md"));
        Assert.Equal("old.md", store.BindingOf(doc)!.Path);
        Assert.True(doc.Dirty.Value);
    }

    [Fact]
    public void PhysicalStorageRejectsRootedAndParentTraversalForEveryPathOperation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"luxel-storage-root-{Guid.NewGuid():N}");
        string outside = Path.Combine(Path.GetTempPath(), $"luxel-storage-outside-{Guid.NewGuid():N}.txt");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
            File.WriteAllText(outside, "outside");
            var storage = new PhysicalFileStorage(root);
            string escape = "../" + Path.GetFileName(outside);

            Assert.Throws<ArgumentException>(() => storage.Exists(escape));
            Assert.Throws<ArgumentException>(() => storage.Read(escape));
            Assert.Throws<ArgumentException>(() => storage.Write(escape, "changed"));
            Assert.Throws<ArgumentException>(() => storage.Delete(escape));
            Assert.Throws<ArgumentException>(() => storage.Watch(escape, () => { }));
            Assert.Throws<ArgumentException>(() => storage.Move(escape, "moved.txt"));
            Assert.Throws<ArgumentException>(() => storage.Move("inside.txt", escape));

            Assert.Throws<ArgumentException>(() => storage.Read(outside));
            Assert.Throws<ArgumentException>(() => storage.Write(outside, "changed"));
            Assert.Throws<ArgumentException>(() => storage.Move("inside.txt", outside));
            Assert.Equal(["inside.txt"], storage.List().ToArray());
            Assert.Equal("outside", File.ReadAllText(outside));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Fact]
    public void PhysicalStorageRejectsSymbolicLinkTraversalForEveryDirectOperation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"luxel-storage-link-root-{Guid.NewGuid():N}");
        string outside = Path.Combine(Path.GetTempPath(), $"luxel-storage-link-outside-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "outside.txt"), "outside");
            File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
            try { Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside); }
            catch (UnauthorizedAccessException) { return; }
            catch (PlatformNotSupportedException) { return; }
            var storage = new PhysicalFileStorage(root);

            Assert.Throws<ArgumentException>(() => storage.Exists("linked/outside.txt"));
            Assert.Throws<ArgumentException>(() => storage.Read("linked/outside.txt"));
            Assert.Throws<ArgumentException>(() => storage.Write("linked/new.txt", "new"));
            Assert.Throws<ArgumentException>(() => storage.Delete("linked/outside.txt"));
            Assert.Throws<ArgumentException>(() => storage.Watch("linked/outside.txt", () => { }));
            Assert.Throws<ArgumentException>(() => storage.Move("inside.txt", "linked/moved.txt"));
            Assert.Throws<ArgumentException>(() => storage.Move("linked/outside.txt", "moved.txt"));
            Assert.Equal(["inside.txt"], storage.List().ToArray());
            Assert.Equal("outside", File.ReadAllText(Path.Combine(outside, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    private sealed class ChangeDuringWatchStorage : IFileStorage
    {
        public MemoryFileStorage Inner { get; } = new();
        public bool Exists(string path) => Inner.Exists(path);
        public string? Read(string path) => Inner.Read(path);
        public void Write(string path, string content) => Inner.Write(path, content);
        public IDisposable? Watch(string path, Action onChanged)
        {
            IDisposable token = Inner.Watch(path, onChanged);
            Inner.Write(path, "external");
            return token;
        }
        public IEnumerable<string> List() => Inner.List();
        public void Delete(string path) => Inner.Delete(path);
        public void Move(string sourcePath, string destinationPath) => Inner.Move(sourcePath, destinationPath);
    }

    private sealed class WatchRequiresExistingFileStorage : IFileStorage
    {
        public MemoryFileStorage Inner { get; } = new();
        public bool Exists(string path) => Inner.Exists(path);
        public string? Read(string path) => Inner.Read(path);
        public void Write(string path, string content) => Inner.Write(path, content);
        public IDisposable? Watch(string path, Action onChanged)
        {
            if (!Inner.Exists(path)) throw new IOException("watch requires an existing destination");
            return Inner.Watch(path, onChanged);
        }
        public IEnumerable<string> List() => Inner.List();
        public void Delete(string path) => Inner.Delete(path);
        public void Move(string sourcePath, string destinationPath) => Inner.Move(sourcePath, destinationPath);
    }

    private sealed class AtomicProvider : IDocumentProvider
    {
        public string Kind => "atomic";
        public string DisplayName => "Atomic";
        public IEditorDocument CreateNew() => new AtomicDocument();
    }

    private sealed class AtomicDocument : IEditorDocument
    {
        private string _saved = "";
        public string Content { get; private set; } = "";
        public string Kind => "atomic";
        public string Title => "atomic";
        public Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public Widget CreateView() => throw new NotSupportedException();
        public void Undo() { }
        public void Redo() { }
        public string Serialize() => Content;
        public void AcceptSavedSnapshot(string content) { _saved = content; Dirty.Value = Content != _saved; }
        public void LoadFrom(string content) { Content = _saved = content; Dirty.Value = false; }
        public void Edit(string content) { Content = content; Dirty.Value = Content != _saved; }
    }

    private sealed class FailingStorage : IFileStorage
    {
        public MemoryFileStorage Inner { get; } = new();
        public bool FailWrites { get; set; }
        public bool FailWatches { get; set; }
        public bool Exists(string path) => Inner.Exists(path);
        public string? Read(string path) => Inner.Read(path);
        public void Write(string path, string content)
        {
            if (FailWrites) throw new IOException("write failed");
            Inner.Write(path, content);
        }
        public IDisposable? Watch(string path, Action onChanged)
        {
            if (FailWatches) throw new IOException("watch failed");
            return Inner.Watch(path, onChanged);
        }
        public IEnumerable<string> List() => Inner.List();
        public void Delete(string path) => Inner.Delete(path);
        public void Move(string sourcePath, string destinationPath) => Inner.Move(sourcePath, destinationPath);
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
