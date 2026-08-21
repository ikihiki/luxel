using Luxel.Controls;
using Luxel.Editor.Browser;
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

    private readonly BrowserWorkspaceStorage _storage = new(persistence, "luxel-editor-demo-v1");
    private BrowserDemoSeed? _seed;

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
        => session.DiagnosticsService.Add(new("demo-missing-normal-map", EditorDiagnosticSeverity.Warning,
            "Demo fixture", "Optional normal map is intentionally missing.", "Materials/Coin.material.json", 8, 18));
}
