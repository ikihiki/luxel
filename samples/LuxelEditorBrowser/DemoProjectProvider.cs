using System.Numerics;
using Luxel.Controls;
using Luxel.Editor.Browser;
using Luxel.NodeGraph;
using Luxel.Workbench;

namespace LuxelEditorBrowser;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public sealed class DemoProjectProvider(HttpClient http, IBrowserWorkspacePersistence persistence) : IBrowserDemoProjectProvider
{
    private static readonly (string StoragePath, string AssetPath)[] Assets =
    [
        ("luxel.project.json", "demo/luxel-demo.project.json"),
        ("Scenes/Main.scene", "demo/Scenes/Main.scene"),
        ("Scripts/Player.cs", "demo/Scripts/Player.cs"),
        ("Materials/Coin.material.json", "demo/Materials/Coin.material.json"),
        ("Assets/Textures/coin.svg", "demo/Assets/Textures/coin.svg"),
        ("README.md", "demo/README.md"),
    ];

    private static readonly INodeCatalog MaterialCatalog = new NodeCatalog(
        new NodeCatalogEntry("texture", "Texture", (id, position) => Node(id, "texture", "Texture", position, false, true)),
        new NodeCatalogEntry("color", "Color", (id, position) => Node(id, "color", "Color", position, false, true)),
        new NodeCatalogEntry("multiply", "Multiply", (id, position) => Node(id, "multiply", "Multiply", position, true, true)),
        new NodeCatalogEntry("output", "Output", (id, position) => Node(id, "output", "Output", position, true, false)));

    private readonly BrowserWorkspaceStorage _storage = new(persistence, "luxel-editor-demo-v1");
    private BrowserDemoSeed? _seed;

    public string GalleryUrl => "../../gallery/";
    public string StorageDescription => _storage.State.StatusText;
    public IFileStorage Storage => _storage;

    public async Task InitializeAsync()
    {
        await _storage.InitializeAsync();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string storagePath, string assetPath) in Assets)
            files[storagePath] = await http.GetStringAsync(assetPath);
        _seed = new BrowserDemoSeed(files);
        _seed.EnsureSeeded(_storage);
        await _storage.FlushAsync();
    }

    public async Task ResetAsync()
    {
        (_seed ?? throw new InvalidOperationException("Demo seed has not initialized.")).Reset(_storage);
        await _storage.FlushAsync();
    }

    public void ConfigureSession(EditorSession session)
    {
        session.Workspace.RegisterProvider(new MaterialDocumentProvider());
        session.DiagnosticsService.Add(new("demo-missing-normal-map", EditorDiagnosticSeverity.Warning,
            "Demo fixture", "Optional normal map is intentionally missing.", "Materials/Coin.material.json", 39, 31));
    }

    private static GraphNode Node(int id, string kind, string title, Vector2 position, bool input, bool output)
    {
        var ports = new List<NodePort>();
        if (kind == "multiply")
            ports.AddRange([
                new NodePort(0, PortDir.In, "color", "a"),
                new NodePort(1, PortDir.In, "color", "b"),
                new NodePort(2, PortDir.Out, "color", "out")
            ]);
        else
        {
            if (input) ports.Add(new NodePort(0, PortDir.In, "color", "in"));
            if (output) ports.Add(new NodePort(input ? 1 : 0, PortDir.Out, "color", "out"));
        }
        return new GraphNode(id, kind, title, position, ports);
    }

    private sealed class MaterialDocumentProvider : IDocumentProvider
    {
        public string Kind => EditorDocumentProviderIds.NodeGraph;
        public string DisplayName => "Material graph";
        public IEditorDocument CreateNew() => new NodeGraphDocument(
            "Untitled material", NodeGraphDoc.Empty,
            configure: view => view.NodeCatalog = MaterialCatalog,
            kind: EditorDocumentProviderIds.NodeGraph);
    }
}
