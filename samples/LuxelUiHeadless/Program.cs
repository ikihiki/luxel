using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

// docs:begin ui-headless-tree
using VectorFont font = VectorFont.Load(Path.Combine(AppContext.BaseDirectory, "fonts", "BIZUDGothic-Regular.ttf"));
var context = new LayoutContext { Font = font };
var counter = new CounterTree();
counter.Layout(Constraints.LooseW(240, 240), context);
int firstChildren = ((StackPanel)counter.Root!).ChildCount;

counter.Count.Value = 3;
bool invalidated = counter.Root is null;
counter.Layout(Constraints.LooseW(240, 240), context);
int secondChildren = ((StackPanel)counter.Root!).ChildCount;

Console.WriteLine($"ui: builds={counter.Builds}, children={firstChildren}->{secondChildren}, invalidated={invalidated}");
// docs:end ui-headless-tree

return counter.Builds == 2 && firstChildren == 1 && secondChildren == 3 && invalidated ? 0 : 1;

sealed class CounterTree : CompositeControl
{
    internal Signal<int> Count { get; } = new(1);
    internal int Builds { get; private set; }

    protected override Widget Build()
    {
        Builds++;
        Widget[] rows = Enumerable.Range(1, Count.Value).Select(i => (Widget)Text($"row {i}")).ToArray();
        return VStack(spacing: 2)[rows];
    }
}
