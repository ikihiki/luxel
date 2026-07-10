using Luxel.NodeGraph;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

/// <summary>
/// テキスト系エディタの <see cref="IEditorDocument"/> アダプタ (ADR-0010)。Code / Markdown /
/// Strudel / plain は **構成差** — 呼び出し側が viewFactory で TextEditorView を構成する
/// (プロバイダ/言語サービス/フォント)。文書の真実は <see cref="Text"/> signal で、
/// TextEditorView の value 束縛 (双方向) がビューと同期する。undo/redo は直近のビューへ委譲。
/// </summary>
public sealed class TextDocument : IEditorDocument, IDisposable
{
    private readonly Func<Signal<string>, TextEditorView> _viewFactory;
    private readonly List<TextEditorView> _views = new();
    private readonly IDisposable _dirtyEffect;
    private string _saved;

    public TextDocument(string kind, string title, Func<Signal<string>, TextEditorView> viewFactory,
                        string content = "", IReadOnlyList<CommandContribution>? contributions = null)
    {
        Kind = kind;
        Title = title;
        _viewFactory = viewFactory;
        Text = new Signal<string>(content);
        _saved = content;
        Contributions = contributions ?? [];
        _dirtyEffect = Reactive.Effect(() => Dirty.Value = Text.Value != _saved);
    }

    /// <summary>文書テキスト (真実)。ビューは value 束縛で双方向同期する。</summary>
    public Signal<string> Text { get; }

    public string Kind { get; }
    public string Title { get; }
    public Signal<bool> Dirty { get; } = new(false);
    public IReadOnlyList<CommandContribution> Contributions { get; }

    public Widget CreateView()
    {
        TextEditorView v = _viewFactory(Text);
        // 編集で真実 (Text) を更新 — value 束縛していないビュー (MarkdownDoc.Create 系) でも
        // Serialize/Dirty が追従する。既存の OnEdit (factory が付けた分) は残す
        Action<TextEditorView>? prev = v.OnEdit;
        v.OnEdit = view => { prev?.Invoke(view); Text.Value = view.Text; };
        _views.Add(v);
        return v;
    }

    /// <summary>undo/redo の委譲先 (直近に作られた生きているビュー)。</summary>
    private TextEditorView? View
    {
        get
        {
            for (int i = _views.Count - 1; i >= 0; i--)
                if (_views[i].Scope is { IsDisposed: false }) return _views[i];
            return _views.Count > 0 ? _views[^1] : null;
        }
    }

    public bool CanUndo => View?.CanUndo ?? false;
    public bool CanRedo => View?.CanRedo ?? false;
    public void Undo() => View?.Undo();
    public void Redo() => View?.Redo();

    /// <summary>直列化 = 現在テキスト。**保存点も更新する** — undo で保存内容へ戻れば
    /// Dirty が消える基準になる (IDocumentStore.Save が直前に呼ぶ契約)。</summary>
    public string Serialize()
    {
        _saved = Text.Peek();
        return _saved;
    }

    public void LoadFrom(string content)
    {
        _saved = content;
        Text.Value = content;   // ビューの外部 value 反映 (状態再作成 + 履歴クリア) が効く
    }

    public void Dispose() => _dirtyEffect.Dispose();
}

/// <summary>
/// ノードグラフの <see cref="IEditorDocument"/> アダプタ (ADR-0010)。直列化は
/// <see cref="NodeGraphJson"/> の JSON 往復。ビューの編集 (OnEdit) で真実
/// (<see cref="Doc"/>) を取り込みダーティにする。
/// </summary>
public sealed class NodeGraphDocument : IEditorDocument
{
    private readonly Action<NodeGraphView>? _configure;
    private readonly List<NodeGraphView> _views = new();
    private string _saved;

    public NodeGraphDocument(string title, NodeGraphDoc doc, Action<NodeGraphView>? configure = null,
                             string kind = "nodegraph", IReadOnlyList<CommandContribution>? contributions = null)
    {
        Title = title;
        Doc = doc;
        Kind = kind;
        _configure = configure;
        Contributions = contributions ?? [];
        _saved = NodeGraphJson.Serialize(doc);
    }

    /// <summary>グラフの真実 (ビューの編集で追従)。</summary>
    public NodeGraphDoc Doc { get; private set; }

    public string Kind { get; }
    public string Title { get; }
    public Signal<bool> Dirty { get; } = new(false);
    public IReadOnlyList<CommandContribution> Contributions { get; }

    public Widget CreateView()
    {
        NodeGraphView v = Kit.NodeGraphView(source: Doc);
        v.OnEdit = view =>
        {
            Doc = view.Graph.Doc;
            Dirty.Value = NodeGraphJson.Serialize(Doc) != _saved;
        };
        _configure?.Invoke(v);
        _views.Add(v);
        return v;
    }

    private NodeGraphView? View
    {
        get
        {
            for (int i = _views.Count - 1; i >= 0; i--)
                if (_views[i].Scope is { IsDisposed: false }) return _views[i];
            return _views.Count > 0 ? _views[^1] : null;
        }
    }

    public bool CanUndo => View?.CanUndo ?? false;
    public bool CanRedo => View?.CanRedo ?? false;
    public void Undo() => View?.Undo();
    public void Redo() => View?.Redo();

    public string Serialize()
    {
        _saved = NodeGraphJson.Serialize(Doc);
        Dirty.Value = false;
        return _saved;
    }

    public void LoadFrom(string content)
    {
        Doc = NodeGraphJson.Deserialize(content);
        _saved = NodeGraphJson.Serialize(Doc);
        Dirty.Value = false;
        View?.Load(Doc);
    }
}
