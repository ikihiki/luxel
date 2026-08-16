using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

public static partial class TutorialUiApp
{
    [Story]
    public static StoryResult LayoutSample(StoryContext ctx) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
        [VStack(16)[
            VStack(4)[
                Heading("Today", 1),
                Muted("小さなタスク画面をstackで構成します")],
            Card(VStack(10)[
                Heading("Write a tutorial", 2),
                Label("説明、実行例、完成チェックを一つの流れにまとめる"),
                HStack(8)[
                    Button(_ => ctx.Log("task started"), "Start"),
                    Button(_ => ctx.Log("task postponed"), "Later", variant: Variant.Outline)]])]];

    [Story]
    public static StoryResult TaskCounterSample(StoryContext ctx)
    {
        Signal<int> completed = ctx.Signal("completed", 1, "完了したタスク数");
        Signal<bool> notifications = ctx.Signal("notifications", true, "通知を表示する");

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
        [Card(VStack(12)[
            Heading("Progress", 2),
            Text($"完了: {completed}", 18),
            HStack(8)[
                Button(_ => { completed.Value--; ctx.Log($"completed = {completed.Value}"); }, "-1"),
                Button(_ => { completed.Value++; ctx.Log($"completed = {completed.Value}"); }, "+1")],
            HStack(8)[Label("Notifications"), Switch(notifications)]])];
    }

    [Story]
    public static StoryResult DialogSample(StoryContext ctx)
    {
        Signal<bool> open = ctx.Signal("open", true, "確認dialogを開く");
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
        [VStack(8)[
            Button(_ => open.Value = true, "Complete task"),
            Dialog(open, Card(VStack(10)[
                Heading("Complete this task?", 2),
                Muted("完了後も履歴から確認できます"),
                HStack(8)[
                    Button(_ => open.Value = false, "Cancel", variant: Variant.Outline),
                    Button(_ => { open.Value = false; ctx.Log("task completed"); }, "Complete")]]))]];
    }
}
