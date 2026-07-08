using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **入力記録 → 決定的リプレイ** (11-B) — play が実際のクリックを <see cref="InputRecorder"/> で
/// 記録し、状態を巻き戻してから <see cref="InputReplayer"/> で再生する。固定 dt なので
/// 「記録時」と「再生後」が同じ絵になる (決定性)。手操作から回帰 play を起こす土台。
/// </summary>
public static class InputReplayStories
{
    [Story("Demos/Framework/InputReplay", Height = 300, Order = 153)]
    public static Widget InputReplay(StoryContext ctx)
    {
        var count = new Signal<int>(0);
        void Inc() => count.Value++;
        System.Func<string> shown = () => count.Value.ToString();

        Button plus = Button(_ => Inc(), "+1 クリック");

        ctx.Play(async d =>
        {
            var rec = new InputRecorder();
            rec.Attach(d.Host);

            rec.Start();                          // ← 手操作をフレーム番号付きで記録
            await d.Click(plus);
            await d.Click(plus);
            await d.Click(plus);
            rec.Stop();
            InputRecording recording = rec.Snapshot();
            rec.Detach();                         // 再生中に再記録しない
            int recorded = count.Value;           // 3
            await d.Snap("recorded");             // 手操作の結果

            count.Value = 0;                      // 状態を巻き戻す
            await d.Step(1);
            await InputReplayer.Replay(d.Host, recording, d.Step);   // 記録を決定的に再生
            await d.Snap("replayed");             // 同じ 3 回が再現される
            await d.Expect(() => count.Value == recorded, "リプレイで同じ結果に収束");
        });

        return Frame(VStack(12)[
            Heading("入力記録 → 決定的リプレイ"),
            Muted("play が +1 を 3 回クリックして記録 → カウンタを 0 に戻す → 記録を再生。固定 dt なので同じ値に戻る。"),
            Text(shown, 40f, color: Bind.From(() => UiTheme.T.Text)),
            plus]);
    }
}
