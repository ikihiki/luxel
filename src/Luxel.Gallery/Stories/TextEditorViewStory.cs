using Luxel.Controls;
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
}
