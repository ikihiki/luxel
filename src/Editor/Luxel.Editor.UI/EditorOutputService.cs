using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public enum EditorOutputLevel { Trace, Info, Warning, Error }
public enum EditorOutputState { Ready, Loading, Error }

public sealed record EditorOutputEntry(DateTimeOffset Timestamp, string Channel, EditorOutputLevel Level, string Message);

public interface IEditorClipboard
{
    void SetText(string text);
}

public sealed class PlatformEditorClipboard : IEditorClipboard
{
    public void SetText(string text)
    {
        if (PlatformClipboard.Current is not { } clipboard)
            throw new InvalidOperationException("The platform clipboard is unavailable.");
        clipboard.SetText(text);
    }
}

public sealed class EditorOutputService
{
    private readonly List<EditorOutputEntry> _entries = [];
    public Signal<int> Version { get; } = new(0);
    public Signal<string> SelectedChannel { get; } = new("General");
    public Signal<bool> AutoScroll { get; } = new(true);
    public Signal<EditorOutputState> State { get; } = new(EditorOutputState.Ready);
    public Signal<string?> Error { get; } = new(null);
    public IReadOnlyList<EditorOutputEntry> Entries { get { _ = Version.Value; return _entries; } }
    public IReadOnlyList<string> Channels => _entries.Select(x => x.Channel)
        .Append("General").Append(SelectedChannel.Peek())
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Write(string channel, string message, EditorOutputLevel level = EditorOutputLevel.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);
        _entries.Add(new EditorOutputEntry(DateTimeOffset.UtcNow, channel.Trim(), level, message));
        Version.Value++;
    }

    public void SelectChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        string? known = Channels.FirstOrDefault(x => string.Equals(x, channel.Trim(), StringComparison.OrdinalIgnoreCase));
        if (known is null) throw new ArgumentException($"Unknown output channel '{channel}'.", nameof(channel));
        SelectedChannel.Value = known;
    }

    public IReadOnlyList<EditorOutputEntry> Current()
    {
        string channel = SelectedChannel.Peek();
        return _entries.Where(x => string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public void Clear(string? channel = null)
    {
        if (channel is null) _entries.Clear();
        else _entries.RemoveAll(x => string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase));
        Version.Value++;
    }

    public string Copy(string? channel = null)
        => string.Join(Environment.NewLine, _entries
            .Where(x => channel is null || string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Message));

    public void BeginLoading()
    {
        Error.Value = null;
        State.Value = EditorOutputState.Loading;
    }

    public void SetReady()
    {
        Error.Value = null;
        State.Value = EditorOutputState.Ready;
    }

    public void SetError(string message)
    {
        Error.Value = message;
        State.Value = EditorOutputState.Error;
        Write("General", message, EditorOutputLevel.Error);
    }
}

public sealed class OutputView : CompositeControl
{
    private readonly IEditorClipboard _clipboard;
    private readonly OutputLogWidget _log;

    public OutputView(EditorOutputService output, IEditorClipboard? clipboard = null)
    {
        Output = output;
        _clipboard = clipboard ?? new PlatformEditorClipboard();
        _log = new OutputLogWidget(output);
    }

    public EditorOutputService Output { get; }
    public Signal<string> CopiedText { get; } = new("");
    public Signal<string?> ActionError { get; } = new(null);
    public float ScrollOffset => _log.ScrollOffset;
    public float MaxScroll => _log.MaxScroll;

    public void SelectChannel(string channel) => Output.SelectChannel(channel);

    public string CopyCurrent()
    {
        string text = Output.Copy(Output.SelectedChannel.Peek());
        try
        {
            _clipboard.SetText(text);
            CopiedText.Value = text;
            ActionError.Value = null;
        }
        catch (Exception ex)
        {
            ActionError.Value = ex.Message;
        }
        return text;
    }

    public void ScrollTo(float offset) => _log.ScrollTo(offset);

    protected override Widget Build()
    {
        int version = Output.Version.Value;
        string channel = Output.SelectedChannel.Value;
        bool autoScroll = Output.AutoScroll.Value;
        IReadOnlyList<string> channels = Output.Channels;
        IReadOnlyList<EditorOutputEntry> entries = Output.Current();
        _log.SetEntries(entries, version, channel, autoScroll);

        var channelButtons = channels.Select(name => (Widget)Button(_ => Output.SelectChannel(name),
            string.Equals(name, channel, StringComparison.OrdinalIgnoreCase) ? $"[{name}]" : name)).ToList();
        channelButtons.Add(Button(_ => Output.Clear(channel), "Clear Channel"));
        channelButtons.Add(Button(_ => Output.Clear(), "Clear All"));
        channelButtons.Add(Button(_ => CopyCurrent(), "Copy"));
        var rows = new List<Widget>
        {
            Text($"Output — {channel}"),
            HStack(6)[channelButtons.ToArray()],
            Check(Output.AutoScroll, "Auto-scroll"),
        };
        if (Output.State.Value == EditorOutputState.Loading) rows.Add(Muted("Loading output…"));
        else
        {
            if (Output.State.Value == EditorOutputState.Error) rows.Add(Text(Output.Error.Value ?? "Output error"));
            rows.Add(entries.Count == 0 ? Muted("No output") : _log);
        }
        if (ActionError.Value is { } error) rows.Add(Text(error));
        return VStack(4)[rows.ToArray()];
    }

    private sealed class OutputLogWidget(EditorOutputService output) : Widget
    {
        private const float RowHeight = 20;
        private const float ViewHeight = 220;
        private readonly ScrollModel _scroll = new();
        private IReadOnlyList<EditorOutputEntry> _entries = [];
        private int _version = -1;
        private string _channel = "";
        private bool _autoScroll;
        private bool _scrollToEnd;
        private float _width = 480;

        public float ScrollOffset => _scroll.ClampedPeek;
        public float MaxScroll => MathF.Max(0, _entries.Count * RowHeight - ViewHeight);
        public override string? DebugDetail => $"Output {_channel}: {_entries.Count} rows @ {ScrollOffset:0.##}";

        public void SetEntries(IReadOnlyList<EditorOutputEntry> entries, int version, string channel, bool autoScroll)
        {
            bool contentChanged = version != _version || !string.Equals(channel, _channel, StringComparison.Ordinal);
            bool enabled = autoScroll && !_autoScroll;
            _entries = entries;
            _version = version;
            _channel = channel;
            _autoScroll = autoScroll;
            if (autoScroll && (contentChanged || enabled)) _scrollToEnd = true;
        }

        public void ScrollTo(float offset)
        {
            output.AutoScroll.Value = false;
            _scroll.ScrollTo(offset);
        }

        protected override void PerformLayout(Constraints constraints, LayoutContext context)
        {
            _width = float.IsFinite(constraints.MaxW) ? constraints.MaxW : 480;
            Size = constraints.Constrain(new Size(_width, ViewHeight));
            _scroll.SetLengths(_entries.Count * RowHeight, Size.Height);
            if (_scrollToEnd)
            {
                _scroll.ScrollTo(_scroll.MaxScroll);
                _scrollToEnd = false;
            }
        }

        protected override void RealizeCore(UiBuildContext context, UiNode parent, Point worldOrigin)
        {
            UiNode root = CreateRoot(context, parent, worldOrigin);
            root.Clip = new RectClip(0, 0, Size.Width, Size.Height);
            UiNode content = context.Canvas.AddChild(root);
            var scene = new Scene2D();
            float fontSize = context.Theme.Peek().FontSm;
            float baselineOffset = (RowHeight - context.Font.Measure("Mg", fontSize).height) / 2 + context.Font.Ascent(fontSize);
            for (int i = 0; i < _entries.Count; i++)
            {
                EditorOutputEntry entry = _entries[i];
                context.Font.AppendText(scene, $"[{entry.Level}] {entry.Message}", 8, i * RowHeight + baselineOffset,
                    fontSize, Color2D.White);
            }
            content.Content = scene;
            context.Effect(() => content.Color = context.Theme.Value.TextMuted);
            context.Effect(() => content.Transform = Affine2D.Translate(0, -_scroll.Clamped));
            ScrollBars.AttachVertical(context, root, _scroll, Size.Width, Size.Height, minThumb: 24);
            context.AddScroll(root, new Rect(0, 0, Size.Width, Size.Height), delta =>
            {
                output.AutoScroll.Value = false;
                _scroll.ScrollBy(-delta);
            });
        }
    }
}
