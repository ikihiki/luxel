using System.Diagnostics;
using Luxel.Controls;
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

    public static Widget Build(StoryInfo story, StoryContext context, StoryResult authored, bool fill = true)
    {
        if (authored.Kind == StoryResultKind.Widget && authored.Widget is not null) return authored.Widget;
        StoryResult result = authored.WithMarkdown(EffectiveMarkdown(story, authored.Markdown));

        (VectorFont? bold, _, _, VectorFont? mono) = RenderingStoryKit.EditorFaces.Value;
        var fences = new Dictionary<string, Func<string, Widget>>
        {
            ["mermaid"] = body => Luxel.Diagram.Factories.DiagramBlock(body, 640f),
            ["math"] = body => Luxel.MathText.Factories.MathBlockView(body, maxWidth: 640f),
        };
        TextEditorView editor = StoryMarkdownDocumentAdapter.FromStoryResult(result, () => UiTheme.T, width: 640f, height: 480f,
            reference => BuildReference(context, reference), bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fences: fences,
            fonts: RenderingStoryKit.JpFallback.Value, fill: fill);
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
        return editor;
    }

    private static Widget BuildReference(StoryContext context, StoryReference reference)
    {
        StoryInfo? story = StoryRegistry.Find(reference.Path);
        if (story is null) return Alert($"ストーリーが見つかりません: {reference.Path}", Intent.Danger);
        int before = context.Knobs.Count;
        int logStart = context.LogSnapshot().Length;
        bool suppressed = context.SuppressPlays;
        context.SuppressPlays = true;
        try
        {
            StoryResult result = story.BuildResult(context);
            Widget body = result.Kind == StoryResultKind.Widget && result.Widget is not null
                ? result.Widget
                : Build(story, context, result, fill: false);
            Widget preview = Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 7,
                corners: RectCorners.TopLeft | RectCorners.TopRight, width: 640)[
                Center(width: 640)[body]];
            Widget details = new StoryReferenceDetails(
                context, story, context.Knobs.Skip(before).ToArray(), logStart);
            Widget card = new StoryReferenceCard(VStack(0, width: 640)[preview, details]);
            return Center()[VStack(6, width: 640)[
                Text(reference.Path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                card
            ]];
        }
        finally { context.SuppressPlays = suppressed; }
    }

    /// <summary>
    /// RectClipは角丸を表現できないため、子が全面を塗るWidget Storyでも外周が四角く
    /// 戻らないよう、最前面で角の外側だけをMarkdown背景色に戻す。
    /// </summary>
    private sealed class StoryReferenceCard(Widget child) : Widget
    {
        private const float CardWidth = 640f;
        private const float Radius = 7f;

        public override IEnumerable<Widget> DebugChildren() => [child];

        protected override void PerformLayout(Constraints c, LayoutContext ctx)
        {
            float width = MathF.Min(CardWidth, c.MaxW);
            Size childSize = child.Layout(new Constraints(width, width, 0, c.MaxH), ctx,
                parentUsesSize: true);
            child.Offset = new Point(0, 0);
            Size = c.Constrain(new Size(width, childSize.Height));
        }

        protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        {
            UiNode root = CreateRoot(ctx, parent, worldOrigin);
            root.Clip = new RectClip(0, 0, Size.Width, Size.Height);
            root.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0,
                Size.Width, Size.Height, Radius);
            ctx.Effect(() => root.Color = ctx.Theme.Value.Surface);

            child.Realize(ctx, root, WorldPos);

            UiNode mask = ctx.Canvas.AddChild(root);
            mask.Z = 10_000;
            mask.Content = CornerMask(Size.Width, Size.Height, Radius);
            ctx.Effect(() => mask.Color = ctx.Theme.Value.Background);
        }

        private static Scene2D CornerMask(float width, float height, float radius)
        {
            var scene = new Scene2D();
            scene.BeginFill(Color2D.White)
                .MoveTo(0, 0).LineTo(radius, 0).QuadTo(0, 0, 0, radius).LineTo(0, 0).Close().End();
            scene.BeginFill(Color2D.White)
                .MoveTo(width, 0).LineTo(width - radius, 0).QuadTo(width, 0, width, radius).LineTo(width, 0).Close().End();
            scene.BeginFill(Color2D.White)
                .MoveTo(width, height).LineTo(width, height - radius).QuadTo(width, height, width - radius, height)
                .LineTo(width, height).Close().End();
            scene.BeginFill(Color2D.White)
                .MoveTo(0, height).LineTo(radius, height).QuadTo(0, height, 0, height - radius)
                .LineTo(0, height).Close().End();
            return scene;
        }
    }

    /// <summary>埋め込みStoryにも通常Galleryと同じArgs / Output / Sourceを提供する。</summary>
    private sealed class StoryReferenceDetails : CompositeControl
    {
        private readonly StoryContext _context;
        private readonly StoryInfo _story;
        private readonly StoryKnob[] _knobs;
        private readonly int _logStart;
        private readonly Signal<int> _tab = new(0);
        private readonly Signal<IReadOnlyList<StoryLogEntry>> _logs = new([]);

        public StoryReferenceDetails(StoryContext context, StoryInfo story, StoryKnob[] knobs, int logStart)
        {
            _context = context;
            _story = story;
            _knobs = knobs;
            _logStart = logStart;
            RefreshLogs();
        }

        protected override Widget Build()
            => Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 7,
                corners: RectCorners.BottomLeft | RectCorners.BottomRight, width: 640, height: 230)[
                Tabs(["Args", "Output", "Source"],
                [
                    Scroll(180, width: 616)[
                        global::Luxel.Gallery.UI.Kit.KnobsTable(_knobs, width: 600,
                            appearance: new global::Luxel.Gallery.UI.KnobsTableAppearance(),
                            onEdit: (_, knob, value) => _context.QueueKnobEdit(knob, value))],
                    Scroll(180, width: 616)[BuildOutput()],
                    GalleryStorySourcePane.Build(_story, 616, 180),
                ], _tab, width: 640, height: 230)];

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

        private Widget BuildOutput()
        {
            IReadOnlyList<StoryLogEntry> entries = _logs.Value;
            if (entries.Count == 0)
                return Text("ランタイムのイベントとエラーがここに表示されます。", 13,
                    color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 10, 0, 0));

            return VStack(5)[entries.Select(entry =>
                (Widget)Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), padding: new Thickness(9, 7),
                    rounded: 5, width: 600)[HStack(8)[
                        Text(entry.Time.Length >= 8 ? entry.Time[..8] : entry.Time, 11,
                            color: Bind.From(() => UiTheme.T.TextMuted), width: 58),
                        Text("イベント", 10, color: Bind.From(() => UiTheme.T.Primary), width: 50),
                        Text(entry.Message, 11, color: Bind.From(() => UiTheme.T.Text), width: 460,
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
