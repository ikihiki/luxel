using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>オーバーレイ/開閉系コントロールのストーリー。</summary>
[StoryMeta("Controls")]
public static class OverlayControlStories
{
    [Story(Path = "Controls/Collections/Tabs/Basic")]
    public static StoryResult TabsBasic(StoryContext ctx)
        => ctx.Snap(Tabs(["One", "Two", "Three"],
            [Label("Content of tab one"), Label("Content of tab two"), Label("Content of tab three")],
            new Signal<int>(0), width: 380, height: 160));

    [Story(Path = "Controls/Collections/Tabs/Examples/SelectionChanged")]
    public static StoryResult TabsEvnet(StoryContext ctx)
    => Frame(Tabs(["One", "Two", "Three"],
        [
            Button(_ => ctx.Log("Content of tab one clicked"), "Content of tab one", margin: new Thickness(0,0,0,0)),
            Button(_ => ctx.Log("Content of tab two clicked"), "Content of tab two", margin: new Thickness(10,0,0,0)),
            Button(_ => ctx.Log("Content of tab three clicked"), "Content of tab three", margin: new Thickness(20,0,0,0))
        ],
        ctx.Signal("selected", 0), width: 380, height: 160));

    [Story(Path = "Controls/Collections/Accordion/Basic")]
    public static StoryResult AccordionBasic() =>
        Accordion("Details", VStack(4)[Label("Hidden line 1"), Label("Hidden line 2")],
            new Signal<bool>(true));

    [Story(Path = "Controls/Overlay/Dropdown/Basic")]
    public static StoryResult DropdownBasic() =>
        Dropdown("Open menu", [("Alpha", () => { }), ("Beta", () => { }), ("Gamma", () => { })]);

    [Story(Path = "Controls/Overlay/Tooltip/Basic")]
    public static StoryResult TooltipBasic() =>
        Tooltip(Button(_ => { }, "Hover me"), "Helpful hint");

    [Story(Path = "Controls/Overlay/MenuRow/Basic")]
    public static StoryResult MenuRowBasic() =>
        MenuRow("Open...", _ => { }, width: 220);

    [Story(Path = "Controls/Overlay/Dialog/Basic")]
    public static StoryResult DialogBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
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
        // The opener and dialog content are structural fixtures required to exercise Dialog.
        return VStack(8)[
            opener,
            Dialog(open, Card(VStack(8)[
                Heading("Dialog title", 2),
                Muted("Esc か外側クリックで閉じる"),
                Button(_ => open.Value = false, "Close")]))];
    }

    [Story(Path = "Controls/Overlay/Toast/Basic")]
    public static StoryResult ToastBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
        // The trigger and message are structural fixtures required to exercise Toast.
        return VStack(8)[
            Button(_ => open.Value = true, "Show toast"),
            Toast(open, Card(Label("Saved successfully")))];
    }

    [Story(Path = "Controls/Overlay/Drawer/Basic")]
    public static StoryResult DrawerBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
        // The trigger and panel content are structural fixtures required to exercise Drawer.
        return VStack(8)[
            Button(_ => open.Value = true, "Open drawer"),
            Drawer(open, Card(Label("Right edge panel")))];
    }
}
