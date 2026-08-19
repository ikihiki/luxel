using System.Runtime.CompilerServices;
using Luxel.Graphics.TwoD;
using Luxel.SceneEdit;
using Luxel.Typography.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed record SceneHierarchyNode(int EntityId, string Name, int? ParentId, bool Visible, bool Locked,
    IReadOnlyList<SceneHierarchyNode> Children);

public enum SceneHierarchyAction { Create, Delete, Rename, Duplicate, ToggleVisibility, ToggleLock }

public sealed class SceneHierarchyController : IDisposable
{
    public const string MetadataComponent = "editor.hierarchy";
    private const string ParentField = "parent";
    private const string VisibleField = "visible";
    private const string LockedField = "locked";
    private readonly string _documentId;
    private readonly SceneDocument _document;
    private readonly EditorSelectionService _selection;
    private readonly HashSet<int> _expanded = [];
    private readonly IDisposable _sceneSelectionSubscription;
    private readonly IDisposable _selectionSync;
    private bool _disposed;

    public SceneHierarchyController(string documentId, SceneDocument document, EditorSelectionService selection)
    {
        _documentId = documentId;
        _document = document;
        _selection = selection;
        SceneEditorView view = _document.View;
        _sceneSelectionSubscription = SceneSelectionHub.Subscribe(view, editor =>
        {
            if (editor.Scene.Selection.IsEmpty) _selection.Clear();
            else _selection.SelectEntities(_documentId, editor.Scene.Selection.Entities, editor.Scene.Selection.Main);
        });
        _selectionSync = Reactive.Effect(() =>
        {
            EditorSelectionState current = _selection.Current.Value;
            if (current.DocumentId != _documentId || current.MainEntityId < 0)
            {
                if (view.SelectionCount != 0) view.SelectEntities([]);
                return;
            }
            int[] valid = current.EntityIds.Where(_document.Doc.HasEntity).ToArray();
            if (valid.Length == 0)
            {
                if (view.SelectionCount != 0) view.SelectEntities([]);
                return;
            }
            int main = valid.Contains(current.MainEntityId) ? current.MainEntityId : valid[0];
            if (!view.IsSelected(main) || view.SelectionCount != valid.Length || valid.Any(id => !view.IsSelected(id)))
                view.SelectEntities(valid, main);
        });
    }

    public Signal<int> Revision => _document.View.Revision;
    public Signal<EditorSelectionState> Selection => _selection.Current;
    public bool IsDisposed => _disposed;
    public int? SelectedEntityId
    {
        get
        {
            EditorSelectionState current = _selection.Current.Peek();
            return current.DocumentId == _documentId && current.MainEntityId >= 0 ? current.MainEntityId : null;
        }
    }
    public IReadOnlySet<int> Expanded => _expanded;
    public string Filter { get; set; } = "";

    public IReadOnlyList<SceneHierarchyNode> Build()
    {
        SceneDoc doc = _document.Doc;
        var children = doc.Entities.ToDictionary(x => x.Id, _ => new List<SceneEntity>());
        var roots = new List<SceneEntity>();
        foreach (SceneEntity entity in doc.Entities)
        {
            int? parent = Parent(entity);
            if (parent is { } id && id != entity.Id && children.TryGetValue(id, out List<SceneEntity>? list)) list.Add(entity);
            else roots.Add(entity);
        }

        SceneHierarchyNode Node(SceneEntity entity, HashSet<int> path)
        {
            if (!path.Add(entity.Id)) return new(entity.Id, entity.Name, Parent(entity), Visible(entity), Locked(entity), []);
            SceneHierarchyNode[] nested = children[entity.Id].Select(x => Node(x, new HashSet<int>(path))).ToArray();
            return new(entity.Id, entity.Name, Parent(entity), Visible(entity), Locked(entity), nested);
        }

        SceneHierarchyNode[] all = roots.Select(x => Node(x, [])).ToArray();
        if (string.IsNullOrWhiteSpace(Filter)) return all;
        SceneHierarchyNode? Include(SceneHierarchyNode node)
        {
            SceneHierarchyNode[] nested = node.Children.Select(Include).Where(x => x is not null).Cast<SceneHierarchyNode>().ToArray();
            return node.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || nested.Length > 0 ? node with { Children = nested } : null;
        }
        return all.Select(Include).Where(x => x is not null).Cast<SceneHierarchyNode>().ToArray();
    }

    public void SetExpanded(int entityId, bool expanded)
    {
        if (expanded) _expanded.Add(entityId); else _expanded.Remove(entityId);
    }

    public void Select(int entityId)
    {
        _ = _document.Doc.Entity(entityId);
        _document.View.SelectEntity(entityId);
        _selection.SelectEntities(_documentId, [entityId], entityId);
    }

    public int Create(string name, int? parentId = null)
    {
        if (parentId is { } parent && (Locked(_document.Doc.Entity(parent)))) throw new InvalidOperationException("The parent entity is locked.");
        int id = SceneCommands.NextEntityId(_document.Doc);
        SceneEntity entity = SceneEntity.Of(id, name, Metadata(parentId, true, false));
        _document.View.ApplyEdit(new AddEntity(entity));
        Select(id);
        return id;
    }

    public void Delete(IEnumerable<int> ids)
    {
        HashSet<int> remove = ids.ToHashSet();
        bool changed;
        do
        {
            changed = false;
            foreach (SceneEntity entity in _document.Doc.Entities)
                if (Parent(entity) is { } parent && remove.Contains(parent) && remove.Add(entity.Id)) changed = true;
        } while (changed);
        foreach (int id in remove) if (Locked(_document.Doc.Entity(id))) throw new InvalidOperationException("Locked entities cannot be deleted.");
        _document.View.ApplyEdit(remove.Select(id => (SceneChange)new RemoveEntity(id)).ToArray());
        _selection.RemoveInvalid(_documentId, _document.Doc.HasEntity);
    }

    public void Rename(int entityId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Entity name is required.", nameof(name));
        EnsureUnlocked(entityId);
        _document.View.ApplyEdit(new RenameEntity(entityId, name));
    }

    public int Duplicate(int entityId)
    {
        EnsureUnlocked(entityId);
        SceneEntity source = _document.Doc.Entity(entityId);
        int id = SceneCommands.NextEntityId(_document.Doc);
        SceneEntity clone = SceneEntity.Of(id, source.Name + " Copy", source.Components);
        _document.View.ApplyEdit(new AddEntity(clone));
        Select(id);
        return id;
    }

    public void Reparent(int entityId, int? parentId)
    {
        EnsureUnlocked(entityId);
        if (parentId == entityId) throw new InvalidOperationException("An entity cannot be parented to itself.");
        if (parentId is { } parent)
        {
            EnsureUnlocked(parent);
            for (int? current = parent; current is { } value; current = Parent(_document.Doc.Entity(value)))
                if (value == entityId) throw new InvalidOperationException("Reparenting would create a cycle.");
        }
        SceneEntity entity = _document.Doc.Entity(entityId);
        _document.View.ApplyEdit(new SetComponent(entityId, Metadata(parentId, Visible(entity), Locked(entity))));
    }

    public void SetVisible(int entityId, bool visible)
    {
        EnsureUnlocked(entityId);
        SceneEntity entity = _document.Doc.Entity(entityId);
        _document.View.ApplyEdit(new SetComponent(entityId, Metadata(Parent(entity), visible, Locked(entity))));
    }

    public void SetLocked(int entityId, bool locked)
    {
        SceneEntity entity = _document.Doc.Entity(entityId);
        _document.View.ApplyEdit(new SetComponent(entityId, Metadata(Parent(entity), Visible(entity), locked)));
    }

    public void Execute(SceneHierarchyAction action, int entityId, string? name = null) => _ = action switch
    {
        SceneHierarchyAction.Create => Create(name ?? "Entity", entityId),
        SceneHierarchyAction.Delete => Run(() => Delete([entityId])),
        SceneHierarchyAction.Rename => Run(() => Rename(entityId, name ?? "Entity")),
        SceneHierarchyAction.Duplicate => Duplicate(entityId),
        SceneHierarchyAction.ToggleVisibility => Run(() => SetVisible(entityId, !Visible(_document.Doc.Entity(entityId)))),
        SceneHierarchyAction.ToggleLock => Run(() => SetLocked(entityId, !Locked(_document.Doc.Entity(entityId)))),
        _ => 0,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _selectionSync.Dispose();
        _sceneSelectionSubscription.Dispose();
    }

    private static class SceneSelectionHub
    {
        private static readonly ConditionalWeakTable<SceneEditorView, Hub> Hubs = new();

        public static IDisposable Subscribe(SceneEditorView view, Action<SceneEditorView> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            Hub hub = Hubs.GetValue(view, static owner => new Hub(owner));
            return hub.Subscribe(listener);
        }

        private sealed class Hub
        {
            private readonly SceneEditorView _view;
            private readonly Action<SceneEditorView>? _previous;
            private readonly Action<SceneEditorView> _dispatch;
            private readonly List<Action<SceneEditorView>> _listeners = [];

            public Hub(SceneEditorView view)
            {
                _view = view;
                _previous = view.OnSelectionChanged;
                _dispatch = Dispatch;
                view.OnSelectionChanged = _dispatch;
            }

            public IDisposable Subscribe(Action<SceneEditorView> listener)
            {
                _listeners.Add(listener);
                return new Subscription(this, listener);
            }

            private void Dispatch(SceneEditorView editor)
            {
                _previous?.Invoke(editor);
                foreach (Action<SceneEditorView> listener in _listeners.ToArray()) listener(editor);
            }

            private void Remove(Action<SceneEditorView> listener)
            {
                _listeners.Remove(listener);
                if (_listeners.Count != 0) return;
                if (ReferenceEquals(_view.OnSelectionChanged, _dispatch)) _view.OnSelectionChanged = _previous;
                Hubs.Remove(_view);
            }

            private sealed class Subscription(Hub owner, Action<SceneEditorView> listener) : IDisposable
            {
                private Hub? _owner = owner;
                public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(listener);
            }
        }
    }

    private static int Run(Action action) { action(); return 0; }
    private void EnsureUnlocked(int id) { if (Locked(_document.Doc.Entity(id))) throw new InvalidOperationException("The entity is locked."); }
    private static int? Parent(SceneEntity entity)
    {
        SceneValue? value = entity.Component(MetadataComponent)?.Get(ParentField);
        int parent = value?.AsInt() ?? 0;
        return parent == 0 ? null : parent;
    }
    private static bool Visible(SceneEntity entity) => entity.Component(MetadataComponent)?.Get(VisibleField)?.AsBool() ?? true;
    private static bool Locked(SceneEntity entity) => entity.Component(MetadataComponent)?.Get(LockedField)?.AsBool() ?? false;
    private static SceneComponent Metadata(int? parent, bool visible, bool locked) => SceneComponent.Of(MetadataComponent,
        (ParentField, SceneValue.Of(parent ?? 0)), (VisibleField, SceneValue.Of(visible)), (LockedField, SceneValue.Of(locked)));
}

public sealed class SceneHierarchyView : CompositeControl
{
    private readonly Signal<string> _filter = new("");
    private readonly Signal<string> _name = new("Entity");
    private readonly Signal<string> _parent = new("");
    private readonly SceneHierarchyTree _tree;

    public SceneHierarchyView(SceneHierarchyController controller)
    {
        Controller = controller;
        _tree = new SceneHierarchyTree(controller, () => _name.Peek(), Run);
    }

    public SceneHierarchyController Controller { get; }
    public Signal<string?> ActionError { get; } = new(null);
    public int? FocusedEntityId => _tree.FocusedEntityId;
    public int? ContextEntityId => _tree.ContextEntityId;
    public bool TreeFocused => _tree.Focused.Peek();
    public Rect TreeBounds => new(_tree.WorldPos.X, _tree.WorldPos.Y, _tree.Size.Width, _tree.Size.Height);

    public void SetFilter(string filter) => _filter.Value = filter ?? "";
    public bool HandleTreeKey(KeyEvent key) => _tree.HandleKey(key);

    public bool CreateEntity()
        => Run(() => Controller.Create(_name.Peek(), ParseParent()));

    public bool RenameSelected()
        => SelectedId() is { } id && Run(() => Controller.Rename(id, _name.Peek()));

    public bool ReparentSelected()
        => SelectedId() is { } id && Run(() => Controller.Reparent(id, ParseParent()));

    protected override Widget Build()
    {
        _ = Controller.Revision.Value;
        _ = Controller.Selection.Value;
        Controller.Filter = _filter.Value;
        IReadOnlyList<SceneHierarchyNode> roots = Controller.Build();
        _tree.SetNodes(roots, !string.IsNullOrWhiteSpace(Controller.Filter));
        int? selected = Controller.SelectedEntityId;
        SceneHierarchyNode? selectedNode = selected is { } selectedId ? Find(roots, selectedId) : null;
        var content = new List<Widget>
        {
            Text("Scene Hierarchy"),
            TextField(_filter, placeholder: "Filter entities", width: 220),
            HStack(4)[TextField(_name, placeholder: "Entity name", width: 140), TextField(_parent, placeholder: "Parent id", width: 90)],
            HStack(4)[Button(_ => CreateEntity(), "Create"), Button(_ => RenameSelected(), "Rename"), Button(_ => ReparentSelected(), "Reparent")],
        };
        if (selectedNode is not null)
            content.Add(HStack(4)[
                Button(_ => Run(() => Controller.Execute(SceneHierarchyAction.Duplicate, selectedNode.EntityId)), "Duplicate"),
                Button(_ => Run(() => Controller.Execute(SceneHierarchyAction.ToggleVisibility, selectedNode.EntityId)), selectedNode.Visible ? "Hide" : "Show"),
                Button(_ => Run(() => Controller.Execute(SceneHierarchyAction.ToggleLock, selectedNode.EntityId)), selectedNode.Locked ? "Unlock" : "Lock"),
                Button(_ => Run(() => Controller.Execute(SceneHierarchyAction.Delete, selectedNode.EntityId)), "Delete")]);
        content.Add(_tree);
        if (ActionError.Value is { } error) content.Add(Text(error));
        return VStack(4)[content.ToArray()];
    }

    private static SceneHierarchyNode? Find(IEnumerable<SceneHierarchyNode> nodes, int id)
    {
        foreach (SceneHierarchyNode node in nodes)
        {
            if (node.EntityId == id) return node;
            if (Find(node.Children, id) is { } found) return found;
        }
        return null;
    }

    private int? SelectedId()
    {
        if (Controller.SelectedEntityId is { } id) return id;
        ActionError.Value = "Select an entity first.";
        return null;
    }

    private int? ParseParent()
    {
        string text = _parent.Peek().Trim();
        if (text.Length == 0) return null;
        if (int.TryParse(text, out int id)) return id;
        throw new ArgumentException("Parent id must be an integer.");
    }

    private bool Run(Action action)
    {
        try { action(); ActionError.Value = null; return true; }
        catch (Exception ex) { ActionError.Value = ex.Message; return false; }
    }

    private sealed class SceneHierarchyTree(
        SceneHierarchyController controller,
        Func<string> name,
        Func<Action, bool> run) : Widget
    {
        private const float RowHeight = 26;
        private const float ViewHeight = 260;
        private readonly ScrollModel _scroll = new();
        private readonly List<Row> _rows = [];
        private IReadOnlyList<SceneHierarchyNode> _roots = [];
        private bool _filtering;
        private float _width = 320;
        private FocusTarget? _focus;

        private sealed record Row(SceneHierarchyNode Node, int Depth, int? ParentId);
        private sealed record EntityDrag(SceneHierarchyTree Owner, int EntityId, string Name);

        public int? FocusedEntityId { get; private set; }
        public int? ContextEntityId { get; private set; }
        public override string? DebugDetail => $"Hierarchy {_rows.Count} rows, focus={FocusedEntityId?.ToString() ?? "none"}";

        public void SetNodes(IReadOnlyList<SceneHierarchyNode> roots, bool filtering)
        {
            _roots = roots;
            _filtering = filtering;
            RefreshRows();
        }

        public bool HandleKey(KeyEvent key)
        {
            if (_rows.Count == 0) return false;
            int current = _rows.FindIndex(row => row.Node.EntityId == (FocusedEntityId ?? controller.SelectedEntityId));
            int next = key.Key switch
            {
                Key.Home => 0,
                Key.End => _rows.Count - 1,
                Key.Up => current <= 0 ? 0 : current - 1,
                Key.Down => current < 0 ? 0 : Math.Min(_rows.Count - 1, current + 1),
                _ => current,
            };
            if (key.Key is Key.Home or Key.End or Key.Up or Key.Down)
            {
                SelectRow(next);
                return true;
            }
            if (current < 0) current = 0;
            Row row = _rows[current];
            switch (key.Key)
            {
                case Key.Right when row.Node.Children.Count > 0:
                    controller.SetExpanded(row.Node.EntityId, true);
                    RefreshRows();
                    MarkNeedsRealize();
                    return true;
                case Key.Left when controller.Expanded.Contains(row.Node.EntityId):
                    controller.SetExpanded(row.Node.EntityId, false);
                    RefreshRows();
                    MarkNeedsRealize();
                    return true;
                case Key.Left when row.ParentId is { } parent:
                    SelectEntity(parent);
                    return true;
                case Key.Enter:
                    SelectEntity(row.Node.EntityId);
                    return true;
                case Key.Space:
                    run(() => controller.SetVisible(row.Node.EntityId, !row.Node.Visible));
                    return true;
                case Key.Delete:
                    run(() => controller.Delete([row.Node.EntityId]));
                    return true;
                case Key.D when key.Ctrl:
                    run(() => controller.Duplicate(row.Node.EntityId));
                    return true;
                default:
                    return false;
            }
        }

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
        {
            _width = float.IsFinite(constraints.MaxW) ? constraints.MaxW : 320;
            Size = constraints.Constrain(new Size(_width, ViewHeight));
            _scroll.SetLengths(_rows.Count * RowHeight, Size.Height);
        }

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            UiNode root = CreateRoot(context, parent, worldOrigin);
            root.Clip = new RectClip(0, 0, Size.Width, Size.Height);
            UiNode content = context.Canvas.AddChild(root);
            context.Effect(() => content.Transform = Affine2D.Translate(0, -_scroll.Clamped));
            _focus ??= new FocusTarget
            {
                OnFocus = focused => Focused.Value = focused,
                OnKey = HandleKey,
            };
            context.AddFocusable(_focus);
            context.AddHit(root, new Rect(0, 0, Size.Width, Size.Height), focus: _focus,
                onContext: e => ContextMenu.Open(context, e.ScreenX, e.ScreenY,
                    ("Create root", () => run(() => controller.Create(name())))),
                acceptsDrop: payload => payload is EntityDrag drag && ReferenceEquals(drag.Owner, this),
                onDrop: (payload, _) =>
                {
                    if (payload is EntityDrag drag) run(() => controller.Reparent(drag.EntityId, null));
                });

            float fontSize = context.Theme.Peek().FontSm;
            float baseline = (RowHeight - context.Font.Measure("Mg", fontSize).height) / 2 + context.Font.Ascent(fontSize);
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                UiNode rowNode = context.Canvas.AddChild(content);
                rowNode.Transform = Affine2D.Translate(0, i * RowHeight);
                var scene = new Scene2D();
                bool selected = controller.SelectedEntityId == row.Node.EntityId;
                if (selected) scene.FillRoundedRect(Color2D.White, 2, 1, MathF.Max(0, Size.Width - 12), RowHeight - 2, 4);
                string chevron = row.Node.Children.Count == 0 ? "  "
                    : controller.Expanded.Contains(row.Node.EntityId) || _filtering ? "⌄ " : "› ";
                string flags = $"{(row.Node.Visible ? "" : "○ ")}{(row.Node.Locked ? "🔒 " : "")}";
                context.Font.AppendText(scene, chevron + flags + row.Node.Name, 8 + row.Depth * 16, baseline,
                    fontSize, Color2D.White);
                rowNode.Content = scene;
                context.Effect(() => rowNode.Color = selected ? context.Theme.Value.Primary : context.Theme.Value.Text);

                bool started = false;
                context.AddHit(rowNode, new Rect(0, 0, Size.Width, RowHeight), focus: _focus,
                    onDragStart: _ => started = false,
                    onDrag: e =>
                    {
                        if (started || MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4 || context.Host is null) return;
                        started = true;
                        var ghost = new Scene2D();
                        ghost.FillRoundedRect(context.Theme.Peek().SurfaceAlt, 0, 0, 180, RowHeight, 4);
                        context.Font.AppendText(ghost, row.Node.Name, 8, baseline, fontSize, context.Theme.Peek().Text);
                        context.Host.BeginDrag(new EntityDrag(this, row.Node.EntityId, row.Node.Name), ghost, e.StartX, RowHeight / 2);
                    },
                    onDragEnd: e =>
                    {
                        if (started) return;
                        float chevronEdge = 8 + row.Depth * 16 + 16;
                        if (row.Node.Children.Count > 0 && e.X <= chevronEdge)
                        {
                            controller.SetExpanded(row.Node.EntityId, !controller.Expanded.Contains(row.Node.EntityId));
                            RefreshRows();
                            MarkNeedsRealize();
                        }
                        else SelectEntity(row.Node.EntityId);
                    },
                    onContext: e => OpenContext(context, e, row.Node),
                    acceptsDrop: payload => payload is EntityDrag drag
                        && ReferenceEquals(drag.Owner, this) && drag.EntityId != row.Node.EntityId,
                    onDrop: (payload, _) =>
                    {
                        if (payload is EntityDrag drag) run(() => controller.Reparent(drag.EntityId, row.Node.EntityId));
                    });
            }
            ScrollBars.AttachVertical(context, root, _scroll, Size.Width, Size.Height, minThumb: 24);
            context.AddScroll(root, new Rect(0, 0, Size.Width, Size.Height), delta => _scroll.ScrollBy(-delta));
        }

        private void OpenContext(UiBuildContext context, PointerEvent e, SceneHierarchyNode node)
        {
            ContextEntityId = node.EntityId;
            ContextMenu.Open(context, e.ScreenX, e.ScreenY,
                ("Create child", () => run(() => controller.Create(name(), node.EntityId))),
                ("Rename", () => run(() => controller.Rename(node.EntityId, name()))),
                ("Duplicate", () => run(() => controller.Duplicate(node.EntityId))),
                (node.Visible ? "Hide" : "Show", () => run(() => controller.SetVisible(node.EntityId, !node.Visible))),
                (node.Locked ? "Unlock" : "Lock", () => run(() => controller.SetLocked(node.EntityId, !node.Locked))),
                ("Move to root", () => run(() => controller.Reparent(node.EntityId, null))),
                ("Delete", () => run(() => controller.Delete([node.EntityId]))));
        }

        private void SelectRow(int index)
        {
            if ((uint)index >= (uint)_rows.Count) return;
            SelectEntity(_rows[index].Node.EntityId);
            _scroll.EnsureVisible(index * RowHeight, (index + 1) * RowHeight, 2);
        }

        private void SelectEntity(int entityId)
        {
            FocusedEntityId = entityId;
            run(() => controller.Select(entityId));
        }

        private void RefreshRows()
        {
            _rows.Clear();
            void Add(IEnumerable<SceneHierarchyNode> nodes, int depth, int? parent)
            {
                foreach (SceneHierarchyNode node in nodes)
                {
                    _rows.Add(new Row(node, depth, parent));
                    if (node.Children.Count > 0 && (_filtering || controller.Expanded.Contains(node.EntityId)))
                        Add(node.Children, depth + 1, node.EntityId);
                }
            }
            Add(_roots, 0, null);
        }
    }
}
