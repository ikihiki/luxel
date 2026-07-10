using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>表示名。ファイルに結んだとき (IDocumentStore.Open/SaveAs) シェルがファイル名を入れる。</summary>
    public string Title { get; set; }

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

    /// <summary>表示名。ファイルに結んだときシェルがファイル名を入れる。</summary>
    public string Title { get; set; }

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

/// <summary>
/// 設定/コンポーネント オブジェクトの <see cref="IEditorDocument"/> アダプタ (ADR-0014 の
/// PropertyGrid 実証)。ビュー = <see cref="PropertyGrid"/> (型別エディタ)、直列化 = JSON
/// (System.Text.Json、enum は文字列・public field 込み)。undo/redo は**プロパティ変更単位**の
/// 履歴 (PropertyGrid の OnChanged を記録して巻き戻す)。
/// </summary>
public sealed class ObjectDocument<T> : IEditorDocument where T : class
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<PropertyGrid> _grids = new();
    private readonly List<(string Name, object? Old, object? New)> _undo = new();
    private readonly List<(string Name, object? Old, object? New)> _redo = new();
    private readonly Dictionary<string, object?> _shadow = new();   // 直前値 (undo の Old 用)
    private string _saved;

    public ObjectDocument(string kind, string title, T target,
                          IReadOnlyList<CommandContribution>? contributions = null)
    {
        Kind = kind;
        Title = title;
        Target = target;
        Contributions = contributions ?? [];
        _saved = JsonSerializer.Serialize(Target, JsonOpts);
        Snapshot();
    }

    /// <summary>編集対象 (真実)。PropertyGrid が直接書き込む。</summary>
    public T Target { get; private set; }

    public string Kind { get; }

    /// <summary>表示名。ファイルに結んだときシェルがファイル名を入れる。</summary>
    public string Title { get; set; }

    public Signal<bool> Dirty { get; } = new(false);
    public IReadOnlyList<CommandContribution> Contributions { get; }

    public Widget CreateView()
    {
        PropertyGrid grid = Kit.PropertyGrid(
            onChanged: (_, name, value) =>
            {
                _undo.Add((name, _shadow.GetValueOrDefault(name), value));
                _redo.Clear();
                _shadow[name] = value;
                UpdateDirty();
            });
        // target は getter 束縛 — LoadFrom でインスタンスが替わっても Refresh で追従する
        // (ファクトリ引数は object? なので Bindable を渡すと箱詰めされてしまう)
        grid.Target.SetBase(new Bindable<object?>(() => Target));
        _grids.Add(grid);
        return grid;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        (string name, object? old, object? @new) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((name, old, @new));
        SetMember(name, old);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        (string name, object? old, object? @new) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((name, old, @new));
        SetMember(name, @new);
    }

    public string Serialize()
    {
        _saved = JsonSerializer.Serialize(Target, JsonOpts);
        Dirty.Value = false;
        return _saved;
    }

    public void LoadFrom(string content)
    {
        Target = JsonSerializer.Deserialize<T>(content, JsonOpts)
            ?? throw new InvalidOperationException($"JSON から {typeof(T).Name} を復元できない");
        _saved = JsonSerializer.Serialize(Target, JsonOpts);
        _undo.Clear();
        _redo.Clear();
        Snapshot();
        Dirty.Value = false;
        RefreshGrids();
    }

    private void SetMember(string name, object? value)
    {
        foreach (PropertyRow row in PropertyGrid.Discover(Target))
            if (row.Name == name) { row.Set(value); break; }
        _shadow[name] = value;
        UpdateDirty();
        RefreshGrids();
    }

    private void Snapshot()
    {
        _shadow.Clear();
        foreach (PropertyRow row in PropertyGrid.Discover(Target)) _shadow[row.Name] = row.Get();
    }

    private void UpdateDirty() => Dirty.Value = JsonSerializer.Serialize(Target, JsonOpts) != _saved;

    private void RefreshGrids()
    {
        foreach (PropertyGrid g in _grids)
            if (g.Scope is { IsDisposed: false }) g.Refresh();
    }
}
