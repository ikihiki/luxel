using System.Diagnostics;
using Luxel.Controls;
using Luxel.Document;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Story metadataを反映してdirect Markdown Storyをnative表示する共通renderer。</summary>
public static class StoryMarkdownRenderer
{
    public static string EffectiveMarkdown(StoryInfo story, string markdown)
        => MarkdownDoc.RenderTocPlaceholder(markdown);

    public static Widget Build(StoryInfo story, StoryContext context, StoryResult authored,
        StoryPageNavigation navigation = default, bool fill = true,
        float availableWidth = StoryMarkdownLayoutPolicy.DefaultViewportWidth,
        float availableHeight = StoryMarkdownLayoutPolicy.DefaultViewportHeight)
    {
        if (authored.Kind == StoryResultKind.Widget && authored.Widget is not null) return authored.Widget;
        StoryMarkdownLayout layout = StoryMarkdownLayoutPolicy.Calculate(availableWidth, availableHeight);
        StoryResult result = authored.WithMarkdown(EffectiveMarkdown(story, authored.Markdown));
        if (fill && !navigation.IsEmpty)
        {
            result.AppendLiteral("\n\n");
            result.AppendFormatted(BuildPageNavigation(context, navigation, layout));
        }

        (VectorFont? bold, _, _, VectorFont? mono) = RenderingStoryKit.EditorFaces.Value;
        var fences = new Dictionary<string, Func<string, Widget>>
        {
            ["mermaid"] = body => Luxel.Diagram.Factories.DiagramBlock(body, layout.EmbedWidth),
            ["math"] = body => Luxel.MathText.Factories.MathBlockView(body, maxWidth: layout.EmbedWidth),
        };
        TextEditorView editor = StoryMarkdownDocumentAdapter.FromStoryResult(result, () => UiTheme.T,
            width: layout.ContentWidth, height: layout.ViewportHeight,
            reference => BuildReference(context, reference, layout.EmbedWidth), bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fences: fences,
            fonts: RenderingStoryKit.JpFallback.Value, fill: fill, appearance: DocumentAppearance());
        string source = editor.DocSource!;
        IReadOnlyList<MarkdownLink> links = MarkdownDecorations.Links(source);
        editor.OnClickOffset = offset =>
        {
            foreach (MarkdownLink link in links)
                if (offset >= link.From && offset < link.To)
                {
                    Navigate(context, editor, source, link.Url);
                    return;
                }
        };
        return fill
            ? new ReadableDocumentFrame(editor, layout.ViewportWidth, layout.ViewportHeight)
            : editor;
    }

    private static TextEditorAppearance DocumentAppearance()
        => new TextEditorAppearance(fontSize: 16f, lineHeight: 1.65f, wrapLineHeight: 1.55f)
            .WithBlock(MarkdownBlockKinds.Heading(1), new TextEditorBlockAppearance(
                FontSize: 32f, FontVariant: FontVariant.Bold))
            .WithBlock(MarkdownBlockKinds.Heading(2), new TextEditorBlockAppearance(
                FontSize: 24f, FontVariant: FontVariant.Bold))
            .WithBlock(MarkdownBlockKinds.Heading(3), new TextEditorBlockAppearance(
                FontSize: 20f, FontVariant: FontVariant.Bold))
            .WithBlock(MarkdownBlockKinds.CodeBlock, new TextEditorBlockAppearance(
                FontSize: 14f, FontVariant: FontVariant.Mono))
            .WithBlock(MarkdownBlockKinds.Quote, new TextEditorBlockAppearance(
                Indent: 14f, BarWidth: 3f));

    private static Widget BuildPageNavigation(
        StoryContext context,
        StoryPageNavigation navigation,
        StoryMarkdownLayout layout)
    {
        Widget previous = navigation.Previous is { } prev
            ? Button(_ => context.Navigate(prev.Path), $"← 前へ: {prev.Name}",
                variant: Variant.Outline, width: layout.NavigationButtonWidth, height: 52)
            : Spacer(width: layout.NavigationButtonWidth, height: 52);
        Widget next = navigation.Next is { } following
            ? Button(_ => context.Navigate(following.Path), $"次へ: {following.Name} →",
                variant: Variant.Outline, width: layout.NavigationButtonWidth, height: 52)
            : Spacer(width: layout.NavigationButtonWidth, height: 52);
        Widget content = layout.StackNavigation
            ? VStack(layout.NavigationGap)[previous, next]
            : HStack(layout.NavigationGap)[previous, next];
        return Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8,
            padding: new Thickness(8), width: layout.EmbedWidth)[content];
    }

    private static Widget BuildReference(StoryContext context, StoryReference reference, float availableWidth)
    {
        StoryInfo? story = StoryRegistry.Find(reference.Path);
        if (story is null) return Alert(NativeRenderingLabels.StoryNotFound(reference.Path), Intent.Danger);
        float width = MathF.Max(120, availableWidth);
        int before = context.Knobs.Count;
        int logStart = context.LogSnapshot().Length;
        bool suppressed = context.SuppressPlays;
        context.SuppressPlays = true;
        try
        {
            StoryResult result = global::Luxel.Gallery.UI.StoryPresentation.Build(story, context);
            Widget body = result.Kind == StoryResultKind.Widget && result.Widget is not null
                ? result.Widget
                : Build(story, context, result, fill: false,
                    availableWidth: width, availableHeight: 360f);
            Widget preview = Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 7,
                corners: RectCorners.TopLeft | RectCorners.TopRight, width: width)[
                Center(width: width)[body]];
            Widget details = new StoryReferenceDetails(
                context, story, context.Knobs.Skip(before).ToArray(), logStart, width);
            Widget card = Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 7,
                clip: true, width: width)[VStack(0, width: width)[preview, details]];
            return Center()[VStack(6, width: width)[
                Text(reference.Path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                card
            ]];
        }
        finally { context.SuppressPlays = suppressed; }
    }

    /// <summary>埋め込みStoryにも通常Galleryと同じArgs / Output / Sourceを提供する。</summary>
    private sealed class StoryReferenceDetails : CompositeControl
    {
        private readonly StoryContext _context;
        private readonly StoryInfo _story;
        private readonly StoryKnob[] _knobs;
        private readonly int _logStart;
        private readonly float _width;
        private readonly Signal<int> _tab = new(0);
        private readonly Signal<IReadOnlyList<StoryLogEntry>> _logs = new([]);

        public StoryReferenceDetails(
            StoryContext context,
            StoryInfo story,
            StoryKnob[] knobs,
            int logStart,
            float width)
        {
            _context = context;
            _story = story;
            _knobs = knobs;
            _logStart = logStart;
            _width = width;
            RefreshLogs();
        }

        protected override Widget Build()
        {
            float contentWidth = MathF.Max(96, _width - 24);
            float tableWidth = MathF.Max(80, contentWidth - 16);
            return Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 7,
                corners: RectCorners.BottomLeft | RectCorners.BottomRight, width: _width, height: 230)[
                Tabs([NativeRenderingLabels.Arguments, NativeRenderingLabels.Output, NativeRenderingLabels.Source],
                [
                    Scroll(180, width: contentWidth)[
                        global::Luxel.Gallery.UI.Kit.KnobsTable(_knobs, width: tableWidth,
                            appearance: new global::Luxel.Gallery.UI.KnobsTableAppearance(),
                            onEdit: (_, knob, value) => _context.QueueKnobEdit(knob, value))],
                    Scroll(180, width: contentWidth)[BuildOutput(tableWidth)],
                    GalleryStorySourcePane.Build(_story, contentWidth, 180),
                ], _tab, width: _width, height: 230)];
        }

        protected override void OnRealize(UiBuildContext ctx)
        {
            void OnLogged(StoryLogEntry _) => RefreshLogs();
            _context.Logged += OnLogged;
            ctx.Own(new ActionDisposable(() => _context.Logged -= OnLogged));
        }

        private void RefreshLogs()
        {
            StoryLogEntry[] snapshot = _context.LogSnapshot();
            _logs.Value = snapshot.Skip(Math.Min(_logStart, snapshot.Length)).ToArray();
        }

        private Widget BuildOutput(float width)
        {
            IReadOnlyList<StoryLogEntry> entries = _logs.Value;
            if (entries.Count == 0)
                return Text(NativeRenderingLabels.RuntimeEventsEmpty, 13,
                    color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 10, 0, 0));

            float messageWidth = MathF.Max(40, width - 58 - 58 - 34);
            return VStack(5)[entries.Select(entry =>
                (Widget)Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), padding: new Thickness(9, 7),
                    rounded: 5, width: width)[HStack(8)[
                        Text(entry.Time.Length >= 8 ? entry.Time[..8] : entry.Time, 12,
                            color: Bind.From(() => UiTheme.T.TextMuted), width: 58),
                        Text("イベント", 12, color: Bind.From(() => UiTheme.T.Primary), width: 58),
                        Text(entry.Message, 12, color: Bind.From(() => UiTheme.T.Text), width: messageWidth,
                            wrap: Luxel.Typography.TextWrap.Word)]])
                .ToArray()];
        }

        private sealed class ActionDisposable(Action action) : IDisposable
        {
            private Action? _action = action;
            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }

    private static void Navigate(StoryContext context, TextEditorView editor, string source, string url)
    {
        if (url.StartsWith("story:", StringComparison.Ordinal))
        {
            context.Navigate(url["story:".Length..]);
            return;
        }
        if (url.StartsWith('#'))
        {
            string slug = url[1..];
            foreach (MarkdownHeading heading in MarkdownDecorations.Headings(source))
                if (MarkdownDoc.Slug(heading.Text) == slug)
                {
                    editor.ScrollToSource(heading.Offset);
                    return;
                }
            return;
        }
        if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                context.Log($"open: {url}");
            }
            catch (Exception error) { context.Log($"link 失敗: {url} ({error.Message})"); }
            return;
        }
        context.Log($"link: {url}");
    }
}
