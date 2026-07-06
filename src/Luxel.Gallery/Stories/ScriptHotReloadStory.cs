using System.Reflection;
using Luxel.Controls;
using Luxel.Ecs;
using Luxel.Scripting;
using Luxel.Scripting.Framework;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **面③: Framework の .csx ゲームロジック + hot reload** (タスク 01) の実演。左でスクリプトを編集し
/// Apply すると <see cref="ScriptSystem"/> が安全に差し替え、右の箱の動きが変わる。構文エラーを入れても
/// 旧ロジックが生き続け診断が出る (ゲームが止まらない)。箱はスクリプトの Update を固定 dt で N ステップ
/// 回した位置に描く (Canvas2D = 決定的、golden 安定)。
/// </summary>
public static class ScriptHotReloadStory
{
    /// <summary>スクリプトが動かす箱の状態 (globals 経由でスクリプトから触る)。</summary>
    public sealed class BoxState { public float X; }

    /// <summary>デモ用 globals: <see cref="ScriptGameGlobals"/> (Log + Systems) + 箱。</summary>
    public sealed class BoxGlobals(Action<string>? log) : ScriptGameGlobals(log)
    {
        public BoxState Box { get; } = new();
    }

    // ゲームスクリプト用 ScriptHost (UI 用とは別 globals 型)。初回コンパイルは重いので Lazy で共有。
    private static readonly Lazy<ScriptHost> GameHost = new(() => new ScriptHost(
        references:
        [
            typeof(object).Assembly, typeof(Enumerable).Assembly,
            typeof(World).Assembly, typeof(Friflo.Engine.ECS.Entity).Assembly,
            typeof(GpuDevice).Assembly, typeof(ScriptGameGlobals).Assembly, typeof(BoxGlobals).Assembly,
        ],
        usings: ["System", "Luxel.Ecs", "Luxel.Scripting.Framework"],
        globalsType: typeof(BoxGlobals)));

    private const int Steps = 40;
    private const float StartX = 18f, BoxY = 40f, BoxSize = 22f, CanvasW = 300, CanvasH = 96;
    private const string InitialCode =
        "// Update: 箱を右へ動かす (w = World, dt = 秒)\nSystems(update: (w, dt) => Box.X += 200 * dt)";

    private sealed class HotReloadBlock : CompositeControl
    {
        private readonly Signal<string> _code;
        private readonly Signal<int> _ver = new(0);
        private readonly CodeEditor _editor;
        private readonly BoxGlobals _globals = new(null);
        private readonly MemoryScriptSource _source;
        private readonly ScriptSystem _sys;
        private readonly World _world = new();
        private readonly float _maxW;

        internal Button ApplyButton { get; }
        internal float BoxX { get; private set; }
        internal bool HasError => _sys.HasError;

        public HotReloadBlock(float maxW)
        {
            _maxW = maxW;
            _code = new Signal<string>(InitialCode);
            _editor = CodeEditor(_code, editorHeight: 110f, editorWidth: maxW - 96);
            (_, _, _, _editor.MonoFont) = EditorFaces.Value;
            _editor.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
            _source = new MemoryScriptSource(_code.Value);
            _sys = new ScriptSystem(GameHost.Value, _source, _globals);   // 初回コンパイル
            ApplyButton = Button(_ => Apply(), "Apply");
            Simulate();
        }

        internal void SetCode(string code) => _code.Value = code;

        private void Apply()
        {
            _source.Set(_code.Value);   // Changed → dirty
            _sys.PollReload();          // フレーム先頭相当: 成功時のみ差し替え、失敗時は旧維持
            Simulate();
            _ver.Value++;
        }

        private void Simulate()
        {
            _globals.Box.X = StartX;
            for (int i = 0; i < Steps; i++) _sys.RunUpdate(_world, 1f / 60);
            BoxX = _globals.Box.X;
        }

        protected override Widget Build()
        {
            _ = _ver.Value;   // TrackBuild — Apply 毎に作り直す
            float bx = Math.Clamp(BoxX, StartX, CanvasW - BoxSize - 2);
            uint grid = Color2D.Rgba(70, 74, 96);

            var kids = new List<Widget>
            {
                HStack(8)[
                    _editor,
                    VStack(4)[
                        ApplyButton,
                        Text(_sys.HasError ? "ERR" : "OK", 13, color: _sys.HasError ? Tw.Red600 : Tw.Green600)]],
                Frame(Canvas2D(CanvasW, CanvasH, draw: s =>
                {
                    s.FillRect(Color2D.Rgba(26, 28, 38), 0, 0, CanvasW, CanvasH);
                    s.StrokeLine(grid, 1f, 0, BoxY + BoxSize + 6, CanvasW, BoxY + BoxSize + 6);
                    s.FillRoundedRect(Color2D.Rgba(90, 180, 250), bx, BoxY, BoxSize, BoxSize, 4);
                })),
            };
            if (_sys.HasError)
            {
                string msg = _sys.RuntimeException?.Message
                    ?? _sys.LastResult?.Diagnostics.FirstOrDefault(d => d.IsError)?.Message
                    ?? "error";
                kids.Add(Text($"診断: {msg} — 旧ロジックで継続", 11, color: Tw.Red600));
            }
            return VStack(8)[kids.ToArray()];
        }

        public override string? DebugDetail => $"hot reload (box x={BoxX:0})";
    }

    [Story("Demos/Scripting/HotReload", Height = 420, Order = 2035)]
    public static Widget HotReload(StoryContext ctx)
    {
        var block = new HotReloadBlock(460);

        ctx.Play(async d =>
        {
            await d.Snap();   // 初期スクリプト (200px/s で動いた箱の位置)
            block.SetCode("Systems(update: (w, dt) => Box.X += 380 * dt)");
            await d.Click(block.ApplyButton);
            await d.Step(3);
            await d.Snap("faster");   // reload で箱がさらに右へ
            await d.Expect(() => !block.HasError && block.BoxX > 200, "reload で速度が上がり箱が右へ");

            block.SetCode("Systems(update: (w, dt) => Box.X +=)");   // 構文エラー
            await d.Click(block.ApplyButton);
            await d.Step(3);
            await d.Snap("error");   // 旧ロジックのまま + 赤字診断
            await d.Expect(() => block.HasError, "構文エラーでも旧が生き診断が出る");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("hot reload — .csx ゲームロジック"),
                Muted("スクリプトを編集 → Apply で安全に差し替え。構文エラーでも旧ロジックが生き続けます。"),
                block]];
    }
}
