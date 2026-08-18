using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>テキスト表示/編集系コントロールのストーリー。</summary>
[StoryMeta("Controls")]
public static class TextControlStories
{
    [Story(Path = "Controls/Text/TextField/Basic")]
    public static StoryResult TextFieldBasic(StoryContext ctx)
    {
        Signal<string> text = ctx.Signal("text", "Hello");
        TextField tf = TextField(text, placeholder: "Type here...");
        // play: クリックでフォーカス → 入力 → signal 反映 → 入力後の絵 (E2E の対話ショーケース)
        ctx.Play(async d =>
        {
            await d.Snap();
            await d.Click(tf);                 // 中心クリック — テキスト末尾より右なのでキャレットは末尾
            await d.Type(" Luxel");
            await d.Expect(() => text.Value == "Hello Luxel", "入力が signal へ反映される");
            await d.Snap("typed");
        });
        return Frame(tf);
    }

    [Story(Path = "Controls/Text/TextField/Playground")]
    public static StoryResult TextFieldPlayground(StoryContext ctx)
    {
        Signal<string> value = ctx.Signal("value", "Editable text");
        return Frame(VStack(8)[TextField(value, placeholder: "Type here..."), Muted($"value: {value}")]);
    }

    [Story(Path = "Controls/Text/TextField/Examples/Slots")]
    public static StoryResult TextFieldSlots(StoryContext ctx) => Frame(
        TextField(ctx.Signal("value", "Search"), placeholder: "Search...")
        [
            TextFieldSlot.Leading(() => Text("⌕", 16, color: Tw.Blue500)),
            TextFieldSlot.Trailing(() => Text("⌘K", 11, color: Tw.Slate500))
        ]);

    [Story(Path = "Controls/Text/TextField/States/Focused")]
    public static StoryResult TextFieldFocused(StoryContext ctx)
    {
        TextField field = TextField(ctx.Signal("value", "Focused"));
        field.Focused.Value = true;
        return Frame(field);
    }

    [Story(Path = "Controls/Text/TextField/States/Invalid")]
    public static StoryResult TextFieldInvalid(StoryContext ctx)
    {
        TextField field = TextField(ctx.Signal("value", "invalid"), utilities: [U.Background(0x20EF4444)]);
        field.Pattern = "^[0-9]+$";
        return Frame(VStack(5)[field, Text("Enter digits only", 12, color: Tw.Red500)]);
    }

    [Story(Path = "Controls/Text/SearchField/Basic")]
    public static StoryResult SearchFieldBasic(StoryContext ctx)
    {
        // CompositeControl の見本: タイプで候補が絞り込まれ (構造状態 → Rebuild)、行クリックで確定
        string[] langs = ["C#", "C++", "Rust", "Go", "TypeScript", "Python", "Ruby", "Swift", "Kotlin", "Zig"];
        return Frame(SearchField(ctx.Signal("query", ""), langs));
    }

    [Story(Path = "Controls/Text/Text/Examples/EllipsisVerticalAlignment")]
    public static StoryResult TextEllipsisVAlign()
    {
        const string lng = "The quick brown fox jumps over the lazy dog near the quiet river bank in autumn evenings.";
        Widget Case(string title, Widget t) => VStack(2)[
            Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6, padding: new Thickness(8), width: 300)[t]];
        return Frame(VStack(8)[
            Case("maxLines: 1 + …", Text(lng, 13, wrap: TextWrap.Word, maxLines: 1)),
            Case("maxLines: 2 + …", Text(lng + " " + lng, 13, wrap: TextWrap.Word, maxLines: 2)),
            Case("VAlign Center (height 70)", Text("centered", 13, wrap: TextWrap.Word, height: 70,
                verticalAlign: TextVAlign.Center, textAlign: Luxel.Typography.TextAlign.Center)),
            Case("VAlign Bottom (height 70)", Text("bottom right", 13, wrap: TextWrap.Word, height: 70,
                verticalAlign: TextVAlign.Bottom, textAlign: Luxel.Typography.TextAlign.Right))]);
    }

    [Story(Path = "Controls/Text/RichTextView/Basic")]
    public static StoryResult RichTextBasic()
    {
        var spans = new[]
        {
            new Luxel.Typography.TextSpan("Rich ", new Luxel.Typography.SpanStyle { Size = 24, Color = Tw.Blue500 }),
            new Luxel.Typography.TextSpan("text ", new Luxel.Typography.SpanStyle { Size = 24, Color = Tw.Red500 }),
            new Luxel.Typography.TextSpan("mixes sizes, "),
            new Luxel.Typography.TextSpan("colors ", new Luxel.Typography.SpanStyle { Color = Tw.Green600 }),
            new Luxel.Typography.TextSpan("and 日本語フォールバック。", new Luxel.Typography.SpanStyle { Size = 14 }),
            new Luxel.Typography.TextSpan("\nWrapping works across spans too — the quick brown fox jumps over the lazy dog."),
        };
        RichTextView rtv = RichTextView(spans, wrap: Luxel.Typography.TextWrap.Word);
        rtv.Fonts = JpFallback.Value;
        return Frame(Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6,
                            padding: new Thickness(10), width: 380)[rtv]);
    }

    [Story(Path = "Controls/Text/Text/Examples/Multiline")]
    public static StoryResult TextMultiline()
    {
        const string en = "The quick brown fox jumps over the lazy dog near the quiet river bank in autumn.";
        const string jp = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。「にゃー」と鳴いた。";
        Widget Case(string title, Widget t) => VStack(2)[
            Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted)),
            Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 6, padding: new Thickness(8), width: 360)[t]];
        return Frame(VStack(8)[
            Case("Word wrap + \\n + 禁則", Text(en + "\n" + jp, 13, wrap: Luxel.Typography.TextWrap.Word)),
            Case("Center", Text(en, 13, wrap: Luxel.Typography.TextWrap.Word, textAlign: Luxel.Typography.TextAlign.Center)),
            Case("Justify (末行は左)", Text(en + " " + en, 13, wrap: Luxel.Typography.TextWrap.Word, textAlign: Luxel.Typography.TextAlign.Justify))]);
    }

    [Story(Path = "Controls/Text/Text/Examples/Styles")]
    public static StoryResult TextStyles(StoryContext ctx) => ctx.Snap(Frame(VStack(6)[
        Text("Large 28px", 28, color: Bind.From(() => UiTheme.T.Text)),
        Text("Body 16px", 16, color: Bind.From(() => UiTheme.T.Text)),
        Text("Muted 13px", 13, color: Bind.From(() => UiTheme.T.TextMuted)),
        Text("Tailwind colored", 16, color: Tw.Blue500),
        Text("Half opacity", 16, color: Bind.From(() => UiTheme.T.Text), opacity: 0.5f)]));

    [Story(Path = "Controls/Text/Text/Examples/Japanese")]
    public static StoryResult Japanese(StoryContext ctx)
    {
        // 同梱フォント (BIZ UDGothic / UDEV Gothic) で日本語が出ることを 1 画面で確認する:
        // 基本フォント直 (Heading/Button/Text) + IME 入力 (TextEditorView) + 等幅の日本語コメント (色分け)。
        Signal<string> input = ctx.Signal("input", "");
        TextEditorView ta = TextEditorView(input, editorHeight: 72f, editorWidth: 420f);
        ta.Fonts = JpFallback.Value;

        Signal<string> code = ctx.Signal("code",
            "// 日本語コメントも等幅で表示される\nint 合計 = 1 + 2;  // 全角識別子");
        TextEditorView ed = TextEditorView(code, editorHeight: 90f, editorWidth: 420f);
        ed.ShowLineNumbers = true;
        ed.EditorFont = EditorFaces.Value.Mono;
        Func<Theme> th = () => UiTheme.T;
        ed.Providers.Add(new SyntaxHighlightProvider(Luxel.Highlight.TextMateHighlighter.Instance, "csharp", th));
        ed.Providers.Add(new CurrentLineProvider(th));

        ctx.Play("jp", async d =>
        {
            await d.Snap();                              // 見出し/ラベル/コメントの日本語 (基本フォント直 + 等幅)
            await d.Click(ta);
            await d.Type("日本語入力テスト");
            await d.Expect(() => input.Value.Contains("日本語"), "IME 経由で日本語が入力される");
            await d.Snap("typed");
        });

        return Frame(VStack(10)[
            Heading("日本語表示 — ひらがな・カタカナ・漢字"),
            HStack(8)[Button(_ => { }, "ボタン"),
                      Text("ラベル：あいうえお アイウエオ 日本語", 14, color: Bind.From(() => UiTheme.T.Text))],
            Muted("TextEditorView に IME で入力："),
            ta,
            Muted("等幅の日本語コメント (色分け)："),
            ed]);
    }
}
