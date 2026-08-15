using Luxel.UI;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/UI")]
public static class LearnUi
{
    [Story]
    public static StoryResult Trees(StoryContext ctx) => $"""
        # UI widget trees

        {Toc()}

        Luxel UIは`Widget`を宣言し、Layoutでsizeとoffsetを決め、Realizeでretained 2D nodeへ接続します。`CompositeControl.Build()`は既存controlを組み合わせる入口です。完全自前描画が不要なら`Widget`を直接継承せず、generated `Kit` factoryでtreeを返します。

        """;

    [Story]
    public static StoryResult Signals(StoryContext ctx) => $"""
        # Signals and dependency tracking

        {Toc()}

        `Signal<T>.Value`は現在のreactive scopeへ依存登録し、setで購読側を無効化します。`Peek()`は追跡しない読み取りです。`CompositeControl`はBuild中に読んだsignalを既定で追跡するため、構造を決める値が変わるとrootを破棄します。

        """;

    [Story]
    public static StoryResult Reconciliation(StoryContext ctx) => $"""
        # Build, layout, and reconciliation

        {Toc()}

        Buildはlazyで、最初のLayout時に呼ばれます。signal変更は同期的に`Root`をnullへし、次のLayoutで最新値を使って一度だけ再Buildします。このheadless sampleはBuild reconciliationまでを検証します。`UiHost`、input hit testing、RetainedCanvasへの部分Realizeはwindow/GPU host側の責務であり、このbundleには含まれません。
        """;
}
