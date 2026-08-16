using Luxel.Editor.Gallery;
using System.Numerics;
using System.Text;
using Luxel.Controls;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;
using Luxel.Scripting;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Editor.Gallery.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>
/// Studio shell dogfood (ToDo 29): Workbench の DockHost / CommandRegistry / DocumentStore の上に
/// SceneEditor + Inspector + AssetBrowser + csx TextDocument + PlayerView + Problems を束ねる。
/// シェルはプロジェクトファイルを直接編集し、保存した内容を Player が読み直す。
/// </summary>
[StoryMeta("Examples/Apps/Studio")]
public static class StudioShellStory
{
    private static readonly Lazy<VectorFont> Font = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    [Story]
    public static StoryResult Shell(StoryContext ctx)
    {
        var storage = new MemoryFileStorage();
        SeedProject(storage);

        var ws = new Workspace();
        var store = new DocumentStore(ws, storage);
        var lang = new CsharpCodeLanguage(new ScriptWorkspace(
            [typeof(PlayerEntity).Assembly, typeof(SceneValue).Assembly, typeof(Vector2).Assembly, typeof(object).Assembly],
            ["System", "System.Numerics", "Luxel.Player", "Luxel.SceneEdit"],
            typeof(BehaviourGlobals)));
        ws.RegisterProvider(new CsxDocumentProvider(lang));
        IEditorDocument script = store.Open("csx", "scripts/runner3d.csx");
        TextDocument scriptDoc = (TextDocument)script;

        SceneEditorView sceneEditor = SceneEditorView(
            source: SceneJson.Deserialize(storage.Read("scenes/arena.scene.json")!),
            viewWidth: 420f, viewHeight: 380f);
        SceneInspector inspector = SceneInspector(editor: sceneEditor, schemas: SceneSchemas.BuiltIns(), width: 230f);
        AssetBrowser assets = AssetBrowser(storage: storage,
            expanded: new HashSet<string> { "scenes", "scripts", "assets" },
            onOpen: (_, path) => { if (path.EndsWith(".csx", StringComparison.Ordinal)) store.Open("csx", path); });

        Signal<string> problems = ctx.Signal("studioShellProblems", "問題なし");
        Signal<string> status = ctx.Signal("studioShellStatus", "Ready");
        Signal<bool> paused = ctx.Signal("studioShellPaused", false);
        PlayerGame? game = null;
        MemoryFileSystem? runningFs = null;

        MemoryFileSystem ToFs()
        {
            var fs = new MemoryFileSystem();
            foreach (string path in storage.List())
                fs.Set(path, Encoding.UTF8.GetBytes(storage.Read(path)!));
            return fs;
        }

        void SaveScene()
        {
            storage.Write("scenes/arena.scene.json", SceneJson.Serialize(sceneEditor.Scene.Doc));
            status.Value = "Scene saved";
        }

        void SaveScript()
        {
            store.Save(script);
            runningFs?.Set("scripts/runner3d.csx", Encoding.UTF8.GetBytes(storage.Read("scripts/runner3d.csx")!));
            status.Value = "Script saved";
        }

        void RefreshProblems()
        {
            IReadOnlyList<string> diags = game?.World.Behaviours?.Diagnostics ?? [];
            List<string> all = diags.ToList();
            if (!storage.Exists("assets/cube.glb")) all.Add("AssetRef missing: res://assets/cube.glb");
            problems.Value = all.Count == 0 ? "問題なし" : string.Join("\n", all);
        }

        void Play()
        {
            SaveScene();
            SaveScript();
            runningFs = ToFs();
            game = PlayerLoader.LoadStart(runningFs);
            paused.Value = false;
            status.Value = "Playing: title -> arena";
            RefreshProblems();
        }

        void ReloadScript()
        {
            SaveScript();
            game?.World.Behaviours?.Reload("res://scripts/runner3d.csx");
            RefreshProblems();
            status.Value = problems.Value == "問題なし" ? "Reloaded" : "Reloaded with problems";
        }

        void AddBeacon()
        {
            sceneEditor.ApplyEdit(new AddEntity(SceneEntity.Of(
                SceneCommands.NextEntityId(sceneEditor.Scene.Doc), "Beacon",
                T3(0, 1.1f, -1.6f, 0.35f, 0.35f, 0.35f),
                Mesh("res://assets/cube.glb"))));
            status.Value = "Beacon added";
        }

        var reg = new CommandRegistry();
        reg.Register("file.new", "New Scene", () => status.Value = "New scene mock", menuPath: "File/New", order: 0);
        reg.Register("file.open", "Open Project", () => status.Value = "Open project mock", menuPath: "File/Open", order: 1);
        reg.Register("file.save", "Save", () => { SaveScene(); SaveScript(); }, key: "Ctrl+S", menuPath: "File/Save", order: 2, toolbar: true);
        reg.Register("project.play", "Play", Play, key: "F5", menuPath: "Run/Play", order: 10, toolbar: true);
        reg.Register("project.pause", "Pause", () => { paused.Value = true; status.Value = "Paused"; }, enabled: () => game is not null, menuPath: "Run/Pause", order: 11, toolbar: true);
        reg.Register("project.step", "Step", () => { game?.World.Update(1f / 60f); game?.ApplySceneRequest(); RefreshProblems(); }, enabled: () => game is not null, menuPath: "Run/Step", order: 12, toolbar: true);
        reg.Register("project.stop", "Stop", () => { game = null; status.Value = "Stopped"; }, enabled: () => game is not null, menuPath: "Run/Stop", order: 13, toolbar: true);
        reg.Register("project.ship", "Ship (mock)", () => status.Value = "Ship command mock: samples/StudioShell", menuPath: "File/Ship", order: 20, toolbar: true);
        reg.Register("scene.addBeacon", "Scene: Add Beacon", AddBeacon, menuPath: "Scene/Add Beacon", order: 30);
        reg.Register("scene.save", "Scene: Save", SaveScene, menuPath: "Scene/Save Scene", order: 31);
        reg.Register("script.reload", "Script: Save + Reload", ReloadScript, key: "Ctrl+Enter", menuPath: "Script/Reload", order: 40, toolbar: true);
        reg.Register("problems.next", "Problems: Jump Next", () => status.Value = problems.Peek() == "問題なし" ? "No problems" : "Jumped to first problem", menuPath: "Problems/Next", order: 50);

        var tree = new Signal<DockTree>(DockTree.Single("scene"));
        tree.Value = tree.Value.Dock("inspector", tree.Value.GroupOf("scene")!.Id, DockSide.Right);
        tree.Value = tree.Value.Dock("player", tree.Value.GroupOf("scene")!.Id, DockSide.Bottom);
        tree.Value = tree.Value.Dock("script", tree.Value.GroupOf("scene")!.Id, DockSide.Right);
        tree.Value = tree.Value.Dock("assets", tree.Value.GroupOf("inspector")!.Id, DockSide.Bottom);
        tree.Value = tree.Value.Dock("problems", tree.Value.GroupOf("player")!.Id, DockSide.Bottom);

        DockItem Resolve(string id) => id switch
        {
            "scene" => new DockItem("arena.scene.json", () => sceneEditor),
            "inspector" => new DockItem("Inspector", () => inspector),
            "assets" => new DockItem("Assets", () => assets),
            "script" => new DockItem(script.Title, () => script.CreateView(), script.Dirty),
            "player" => new DockItem("Play View", PlayerView),
            "problems" => new DockItem("Problems", ProblemsView),
            _ => new DockItem(id, () => Muted(id)),
        };

        Widget PlayerView() => Border(background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(8))[
            VStack(6)[
                HStack(6)[Badge("Play View", Intent.Primary),
                          Text($"{status}", 12, color: Bind.From(() => UiTheme.T.TextMuted))],
                Canvas2D(520f, 320f, animate: (s, _) =>
                {
                    if (game is null)
                    {
                        s.FillRect(TilePalette.Pack(18, 23, 31), 0, 0, 520, 320);
                        Font.Value.AppendText(s, "(Play View overlay: stopped)", 170, 164, 13, TilePalette.Pack(150, 158, 174));
                        return;
                    }
                    if (!paused.Peek())
                    {
                        game.World.Update(1f / 60f);
                        game.ApplySceneRequest();
                        RefreshProblems();
                    }
                    game.World.Render(s, 520, 320, Font.Value);
                    s.FillRect(TilePalette.Pack(8, 11, 16, 180), 10, 10, 170, 24);
                    Font.Value.AppendText(s, $"Overlay: {game.ScenePath}", 16, 27, 11, TilePalette.Pack(220, 226, 238));
                })]];

        Widget ProblemsView() => Border(background: Bind.From(() => UiTheme.T.Surface), padding: new Thickness(8))[
            VStack(6)[
                HStack(6)[Badge("Problems", Intent.Primary),
                          Text($"{status}", 12, color: Bind.From(() => UiTheme.T.TextMuted))],
                Text($"{problems}", 12, color: Bind.From(() => problems.Value == "問題なし" ? UiTheme.T.TextMuted : UiTheme.T.Danger))]];

        MenuBar menuBar = MenuBar(reg, contributions: () => ws.Active.Value?.Contributions ?? []);
        Toolbar toolbar = Toolbar(reg, contributions: () => ws.Active.Value?.Contributions ?? []);
        DockHost host = DockHost(tree, Resolve, closeRemoves: false);
        StatusBar statusBar = StatusBar(
            left: [Muted("Studio Shell"), Muted($"{status}")],
            right: [Badge("Ready", Intent.Success)]);
        var palette = new PaletteOpener { OnOpen = c => CommandPalette.Open(c, reg, ws.Active.Value?.Contributions ?? []) };

        Reactive.Effect(() => { _ = script.Dirty.Value; _ = problems.Value; toolbar.Refresh(); });

        ctx.Play("shell", async d =>
        {
            reg.BindShortcuts(d.Host);
            await d.Snap();
            reg.Run("scene.addBeacon");
            reg.Run("file.save");
            await d.Expect(() => storage.Read("scenes/arena.scene.json")!.Contains("Beacon"), "Scene command -> save");
            reg.Run("project.play");
            await d.Step(28);
            await d.Expect(() => game!.World is Player3DWorld, "2D title csx requests 3D arena");
            await d.Expect(() => game!.World.Find("Beacon") is not null, "saved scene is loaded into Player");
            await d.Snap("playing");
            var scriptTab = host.TabCenter("script")!.Value;
            await d.Click(scriptTab.X, scriptTab.Y);
            TextEditorView scriptView = (TextEditorView)host.ViewOf("script")!;
            scriptDoc.Text.Value = scriptDoc.Text.Peek().Replace("1.0f * dt", "BROKEN * dt", StringComparison.Ordinal);
            scriptView.SetSearch("BROKEN");
            reg.Run("script.reload");
            await d.Step(1);
            await d.Expect(() => problems.Peek() != "問題なし", "compile diagnostics appear in Problems");
            await d.Snap("problems");
            scriptDoc.Text.Value = scriptDoc.Text.Peek().Replace("BROKEN * dt", "1.0f * dt", StringComparison.Ordinal);
            scriptView.SetSearch("1.0f");
            reg.Run("script.reload");
            await d.Expect(() => problems.Peek() == "問題なし", "fix + reload clears Problems");
            reg.Run("project.ship");
            await d.Expect(() => status.Peek().Contains("Ship command mock"), "Ship command is routed");
            await d.Snap("fixed");
        });

        menuBar.GridRow(0);
        toolbar.GridRow(1);
        palette.GridRow(1);
        palette.HAlign.SetBase(Align.End);
        host.GridRow(2);
        statusBar.GridRow(3);
        Grid shell = Grid(rows: [GridLength.Px(Luxel.Controls.MenuBar.BarH), GridLength.Px(34),
                                 GridLength.Star(), GridLength.Px(Luxel.Controls.StatusBar.BarH)])[
            menuBar, toolbar, palette, host, statusBar];
        shell.HAlign.SetBase(Align.Stretch);
        shell.VAlign.SetBase(Align.Stretch);
        return shell;
    }

    private sealed class CsxDocumentProvider(ICodeLanguage lang) : IDocumentProvider
    {
        public string Kind => "csx";
        public string DisplayName => "C# Script";

        public IEditorDocument CreateNew()
            => new TextDocument("csx", "script.csx", text =>
            {
                TextEditorView v = TextEditorView(text, editorHeight: 320f, editorWidth: 520f);
                v.Fill = true;
                v.ShowLineNumbers = true;
                v.EditorFont = EditorFaces.Value.Mono;
                v.LanguageService = lang;
                v.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
                v.Providers.Add(new DiagnosticsProvider(lang, () => UiTheme.T));
                v.Providers.Add(new CurrentLineProvider(() => UiTheme.T));
                return v;
            });
    }

    private sealed class PaletteOpener : CompositeControl
    {
        public required Action<UiBuildContext> OnOpen;
        private UiBuildContext? _ctx;

        protected override void OnRealize(UiBuildContext ctx) => _ctx = ctx;

        protected override Widget Build()
            => Button(_ => { if (_ctx is not null) OnOpen(_ctx); }, "Command Palette");
    }

    private static void SeedProject(MemoryFileStorage storage)
    {
        var title = SceneDoc.Of(SceneSpace.TwoD,
            [SceneEntity.Of(1, "START 3D", T2(250, 154), Tint(0.55f, 0.78f, 0.95f), Script("res://scripts/title.csx"))],
            [TileLayer.Of(1, "title", "res://atlas/title.atlas.json", 32, 16, 10)]);
        var cam = SceneSchemas.NewComponent(SceneSchemas.Camera3D)
            .With("target", SceneValue.Of(new Vector3(0, 0.5f, 0)))
            .With("distance", SceneValue.Of(7.5f))
            .With("yaw", SceneValue.Of(0.64f))
            .With("pitch", SceneValue.Of(0.38f));
        var arena = SceneDoc.Of(SceneSpace.ThreeD,
            [
                SceneEntity.Of(1, "Runner", T3(-2.2f, 0.45f, 0), Mesh("res://assets/cube.glb"), Script("res://scripts/runner3d.csx")),
                SceneEntity.Of(2, "Gate", T3(1.7f, 0.7f, 0, 0.4f, 1.4f, 1.6f), Mesh("res://assets/cube.glb")),
                SceneEntity.Of(3, "Camera", cam),
            ]);
        storage.Write("project.luxel", GameProjectJson.Serialize(new GameProject("Studio Shell", "res://scenes/title.scene.json", 520, 320)));
        storage.Write("scenes/title.scene.json", SceneJson.Serialize(title));
        storage.Write("scenes/arena.scene.json", SceneJson.Serialize(arena));
        storage.Write("scripts/title.csx", "Update = (self, world, dt) => { if (world.Time > 0.20f) world.RequestScene(\"res://scenes/arena.scene.json\"); };");
        storage.Write("scripts/runner3d.csx", "Update = (self, world, dt) => { self.Pos3D.X += 1.0f * dt; self.Pos3D.Z = 0.45f * MathF.Sin(world.Time * 3f); };");
        storage.Write("assets/cube.glb", "glTF");
    }

    private static SceneComponent T2(float x, float y)
        => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));

    private static SceneComponent T3(float x, float y, float z, float sx = 1, float sy = 1, float sz = 1)
        => SceneSchemas.NewComponent(SceneSchemas.Transform3D)
            .With("pos", SceneValue.Of(new Vector3(x, y, z)))
            .With("scale", SceneValue.Of(new Vector3(sx, sy, sz)));

    private static SceneComponent Tint(float r, float g, float b)
        => SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(r, g, b, 1))));

    private static SceneComponent Mesh(string p)
        => SceneSchemas.NewComponent(SceneSchemas.Mesh3D).With("asset", SceneValue.Of(p));

    private static SceneComponent Script(string p)
        => SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of(p));
}
