using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>オーバーレイ/開閉系コントロールのストーリー。</summary>
[StoryMeta("Controls")]
public static class OverlayControlStories
{
    [Story]
    public static Widget TabsBasic(StoryContext ctx)
        => ctx.Snap(Frame(Tabs(["One", "Two", "Three"],
            [Label("Content of tab one"), Label("Content of tab two"), Label("Content of tab three")],
            ctx.Signal("selected", 0), width: 380, height: 160)));

    [Story]
    public static Widget TabsEvnet(StoryContext ctx)
    => Frame(Tabs(["One", "Two", "Three"],
        [
            Button(_ => ctx.Log("Content of tab one clicked"), "Content of tab one", margin: new Thickness(0,0,0,0)),
            Button(_ => ctx.Log("Content of tab two clicked"), "Content of tab two", margin: new Thickness(10,0,0,0)),
            Button(_ => ctx.Log("Content of tab three clicked"), "Content of tab three", margin: new Thickness(20,0,0,0))
        ],
        ctx.Signal("selected", 0), width: 380, height: 160));

    [Story]
    public static Widget AccordionBasic(StoryContext ctx) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Accordion("Details", VStack(4)[Label("Hidden line 1"), Label("Hidden line 2")],
                       ctx.Signal("expanded", true))];

    [Story]
    public static Widget DropdownBasic() =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Dropdown("Open menu", [("Alpha", () => { }), ("Beta", () => { }), ("Gamma", () => { })])];

    [Story]
    public static Widget TooltipBasic() => Frame(
        Tooltip(Button(_ => { }, "Hover me"), "Helpful hint"));

    [Story]
    public static Widget MenuRowBasic() => Frame(
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(6))
            [VStack(2)[
                MenuRow("Open...", _ => { }, hAlign: Align.Stretch),
                MenuRow("Save", _ => { }, hAlign: Align.Stretch),
                MenuRow("Exit", _ => { }, hAlign: Align.Stretch)]]);

    [Story]
    public static Widget DialogBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        Button opener = Button(_ => open.Value = true, "Open dialog");
        // play: 初期 (開) → Esc で閉じる → 再度開く (E2E の対話ショーケース)
        ctx.Play(async d =>
        {
            await d.Snap();                                    // 開いた状態 (初期)
            await d.Key(Key.Escape);
            await d.Step(30);   // 閉フェード (~0.2s) の静定待ち
            await d.Expect(() => !open.Value, "Esc でダイアログが閉じる");
            await d.Snap("closed");
            await d.Click(opener);
            await d.Expect(() => open.Value, "ボタンで再度開く");
        });
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                opener,
                Dialog(open, Card(VStack(8)[
                    Heading("Dialog title", 2),
                    Muted("Esc か外側クリックで閉じる"),
                    Button(_ => open.Value = false, "Close")]))]];
    }

    [Story]
    public static Widget ToastBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                Button(_ => open.Value = true, "Show toast"),
                Toast(open, Card(Label("Saved successfully")))]];
    }

    [Story]
    public static Widget DrawerBasic(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true);
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [VStack(8)[
                Button(_ => open.Value = true, "Open drawer"),
                Drawer(open, Card(VStack(6)[Heading("Drawer", 2), Label("Right edge panel")]))]];
    }
}
