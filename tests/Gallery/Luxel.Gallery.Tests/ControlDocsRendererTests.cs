using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Gallery.UI;

namespace Luxel.Tests;

public sealed class ControlDocsRendererTests
{
    private static readonly GeneratedComponentStoryDescriptor Descriptor = new(
        "global::Probe.Control", "Probe", "Input", "Control");

    [Fact]
    public void Renderer_emits_stable_headings_semantic_keyboard_table_and_one_primary_story()
    {
        StoryResult result = ControlDocsRenderer.Render(ValidPage(), Descriptor);

        Assert.Equal(ControlDocsRenderer.HeadingOrder,
            MarkdownDecorations.Headings(result.Markdown)
                .Where(static heading => heading.Level == 2)
                .Select(static heading => $"## {heading.Text}")
                .ToArray());
        Assert.Contains("| キー | 操作 |", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("| `Enter` | 起動します。 |", result.Markdown, StringComparison.Ordinal);
        Assert.Equal(Descriptor.BasicPath, Assert.Single(result.References).Path);
        Assert.DoesNotContain($"story:{Descriptor.BasicPath}", result.Markdown, StringComparison.Ordinal);
        Assert.Equal(1, result.Markdown.Split($"story:{Descriptor.PlaygroundPath}", StringSplitOptions.None).Length - 1);
        StoryMarkdownEmbed embed = Assert.Single(result.Embeds);
        Assert.Equal("ControlApiTable", embed.Kind);
        Assert.Equal("Control", embed.Reference);
        Assert.True(embed.IncludeInherited);
    }

    [Fact]
    public void Renderer_rejects_missing_use_or_avoid_guidance()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ControlDocsRenderer.Render(ValidPage() with { UseWhen = [] }, Descriptor));
        Assert.Throws<InvalidOperationException>(() =>
            ControlDocsRenderer.Render(ValidPage() with { AvoidWhen = [] }, Descriptor));
    }

    [Fact]
    public void Renderer_rejects_primary_basic_duplication_and_missing_playground()
    {
        ControlDocsPage page = ValidPage();
        Assert.Throws<InvalidOperationException>(() => ControlDocsRenderer.Render(page with
        {
            RelatedStories =
            [
                .. page.RelatedStories,
                new(Descriptor.BasicPath, "重複", "Basic を関連リンクへ重複させます。", StoryKind.Basic),
            ],
        }, Descriptor));
        Assert.Throws<InvalidOperationException>(() =>
            ControlDocsRenderer.Render(page with { RelatedStories = [] }, Descriptor));
    }

    private static ControlDocsPage ValidPage() => new(
        Descriptor.ComponentType,
        "Control",
        "構造化 renderer を検証するコントロールです。",
        ["一つの処理を起動する場合。"],
        ["値を連続編集する場合。"],
        [new("Slider", "連続値を編集します。")],
        "Control(onRun: Run)",
        "本体とラベルから構成します。",
        "標準 variant を提供します。",
        "処理結果は呼び出し側が所有します。",
        "ポインターで起動します。",
        [new("`Enter`", "起動します。")],
        "focus 中に Enter で起動し、dismissal はありません。",
        new ControlDocsAccessibility(
            "見えるラベルを指定します。",
            "button semantics を公開します。",
            "無効状態を公開します。",
            "文字と背景のコントラストを確認します。",
            "重要情報を motion だけに依存させません。",
            "追加の制約はありません。"),
        new ControlDocsThemeLayout(
            "テーマ色へ追従します。",
            "親の制約に従います。",
            "内容に応じた寸法です。"),
        new ControlDocsConstraints(
            "同期処理だけを起動します。",
            "callback の寿命は呼び出し側が管理します。",
            "portable host で利用します。"),
        new ControlDocsApi(
            "Control",
            "`onRun` が中心です。",
            "起動時に callback を一度呼びます。"),
        new ControlDocsStory(Descriptor.BasicPath, "基本例", "最小構成です。", StoryKind.Basic),
        [
            new(Descriptor.PlaygroundPath, "プレイグラウンド", "パラメーターを確認します。", StoryKind.Playground),
            new("Controls/Input/Control/Examples/Interactive", "操作例", "起動を確認します。", StoryKind.Example),
        ]);
}
