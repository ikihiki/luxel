using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>オーバーレイ/開閉系コントロールのストーリー。</summary>
[StoryMeta("Controls")]
public static class OverlayControlStories
{
    [Story(Path = "Controls/Collections/Tabs/Basic",
        ShortDescription = "選択 index を外部 Signal で所有し、見出しと対応する内容を切り替えます。")]
    public static StoryResult TabsBasic(StoryContext ctx)
        => ctx.Snap(Tabs(["1つ目", "2つ目", "3つ目"],
            [Label("1つ目の内容"), Label("2つ目の内容"), Label("3つ目の内容")],
            new Signal<int>(0), width: 380, height: 160));

    [Story(Path = "Controls/Collections/Tabs/Examples/SelectionChanged",
        ShortDescription = "selected の変更と、選択したタブ内容の操作を確認する例です。")]
    public static StoryResult TabsEvnet(StoryContext ctx)
    => Frame(Tabs(["1つ目", "2つ目", "3つ目"],
        [
            Button(_ => ctx.Log("1つ目の内容をクリック"), "1つ目の内容", margin: new Thickness(0,0,0,0)),
            Button(_ => ctx.Log("2つ目の内容をクリック"), "2つ目の内容", margin: new Thickness(10,0,0,0)),
            Button(_ => ctx.Log("3つ目の内容をクリック"), "3つ目の内容", margin: new Thickness(20,0,0,0))
        ],
        ctx.Signal("selected", 0), width: 380, height: 160));

    [Story(Path = "Controls/Collections/Accordion/Basic",
        ShortDescription = "詳細を必要なときだけ開き、初期展開状態を呼び出し側で決める基本例です。")]
    public static StoryResult AccordionBasic() =>
        Accordion("Details", VStack(4)[Label("Hidden line 1"), Label("Hidden line 2")],
            new Signal<bool>(true));


    [Story(Path = "Controls/Collections/TabStrip/Basic", ArgsEnabled = false,
        ShortDescription = "選択、未保存印、閉じる操作、無効状態を一つのタブ列で確認します。",
        LongDescription = "外部所有の selectedKey と決定的な項目を使い、TabStrip の選択要求と閉じる要求を Output へ記録します。")]
    public static StoryResult TabStripBasic(StoryContext ctx)
    {
        var selected = new Signal<string>("scene");
        TabStrip strip = TabStrip(
            items:
            [
                new("scene", "Scene", new Signal<bool>(true), Tooltip: "未保存のシーン"),
                new("script", "Player.cs", Badge: "2"),
                new("readme", "README", Closable: false),
                new("build", "Build", Disabled: true),
            ],
            selectedKey: Bind.From<string?>(() => selected.Value),
            onSelect: (_, key) => { selected.Value = key; ctx.Log($"select: {key}"); },
            onCloseRequest: (_, key) => ctx.Log($"close request: {key}"),
            width: 430);
        return VStack(8)[strip, Text((Func<string>)(() => $"選択中: {selected.Value}"), 13)];
    }

    [Story(Path = "Controls/Overlay/Popover/Basic", ArgsEnabled = false,
        ShortDescription = "起点ボタンの下へ補助操作を配置し、非モーダルな開閉と位置決めを確認します。",
        LongDescription = "アンカー矩形は起点ボタンの実配置から計算します。Gallery の決定的なインライン構成では初期表示を開き、外側クリックと Escape で閉じられます。")]
    public static StoryResult PopoverBasic(StoryContext ctx)
    {
        var open = new Signal<bool>(true);
        Button anchor = Button(_ => open.Value = !open.Value, "表示オプション");
        Popover popover = Popover(open,
            Card(VStack(6)[
                Heading("表示オプション", 3),
                Check(new Signal<bool>(true), "グリッドを表示"),
                Button(_ => { open.Value = false; ctx.Log("popover apply"); }, "適用", variant: Variant.Tonal)
            ]),
            anchor: () => new Rect(anchor.WorldPos.X, anchor.WorldPos.Y, anchor.Size.Width, anchor.Size.Height));
        return VStack(8)[anchor, popover];
    }

    [Story(Path = "Controls/Overlay/Dropdown/Basic",
        ShortDescription = "起点ボタンから短い操作一覧を開き、選択後に自動で閉じる基本例です。")]
    public static StoryResult DropdownBasic() =>
        Dropdown("Open menu", [("Alpha", () => { }), ("Beta", () => { }), ("Gamma", () => { })]);

    [Story(Path = "Controls/Overlay/Tooltip/Basic",
        ShortDescription = "主操作を妨げず、hover 中だけ補足情報をアンカー上へ表示します。")]
    public static StoryResult TooltipBasic() =>
        Tooltip(Button(_ => { }, "Hover me"), "Helpful hint");

    [Story(Path = "Controls/Overlay/MenuRow/Basic",
        ShortDescription = "メニュー内の一操作を全幅の行として表し、hover とクリックを提供します。")]
    public static StoryResult MenuRowBasic() =>
        MenuRow("Open...", _ => { }, width: 220);

    [Story(Path = "Controls/Overlay/Dialog/Basic",
        ShortDescription = "open Signal、Escape、起点ボタンによる開閉経路を確認する基本例です。")]
    public static StoryResult DialogBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
        Button opener = Button(_ => open.Value = true, "ダイアログを開く");
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
                Heading("ダイアログ", 2),
                Muted("Esc か外側クリックで閉じる"),
                Button(_ => open.Value = false, "閉じる")]))];
    }

    [Story(Path = "Controls/Overlay/Toast/Basic",
        ShortDescription = "処理結果を画面端へ一時表示し、主作業を遮らず再表示できる基本例です。")]
    public static StoryResult ToastBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
        // The trigger and message are structural fixtures required to exercise Toast.
        return VStack(8)[
            Button(_ => open.Value = true, "Show toast"),
            Toast(open, Card(Label("Saved successfully")))];
    }

    [Story(Path = "Controls/Overlay/Drawer/Basic",
        ShortDescription = "補助作業面を右端から重ね、主画面を残したまま開閉する基本例です。")]
    public static StoryResult DrawerBasic(StoryContext ctx)
    {
        Signal<bool> open = new(true);
        // The trigger and panel content are structural fixtures required to exercise Drawer.
        return VStack(8)[
            Button(_ => open.Value = true, "Open drawer"),
            Drawer(open, Card(Label("Right edge panel")))];
    }
}
