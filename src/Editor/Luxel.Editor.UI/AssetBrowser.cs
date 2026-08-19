using Luxel.Graphics.TwoD;
using Luxel.Mathematics;
using Luxel.Platform;
using Luxel.Typography.TwoD;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public enum AssetBrowserViewMode { List, Grid }

/// <summary>Portable file-drop payload understood by the production asset pane.</summary>
public sealed record AssetImportPayload(IReadOnlyList<(string Name, string Content)> Files);

/// <summary>
/// Asset pane composed from a folder-only tree and a separate current-folder list/grid. Storage mutations are
/// delegated to <see cref="IAssetOperations"/>; successful mutations emit detailed results from that service so
/// session composition can coordinate open document bindings.
/// </summary>
[UiComponent]
public sealed partial class AssetBrowser : CompositeControl
{
    [UiParam] private readonly Bindable<IFileStorage> _storage = new();
    [UiParam] private readonly Bindable<IAssetOperations> _operations = new();
    [UiParam] private readonly BindableString _filter = new();
    [UiParam] private readonly Bindable<ISet<string>> _expanded = new();

    [UiEvent] public UiEvent<AssetBrowser, string> OnOpen;
    [UiEvent] public UiEvent<AssetBrowser> OnImportRequest;
    [UiEvent] public UiEvent<AssetBrowser, string> OnRevealRequest;
    [UiEvent] public UiEvent<AssetBrowser, AssetMutationResult> OnMutation;

    private readonly Signal<string> _filterDraft = new("");
    private bool _filterInitialized;
    private readonly Signal<string> _pathDraft = new("new-asset.txt");
    private readonly Signal<string> _nameDraft = new("");
    private readonly Signal<string> _folderDraft = new("");
    private readonly Signal<AssetBrowserViewMode> _viewMode = new(AssetBrowserViewMode.List);
    private AssetBrowserModel? _model;
    private IAssetOperations? _modelOperations;
    private IAssetOperations? _fallbackOperations;

    public Signal<string?> LastError { get; } = new(null);
    public AssetBrowserModel Model => EnsureModel();
    public AssetBrowserViewMode ViewMode => _viewMode.Peek();
    public string CurrentFolder => Model.CurrentFolder.Peek();
    public IReadOnlySet<string> SelectedPaths => Model.Selection;
    public string Selected => Model.Selection.Order(StringComparer.Ordinal).FirstOrDefault() ?? "";
    public IReadOnlyList<AssetBrowserItem> CurrentItems => Model.CurrentItems();
    public IReadOnlyList<TreeNode> FolderTree => Model.FolderTree();

    public EditorCapabilityState RevealCapability => EffectiveOperations()?.RevealCapability
        ?? new(EditorCapabilityAvailability.Unsupported, "No asset operations service is configured.");
    public EditorCapabilityState ImportCapability => EffectiveOperations()?.ImportCapability
        ?? new(EditorCapabilityAvailability.Unsupported, "No asset operations service is configured.");

    public void SetViewMode(AssetBrowserViewMode mode) => _viewMode.Value = mode;

    public bool OpenFolder(string folder)
    {
        try { Model.OpenFolder(folder); LastError.Value = null; return true; }
        catch (Exception ex) { LastError.Value = ex.Message; return false; }
    }

    public bool SelectAsset(string path, bool open = false, bool additive = false)
    {
        AssetBrowserModel model = Model;
        if (!model.Paths.Contains(path, StringComparer.Ordinal))
        {
            LastError.Value = $"Asset not found: {path}";
            return false;
        }
        model.Select(path, additive);
        _nameDraft.Value = AssetPath.Name(path);
        LastError.Value = null;
        if (open) OnOpen.Invoke(this, path);
        return true;
    }

    public bool CreateAsset(string path, string content = "")
        => RunMutation(ops => ops.CreateAsset(path, content), selectCreated: true);

    public bool RenameSelected(string newName)
    {
        string selected = RequireSingleSelection();
        return selected.Length > 0 && RunMutation(ops => ops.RenameAsset(selected, newName), selectCreated: true);
    }

    public bool MoveSelected(string folder)
    {
        string selected = RequireSingleSelection();
        return selected.Length > 0 && RunMutation(ops => ops.MoveAsset(selected, folder), selectCreated: true);
    }

    public bool DuplicateSelected()
    {
        string selected = RequireSingleSelection();
        return selected.Length > 0 && RunMutation(ops => ops.DuplicateAsset(selected), selectCreated: true);
    }

    public bool DeleteSelected()
    {
        string[] selected = Model.Selection.ToArray();
        if (selected.Length == 0) { LastError.Value = "Select one or more assets first."; return false; }
        return RunMutation(ops => ops.DeleteAssets(selected), selectCreated: false);
    }

    public bool Import(string folder, IEnumerable<(string Name, string Content)> files)
        => RunMutation(ops => ops.ImportAssets(folder, files), selectCreated: true);

    /// <summary>Drop bridge used by the realized list/grid and by host integration tests.</summary>
    public bool HandleDrop(object payload)
    {
        if (payload is not AssetImportPayload import)
        {
            LastError.Value = "Unsupported asset drop payload.";
            return false;
        }
        return Import(CurrentFolder, import.Files);
    }

    private bool RunMutation(Func<IAssetOperations, AssetMutationResult> action, bool selectCreated)
    {
        IAssetOperations? operations = EffectiveOperations();
        if (operations is null) { LastError.Value = "No asset operations service is configured."; return false; }
        try
        {
            AssetMutationResult result = action(operations);
            bool refreshed = Model.Refresh();
            if (selectCreated)
                Model.SelectMany(result.CreatedPaths.Where(x => Model.Paths.Contains(x, StringComparer.Ordinal)));
            else
                Model.SelectMany([]);
            LastError.Value = !result.Succeeded ? result.FailureMessage : Model.Error.Peek();
            OnMutation.Invoke(this, result);
            return result.Succeeded && refreshed;
        }
        catch (Exception ex)
        {
            LastError.Value = ex.Message;
            return false;
        }
    }

    private string RequireSingleSelection()
    {
        if (Model.Selection.Count == 1) return Selected;
        LastError.Value = Model.Selection.Count == 0
            ? "Select an asset first."
            : "This operation requires a single selected asset.";
        return "";
    }

    public bool Refresh()
    {
        bool success = Model.Refresh();
        LastError.Value = Model.Error.Peek();
        return success;
    }

    private IAssetOperations? EffectiveOperations()
    {
        if (Operations.Get() is { } configured) return configured;
        if (Storage.Get() is not { } storage) return null;
        return _fallbackOperations ??= new AssetOperations(new FileAssetStorage(storage));
    }

    private AssetBrowserModel EnsureModel()
    {
        IAssetOperations operations = EffectiveOperations()
            ?? throw new InvalidOperationException("No asset storage or operations service is configured.");
        if (_model is null || !ReferenceEquals(_modelOperations, operations))
        {
            _modelOperations = operations;
            _model = new AssetBrowserModel(operations);
        }
        return _model;
    }

    /// <summary>Legacy full file tree builder retained for existing stories/tests.</summary>
    public static List<TreeNode> BuildTree(IEnumerable<string> paths)
    {
        var root = new Dir();
        foreach (string p in paths)
        {
            string[] parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Dir cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
                cur = cur.Dirs.TryGetValue(parts[i], out Dir? d) ? d : cur.Dirs[parts[i]] = new Dir();
            if (parts.Length > 0) cur.Files.Add(parts[^1]);
        }
        return ToNodes(root, "");
    }

    private sealed class Dir
    {
        public readonly SortedDictionary<string, Dir> Dirs = new(StringComparer.Ordinal);
        public readonly SortedSet<string> Files = new(StringComparer.Ordinal);
    }

    private static List<TreeNode> ToNodes(Dir dir, string prefix)
    {
        var nodes = new List<TreeNode>();
        foreach ((string name, Dir sub) in dir.Dirs)
        {
            string path = prefix.Length == 0 ? name : $"{prefix}/{name}";
            nodes.Add(new TreeNode(path, name, ToNodes(sub, path)));
        }
        foreach (string f in dir.Files)
        {
            string path = prefix.Length == 0 ? f : $"{prefix}/{f}";
            nodes.Add(new TreeNode(path, f, Tag: path));
        }
        return nodes;
    }

    protected override Widget Build()
    {
        AssetBrowserModel model;
        try { model = EnsureModel(); }
        catch (Exception ex) { LastError.Value = ex.Message; return Text(ex.Message); }

        _ = model.Version.Value;
        if (!_filterInitialized)
        {
            _filterDraft.Value = Filter.Or("");
            model.Filter.Value = _filterDraft.Peek();
            _filterInitialized = true;
        }
        model.Filter.Value = _filterDraft.Value;
        string folder = model.CurrentFolder.Value;
        IReadOnlyList<AssetBrowserItem> items = model.CurrentItems();
        IReadOnlyList<TreeNode> folders = model.FolderTree();
        LastError.Value = model.Error.Peek() ?? LastError.Peek();

        TreeView tree = TreeView(folders, expanded: Expanded.Get()!, selected: folder,
            onSelect: (_, node) => { if (node.Tag is string path) OpenFolder(path); });
        var itemView = new AssetItemsView(items, model.Selection, _viewMode.Value,
            onSelect: (item, additive, toggle) =>
            {
                if (item.IsFolder) { OpenFolder(item.Path); return; }
                model.Select(item.Path, additive, toggle);
                _nameDraft.Value = item.Name;
                if (!additive) OnOpen.Invoke(this, item.Path);
            },
            onContextItems: item => ContextItems(item),
            onDrop: HandleDrop);

        var toolbar = HStack(6)[
            Button(_ => OpenFolder(""), "Assets"),
            Text($"/{folder}", 12),
            Button(_ => SetViewMode(AssetBrowserViewMode.List), "List"),
            Button(_ => SetViewMode(AssetBrowserViewMode.Grid), "Grid"),
            Button(_ => Refresh(), "Refresh")];

        var actions = new List<Widget>
        {
            HStack(6)[TextField(_pathDraft, placeholder: "asset path", width: 170), Button(_ => CreateAsset(_pathDraft.Peek()), "Create")],
            HStack(6)[TextField(_nameDraft, placeholder: "new name", width: 120), Button(_ => RenameSelected(_nameDraft.Peek()), "Rename"), Button(_ => DuplicateSelected(), "Duplicate")],
            HStack(6)[TextField(_folderDraft, placeholder: "target folder", width: 120), Button(_ => MoveSelected(_folderDraft.Peek()), "Move"), Button(_ => DeleteSelected(), "Delete")],
        };
        if (ImportCapability.CanExecute) actions.Add(Button(_ => OnImportRequest.Invoke(this), "Import…"));
        else actions.Add(Muted($"Import: {ImportCapability.Reason}"));
        if (RevealCapability.CanExecute) actions.Add(Button(_ => { if (Selected.Length > 0) OnRevealRequest.Invoke(this, Selected); }, "Reveal"));
        else actions.Add(Muted($"Reveal: {RevealCapability.Reason}"));
        if (LastError.Value is { } error) actions.Add(Text(error));
        var currentFolderPane = new List<Widget> { itemView };
        currentFolderPane.AddRange(actions);

        return VStack(7)[
            TextField(_filterDraft, placeholder: "Filter current folder", width: 260),
            toolbar,
            HStack(10)[
                Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), padding: new Thickness(6), width: 190)[tree],
                VStack(6)[currentFolderPane.ToArray()]]
        ];
    }

    private (string Label, Action Action)[] ContextItems(AssetBrowserItem item)
    {
        if (item.IsFolder) return [("Open Folder", () => OpenFolder(item.Path))];
        if (!Model.Selection.Contains(item.Path)) SelectAsset(item.Path);
        var items = new List<(string Label, Action Action)>
        {
            ("Open", () => OnOpen.Invoke(this, item.Path)),
            ("Duplicate", () => DuplicateSelected()),
            ("Rename", () => RenameSelected(_nameDraft.Peek())),
            ("Move", () => MoveSelected(_folderDraft.Peek())),
            ("Delete", () => DeleteSelected()),
        };
        if (RevealCapability.CanExecute)
            items.Add(("Reveal in File Manager", () => OnRevealRequest.Invoke(this, item.Path)));
        return items.ToArray();
    }
}

/// <summary>Actual current-folder list/grid surface with additive selection, context menu, and import drop target.</summary>
public sealed class AssetItemsView(
    IReadOnlyList<AssetBrowserItem> items,
    IReadOnlySet<string> selection,
    AssetBrowserViewMode mode,
    Action<AssetBrowserItem, bool, bool> onSelect,
    Func<AssetBrowserItem, (string Label, Action Action)[]> onContextItems,
    Func<object, bool> onDrop) : Widget
{
    private const float ListRowHeight = 27f;
    private const float GridCellWidth = 112f;
    private const float GridCellHeight = 70f;
    private float _width;
    private float _height;

    public IReadOnlyList<AssetBrowserItem> Items => items;
    public AssetBrowserViewMode Mode => mode;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _width = float.IsFinite(c.MaxW) ? MathF.Max(220, c.MaxW) : 360;
        int columns = Math.Max(1, (int)(_width / GridCellWidth));
        float contentHeight = mode == AssetBrowserViewMode.List
            ? Math.Max(ListRowHeight, items.Count * ListRowHeight)
            : Math.Max(GridCellHeight, ((items.Count + columns - 1) / columns) * GridCellHeight);
        _height = Math.Min(280, contentHeight);
        Size = c.Constrain(new Size(_width, _height));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode root = CreateRoot(ctx, parent, worldOrigin);
        var background = new Scene2D();
        background.FillRoundedRect(Color2D.White, 0, 0, Size.Width, Size.Height, 5);
        root.Content = background;
        ctx.Effect(() => root.Color = ctx.Theme.Value.Surface);
        ctx.AddHit(root, new Rect(0, 0, Size.Width, Size.Height),
            acceptsDrop: payload => payload is AssetImportPayload,
            onDrop: (payload, _) => onDrop(payload));

        int columns = Math.Max(1, (int)(Size.Width / GridCellWidth));
        for (int i = 0; i < items.Count; i++)
        {
            AssetBrowserItem item = items[i];
            float x = mode == AssetBrowserViewMode.List ? 0 : i % columns * GridCellWidth;
            float y = mode == AssetBrowserViewMode.List ? i * ListRowHeight : i / columns * GridCellHeight;
            float w = mode == AssetBrowserViewMode.List ? Size.Width : GridCellWidth - 4;
            float h = mode == AssetBrowserViewMode.List ? ListRowHeight - 1 : GridCellHeight - 4;
            if (y >= Size.Height) break;

            UiNode cell = ctx.Canvas.AddChild(root);
            cell.Transform = Affine2D.Translate(x, y);
            var scene = new Scene2D();
            scene.FillRoundedRect(Color2D.White, 2, 1, w - 4, h - 2, 4);
            float fs = mode == AssetBrowserViewMode.List ? 12 : 11;
            string prefix = item.IsFolder ? "▸ " : "";
            string label = prefix + item.Name;
            float baseline = mode == AssetBrowserViewMode.List
                ? (h - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs)
                : 22 + ctx.Font.Ascent(fs);
            ctx.Font.AppendText(scene, label, 8, baseline, fs, Color2D.White);
            cell.Content = scene;
            bool selected = selection.Contains(item.Path);
            ctx.Effect(() => cell.Color = selected ? ctx.Theme.Value.Primary : ctx.Theme.Value.Text);
            ctx.AddHit(cell, new Rect(0, 0, w, h),
                onClickPos: e => onSelect(item, e.Ctrl || e.Shift, e.Ctrl),
                onContext: e => ContextMenu.Open(ctx, e.ScreenX, e.ScreenY, onContextItems(item)),
                cursor: CursorKind.Hand);
        }
    }
}
