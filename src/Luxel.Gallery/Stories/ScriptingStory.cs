using Luxel.Controls;
using Luxel.Scripting;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// C# スクリプト (P1) — docs に埋め込める ```csx ライブブロックの実演。
/// エディタで編集 → Run = Roslyn コンパイル (ソースキャッシュ) → 最後の式の Widget をその場に実体化。
/// コンパイルエラーは行番号付きでインライン表示、実行時例外もスクリプト行へマップされる。
/// 初期状態は未実行 (コード表示のみ) — snap/E2E 決定的。
/// </summary>
public static class ScriptingStory
{
    /// <summary>スクリプトから裸で見える API 面 (csx の globals)。</summary>
    public sealed class CsxGlobals
    {
        public required StoryContext Ctx { get; init; }
        /// <summary>Log パネルへ (デバッグの第一手段)。</summary>
        public void Log(string message) => Ctx.Log(message);
    }

    // ScriptHost / ScriptWorkspace で共有する参照・using (スクリプトから見える API 面)
    private static readonly System.Reflection.Assembly[] Refs =
    [
        typeof(object).Assembly, typeof(Enumerable).Assembly,
        typeof(GpuDevice).Assembly, typeof(Scene2D).Assembly,
        typeof(Widget).Assembly, typeof(Kit).Assembly,
        typeof(Luxel.UI.Tailwind.Tw).Assembly, typeof(CsxGlobals).Assembly,
    ];
    private static readonly string[] Usings =
    [
        "System", "System.Linq", "System.Collections.Generic",
        "Luxel.UI", "Luxel.TwoD", "Luxel.Controls",
        "Luxel.Controls.Kit",       // 静的 import — Button/VStack/… が裸で書ける
        "Luxel.UI.Tailwind",
    ];

    /// <summary>プロセス共有の ScriptHost (初回コンパイルは 1-2 秒)。</summary>
    private static readonly Lazy<ScriptHost> Host = new(() => new ScriptHost(Refs, Usings, typeof(CsxGlobals)));
    /// <summary>プロセス共有の言語サービス (補完/ホバー — in-proc Roslyn)。</summary>
    private static readonly Lazy<ScriptWorkspace> Ws = new(() => new ScriptWorkspace(Refs, Usings));

    /// <summary>csx プレイグラウンド: コードエディタ + Run + インライン診断 + 出力 (返した Widget)。</summary>
    private sealed class CsxBlock : CompositeControl, IDisposable
    {
        private readonly StoryContext _ctx;
        private readonly Signal<string> _code;
        private readonly Signal<string> _status = new("");
        private readonly Signal<int> _ver = new(0);   // 出力/診断の構造変化 → TrackBuild が Rebuild
        private readonly TextArea _editor;
        private readonly float _maxW;

        private string _diags = "";       // 構造状態 (Rebuild で反映)
        private Widget? _output;
        private IReadOnlyList<Luxel.Scripting.CompletionItem> _completions = [];   // 補完候補 (構造状態)
        private string _hover = "";       // 選択候補のホバー (型/シグネチャ)

        /// <summary>コードエディタ (play からクリックしてフォーカス/キャレット移動する)。</summary>
        internal TextArea Editor => _editor;
        /// <summary>Run ボタン (play からクリックするために公開)。</summary>
        internal Button RunButton { get; }
        /// <summary>補完ボタン (キャレット位置の候補を出す)。</summary>
        internal Button CompleteButton { get; }
        /// <summary>直近 Run が成功して Widget を出したか (play の Expect 用)。</summary>
        internal bool LastRunOk { get; private set; }
        /// <summary>直近の補完候補数 (play の Expect 用)。</summary>
        internal int CompletionCount => _completions.Count;

        public CsxBlock(string initialCode, float maxWidth, StoryContext ctx)
        {
            _ctx = ctx;
            _maxW = MathF.Max(240, maxWidth);
            _code = new Signal<string>(initialCode);
            _editor = TextArea(_code, height: 150f, width: _maxW - 96);
            _editor.Fonts = StoryKit.JpFallback.Value;
            RunButton = Button(_ => Run(), "Run");
            CompleteButton = Button(_ => Complete(), "補完", variant: Variant.Ghost, fontSize: 12f);
        }

        /// <summary>コードを差し替える (play からエラー例の検証に使う)。</summary>
        internal void SetCode(string code) => _code.Value = code;

        /// <summary>キャレット位置で補完候補を取る (P2 言語サービス)。上位を候補リストに出す。</summary>
        private void Complete()
        {
            _completions = Ws.Value.Complete(_code.Value, _editor.CaretOffset)
                .Take(12).ToList();
            _hover = "";
            _ver.Value++;
        }

        /// <summary>候補を確定 (キャレットへ挿入) + その型情報をホバー表示。</summary>
        private void Pick(Luxel.Scripting.CompletionItem item)
        {
            _editor.InsertAtCaret(item.InsertText);
            _hover = Ws.Value.Hover(_code.Value, _editor.CaretOffset)?.Text ?? $"{item.Kind}: {item.Label}";
            _completions = [];
            _ver.Value++;
        }

        private void Run()
        {
            ScriptResult r = Host.Value.Run(_code.Value, new CsxGlobals { Ctx = _ctx });
            LastRunOk = false;
            if (!r.Success)
            {
                _output = null;
                _diags = r.Exception is not null
                    ? $"実行時例外{(r.ExceptionLine is int l ? $" (行 {l})" : "")}: {r.Exception.Message}"
                    : string.Join("\n", r.Diagnostics.Where(d => d.IsError)
                        .Select(d => $"行 {d.Line}:{d.Column} {d.Message}"));
                _status.Value = "✗";
            }
            else
            {
                (_output as IDisposable)?.Dispose();
                _output = r.ReturnValue as Widget;
                _diags = _output is null && r.ReturnValue is not null
                    ? $"戻り値が Widget ではありません: {r.ReturnValue.GetType().Name}" : "";
                _status.Value = _output is not null ? "✓" : "✓ (出力なし)";
                LastRunOk = _output is not null;
            }
            _ver.Value++;   // 構造変化 — Rebuild
        }

        protected override Widget Build()
        {
            _ = _ver.Value;   // TrackBuild — Run/補完 毎に作り直す
            Func<string> status = () => _status.Value;
            var kids = new List<Widget>
            {
                HStack(6)[
                    _editor,
                    VStack(4)[
                        RunButton,
                        CompleteButton,
                        Text(status, 12, color: Bind.From(() => UiTheme.T.TextMuted))]],
            };
            // 補完候補リスト (キャレット位置の候補 — クリックで挿入)
            if (_completions.Count > 0)
            {
                var rows = new List<Widget>();
                foreach (Luxel.Scripting.CompletionItem it in _completions)
                {
                    Luxel.Scripting.CompletionItem captured = it;
                    rows.Add(Button(_ => Pick(captured), $"{it.Label}  ·  {it.Kind}",
                        variant: Variant.Ghost, fontSize: 12f, hAlign: Align.Stretch));
                }
                kids.Add(Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 6,
                                padding: new Thickness(4), width: _maxW)[VStack(1)[rows.ToArray()]]);
            }
            if (_hover.Length > 0)
                kids.Add(Text(_hover, 11, color: Bind.From(() => UiTheme.T.TextMuted)));
            if (_diags.Length > 0)
                kids.Add(Text(_diags, 11, color: Tw.Red600));
            if (_output is not null)
                kids.Add(Border(background: Bind.From(() => UiTheme.T.SurfaceAlt),
                                rounded: 6, padding: new Thickness(10), width: _maxW)[_output]);
            return VStack(6)[kids.ToArray()];
        }

        public override string? DebugDetail => $"csx ({_code.Value.Length} 文字)";

        public void Dispose() => (_output as IDisposable)?.Dispose();
    }

    [Story("Demos/Scripting/LiveCsx", Height = 520, Order = 2032)]
    public static Widget LiveCsx(StoryContext ctx)
    {
        var block = new CsxBlock(
            "// 最後の式の Widget が下に実体化される。Log(...) は Log タブへ\n" +
            "var names = new[] { \"Luxel\", \"Roslyn\", \"csx\" };\n" +
            "VStack(6)[\n" +
            "    Label($\"こんにちは {string.Join(\" + \", names)}\"),\n" +
            "    Button(_ => Log(\"クリックされた!\"), \"Click me\")]",
            maxWidth: 440, ctx);

        ctx.Play("run", async d =>
        {
            await d.Snap();                          // 未実行 (コード表示のみ)
            await d.Click(block.RunButton);          // コンパイル + 実行 (初回は数秒)
            await d.Step(4);
            await d.Snap("ran");                     // 返した Widget が出た絵
            await d.Expect(() => block.LastRunOk, "スクリプトが Widget を返して実体化される");
        });
        ctx.Play("error", async d =>
        {
            block.SetCode("var x = 1 +\nOops(x)");   // 構文エラー + 未定義シンボル
            await d.Click(block.RunButton);
            await d.Step(4);
            await d.Snap("diag");                    // 行番号付き診断が赤字で出た絵
            await d.Expect(() => !block.LastRunOk, "エラー時は Widget を出さず診断を表示");
        });
        ctx.Play("complete", async d =>
        {
            // 文字列メンバーの補完 — キャレットを "hi". の直後に置いて「補完」
            block.SetCode("\"hi\".");
            await d.Step(1);
            await d.Click(block.Editor);             // クリックでフォーカス + キャレット末尾
            await d.Click(block.CompleteButton);
            await d.Step(2);
            await d.Snap("list");                    // 候補リストが出た絵
            await d.Expect(() => block.CompletionCount > 0, "キャレット位置の補完候補が出る");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("C# ライブスクリプト (csx)"),
                Muted("エディタで編集して Run — Roslyn がコンパイルし、最後の式の Widget を下に実体化。エラーは行番号付き。"),
                block]];
    }
}
