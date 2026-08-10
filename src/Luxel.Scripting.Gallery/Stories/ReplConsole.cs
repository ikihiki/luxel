using Luxel.Controls;
using Luxel.Scripting;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>継続 REPL コンソール — 行を投入すると前の行で宣言した変数が次に見える。DevTools の
/// スクリプトコンソール相当。1 行 = 1 Submit で、状態は <see cref="ScriptSession"/> が保つ。
/// <para>ストーリー (Examples/Scripting/Repl) と Gallery 下ペインの「Console」タブの両方が使う共有部品。
/// <see cref="ScriptGlobals"/> を差し替えて Log の宛先 (現在のストーリー) を注入する。</para></summary>
public sealed class ReplConsole : CompositeControl
{
    private readonly Signal<string> _input;
    private readonly Signal<int> _ver = new(0);
    private readonly TextEditorView _editor;
    private readonly ScriptSession _session;
    private readonly List<(string In, string Out, bool Ok)> _history = new();
    private readonly float _maxW;

    internal Button SubmitButton { get; }
    internal int HistoryCount => _history.Count;
    internal string LastOutput => _history.Count > 0 ? _history[^1].Out : "";

    /// <param name="globals">スクリプトから裸で見える API 面 (Log の宛先など)。セッションはこの
    /// インスタンスで開かれ、Gallery 起動中ずっと生きる (タブ/ストーリー切替で失わない)。</param>
    /// <param name="initial">エディタの初期テキスト。</param>
    public ReplConsole(float maxWidth, ScriptHost host, ScriptGlobals globals, string initial = "var greeting = \"Luxel\";")
    {
        _maxW = MathF.Max(240, maxWidth);
        _input = new Signal<string>(initial);
        _editor = TextEditorView(_input, editorHeight: 40f, editorWidth: _maxW - 96);
        _editor.Fonts = StoryKit.JpFallback.Value;
        _editor.EditorFont = StoryKit.EditorFaces.Value.Mono;
        _session = host.OpenSession(globals);
        SubmitButton = Button(_ => Submit(), "▷");
    }

    internal void SetInput(string s) => _input.Value = s;

    private void Submit()
    {
        string code = _input.Value;
        ScriptResult r = _session.Submit(code);
        string outp = r.Success
            ? (r.ReturnValue?.ToString() ?? "(void)")
            : r.Exception is not null ? $"例外: {r.Exception.Message}"
            : string.Join("; ", r.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        _history.Add((code, outp, r.Success));
        _input.Value = "";
        _ver.Value++;
    }

    protected override Widget Build()
    {
        _ = _ver.Value;
        var rows = new List<Widget>();
        foreach ((string inp, string outp, bool ok) in _history)
        {
            rows.Add(Text($"› {inp}", 12, color: Bind.From(() => UiTheme.T.TextMuted)));
            rows.Add(Text(outp, 12, color: ok ? Tw.Green600 : Tw.Red600, margin: new Thickness(12, 0, 0, 0)));
        }
        return VStack(6)[
            Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 6,
                   padding: new Thickness(8), width: _maxW)[VStack(2)[rows.ToArray()]],
            HStack(6)[_editor, SubmitButton]];
    }

    public override string? DebugDetail => $"repl ({_history.Count} 行)";
}
