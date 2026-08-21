using Luxel.SceneEdit;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

/// <summary>Creates the production Editor document/pane/command composition used by every product host.</summary>
public static class EditorProductSessionFactory
{
    public const string ProjectFile = "luxel.project.json";
    public const string SceneFile = "Scenes/Main.scene";
    public const string ScriptFile = "Scripts/Player.cs";
    public const string ReadmeFile = "README.md";

    public static EditorSession Create(IFileStorage files, IEditorSettingsStore? settings = null,
        IEditorSavePathPicker? savePaths = null, IEditorAssetHost? assetHost = null,
        IHostCapabilities? capabilities = null, IBuildService? builds = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        SeedIfEmpty(files);

        var scene = new SceneDocument("Main.scene", SceneJson.Deserialize(files.Read(SceneFile)!),
            doc => EditorKit.SceneEditorView(source: doc));
        var script = new TextDocument(EditorDocumentProviderIds.Text, "Player.cs", text =>
        {
            TextEditorView view = Kit.TextEditorView(text, editorHeight: 480, editorWidth: 720);
            view.Fill = true;
            view.ShowLineNumbers = true;
            return view;
        }, files.Read(ScriptFile) ?? "");
        var readme = new TextDocument(EditorDocumentProviderIds.Text, "README.md", text =>
        {
            TextEditorView view = Kit.TextEditorView(text, editorHeight: 420, editorWidth: 680);
            view.Fill = true;
            view.ShowLineNumbers = true;
            return view;
        }, files.Read(ReadmeFile) ?? "");

        var documents = new Dictionary<string, IEditorDocument>(StringComparer.Ordinal)
        {
            ["scene"] = scene,
            ["script"] = script,
            ["readme"] = readme,
        };
        DockTree layout = DockTree.Single("scene", "script", "readme");
        layout = layout.Dock(EditorPaneIds.Hierarchy, layout.Groups.First().Id, DockSide.Left);
        layout = layout.Dock(EditorPaneIds.Inspector, layout.Groups.First().Id, DockSide.Right);
        layout = layout.Dock(EditorPaneIds.Assets, layout.GroupOf(EditorPaneIds.Hierarchy)!.Id, DockSide.Bottom);
        layout = layout.Dock(EditorPaneIds.Output, layout.Groups.First().Id, DockSide.Bottom);

        var session = new EditorSession(files, documents, layout, settings, savePaths, assetHost, capabilities);
        session.Documents.SaveAs(scene, SceneFile);
        session.Documents.SaveAs(script, ScriptFile);
        session.Documents.SaveAs(readme, ReadmeFile);
        session.Commands.Register(EditorCommandIds.Build, "Build", () =>
        {
            if (builds is null || !builds.IsAvailable)
            {
                session.StatusText.Value = "Build is unsupported on this host.";
                return;
            }
            try { builds.Build(); session.StatusText.Value = "Build completed"; }
            catch (Exception error) { session.ReportFailure("build", error); }
        }, enabled: () => builds?.IsAvailable == true, key: "Ctrl+Shift+B", menuPath: "Run/Build", toolbar: true);
        return session;
    }

    public static void SeedIfEmpty(IFileStorage files)
    {
        if (!files.Exists(ProjectFile))
            files.Write(ProjectFile, "{\n  \"name\": \"Luxel Editor Project\",\n  \"startScene\": \"Scenes/Main.scene\"\n}");
        if (!files.Exists(SceneFile))
        {
            SceneDoc scene = SceneDoc.Of(SceneSpace.TwoD,
            [
                SceneEntity.Of(1, "Camera"),
                SceneEntity.Of(2, "Player"),
            ]);
            files.Write(SceneFile, SceneJson.Serialize(scene));
        }
        if (!files.Exists(ScriptFile))
            files.Write(ScriptFile, "using System.Numerics;\n\npublic sealed class Player\n{\n    public Vector2 Position { get; set; }\n}\n");
        if (!files.Exists(ReadmeFile))
            files.Write(ReadmeFile, "# Luxel Editor\n\nThis project is stored by the active Editor host.\n");
    }
}
