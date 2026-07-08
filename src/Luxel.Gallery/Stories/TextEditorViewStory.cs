using Luxel.Controls;
using Luxel.Editor;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>TextEditorView — テキストエディタ新スタック (ADR-0006 / ToDo 22) のビュー。
/// 編集意味論・座標写像・装飾は canvas 非依存の Luxel.Editor が持ち、この widget は入力を Transaction にして
/// ジオメトリの矩形を塗るだけ。折返し・プロポーショナル・マルチカーソルはジオメトリ由来で最初から正しい。</summary>
public static class TextEditorViewStory
{
    [Story("Controls/TextEditorView/Basic", Height = 300)]
    public static Widget Basic(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text",
            "新スタックのテキストエディタ。\nTransaction ベースで undo が正確、\nマルチカーソルが native。");
        TextEditorView ed = TextEditorView(value, editorHeight: 200f, editorWidth: 560f);

        ctx.Play("edit", async d =>
        {
            await d.Snap();                         // 初期テキスト + キャレット
            await d.Click(ed);                      // フォーカス
            await d.Key(Key.End);
            await d.Type(" 折返しも正しい。");
            await d.Expect(() => ed.Text.Contains("折返し"), "入力が反映される");
            await d.Snap("typed");
            await d.Key(Key.A, ctrl: true);         // 全選択
            await d.Expect(() => ed.HasSelection, "Ctrl+A で全選択");
            await d.Snap("selected");
        });
        ctx.Play("undo", async d =>
        {
            await d.Click(ed);
            await d.Key(Key.End);
            await d.Type("XYZ");
            await d.Expect(() => ed.Text.Contains("XYZ"), "入力される");
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => !ed.Text.Contains("XYZ"), "undo で戻る");
            await d.Snap("undone");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView (新スタック)"),
                Muted("Luxel.Editor (Transaction + native 複数レンジ + 装飾) を canvas に載せた薄いビュー。"),
                ed]];
    }

    // 行頭番号 + 行内 widget (◯ をチェックボックスに置換) + 左縦バーを供給する装飾プロバイダ (デモ用)
    private sealed class ListDecoProvider : IDecorationProvider
    {
        public string Owner => "list-demo";
        public DecorationSet Provide(EditorState s)
        {
            var d = new List<Decoration>();
            TextDoc doc = s.Doc;
            for (int i = 0; i < doc.LineCount; i++)
                d.Add(new LinePrefixDecoration(doc.LineStart(i), $"{i + 1}. ", 0xFF8A8A8A));
            int ci = 0;
            for (int idx = doc.Text.IndexOf('◯'); idx >= 0; idx = doc.Text.IndexOf('◯', idx + 1))
                d.Add(new WidgetDecoration(idx, idx + 1, 44f, 22f, $"chk{ci++}"));
            d.Add(new BlockDecoration(0, doc.Length, BarColor: 0xFF4A90D9, BarWidth: 3f));
            return new DecorationSet(d);
        }
    }

    [Story("Controls/TextEditorView/Widgets", Height = 260, Order = 1)]
    public static Widget Widgets(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("text", "牛乳を買う ◯\n卵を買う ◯\nパンを買う ◯");
        TextEditorView ed = TextEditorView(value, editorHeight: 150f, editorWidth: 460f);

        Signal<bool>[] checks = [ctx.Signal("c0", false), ctx.Signal("c1", false), ctx.Signal("c2", true)];
        Switch[] toggles = [Switch(checks[0]), Switch(checks[1]), Switch(checks[2])];
        ed.WidgetResolver = key => key switch
        {
            "chk0" => toggles[0],
            "chk1" => toggles[1],
            "chk2" => toggles[2],
            _ => null,
        };
        ed.Providers.Add(new ListDecoProvider());

        ctx.Play("check", async d =>
        {
            await d.Snap();                                  // 番号 + 行内チェックボックス + 左バー
            await d.Click(toggles[0]);                       // 行内 widget を押す
            await d.Step(1);
            await d.Expect(() => checks[0].Value, "行内チェックボックスが押せて ON になる");
            await d.Snap("checked");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("TextEditorView — 行内 widget + 行頭装飾"),
                Muted("装飾プロバイダが行頭番号・置換 widget (◯→チェックボックス)・左縦バーを供給。widget は行内でホストされ状態を持つ。"),
                ed]];
    }
}
