using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

public static partial class TutorialStories
{
    [Story]
    public static StoryResult CounterSample(StoryContext ctx)
    {
        Signal<int> count = ctx.Signal("count", 0, "ボタンで増減する値");

        return Card(VStack(12)[
            Heading("Counter", 2),
            Text($"現在の値: {count}", 18),
            HStack(8)[
                Button(_ => { count.Value--; ctx.Log($"count = {count.Value}"); }, "-1"),
                Button(_ => { count.Value++; ctx.Log($"count = {count.Value}"); }, "+1")]
        ]);
    }
}
