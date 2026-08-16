using Luxel.Controls;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

public static partial class DocsMeta
{
    [Story]
    public static StoryResult OrbitSample(StoryContext ctx)
    {
        var speed = ctx.Signal("speed", 0.7f, "Orbit speed used by the embedded sample.");
        return Card(VStack(8)[
            Heading("Embedded sample", 3),
            Text($"Orbit speed: {speed}")
        ]);
    }
}
