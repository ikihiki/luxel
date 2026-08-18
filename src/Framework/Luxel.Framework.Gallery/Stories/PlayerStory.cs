using Luxel.Framework.Gallery;
using System.Numerics;
using System.Text;
using Luxel.Controls;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;
using Luxel.Scripting;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Controls.EditorKit;
using static Luxel.Gallery.Stories.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>Luxel.Player — データ駆動ゲームランタイム (ADR-0015 / ToDo 27 GE-3)。エディタが吐く
/// プロジェクトフォルダ (project.luxel + scenes) を VFS から読み、SceneCompiler が world へ一方向に
/// 構築して固定 dt で駆動する。見た目はエディタと同じプレースホルダ (TilePalette 共有) —
/// SceneEditorView で編集した絵がそのまま動く。csx ビヘイビアは S2、exe は S3。</summary>
[StoryMeta("Examples/Apps/Player")]
public static class PlayerStory
{
    private static readonly Lazy<VectorFont> Font = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    // fixture プロジェクト: エディタ形式そのままの JSON を MemoryFileSystem に置く
    private static IVirtualFileSystem FixtureProject()
    {
        SceneComponent T2(float x, float y)
            => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));
        SceneComponent Tint(float r, float g, float b)
            => SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(r, g, b, 1))));

        var cells = new int[15 * 9];
        for (int x = 0; x < 15; x++) cells[8 * 15 + x] = 1;                    // 地面 = 草
        cells[6 * 15 + 9] = 3; cells[6 * 15 + 10] = 3; cells[6 * 15 + 11] = 3; // 浮き足場 = 石
        SceneComponent Script(string path)
            => SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of(path));
        var scene = SceneDoc.Of(SceneSpace.TwoD,
            [
                SceneEntity.Of(1, "Player", T2(110, 216), Tint(0.45f, 0.66f, 0.95f), Script("res://scripts/walk.csx")),
                SceneEntity.Of(2, "Coin", T2(330, 150), Tint(0.9f, 0.78f, 0.35f), Script("res://scripts/bob.csx")),
            ],
            [TileLayer.Of(1, "ground", "res://atlas/tiles.atlas.json", 32, 15, 9, cells)]);

        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("Player デモ", "res://scenes/main.scene.json", 480, 288)));
        Put("scenes/main.scene.json", SceneJson.Serialize(scene));
        // csx ビヘイビア (ADR-0018): スクリプトは Update を設定するだけ・状態はコンポーネント側
        Put("scripts/walk.csx", "Update = (self, world, dt) => { self.Pos.X += 60f * dt; };");
        Put("scripts/bob.csx", "Update = (self, world, dt) => { self.Pos.Y = 150f + 24f * MathF.Sin(world.Time * 4f); };");
        return fs;
    }

    private static IVirtualFileSystem FixtureProject3D()
    {
        SceneComponent T3(float x, float y, float z, float sx = 1, float sy = 1, float sz = 1)
            => SceneSchemas.NewComponent(SceneSchemas.Transform3D)
                .With("pos", SceneValue.Of(new Vector3(x, y, z)))
                .With("scale", SceneValue.Of(new Vector3(sx, sy, sz)));
        SceneComponent Mesh(string path)
            => SceneSchemas.NewComponent(SceneSchemas.Mesh3D).With("asset", SceneValue.Of(path));
        SceneComponent Script(string path)
            => SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of(path));
        var cam = SceneSchemas.NewComponent(SceneSchemas.Camera3D)
            .With("target", SceneValue.Of(new Vector3(0.2f, 0.6f, 0.2f)))
            .With("distance", SceneValue.Of(7.2f))
            .With("yaw", SceneValue.Of(0.68f))
            .With("pitch", SceneValue.Of(0.4f));
        var scene = SceneDoc.Of(SceneSpace.ThreeD,
            [
                SceneEntity.Of(1, "Walker", T3(-1.5f, 0.5f, 0, 1, 1, 1), Mesh("res://assets/cube.glb"), Script("res://scripts/walk3d.csx")),
                SceneEntity.Of(2, "Crate", T3(1.2f, 0.5f, 0.4f, 1.2f, 1, 1.2f), Mesh("res://assets/cube.glb")),
                SceneEntity.Of(3, "Camera", cam),
            ]);

        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("Player 3D デモ", "res://scenes/main.scene.json", 520, 320)));
        Put("scenes/main.scene.json", SceneJson.Serialize(scene));
        Put("scripts/walk3d.csx", "Update = (self, world, dt) => { self.Pos3D.X += 0.8f * dt; self.Pos3D.Z = 0.35f * MathF.Sin(world.Time * 2f); };");
        fs.Set("assets/cube.glb", [0x67, 0x6c, 0x54, 0x46]); // glTF binary magic; loader v1 は参照検証のみ
        return fs;
    }

    [Story]
    public static StoryResult Basic(StoryContext ctx)
    {
        PlayerGame game = PlayerLoader.LoadStart(FixtureProject());
        Player2DWorld world = game.World2D;
        float w = game.Project.WindowWidth, h = game.Project.WindowHeight;

        // Canvas2D の animate = Tick 累積 (wall-clock 禁止) — snap の固定ステップで決定的
        Canvas2D view = Canvas2D(w, h, animate: (s, _) => world.Render(s, w, h, Font.Value));

        ctx.Play("run", async d =>
        {
            await d.Snap();                              // 読み込んだプロジェクトの初期状態 (タイル + tint 付き箱)
            await d.Expect(() => world.Behaviours!.Diagnostics.Count == 0, "csx がコンパイルできている");
            for (int i = 0; i < 30; i++) world.Update(1f / 60f);   // csx ビヘイビアがエンティティを動かす
            await d.Step(1);                             // animate が再エンコード
            await d.Expect(() => MathF.Abs(world.Find("Player")!.Pos.X - 140f) < 0.01f, "walk.csx が 60px/s で移動");
            await d.Expect(() => world.Find("Coin")!.Pos.Y != 150f, "bob.csx が上下動");
            await d.Expect(() => world.TileAt(1, 0, 8) == 1, "タイルはランタイムへ素通し");
            await d.Snap("stepped");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("Luxel.Player — データ駆動ランタイム"),
                Muted("project.luxel + scenes/*.scene.json (エディタ形式そのまま) を VFS から読み、SceneCompiler が 2D world へ一方向構築。固定 dt で駆動し、見た目はエディタと同じプレースホルダ (TilePalette 共有)。"),
                view]];
    }

    [Story]
    public static StoryResult ThreeD(StoryContext ctx)
    {
        PlayerGame game = PlayerLoader.LoadStart(FixtureProject3D());
        Player3DWorld world = game.World3D;
        float w = game.Project.WindowWidth, h = game.Project.WindowHeight;
        Canvas2D view = Canvas2D(w, h, animate: (s, _) => world.Render(s, w, h, Font.Value));

        ctx.Play("run3d", async d =>
        {
            await d.Snap();
            await d.Expect(() => world.MissingAssets.Count == 0, "mesh3d の glb AssetRef は VFS 上で解決できる");
            await d.Expect(() => world.RayCast(new Vector3(-1.5f, 0.5f, -6f), Vector3.UnitZ, out Player3DHit hit) && hit.Entity.Id == 1, "AABB raycast が Walker に当たる");
            float x0 = world.Entity(1).Pos3D.X;
            for (int i = 0; i < 30; i++) world.Update(1f / 60f);
            await d.Expect(() => world.Entity(1).Pos3D.X > x0 + 0.35f, "3D csx が Pos3D を更新");
            world.Camera.Orbit(0.35f, -0.08f);
            await d.Step(1);
            await d.Snap("orbit");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("Luxel.Player — 3D バックエンド"),
                Muted("SceneCompiler が transform3d / mesh3d(glb AssetRef) / camera3d を Player3DWorld へ構築。csx は Pos3D を更新し、AABB query/raycast と Scene2D 投影の実窓描画で確認する。"),
                view]];
    }

    // プレイインエディタ (ADR-0017): ▶ = 編集中 SceneDoc から都度コンパイル、⏹ = プレイ world を破棄。
    // gizmo/DevStats オーバーレイの統合は Studio シェル (GE-7 dogfood) で。
    [Story]
    public static StoryResult PlayInEditor(StoryContext ctx)
    {
        IVirtualFileSystem fs = FixtureProject();
        SceneDoc doc = PlayerLoader.LoadScene(fs, "res://scenes/main.scene.json");
        SceneEditorView ed = SceneEditorView(source: doc, viewWidth: 300f, viewHeight: 300f);

        // プレイ状態 (world = null なら停止中)
        Player2DWorld? world = null;
        bool paused = false;
        int pendingSteps = 0;
        Signal<string> status = ctx.Signal("playState", "停止中 (編集を ▶ で実行)");

        void Play()
        {
            world = SceneCompiler.Compile2D(ed.Scene.Doc);   // 編集中の最新 doc から別インスタンスを構築
            var behaviours = new PlayerBehaviours(fs);
            behaviours.LoadAll(world);
            world.Behaviours = behaviours;
            paused = false;
            status.Value = "実行中 (プレイ状態は捨てられる)";
        }
        void Stop() { world = null; status.Value = "停止中 (編集を ▶ で実行)"; }

        Canvas2D view = Canvas2D(300f, 300f, animate: (s, _) =>
        {
            if (world is null)
            {
                s.FillRect(TilePalette.Pack(22, 26, 34), 0, 0, 300, 300);
                Font.Value.AppendText(s, "(stopped)", 110, 150, 13, TilePalette.Pack(120, 126, 140));
                return;
            }
            if (!paused) world.Update(1f / 60f);
            else if (pendingSteps > 0) { world.Update(1f / 60f); pendingSteps--; }
            world.Render(s, 300, 300, Font.Value);
        });

        Button play = Button(_ => Play(), "▶ 実行"), pause = Button(_ => paused = !paused, "一時停止"),
               step = Button(_ => pendingSteps++, "ステップ"), stop = Button(_ => Stop(), "停止");

        ctx.Play("play", async d =>
        {
            await d.Snap();                              // 左 = エディタ / 右 = 停止中
            float editorX = ed.EntityPos2D(1).X;
            await d.Click(play);
            await d.Step(30);                            // 30 フレーム実行 (walk.csx が動かす)
            await d.Expect(() => world!.Entity(1).Pos.X > editorX + 20f, "プレイ world は csx で動く");
            await d.Expect(() => ed.EntityPos2D(1).X == editorX, "編集 doc は不変 (別インスタンス)");
            await d.Click(pause);
            float atPause = world!.Entity(1).Pos.X;
            await d.Step(5);
            await d.Expect(() => world!.Entity(1).Pos.X == atPause, "⏸ で止まる");
            await d.Click(step);
            await d.Step(2);
            await d.Expect(() => world!.Entity(1).Pos.X > atPause, "⏭ で 1 ステップ");
            await d.Snap("playing");
            await d.Click(stop);
            await d.Step(1);
            await d.Expect(() => world is null, "⏹ でプレイ world を破棄");
            await d.Snap("stopped");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("プレイインエディタ (ADR-0017)"),
                Muted("▶ = 編集中の SceneDoc から SceneCompiler で別インスタンスを都度構築 (csx 込み)。⏸/⏭ = 固定 dt の一時停止/ステップ。⏹ = プレイ world を破棄 — 編集状態は汚染されない。"),
                HStack(6)[play, pause, step, stop, Text($"{status}", 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8, 5, 0, 0))],
                HStack(12)[ed, view]]];
    }

    // スクリプト編集統合 (GE-5): csx を TextEditorView (診断波線) で編集 → 保存で
    // PlayerBehaviours.Reload (ホットリロード)。壊れた保存は旧挙動継続 + Problems 表示 (ADR-0018 の失敗契約の UI)。
    [Story]
    public static StoryResult ScriptEditor(StoryContext ctx)
    {
        IVirtualFileSystem fs = FixtureProject();
        PlayerGame game = PlayerLoader.LoadStart(fs);
        Player2DWorld world = game.World2D;
        const string scriptRes = "res://scripts/walk.csx";
        const string scriptFile = "scripts/walk.csx";

        // 言語サービス: 実行時と同じ references/usings + BehaviourGlobals を globals に —
        // エディタがランタイムと同じ言語風景を見る (Update が偽エラーにならない)
        var ws = new ScriptWorkspace(
            [typeof(PlayerEntity).Assembly, typeof(SceneValue).Assembly, typeof(Vector2).Assembly, typeof(object).Assembly],
            ["System", "System.Numerics", "Luxel.Player", "Luxel.SceneEdit"],
            typeof(BehaviourGlobals));
        var lang = new CsharpCodeLanguage(ws);

        Signal<string> code = ctx.Signal("code",
            Encoding.UTF8.GetString(fs.ReadAsync(scriptFile, CancellationToken.None).GetAwaiter().GetResult()));
        TextEditorView ed = TextEditorView(code, editorHeight: 130f, editorWidth: 448f);
        ed.ShowLineNumbers = true;
        (_, _, _, VectorFont? mono) = EditorFaces.Value;
        ed.EditorFont = mono;
        ed.LanguageService = lang;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        ed.Providers.Add(new DiagnosticsProvider(lang, () => UiTheme.T));

        Signal<string> problems = ctx.Signal("problems", "問題なし");
        Button save = Button(_ =>
        {
            ((MemoryFileSystem)fs).Set(scriptFile, Encoding.UTF8.GetBytes(ed.Text));
            world.Behaviours!.Reload(scriptRes);
            IReadOnlyList<string> diags = world.Behaviours.Diagnostics;
            problems.Value = diags.Count == 0 ? "問題なし" : string.Join(" / ", diags);
        }, "保存 (リロード)");

        Canvas2D view = Canvas2D(448f, 288f, animate: (s, _) =>
        {
            world.Update(1f / 60f);
            world.Render(s, 448, 288, Font.Value);
        });

        ctx.Play("hotreload", async d =>
        {
            await d.Snap();                                        // エディタ + 走行中の world
            float x0 = world.Entity(1).Pos.X;
            await d.Step(30);
            await d.Expect(() => world.Entity(1).Pos.X - x0 is > 25f and < 35f, "初期 60px/s");
            ed.SetSearch("60f"); ed.ReplaceAll("240f");            // エディタで速度を書き換え
            await d.Click(save);                                   // 保存 → ホットリロード
            float x1 = world.Entity(1).Pos.X;
            await d.Step(30);
            await d.Expect(() => world.Entity(1).Pos.X - x1 > 100f, "リロード後 240px/s");
            ed.SetSearch("dt) =>"); ed.ReplaceAll("dt) =>>");      // 壊す
            await d.Click(save);
            await d.Step(1);
            await d.Expect(() => problems.Peek() != "問題なし", "壊れた保存は Problems に診断");
            float x2 = world.Entity(1).Pos.X;
            await d.Step(30);
            await d.Expect(() => world.Entity(1).Pos.X - x2 > 100f, "旧挙動 (240px/s) を維持 — 落ちない");
            await d.Snap("problems");                              // 赤波線 + Problems 表示
            ed.SetSearch("dt) =>>"); ed.ReplaceAll("dt) =>");      // 直す
            await d.Click(save);
            await d.Expect(() => problems.Peek() == "問題なし", "直して保存 → 診断が消える");
            await d.Snap("fixed");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("スクリプト編集 + ホットリロード (GE-5)"),
                Muted("csx を TextEditorView (診断波線 + 補完) で編集し、保存で PlayerBehaviours.Reload。壊れた保存は旧挙動を維持して Problems に診断 (ADR-0018 の失敗契約)。"),
                ed,
                HStack(8)[save, Text($"{problems}", 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 5, 0, 0))],
                view]];
    }
}
