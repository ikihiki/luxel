using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>メニュー/コマンド (ADR-0013 / ToDo 26 WS-M) — CommandRegistry を単一の真実に、
/// MenuBar / CommandPalette / Toolbar / Keymap をその純粋ビューとして生成する。</summary>
public static class CommandStory
{
    /// <summary>共通のデモ用レジストリ。log へ実行を記録する。</summary>
    private static CommandRegistry DemoRegistry(Action<string> log, Func<bool>? saveEnabled = null)
    {
        var reg = new CommandRegistry();
        reg.Register("file.new", "新規ファイル", () => log("file.new"), key: "Ctrl+N", menuPath: "File/新規", toolbar: true);
        reg.Register("file.save", "保存", () => log("file.save"), enabled: saveEnabled, key: "Ctrl+S", menuPath: "File/保存", toolbar: true);
        reg.Register("file.exit", "終了", () => log("file.exit"), menuPath: "File/終了", order: 99);
        reg.Register("edit.find", "検索", () => log("edit.find"), key: "Ctrl+F", menuPath: "Edit/検索");
        reg.Register("view.theme", "テーマ切替", () => log("view.theme"), menuPath: "View/テーマ切替");
        return reg;
    }

    /// <summary>DebugChildren を辿って DebugDetail 一致の widget を探す (play 用 — メニュー行など)。</summary>
    private static Widget? FindByDetail(Widget root, string detail)
    {
        if (root.DebugDetail == detail) return root;
        foreach (Widget c in root.DebugChildren())
            if (FindByDetail(c, detail) is { } hit) return hit;
        return null;
    }

    [Story("Controls/MenuBar/Basic", Height = 300)]
    public static Widget MenuBarBasic(StoryContext ctx)
    {
        string ran = "";
        CommandRegistry reg = DemoRegistry(id => { ran = id; ctx.Log($"run: {id}"); },
                                           saveEnabled: () => false);   // 保存は disabled の見本
        // アクティブ doc の寄与 (Graph 章が合成される想定のデモ)
        var docContrib = new[] { new CommandContribution(
            new Command("graph.layout", "自動整列", () => { ran = "graph.layout"; ctx.Log("run: graph.layout"); }),
            MenuPath: "Graph/自動整列") };
        MenuBar bar = MenuBar(reg, contributions: () => docContrib);

        ctx.Play(async d =>
        {
            await d.Snap();                                     // バー (File / Edit / View / Graph)
            await d.Click(bar.RootLabel("File")!);              // ドロップダウンを開く
            await d.Snap("open");                               // 新規 (Ctrl+N) / 保存 (灰) / 終了
            Widget fileLabel = bar.RootLabel("File")!;
            float rowX = fileLabel.WorldPos.X + 60;
            float rowY = fileLabel.WorldPos.Y + Luxel.Controls.MenuBar.BarH + 12;   // 先頭行 (新規)
            await d.Click(rowX, rowY);
            await d.Expect(() => ran == "file.new", "メニュー項目でコマンド実行");
            await d.Snap("ran");                                // 閉じている
        });

        return VStack(0)[
            bar,
            Text("CommandRegistry のメニュー寄与 (パス文字列) + アクティブ doc の寄与 (Graph 章) を合成。保存は enablement=false。",
                 13, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8, 10, 0, 0))];
    }

    [Story("Controls/CommandPalette/Basic", Height = 380)]
    public static Widget CommandPaletteBasic(StoryContext ctx)
    {
        string ran = "";
        CommandRegistry reg = DemoRegistry(id => { ran = id; ctx.Log($"run: {id}"); });
        CommandPalette.PaletteView? view = null;
        var opener = new PaletteOpener { OnOpen = c => view = CommandPalette.Open(c, reg) };

        ctx.Play(async d =>
        {
            await d.Click(opener.OpenButton!);                  // パレットを開く
            await d.Snap("open");                               // 全コマンド一覧 + 先頭選択
            // クエリ入力 → 絞り込み → Enter で実行
            Widget field = FindByDetail(view!, "(placeholder: コマンドを検索…)") ?? view!;
            await d.Click(field);                               // フォーカス
            await d.Type("テーマ");
            await d.Snap("filtered");                           // テーマ切替だけに絞られ選択中
            await d.Key(Key.Enter);
            await d.Expect(() => ran == "view.theme", "Enter で選択コマンド実行");
        });

        return VStack(10)[
            Heading("CommandPalette — 発見性の主役"),
            Muted("クエリで絞り込み、↑↓ 選択、Enter 実行、Esc/外側で閉じる。キーバインドも表示。"),
            opener];
    }

    /// <summary>ctx を捕まえてパレットを開くボタン (パレットは canvas 直下に実体化するため
    /// UiBuildContext が要る)。</summary>
    private sealed class PaletteOpener : CompositeControl
    {
        public required Action<UiBuildContext> OnOpen;
        public Widget? OpenButton;
        private UiBuildContext? _ctx;

        protected override void OnRealize(UiBuildContext ctx) => _ctx = ctx;

        protected override Widget Build()
            => OpenButton = Button(_ => { if (_ctx is not null) OnOpen(_ctx); }, "パレットを開く (Ctrl+Shift+P)");
    }

    [Story("Controls/Toolbar/Basic", Height = 200)]
    public static Widget ToolbarBasic(StoryContext ctx)
    {
        string ran = "";
        CommandRegistry reg = DemoRegistry(id => { ran = id; ctx.Log($"run: {id}"); },
                                           saveEnabled: () => false);
        var docContrib = new[] { new CommandContribution(
            new Command("graph.run", "▶ 実行", () => { ran = "graph.run"; ctx.Log("run: graph.run"); }),
            Toolbar: true, Order: -1) };
        Toolbar tb = Toolbar(reg, contributions: () => docContrib);

        ctx.Play(async d =>
        {
            await d.Snap();                                     // ▶ 実行 / 新規ファイル / 保存 (灰)
            Widget run = FindByDetail(tb, "▶ 実行")!;
            await d.Click(run);
            await d.Expect(() => ran == "graph.run", "ツールバーでコマンド実行");
        });

        return VStack(10)[
            Heading("Toolbar — 掲載コマンドのボタン列"),
            Muted("寄与 (▶ 実行) + 登録分。保存は enablement=false でグレー非活性。"),
            tb];
    }
}
