using Luxel.UI;

namespace Luxel.Workbench;

/// <summary>
/// 開いているドキュメント群の管理 (ADR-0010)。プロバイダ登録・開閉・アクティブ切替・
/// ダーティ集約を持ち、undo/redo はアクティブドキュメントへ**委譲**する (シェルは内部
/// モデルを知らない)。UI へは signal で結線する: <see cref="Documents"/> は Effect 内で
/// 読むと開閉に追従し、<see cref="Active"/>/<see cref="AnyDirty"/> はそのまま signal。
/// レイアウト (どのペインにどのタブか) は持たない — それは DockTree (S(C2))。
/// </summary>
public sealed class Workspace
{
    private readonly List<IEditorDocument> _docs = new();
    private readonly Dictionary<string, IDocumentProvider> _providers = new();
    private readonly Signal<int> _version = new(0);   // 開閉 (構造変化) で bump

    public Workspace()
    {
        // 集約 = 開いている doc のどれかがダーティか。_version 読みで開閉にも追従する
        // (閉じたダーティ doc は集約から外れ、開いた doc の Dirty は購読に入る)。
        AnyDirty = new Computed<bool>(() =>
        {
            _ = _version.Value;
            foreach (IEditorDocument d in _docs)
                if (d.Dirty.Value) return true;
            return false;
        });
    }

    /// <summary>アクティブドキュメント (無ければ null)。タブ選択・undo 委譲先。</summary>
    public Signal<IEditorDocument?> Active { get; } = new(null);

    /// <summary>開いているどれかがダーティか (ウィンドウタイトルの ● や終了確認用)。</summary>
    public Computed<bool> AnyDirty { get; }

    /// <summary>開いているドキュメント (開いた順)。Effect 内で読むと開閉で再実行される。</summary>
    public IReadOnlyList<IEditorDocument> Documents
    {
        get { _ = _version.Value; return _docs; }
    }

    // ---- プロバイダ ----

    /// <summary>種別プロバイダを登録する。同じ Kind の再登録は上書き。</summary>
    public void RegisterProvider(IDocumentProvider provider) => _providers[provider.Kind] = provider;

    /// <summary>登録済みプロバイダ (新規作成メニューの列挙用)。</summary>
    public IReadOnlyCollection<IDocumentProvider> Providers => _providers.Values;

    /// <summary>Kind のプロバイダ。未登録なら null。</summary>
    public IDocumentProvider? ProviderFor(string kind) => _providers.GetValueOrDefault(kind);

    // ---- 開閉 ----

    /// <summary>新規ドキュメントを作って開く (追加 + アクティブ化)。Kind 未登録は例外。</summary>
    public IEditorDocument New(string kind)
    {
        IDocumentProvider p = _providers.GetValueOrDefault(kind)
            ?? throw new ArgumentException($"未登録のドキュメント種別: {kind}", nameof(kind));
        IEditorDocument doc = p.CreateNew();
        Open(doc);
        return doc;
    }

    /// <summary>直列化表現からドキュメントを開く (CreateNew + LoadFrom + 追加 + アクティブ化)。
    /// ファイル IO は持たない — 内容の入手は IDocumentStore (S(C2)) の責務。</summary>
    public IEditorDocument Open(string kind, string content)
    {
        IDocumentProvider p = _providers.GetValueOrDefault(kind)
            ?? throw new ArgumentException($"未登録のドキュメント種別: {kind}", nameof(kind));
        IEditorDocument doc = p.CreateNew();
        doc.LoadFrom(content);
        Open(doc);
        return doc;
    }

    /// <summary>作成済みドキュメントを開く。既に開いていれば追加せずアクティブ化だけする。</summary>
    public void Open(IEditorDocument doc)
    {
        if (!_docs.Contains(doc))
        {
            _docs.Add(doc);
            _version.Value++;
        }
        Active.Value = doc;
    }

    /// <summary>ドキュメントを閉じる (一覧から外し、IDisposable なら破棄)。ダーティ確認は
    /// しない — 「保存しますか」はシェルが <see cref="IEditorDocument.Dirty"/> を見て閉じる前に
    /// 行う。閉じたのがアクティブなら隣 (同じ位置、末尾なら新しい末尾) をアクティブにする。</summary>
    public bool Close(IEditorDocument doc)
    {
        int i = _docs.IndexOf(doc);
        if (i < 0) return false;
        _docs.RemoveAt(i);
        if (ReferenceEquals(Active.Peek(), doc))
            Active.Value = _docs.Count == 0 ? null : _docs[Math.Min(i, _docs.Count - 1)];
        _version.Value++;
        (doc as IDisposable)?.Dispose();
        return true;
    }

    /// <summary>開いているドキュメントをアクティブにする。開いていなければ何もしない。</summary>
    public void Activate(IEditorDocument doc)
    {
        if (_docs.Contains(doc)) Active.Value = doc;
    }

    // ---- undo/redo 委譲 ----

    /// <summary>アクティブドキュメントが undo 可能か。</summary>
    public bool CanUndo => Active.Peek()?.CanUndo ?? false;

    /// <summary>アクティブドキュメントが redo 可能か。</summary>
    public bool CanRedo => Active.Peek()?.CanRedo ?? false;

    /// <summary>アクティブドキュメントへ undo を委譲する。</summary>
    public void Undo() => Active.Peek()?.Undo();

    /// <summary>アクティブドキュメントへ redo を委譲する。</summary>
    public void Redo() => Active.Peek()?.Redo();
}
